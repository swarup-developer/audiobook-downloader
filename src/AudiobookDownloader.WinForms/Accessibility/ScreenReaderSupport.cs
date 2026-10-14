using System.Runtime.InteropServices;
using System.Windows.Forms.Automation;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Raises the same operating-system accessibility events a native Windows app
/// would raise, so whatever screen reader the user runs (NVDA, Narrator,
/// JAWS…) reads the interface by itself. The app never speaks with a voice of
/// its own — this is the supported alternative.
/// </summary>
internal static class ScreenReader
{
    private const uint EventObjectLiveRegionChanged = 0x8016;
    private const uint EventSystemAlert = 0x0002;
    private const int ObjectIdClient = unchecked((int)0xFFFFFFFC);
    private const int ChildIdSelf = 0;

    [DllImport("user32.dll", SetLastError = false)]
    private static extern void NotifyWinEvent(uint eventId, IntPtr hwnd, int objectId, int childId);

    /// <summary>
    /// Tell the screen reader that a live region's content changed. The screen
    /// reader decides whether and when to read it, exactly as it would for a
    /// native Windows app.
    /// </summary>
    public static void RaiseLiveRegionChanged(Control control)
    {
        if (control is null || !control.IsHandleCreated)
            return;

        try
        {
            control.AccessibilityObject.RaiseLiveRegionChanged();
        }
        catch
        {
            // Older screen readers or hosts may not support live regions; the
            // WinEvent path below still works.
            Notify(control, EventObjectLiveRegionChanged);
        }
    }

    /// <summary>Raise a system alert for something the user must hear immediately (an error).</summary>
    public static void RaiseAlert(Control control)
    {
        if (control is null || !control.IsHandleCreated)
            return;

        Notify(control, EventSystemAlert);
    }

    private static void Notify(Control control, uint eventId)
    {
        try { NotifyWinEvent(eventId, control.Handle, ObjectIdClient, ChildIdSelf); }
        catch { /* announcements are best-effort by nature */ }
    }
}

/// <summary>
/// A label exposed as an accessibility live region: when its text changes and
/// <see cref="Announce"/> is called, NVDA and other screen readers read the new
/// text automatically, without any built-in speech.
/// </summary>
internal sealed class LiveRegionLabel : Label
{
    public LiveRegionLabel()
    {
        // Label already implements LiveSetting; Polite waits for a natural pause.
        LiveSetting = AutomationLiveSetting.Polite;
        AccessibleRole = AccessibleRole.StaticText;
    }

    /// <summary>Ask the screen reader to read the current text.</summary>
    public void Announce()
    {
        if (LiveSetting == AutomationLiveSetting.Off)
            return;
        ScreenReader.RaiseLiveRegionChanged(this);
    }
}
