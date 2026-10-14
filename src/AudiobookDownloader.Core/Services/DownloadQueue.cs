namespace AudiobookDownloader.Core;

/// <summary>Counts of how a queue run ended, raised once when the run finishes.</summary>
public sealed record QueueFinishedSummary(int Completed, int Failed, int Cancelled);

/// <summary>
/// Sequential download queue. Books added here are processed one at a time;
/// each item moves through Pending → Active → Completed / Failed / Cancelled.
/// Failed or cancelled items can be retried. The UI listens to the Changed
/// event to keep the queue panel up to date.
/// </summary>
/// <remarks>
/// Given a <see cref="QueueStore"/>, the queue also survives the app being closed:
/// what is still outstanding is written down as it changes, and
/// <see cref="RestoreSaved"/> brings it back on the next run, so a batch of twenty
/// books does not become a batch of the seven that happened to finish.
/// </remarks>
public sealed class DownloadQueue
{
    private readonly DownloadManager _downloader;
    private readonly QueueStore? _store;
    private readonly List<QueueItem> _items = [];
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;

    /// <param name="store">
    /// Where to remember the unfinished work. Without one the queue is only as
    /// durable as the process, which is what the offline checks use.
    /// </param>
    public DownloadQueue(DownloadManager downloader, QueueStore? store = null)
    {
        _downloader = downloader;
        _store = store;
    }

    /// <summary>Raised (on a background thread) whenever any item's row text changes.</summary>
    public event Action? Changed;

    /// <summary>Raised once when a run that actually processed items comes to an end.</summary>
    public event Action<QueueFinishedSummary>? QueueFinished;

    public IReadOnlyList<QueueItem> Items
    {
        get { lock (_lock) return _items.ToList(); }
    }

    public bool IsRunning => _worker is not null && !_worker.IsCompleted;

    public QueueItem Enqueue(IBookProvider provider, BookInfo book)
    {
        var item = new QueueItem(provider, book);
        lock (_lock) _items.Add(item);
        Save();
        RaiseChanged();
        return item;
    }

    /// <summary>
    /// Bring back the downloads an earlier run left unfinished — a batch that was
    /// interrupted, and any item that failed or was cancelled and can be tried
    /// again — so nothing has to be found and queued a second time.
    /// </summary>
    /// <param name="resolve">
    /// Finds the provider an item came from. Defaults to the registered providers;
    /// the offline checks pass their own, so they need not disturb the registry.
    /// </param>
    /// <remarks>
    /// Items come back as they were left: pending work is pending again, and
    /// something the user cancelled stays cancelled until they press Retry — a
    /// cancel is an instruction, not an accident. An item from a provider this
    /// build does not have is logged and left out. Safe to call more than once:
    /// a book already in the queue is not added twice. Returns how many came back.
    /// </remarks>
    public int RestoreSaved(Func<string, IBookProvider?>? resolve = null)
    {
        resolve ??= id => ProviderRegistry.Find(id);

        var stored = _store?.Load() ?? [];
        var restored = 0;

        lock (_lock)
        {
            foreach (var entry in stored)
            {
                if (_items.Any(item => item.Provider.Id == entry.ProviderId &&
                                       item.Book.BookId == entry.Book.BookId))
                    continue;

                if (resolve(entry.ProviderId) is not { } provider)
                {
                    Log.Warn($"Queue: \"{entry.Book}\" came from the \"{entry.ProviderId}\" provider, " +
                             "which this build does not have; it was left out of the queue.");
                    continue;
                }

                _items.Add(new QueueItem(provider, entry.Book)
                {
                    Status = entry.Status,
                    Error = entry.Error,
                    Message = entry.Status switch
                    {
                        QueueStatus.Failed => string.IsNullOrWhiteSpace(entry.Error)
                            ? "Failed"
                            : $"Failed: {entry.Error}",
                        QueueStatus.Cancelled => "Cancelled",
                        _ => "Waiting…",
                    },
                });
                restored++;
            }
        }

        if (restored > 0)
        {
            Log.Info($"Queue: {restored} unfinished download(s) from the last run came back.");
            Save();
            RaiseChanged();
        }

        return restored;
    }

    /// <summary>
    /// Remove a queued (not yet started) item. Anything already running or
    /// finished is ignored — use CancelActive, Retry or ClearFinished for those.
    /// </summary>
    public void Remove(QueueItem item)
    {
        if (item.Status is not QueueStatus.Pending)
            return; // only items that have not started yet can be removed
        lock (_lock) _items.Remove(item);
        Save();
        RaiseChanged();
    }

    public void ClearFinished()
    {
        lock (_lock)
            _items.RemoveAll(i => i.Status is QueueStatus.Completed or QueueStatus.Cancelled);
        Save();
        RaiseChanged();
    }

    /// <summary>Mark a failed/cancelled item as pending again, then start the worker.</summary>
    public void Retry(QueueItem item)
    {
        if (item.Status is not (QueueStatus.Failed or QueueStatus.Cancelled))
            return;
        item.Status = QueueStatus.Pending;
        item.Error = null;
        item.Message = "Waiting…";
        item.Percent = 0;
        Save();
        RaiseChanged();
        Start();
    }

    public void Start()
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _worker = Task.Run(() => RunAsync(token), token);
    }

    /// <summary>Cancel the active item; pending items stay pending.</summary>
    public void CancelActive()
    {
        _cts?.Cancel();
    }

    private async Task RunAsync(CancellationToken token)
    {
        var processed = 0;
        try
        {
            // Re-snapshot on each pass so books enqueued while running are picked up too.
            while (true)
            {
                List<QueueItem> batch;
                lock (_lock)
                    batch = _items.Where(i => i.Status == QueueStatus.Pending).ToList();
                if (batch.Count == 0)
                    break;

                foreach (var item in batch)
                {
                    token.ThrowIfCancellationRequested();
                    await RunOneAsync(item, token);
                    processed++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // outer cancel: any item still marked Active becomes Cancelled
            lock (_lock)
            {
                foreach (var i in _items.Where(i => i.Status == QueueStatus.Active))
                {
                    i.Status = QueueStatus.Cancelled;
                    i.Message = "Cancelled";
                }
            }
            Save();
            RaiseChanged();
        }
        finally
        {
            // Whatever the run ended as, the file says what is still outstanding.
            Save();

            if (processed > 0)
            {
                List<QueueItem> snapshot;
                lock (_lock) snapshot = _items.ToList();
                QueueFinished?.Invoke(new QueueFinishedSummary(
                    Completed: snapshot.Count(i => i.Status == QueueStatus.Completed),
                    Failed: snapshot.Count(i => i.Status == QueueStatus.Failed),
                    Cancelled: snapshot.Count(i => i.Status == QueueStatus.Cancelled)));
            }
            RaiseChanged();
        }
    }

    private async Task RunOneAsync(QueueItem item, CancellationToken token)
    {
        item.Status = QueueStatus.Active;
        item.Percent = 0;
        item.Message = "Starting…";
        RaiseChanged();

        try
        {
            var ticket = await item.Provider.GetDownloadTicketAsync(item.Book, token);

            var progress = new Progress<DownloadProgress>(p =>
            {
                item.Percent = p.Percent;
                item.Message = p.Message;
                RaiseChanged();
            });

            var result = await _downloader.DownloadAsync(item.Book, ticket, progress, token);

            if (result.Success)
            {
                item.Status = QueueStatus.Completed;
                item.Percent = 100;
                item.OutputFile = result.OutputFile;
                item.Message = $"Saved {result.OutputFile}";
                Log.Info($"Queue: downloaded '{item.Book.Title}' to {result.OutputFile}");
            }
            else if (result.Cancelled)
            {
                // The user stopped it, which is not a failure.
                item.Status = QueueStatus.Cancelled;
                item.Message = "Cancelled";
                Log.Info($"Queue: '{item.Book.Title}' was cancelled. Any partial file was kept.");
            }
            else
            {
                item.Status = QueueStatus.Failed;
                item.Error = result.ErrorMessage;
                item.Message = $"Failed: {result.ErrorMessage}";
                Log.Warn($"Queue: '{item.Book.Title}' failed: {result.ErrorMessage}");
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = QueueStatus.Cancelled;
            item.Message = "Cancelled";
        }
        catch (Exception ex)
        {
            item.Status = QueueStatus.Failed;
            item.Error = ex.Message;
            item.Message = $"Failed: {ex.Message}";
            Log.Error($"Queue: '{item.Book.Title}' failed: {ex.Message}");
        }

        // Written here rather than on every progress step: what has to survive is
        // what became of an item, not how far along it was when the app stopped.
        Save();
        RaiseChanged();
    }

    /// <summary>Remember what is still outstanding, so a restart has something to resume.</summary>
    private void Save()
    {
        if (_store is null)
            return;

        lock (_lock)
            _store.Save(_items);
    }

    private void RaiseChanged() => Changed?.Invoke();
}
