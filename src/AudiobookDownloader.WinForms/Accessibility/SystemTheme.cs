using Microsoft.Win32;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Keeps the few colours the app chooses for itself in step with Windows —
/// including high contrast, which is simply a theme the user selects. Controls
/// left at their default colours already follow it; anything coloured
/// explicitly goes through here and is refreshed when the theme changes, so
/// turning high contrast on does not need a restart.
/// </summary>
internal static class SystemTheme
{
    /// <summary>Quiet text for hints. High contrast has no muted shade, so it uses normal text.</summary>
    public static Color HintText =>
        SystemInformation.HighContrast ? SystemColors.WindowText : SystemColors.GrayText;

    /// <summary>Background for a read-only text area such as the activity log.</summary>
    public static Color ReadOnlyBackground => SystemColors.Window;

    /// <summary>Background of the strip shown when every download has finished.</summary>
    public static Color NoticeBackground => SystemColors.Info;

    /// <summary>Text colour of the finished-downloads strip.</summary>
    public static Color NoticeText => SystemColors.InfoText;

    /// <summary>True while Windows is running a high-contrast theme.</summary>
    public static bool HighContrast => SystemInformation.HighContrast;

    /// <summary>
    /// Apply the colours now, then again whenever Windows changes its colours or
    /// accessibility settings. The handler is released when the form closes, so
    /// a dialog going away cannot leave one behind.
    /// </summary>
    public static void Follow(Form form, Action apply)
    {
        apply();

        void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category is not (UserPreferenceCategory.Color
                or UserPreferenceCategory.Accessibility
                or UserPreferenceCategory.General
                or UserPreferenceCategory.VisualStyle
                or UserPreferenceCategory.Window))
                return;

            // The event can arrive on another thread, and the form may be closing.
            if (form.IsDisposed || !form.IsHandleCreated)
                return;

            try
            {
                form.BeginInvoke(apply);
            }
            catch (InvalidOperationException)
            {
                // The window closed between the check above and this call, which
                // surfaces as InvalidOperationException or ObjectDisposedException.
            }
        }

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        form.FormClosed += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
