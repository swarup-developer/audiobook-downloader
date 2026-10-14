using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // AudiobookDownloader.WinForms.exe --self-test [report-path]
        // Constructs every dialog and checks its accessibility coverage, then
        // exits with 0 (all good) or 1 (problems found). Nothing is downloaded.
        // Adding --self-test-live also reads the real LibriVox catalogue.
        var live = args.Any(a => a.Equals("--self-test-live", StringComparison.OrdinalIgnoreCase));
        if (live || args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.Run(
                args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)), live);

        InstallCrashHandlers();

        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>
    /// A failure that reaches these handlers is a bug, but it must never take the
    /// app down silently mid-download. Everything is written to the log, and the
    /// user gets one plain message telling them what failed and where the log is.
    /// </summary>
    private static void InstallCrashHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report("unexpected error", e.Exception, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report("unexpected error", e.ExceptionObject as Exception, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error($"Background task failed: {e.Exception}");
            e.SetObserved();
        };
    }

    private static void Report(string what, Exception? ex, bool fatal)
    {
        var detail = ex?.ToString() ?? "(no details available)";
        try { Log.Error($"Unhandled {what}: {detail}"); } catch { /* logging must never throw here */ }

        // AppDomain handlers can fire on a background thread; only touch the UI
        // from the thread that owns it.
        var owner = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
        if (owner is { IsHandleCreated: true } && owner.InvokeRequired)
        {
            try { owner.Invoke(() => ShowMessage(owner, ex)); }
            catch { /* the app is going down; the log entry above is the record */ }
            return;
        }

        try { ShowMessage(owner, ex); }
        catch { /* nothing more we can safely do */ }
    }

    private static void ShowMessage(IWin32Window? owner, Exception? ex)
    {
        var text = string.Format(Strings.T("Fatal.Message"),
            ex?.GetType().Name ?? "Error",
            ex?.Message ?? Strings.T("Fatal.NoDetails"),
            Log.FilePath);

        MessageBox.Show(owner, text, Strings.T("Fatal.Title"),
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
