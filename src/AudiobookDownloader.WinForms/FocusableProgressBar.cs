namespace AudiobookDownloader.WinForms;

/// <summary>
/// A progress bar that can take focus. Pressing Download moves focus here, so the
/// screen reader reads the download's progress as it goes (NVDA beeps for a focused
/// progress bar on its own), and Tab reaches it like any other control.
/// </summary>
internal sealed class FocusableProgressBar : ProgressBar
{
    public FocusableProgressBar()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    /// <summary>A focus rectangle, so sighted keyboard users can see where focus is.</summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        const int WM_PAINT = 0x000F;
        if (m.Msg == WM_PAINT && Focused)
        {
            using var g = CreateGraphics();
            ControlPaint.DrawFocusRectangle(g, new Rectangle(1, 1, Width - 2, Height - 2));
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}
