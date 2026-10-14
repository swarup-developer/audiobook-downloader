namespace AudiobookDownloader.WinForms;

/// <summary>Settings, Accessibility: what the screen reader is told, and the app's sounds.</summary>
internal sealed partial class SettingsForm
{
    private TabPage BuildAccessibilityPage()
    {
        var page = NewPage(Strings.T("Settings.Accessibility"));
        var section = new SettingsSection(page, Announce);

        section.Group(Strings.T("Group.Announcements"));
        var status = section.AddCheck(Strings.T("Set.AnnounceStatus"), _settings.AnnounceStatusChanges,
            "Announce status changes through the screen reader");
        _apply.Add(() => _settings.AnnounceStatusChanges = status.Checked);

        var progress = section.AddCheck(Strings.T("Set.AnnounceProgress"), _settings.AnnounceProgress,
            "Announce download progress milestones");
        _apply.Add(() => _settings.AnnounceProgress = progress.Checked);

        var chapters = section.AddCheck(Strings.T("Set.AnnounceChapters"), _settings.AnnounceChapters,
            "Say each chapter's name when the player reaches it");
        _apply.Add(() => _settings.AnnounceChapters = chapters.Checked);

        section.Group(Strings.T("Group.Sounds"));
        var beeps = section.AddCheck(Strings.T("Set.ProgressBeeps"), _settings.ProgressBeeps,
            "Beep as a download progresses");
        _apply.Add(() => _settings.ProgressBeeps = beeps.Checked);

        section.AddNote(Strings.T("Set.AccessibilityNote"));
        return page;
    }
}
