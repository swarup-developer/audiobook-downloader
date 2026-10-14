using System.Net;
using System.Net.Sockets;
using System.Text;
using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Serves one byte array over loopback, with knobs to simulate a broken connection,
/// so the download engine, the queue and the catalogue checks can run end to end
/// without the internet.
/// </summary>
internal sealed class LoopbackFileServer : IDisposable
{
    private readonly byte[] _payload;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private int _requests;
    private int _rangeRequests;

    public LoopbackFileServer(byte[] payload)
    {
        _payload = payload;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Send only this many bytes on the first response, then drop the connection.</summary>
    public int? TruncateFirstResponseAfter { get; init; }

    /// <summary>Ignore range requests entirely, like many simple servers do.</summary>
    public bool NeverHonourRange { get; init; }

    /// <summary>Send the body in pieces of this size, to make a slow transfer.</summary>
    public int ChunkSize { get; init; }

    /// <summary>Pause this long between pieces, so a transfer can be cancelled mid-way.</summary>
    public int ChunkDelayMs { get; init; }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>How many requests asked for a range, which proves resuming happened.</summary>
    public int RangeRequests => Volatile.Read(ref _rangeRequests);

    /// <summary>A ticket for the book this server is standing in for.</summary>
    public DownloadTicket TicketFor(BookInfo book) =>
        new(book.BookId, TicketFormat.PlainUrl, new Uri($"http://127.0.0.1:{Port}/book.m4a"),
            null, null, null, SuggestedExtension: ".m4a");

    /// <summary>
    /// True when the file at this path holds exactly the bytes this server served,
    /// which is how a check knows a download arrived complete and undamaged.
    /// </summary>
    public bool ServedTo(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path)
                   && File.Exists(path)
                   && File.ReadAllBytes(path) is { } actual
                   && actual.Length == _payload.Length
                   && actual.AsSpan().SequenceEqual(_payload);
        }
        catch
        {
            return false;
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { return; }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var rangeFrom = await ReadRequestAsync(stream, _cts.Token);
                if (rangeFrom is null)
                    return;

                var number = Interlocked.Increment(ref _requests);

                long start = 0;
                if (!NeverHonourRange && rangeFrom is { } from && from > 0 && from < _payload.Length)
                {
                    start = from;
                    Interlocked.Increment(ref _rangeRequests);
                }

                var body = _payload.AsMemory((int)start);
                var truncate = number == 1 && TruncateFirstResponseAfter is { } limit && limit < body.Length;
                var send = truncate ? TruncateFirstResponseAfter!.Value : body.Length;

                var head = new StringBuilder()
                    .Append(start > 0 ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n")
                    .Append($"Content-Length: {body.Length}\r\n")
                    .Append(start > 0 ? $"Content-Range: bytes {start}-{_payload.Length - 1}/{_payload.Length}\r\n" : "")
                    .Append("Accept-Ranges: bytes\r\n")
                    .Append("Connection: close\r\n\r\n")
                    .ToString();

                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _cts.Token);

                var offset = 0;
                while (offset < send)
                {
                    var count = Math.Min(ChunkSize <= 0 ? send : ChunkSize, send - offset);
                    await stream.WriteAsync(body.Slice(offset, count), _cts.Token);
                    await stream.FlushAsync(_cts.Token);
                    offset += count;

                    if (ChunkDelayMs > 0)
                        await Task.Delay(ChunkDelayMs, _cts.Token);
                }
                // Closing with fewer bytes than Content-Length is exactly how a
                // dropped connection looks to the client.
            }
        }
        catch
        {
            // A client that goes away mid-response is normal for these tests.
        }
    }

    /// <summary>Reads the request headers and returns the requested range start, if any.</summary>
    private static async Task<long?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var text = new StringBuilder();
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read <= 0)
                break;

            text.Append(Encoding.ASCII.GetString(buffer, total, read));
            total += read;

            if (text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                break;
        }

        var request = text.ToString();
        if (request.Length == 0)
            return null;

        foreach (var line in request.Split("\r\n"))
        {
            if (!line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = line["Range:".Length..].Trim();
            var dash = value.IndexOf('-');
            if (value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) && dash > "bytes=".Length &&
                long.TryParse(value["bytes=".Length..dash], out var from))
                return from;
        }

        return 0;
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* best effort */ }
        try { _listener.Stop(); } catch { /* best effort */ }
        _cts.Dispose();
    }
}
