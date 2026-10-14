using AudiobookDownloader.Core;
using System.Reflection;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// About dialog, reachable from the application menu (Alt, then Help, then About)
/// and fully keyboard accessible: the text sits in one read-only, scrollable box
/// so a screen reader can read it line by line with the arrow keys.
/// </summary>
internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        Text = Strings.T("About.Title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 400);
        AutoScaleMode = AutoScaleMode.Font;
        Strings.ApplyDirection(this);

        var heading = new Label
        {
            Text = Strings.T("App.Title"),
            Font = new Font(Font.FontFamily, Font.Size + 3f, FontStyle.Bold),
            Location = new Point(16, 16),
            AutoSize = true,
            AccessibleName = Strings.T("About.Heading"),
            AccessibleRole = AccessibleRole.StaticText,
            TabStop = false,
        };
        Controls.Add(heading);

        var details = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            // No explicit background: a read-only text box already uses the Windows
            // window colour, and leaving it alone lets it follow a theme change.
            Location = new Point(16, 52),
            Size = new Size(528, 290),
            TabIndex = 0,
            Text = string.Join(Environment.NewLine,
            [
                string.Format(Strings.T("About.Version"), version),
                "",
                Strings.T("About.Summary"),
                "",
                Strings.T("About.Providers"),
                "  • Audible — your own library, DRM removed for personal backups.",
                "  • LibriVox — free public-domain audiobooks.",
                "  • Internet Archive — free audio collections.",
                "  • Project Gutenberg — free books in many languages.",
                "  • Local files — convert AAX and AAXC files you already own.",
                "",
                Strings.T("About.Accessibility"),
                "",
                Strings.T("About.SettingsFolder"),
                "  " + AppSettings.SettingsFolder,
                "",
                Strings.T("About.Licence"),
                "",
                "GNU AGPL 3.0. Source code:",
                "  https://github.com/" + UpdateChecker.Repository,
            ]),
            AccessibleName = Strings.T("About.Text"),
            AccessibleRole = AccessibleRole.Text,
        };
        Controls.Add(details);

        var close = new Button
        {
            Text = Strings.T("Common.Close"),
            DialogResult = DialogResult.Cancel,
            Location = new Point(456, 352),
            Size = new Size(88, 30),
            TabIndex = 1,
        };
        close.AccessibleName = Strings.T("About.Close");
        Controls.Add(close);

        AcceptButton = close;
        CancelButton = close;
        ActiveControl = details;
    }
}
