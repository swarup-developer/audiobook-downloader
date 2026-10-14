using System.Text.Json;

namespace AudiobookDownloader.Core;

/// <summary>One unfinished download, exactly as it is written to disk.</summary>
/// <remarks>
/// Only what the queue needs to pick the work up again: the provider the book came
/// from, the book itself, and what became of it. No download ticket is stored — a
/// ticket is a short-lived address with a key attached, and asking the provider for
/// a fresh one on the next run is both safer and what lets a signed-in library work
/// after the sign-in has been renewed.
/// </remarks>
public sealed record StoredQueueItem(string ProviderId, BookInfo Book, QueueStatus Status, string? Error);

/// <summary>
/// The queue's own file: the work that was still outstanding when the app last
/// closed, so a batch that was interrupted comes back instead of being forgotten.
/// </summary>
/// <remarks>
/// It holds only what is left to do — an item still pending, or one that failed or
/// was cancelled and can be tried again. A book that finished is not written at all:
/// it is already on disk, and a list of last session's successes would only push the
/// outstanding work off the queue panel.
///
/// An item that was being downloaded is stored as <see cref="QueueStatus.Pending"/>.
/// An app that was closed mid-transfer is not a user who cancelled anything, and the
/// partial file it leaves behind is waiting to be resumed — while a real cancel *is*
/// written down as a cancel, because that is an instruction and must not start
/// itself again on the next run.
///
/// Nothing in this file is allowed to stop the app starting. A file that cannot be
/// read is logged and treated as an empty queue, and a file that cannot be written is
/// logged and ignored, because the next run's downloads matter more than the record
/// of the last one.
/// </remarks>
public sealed class QueueStore(string filePath)
{
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    /// <summary>The queue file of this installation, beside the settings file.</summary>
    public static QueueStore Default() => new(Path.Combine(AppSettings.SettingsFolder, "queue.json"));

    /// <summary>Where the queue is kept, for the log and the self-test.</summary>
    public string FilePath { get; } = filePath;

    /// <summary>
    /// The items an earlier run left unfinished, oldest first. A file that is missing,
    /// empty or unreadable gives nothing back rather than an exception.
    /// </summary>
    public IReadOnlyList<StoredQueueItem> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            var stored = JsonSerializer.Deserialize<List<StoredQueueItem>>(File.ReadAllText(FilePath)) ?? [];

            // A book with no provider, or no identity of its own, could never be
            // downloaded again, so it is dropped rather than carried around.
            var usable = stored
                .Where(item => item.Book is not null && !string.IsNullOrWhiteSpace(item.ProviderId))
                .Select(item => item with { Status = WorkToDo(item.Status) })
                .ToList();

            if (usable.Count != stored.Count)
                Log.Warn($"Queue: {stored.Count - usable.Count} saved item(s) had no provider or book and were dropped.");

            return usable;
        }
        catch (Exception ex)
        {
            Log.Warn($"The saved queue could not be read and is being ignored: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// What a status means for an app that is not running. A download cannot be in
    /// progress in a process that is starting, so anything that was being downloaded
    /// is work to do again — and what is written down is never a status that claims
    /// otherwise.
    /// </summary>
    private static QueueStatus WorkToDo(QueueStatus status) =>
        status == QueueStatus.Active ? QueueStatus.Pending : status;

    /// <summary>
    /// Writes the work that is still outstanding — whole or not at all: the items are
    /// written beside the real file and moved over it, so an app that stops mid-write
    /// leaves the previous queue intact instead of half a file.
    /// </summary>
    public void Save(IEnumerable<QueueItem> items)
    {
        try
        {
            var unfinished = items
                .Where(item => item.Status != QueueStatus.Completed)
                .Select(item => new StoredQueueItem(
                    item.Provider.Id,
                    item.Book,
                    WorkToDo(item.Status),
                    item.Error))
                .ToList();

            // Nothing to remember, and nothing left over from an earlier run.
            if (unfinished.Count == 0 && !File.Exists(FilePath))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(unfinished, Format));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"The queue could not be saved: {ex.Message}");
        }
    }
}
