namespace AudiobookDownloader.Core;

public enum QueueStatus
{
    Pending,
    Active,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>One entry in the download queue, shown in the queue list.</summary>
public sealed class QueueItem(IBookProvider provider, BookInfo book)
{
    public Guid Id { get; } = Guid.NewGuid();
    public IBookProvider Provider { get; } = provider;
    public BookInfo Book { get; } = book;

    public QueueStatus Status { get; internal set; } = QueueStatus.Pending;
    public double Percent { get; internal set; }
    public string Message { get; internal set; } = "Waiting…";
    public string? OutputFile { get; internal set; }
    public string? Error { get; internal set; }

    /// <summary>What NVDA reads for this row, e.g. "My Book - Jane Doe, active, 45 percent".</summary>
    public override string ToString()
    {
        var state = Status switch
        {
            QueueStatus.Pending => "pending",
            QueueStatus.Active => $"active, {Percent:0} percent",
            QueueStatus.Completed => "completed",
            QueueStatus.Failed => $"failed: {Error}",
            QueueStatus.Cancelled => "cancelled",
            _ => "",
        };
        return $"{Book} — {state}";
    }
}
