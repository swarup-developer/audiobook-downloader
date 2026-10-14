using System.Text.Json;
using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Offline checks that a batch of downloads outlives the app: what is still
/// outstanding is written down as it changes, comes back on the next run, resumes
/// from the partial file instead of starting the book again — and a cancel is
/// remembered as a cancel rather than quietly restarting itself.
/// A loopback server stands in for the internet, so nothing here touches the network.
/// </summary>
internal static class SelfTestQueue
{
    /// <summary>Big enough to be interrupted half way through, and to prove resuming.</summary>
    private static readonly byte[] Payload = CreatePayload();

    public static void Run(Action<string> line, Action<string> fail)
    {
        var folder = Path.Combine(Path.GetTempPath(), "audiobook-selftest-queue");
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            fail($"could not create the test folder {folder}: {ex.Message}");
            return;
        }

        try
        {
            // Every check that waits on asynchronous work must run on a pool thread:
            // this one has a UI synchronization context, so blocking it while an await
            // wants to come back here would deadlock.
            Task.Run(() =>
            {
                CheckWhatTheQueueRemembers(folder, line, fail);
                CheckInterruptedBatchResumesAsync(folder, line, fail).GetAwaiter().GetResult();
                CheckCancelIsNotAnAccidentAsync(folder, line, fail).GetAwaiter().GetResult();
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            fail($"the queue checks could not run: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        }
    }

    // ------------------------------------------------------------------
    // The file itself
    // ------------------------------------------------------------------

    /// <summary>
    /// What the queue writes down, and what it refuses to be stopped by.
    /// </summary>
    private static void CheckWhatTheQueueRemembers(string folder, Action<string> line, Action<string> fail)
    {
        line("== The queue's own file ==");

        var store = new QueueStore(Path.Combine(folder, "remember.json"));
        using var server = new LoopbackFileServer(Payload);
        var provider = new LoopbackProvider(server, "selftest");

        var wrong = 0;
        void Complain(string message)
        {
            wrong++;
            fail(message);
        }

        // 0) It belongs beside the settings file, which is where somebody looks for it.
        var beside = Path.Combine(AppSettings.SettingsFolder, "queue.json");
        if (QueueStore.Default().FilePath != beside)
            Complain($"the queue is kept at \"{QueueStore.Default().FilePath}\" " +
                     $"instead of beside the settings, at \"{beside}\"");

        // 1) Queued work is on disk, in order, from the moment it is queued.
        var queue = new DownloadQueue(new DownloadManager(Settings(folder)), store);
        queue.Enqueue(provider, Book("r1", "First Queued"));
        queue.Enqueue(provider, Book("r2", "Second Queued"));

        var written = store.Load();
        if (written.Count != 2)
            Complain($"the queue wrote {written.Count} item(s) to disk instead of 2");
        else if (written[0].Book.BookId != "r1" || written[1].Book.BookId != "r2")
            Complain("the queue was written in a different order from the one it was queued in");
        else if (written.Any(item => item.ProviderId != "selftest" || item.Status != QueueStatus.Pending))
            Complain("a freshly queued item was not written down as pending work");

        // 2) The same work comes back, as it was left, and only once.
        var reopened = new DownloadQueue(new DownloadManager(Settings(folder)), store);
        var restored = reopened.RestoreSaved(id => id == "selftest" ? provider : null);

        if (restored != 2)
            Complain($"a new run brought back {restored} item(s) instead of the 2 that were queued");
        else if (!reopened.Items.Select(item => item.Book.BookId).SequenceEqual(["r1", "r2"]))
            Complain("the items that came back are not the books that were queued, in order");
        else if (reopened.Items.Any(item => item.Status != QueueStatus.Pending))
            Complain("queued work came back as something other than pending work");
        else if (reopened.RestoreSaved(id => id == "selftest" ? provider : null) != 0)
            Complain("restoring twice queued the same books a second time");

        // 3) A queue left behind by a build with other providers, and a file that is
        //    not a queue at all, both have to leave the app working rather than stop
        //    it from starting.
        File.WriteAllText(store.FilePath, JsonSerializer.Serialize<StoredQueueItem[]>(
        [
            new StoredQueueItem("a-provider-this-build-does-not-have", Book("gone", "Gone"), QueueStatus.Pending, null),
            new StoredQueueItem("selftest", Book("kept", "Was Being Downloaded"), QueueStatus.Active, null),
        ]));

        var orphans = new DownloadQueue(new DownloadManager(Settings(folder)), store);
        var kept = orphans.RestoreSaved(id => id == "selftest" ? provider : null);

        if (kept != 1 || orphans.Items[0].Book.BookId != "kept")
            Complain("an item from a provider this build does not have was not left out cleanly");
        else if (orphans.Items[0].Status != QueueStatus.Pending)
            Complain("a book that was being downloaded when the app stopped did not come back as work to do");

        File.WriteAllText(store.FilePath, "{ this is not a queue");

        var unreadable = new DownloadQueue(new DownloadManager(Settings(folder)), store);
        var fromRubbish = unreadable.RestoreSaved(id => id == "selftest" ? provider : null);

        if (fromRubbish != 0 || unreadable.Items.Count != 0)
            Complain($"a queue file that cannot be read produced {fromRubbish} item(s) instead of none");

        // ... and the app goes on working: the next book queued rewrites a valid file.
        unreadable.Enqueue(provider, Book("r3", "After Rubbish"));

        if (store.Load().Count != 1)
            Complain("the queue file was not written again after an unreadable one was replaced");
        else if (File.Exists(store.FilePath + ".tmp"))
            Complain("the queue was left half-written instead of being moved into place");

        // 4) A queue with no file of its own keeps nothing, which is what the other
        //    offline checks rely on.
        var memoryOnly = new DownloadQueue(new DownloadManager(Settings(folder)));
        memoryOnly.Enqueue(provider, Book("r4", "Not Written"));

        if (memoryOnly.RestoreSaved(id => id == "selftest" ? provider : null) != 0)
            Complain("a queue with no file of its own invented a saved item");

        if (wrong == 0)
        {
            line("   queued work is written down as it is queued, and comes back in the same order");
            line("   an item from a provider this build does not have, and a file that cannot be read " +
                 "at all, are both left out without stopping the app");
        }

        line("");
    }

    // ------------------------------------------------------------------
    // An interrupted batch
    // ------------------------------------------------------------------

    /// <summary>
    /// A batch cut off part way through comes back, and the book that was interrupted
    /// resumes from where it stopped rather than starting again from the beginning.
    /// </summary>
    /// <remarks>
    /// The closing of the app is modelled rather than performed: the transfer is cut
    /// off by a timer, which leaves exactly the partial file a killed app leaves, and
    /// the queue file is the one the queue wrote when the books were queued — an item
    /// only ever becomes "active" in memory, never on disk.
    /// </remarks>
    private static async Task CheckInterruptedBatchResumesAsync(string folder, Action<string> line, Action<string> fail)
    {
        line("== A batch that outlived the app ==");

        var settings = Settings(folder);
        var manager = new DownloadManager(settings);
        var store = new QueueStore(Path.Combine(folder, "batch.json"));

        var wrong = 0;
        void Complain(string message)
        {
            wrong++;
            fail(message);
        }

        using var server = new LoopbackFileServer(Payload) { ChunkSize = 4096, ChunkDelayMs = 5 };
        var provider = new LoopbackProvider(server, "selftest");
        var interrupted = Book("b1", "Interrupted");
        var untouched = Book("b2", "Not Started");

        // The run the app did not survive: two books queued — so two books written
        // down — and the first one cut off part way through.
        var before = new DownloadQueue(manager, store);
        before.Enqueue(provider, interrupted);
        before.Enqueue(provider, untouched);

        using (var cutOff = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
            await manager.DownloadAsync(interrupted, server.TicketFor(interrupted), null, cutOff.Token);

        if (!File.Exists(PartialOf(settings, interrupted)))
            Complain("the cut-off download left no partial file, so there was nothing to resume");

        // The next run, as if the app had been opened again.
        var after = new DownloadQueue(manager, store);
        var restored = after.RestoreSaved(id => id == "selftest" ? provider : null);

        if (restored != 2)
            Complain($"the next run brought back {restored} of the 2 books that were interrupted");
        else if (!after.Items.Select(item => item.Book.BookId).SequenceEqual(["b1", "b2"]))
            Complain("the batch came back in a different order from the one it was queued in");
        else if (after.Items.Any(item => item.Status != QueueStatus.Pending))
            Complain("an interrupted batch came back as something other than pending work");

        var finished = Finished(after);
        after.Start();
        await finished;

        if (!server.ServedTo(FinishedPath(settings, interrupted)))
            Complain("the book that was interrupted never finished correctly");
        else if (!server.ServedTo(FinishedPath(settings, untouched)))
            Complain("the book that had not started never finished correctly");
        else if (server.RangeRequests == 0)
            Complain("the interrupted book started again from the beginning instead of resuming");

        if (File.Exists(PartialOf(settings, interrupted)))
            Complain("a partial file was left behind after the batch finished");

        // Finished books are not remembered: the file holds what is left to do.
        var afterwards = new DownloadQueue(manager, store);
        if (afterwards.RestoreSaved(id => id == "selftest" ? provider : null) != 0)
            Complain("finished downloads were remembered as work still to do");

        if (wrong == 0)
        {
            line($"   the batch came back and finished; the interrupted book resumed with " +
                 $"{server.RangeRequests} range request(s)");
            line("   nothing was left in the queue file once the batch had finished");
        }

        line("");
    }

    // ------------------------------------------------------------------
    // A cancel
    // ------------------------------------------------------------------

    /// <summary>
    /// A cancelled download is remembered as cancelled, so it does not start itself
    /// again on the next run — while Retry picks it up and resumes it. A failed one is
    /// remembered too, so it can be retried instead of searched for again.
    /// </summary>
    private static async Task CheckCancelIsNotAnAccidentAsync(string folder, Action<string> line, Action<string> fail)
    {
        line("== A cancel is an instruction ==");

        var settings = Settings(folder);
        var manager = new DownloadManager(settings);
        var store = new QueueStore(Path.Combine(folder, "cancel.json"));

        var wrong = 0;
        void Complain(string message)
        {
            wrong++;
            fail(message);
        }

        using var server = new LoopbackFileServer(Payload) { ChunkSize = 4096, ChunkDelayMs = 20 };
        var provider = new LoopbackProvider(server, "selftest");
        var stopped = Book("c1", "Stopped By Hand");

        var queue = new DownloadQueue(manager, store);
        queue.Enqueue(provider, stopped);
        var finished = Finished(queue);
        queue.Start();

        await WaitUntilAsync(() => File.Exists(PartialOf(settings, stopped)) &&
                                   new FileInfo(PartialOf(settings, stopped)).Length > 0,
                             TimeSpan.FromSeconds(10));

        queue.CancelActive();
        await finished;

        if (queue.Items[0].Status != QueueStatus.Cancelled)
            Complain($"a cancelled download ended up as \"{queue.Items[0].Status}\"");

        // The next run must not start it again by itself.
        var reopened = new DownloadQueue(manager, store);
        var restored = reopened.RestoreSaved(id => id == "selftest" ? provider : null);

        if (restored != 1 || reopened.Items[0].Status != QueueStatus.Cancelled)
            Complain("a download the user cancelled came back as work to be started again");
        else if (reopened.Items[0].Percent != 0)
            Complain("a cancelled download came back part-way through, as if it were still running");

        var beforeRetry = server.RangeRequests;
        var retried = Finished(reopened);
        reopened.Retry(reopened.Items[0]);
        await retried;

        if (reopened.Items[0].Status != QueueStatus.Completed)
            Complain($"retrying a remembered cancel ended as \"{reopened.Items[0].Status}\"");
        else if (!server.ServedTo(FinishedPath(settings, stopped)))
            Complain("retrying a remembered cancel did not produce the file");
        else if (server.RangeRequests <= beforeRetry)
            Complain("retrying a remembered cancel started again instead of resuming");

        // A failure is remembered too, and does not start again by itself.
        var failing = new DownloadQueue(manager, store);
        failing.Enqueue(new RefusingProvider(), Book("f1", "Nowhere To Fetch It From"));
        var failed = Finished(failing);
        failing.Start();
        await failed;

        if (failing.Items[0].Status != QueueStatus.Failed)
            Complain($"a download that could not be fetched ended up as \"{failing.Items[0].Status}\" " +
                     $"(message: \"{failing.Items[0].Message}\")");

        var afterFailure = new DownloadQueue(manager, store);
        var backFromFailure = afterFailure.RestoreSaved(id => id == "selftest" ? provider : null);

        if (backFromFailure != 1 || afterFailure.Items[0].Status != QueueStatus.Failed)
            Complain("a failed download was not remembered, so it could not be retried after a restart");
        else if (string.IsNullOrWhiteSpace(afterFailure.Items[0].Error))
            Complain("a remembered failure came back with no reason for it");

        if (wrong == 0)
        {
            line("   a cancelled download stayed cancelled, and Retry resumed it from where it stopped");
            line("   a failed download was remembered with its reason, ready to be retried");
        }

        line("");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static AppSettings Settings(string folder) => new()
    {
        OutputFolder = Path.Combine(folder, "out"),
    };

    private static BookInfo Book(string id, string title) =>
        new("selftest", id, title, "Author", "", 0, null, null, null);

    private static byte[] CreatePayload()
    {
        var payload = new byte[200_000];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i % 251);
        return payload;
    }

    private static string FinishedPath(AppSettings settings, BookInfo book) =>
        Path.Combine(settings.OutputFolder, DownloadManager.MakeFileName(book) + ".m4a");

    private static string PartialOf(AppSettings settings, BookInfo book) =>
        FinishedPath(settings, book) + DownloadManager.PartExtension;

    /// <summary>
    /// A task that completes when the queue's run comes to an end — whether the run
    /// says so itself or the worker has simply stopped, so a check can never wait
    /// forever on a run that ended without announcing it.
    /// </summary>
    private static Task Finished(DownloadQueue queue)
    {
        var completion = new TaskCompletionSource();

        void OnFinished(QueueFinishedSummary _)
        {
            queue.QueueFinished -= OnFinished;
            completion.TrySetResult();
        }

        queue.QueueFinished += OnFinished;

        _ = Task.Run(async () =>
        {
            // Wait for the run to start — the queue may not have been asked to start
            // yet — and then for it to come to a stop. It never gives up on a run that
            // is still going: a check may wait too long, but it must not mistake work
            // in progress for work that has ended.
            var startBy = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!queue.IsRunning && !completion.Task.IsCompleted && DateTime.UtcNow < startBy)
                await Task.Delay(10);

            while (queue.IsRunning)
                await Task.Delay(10);

            completion.TrySetResult();
        });

        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(20);
        }

        return condition();
    }

    /// <summary>
    /// A provider that hands out loopback tickets, so a whole batch can be downloaded
    /// without the internet. It is deliberately the smallest provider there can be:
    /// what is under test is the queue's own bookkeeping, not any real service.
    /// </summary>
    private sealed class LoopbackProvider(LoopbackFileServer server, string id) : IBookProvider
    {
        public string Id => id;
        public string DisplayName => "Self-test";
        public string Description => "A loopback provider used by the offline checks.";
        public bool RequiresLogin => false;
        public bool HasDrm => false;
        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Disconnect()
        {
        }

        public Task<IReadOnlyList<BookInfo>> GetBooksAsync(string query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BookInfo>>([]);

        public Task<DownloadTicket> GetDownloadTicketAsync(BookInfo book, CancellationToken ct = default) =>
            Task.FromResult(server.TicketFor(book));
    }

    /// <summary>
    /// A provider that refuses to hand out a ticket, the way a service does when a
    /// book's licence is no longer valid. It fails on the spot rather than through a
    /// dropped connection, so the check does not sit through the download engine's
    /// own retries before it can say what happened.
    /// </summary>
    private sealed class RefusingProvider : IBookProvider
    {
        public string Id => "selftest";

        public string DisplayName => "Self-test";

        public string Description => "A loopback provider used by the offline checks.";

        public bool RequiresLogin => false;

        public bool HasDrm => false;

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Disconnect()
        {
        }

        public Task<IReadOnlyList<BookInfo>> GetBooksAsync(string query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BookInfo>>([]);

        public Task<DownloadTicket> GetDownloadTicketAsync(BookInfo book, CancellationToken ct = default) =>
            throw new InvalidOperationException("the licence for this book is no longer valid (self-test)");
    }
}
