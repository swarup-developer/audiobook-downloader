using System.Buffers.Binary;
using System.Text;

namespace AudiobookDownloader.Core;

/// <summary>
/// Reads the chapter list of an M4B or M4A file: the QuickTime chapter track, the
/// one Audible files, iTunes and ffmpeg all write.
/// </summary>
/// <remarks>
/// Read here rather than through the decryption library, whose reader takes the
/// chapter track's times in the audio's own time units and so finds one chapter,
/// or chapters a fraction of a second long, in files whose chapter track keeps its
/// own (ffmpeg's and this app's among them). Only the box headers and the chapter
/// track's small tables and titles are read, so a book of many hours opens at once.
/// </remarks>
public static class Mp4Chapters
{
    private sealed record Track(uint Id, string Handler, uint Timescale, long Length, long[] Durations, long[] Sizes, long[] Offsets, uint[] ChapterRefs);

    /// <summary>The chapters in playing order, or an empty list when the file has none.</summary>
    public static IReadOnlyList<(string Title, TimeSpan Start)> Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var moov = FindBox(file, 0, file.Length, "moov");
            if (moov is null)
                return [];

            var tracks = new List<Track>();
            foreach (var trak in Children(file, moov.Value.Start, moov.Value.End).Where(b => b.Type == "trak"))
                if (ReadTrack(file, trak.Start, trak.End) is { } track)
                    tracks.Add(track);

            // The audio track names its chapter track; failing that, any text track will do.
            var referenced = tracks.SelectMany(t => t.ChapterRefs).ToHashSet();
            var chapters = tracks.FirstOrDefault(t => referenced.Contains(t.Id))
                           ?? tracks.FirstOrDefault(t => t.Handler is "text" or "sbtl");
            if (chapters is null || chapters.Timescale == 0)
                return [];

            // Some writers (the decryption library among them) put the chapter times in
            // the audio's time units while the chapter track declares its own. Of the
            // two readings, the one whose chapters add up to the book's length is right.
            var timescale = (double)chapters.Timescale;
            var audio = tracks.FirstOrDefault(t => t.Handler == "soun");
            var ticks = chapters.Durations.Sum();
            if (audio is { Timescale: > 0, Length: > 0 } && ticks > 0 && audio.Timescale != chapters.Timescale)
            {
                var book = (double)audio.Length / audio.Timescale;
                var asDeclared = ticks / timescale;
                var asAudio = (double)ticks / audio.Timescale;
                if (Math.Abs(asAudio - book) < Math.Abs(asDeclared - book))
                    timescale = audio.Timescale;
            }

            var result = new List<(string, TimeSpan)>();
            long time = 0;
            for (var i = 0; i < chapters.Sizes.Length && i < chapters.Offsets.Length; i++)
            {
                var title = ReadTitle(file, chapters.Offsets[i], chapters.Sizes[i]);
                result.Add((string.IsNullOrWhiteSpace(title) ? $"Chapter {i + 1}" : title.Trim(),
                    TimeSpan.FromSeconds(time / timescale)));
                time += i < chapters.Durations.Length ? chapters.Durations[i] : 0;
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            Log.Info($"Chapters could not be read from {Path.GetFileName(path)}: {ex.Message}");
            return [];
        }
    }

    // ------------------------------------------------------------------
    // Boxes
    // ------------------------------------------------------------------

    private readonly record struct Box(string Type, long Start, long End);

    /// <summary>The boxes directly inside the range [start, end).</summary>
    private static IEnumerable<Box> Children(Stream s, long start, long end)
    {
        var header = new byte[16];
        var position = start;
        while (position + 8 <= end)
        {
            s.Position = position;
            if (s.Read(header, 0, 8) < 8)
                yield break;

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header, 4, 4);
            var body = position + 8;

            if (size == 1)
            {
                if (s.Read(header, 8, 8) < 8)
                    yield break;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                body = position + 16;
            }
            else if (size == 0)
            {
                size = end - position;
            }

            if (size < 8 || position + size > end)
                yield break;

            yield return new Box(type, body, position + size);
            position += size;
        }
    }

    private static Box? FindBox(Stream s, long start, long end, string type) =>
        Children(s, start, end).FirstOrDefault(b => b.Type == type) is { Type: not null } found ? found : null;

    private static Box? Descend(Stream s, Box parent, params string[] types)
    {
        Box? current = parent;
        foreach (var type in types)
        {
            current = current is { } box ? FindBox(s, box.Start, box.End, type) : null;
            if (current is null)
                return null;
        }
        return current;
    }

    private static byte[] Bytes(Stream s, Box box, int max = 64 * 1024 * 1024)
    {
        var length = (int)Math.Min(box.End - box.Start, max);
        var data = new byte[length];
        s.Position = box.Start;
        s.ReadExactly(data);
        return data;
    }

    private static Track? ReadTrack(Stream s, long start, long end)
    {
        var trak = new Box("trak", start, end);
        var tkhd = Descend(s, trak, "tkhd");
        var mdhd = Descend(s, trak, "mdia", "mdhd");
        var hdlr = Descend(s, trak, "mdia", "hdlr");
        var stbl = Descend(s, trak, "mdia", "minf", "stbl");
        if (tkhd is null || mdhd is null || hdlr is null || stbl is null)
            return null;

        var tk = Bytes(s, tkhd.Value, 256);
        var id = tk[0] == 1 ? BinaryPrimitives.ReadUInt32BigEndian(tk.AsSpan(20)) : BinaryPrimitives.ReadUInt32BigEndian(tk.AsSpan(12));

        var md = Bytes(s, mdhd.Value, 256);
        var timescale = md[0] == 1 ? BinaryPrimitives.ReadUInt32BigEndian(md.AsSpan(20)) : BinaryPrimitives.ReadUInt32BigEndian(md.AsSpan(12));
        var length = md[0] == 1 ? (long)BinaryPrimitives.ReadUInt64BigEndian(md.AsSpan(24)) : BinaryPrimitives.ReadUInt32BigEndian(md.AsSpan(16));

        var hd = Bytes(s, hdlr.Value, 64);
        var handler = Encoding.ASCII.GetString(hd, 8, 4);

        var chapterRefs = Descend(s, trak, "tref", "chap") is { } chap
            ? ReadUInts(Bytes(s, chap, 4096), 0)
            : [];

        // Only the chapter (text) track's tables are needed; the audio's can be huge.
        if (handler is not ("text" or "sbtl"))
            return new Track(id, handler, timescale, length, [], [], [], chapterRefs);

        var durations = ReadStts(Bytes(s, Descend(s, stbl.Value, "stts")!.Value));
        var sizes = ReadStsz(Bytes(s, Descend(s, stbl.Value, "stsz")!.Value));
        var chunkOffsets = Descend(s, stbl.Value, "stco") is { } stco
            ? ReadUInts(Bytes(s, stco), 8).Select(o => (long)o).ToArray()
            : Descend(s, stbl.Value, "co64") is { } co64 ? ReadULongs(Bytes(s, co64)) : [];
        var stsc = Descend(s, stbl.Value, "stsc") is { } stscBox ? Bytes(s, stscBox) : [];

        return new Track(id, handler, timescale, length, durations, sizes, SampleOffsets(chunkOffsets, stsc, sizes), chapterRefs);
    }

    private static uint[] ReadUInts(byte[] data, int skip)
    {
        var list = new List<uint>();
        var start = skip;
        if (skip == 8)
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
            for (var i = 0; i < count && start + 4 <= data.Length; i++, start += 4)
                list.Add(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start)));
            return [.. list];
        }

        for (; start + 4 <= data.Length; start += 4)
            list.Add(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start)));
        return [.. list];
    }

    private static long[] ReadULongs(byte[] data)
    {
        var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        var list = new long[count];
        for (var i = 0; i < count && 8 + i * 8 + 8 <= data.Length; i++)
            list[i] = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(8 + i * 8));
        return list;
    }

    private static long[] ReadStts(byte[] data)
    {
        var entries = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        var durations = new List<long>();
        for (var i = 0; i < entries && 8 + i * 8 + 8 <= data.Length; i++)
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8 + i * 8));
            var delta = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12 + i * 8));
            for (var n = 0; n < count && durations.Count < 100_000; n++)
                durations.Add(delta);
        }
        return [.. durations];
    }

    private static long[] ReadStsz(byte[] data)
    {
        var fixedSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
        var sizes = new long[Math.Min(count, 100_000)];
        for (var i = 0; i < sizes.Length; i++)
            sizes[i] = fixedSize != 0 ? fixedSize : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12 + i * 4));
        return sizes;
    }

    /// <summary>Where each sample starts, from the chunk offsets and the sample-to-chunk table.</summary>
    private static long[] SampleOffsets(long[] chunkOffsets, byte[] stsc, long[] sizes)
    {
        var runs = new List<(uint FirstChunk, uint PerChunk)>();
        if (stsc.Length >= 8)
        {
            var entries = BinaryPrimitives.ReadUInt32BigEndian(stsc.AsSpan(4));
            for (var i = 0; i < entries && 8 + i * 12 + 12 <= stsc.Length; i++)
                runs.Add((BinaryPrimitives.ReadUInt32BigEndian(stsc.AsSpan(8 + i * 12)),
                          BinaryPrimitives.ReadUInt32BigEndian(stsc.AsSpan(12 + i * 12))));
        }
        if (runs.Count == 0)
            runs.Add((1, 1));

        var offsets = new List<long>();
        var sample = 0;
        for (var chunk = 0; chunk < chunkOffsets.Length && sample < sizes.Length; chunk++)
        {
            var perChunk = runs.Last(r => r.FirstChunk <= chunk + 1).PerChunk;
            var position = chunkOffsets[chunk];
            for (var n = 0; n < perChunk && sample < sizes.Length; n++, sample++)
            {
                offsets.Add(position);
                position += sizes[sample];
            }
        }
        return [.. offsets];
    }

    /// <summary>A chapter title: two bytes of length, then UTF-8 (or UTF-16 after a byte order mark).</summary>
    private static string ReadTitle(Stream s, long offset, long size)
    {
        if (size < 2 || size > 64 * 1024)
            return "";

        var data = new byte[size];
        s.Position = offset;
        s.ReadExactly(data);

        var length = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(data), (int)size - 2);
        if (length >= 2 && data[2] == 0xFE && data[3] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data, 4, length - 2);
        if (length >= 2 && data[2] == 0xFF && data[3] == 0xFE)
            return Encoding.Unicode.GetString(data, 4, length - 2);
        return Encoding.UTF8.GetString(data, 2, length);
    }
}
