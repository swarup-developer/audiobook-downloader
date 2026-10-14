using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>Settings, General: where books go, what happens when they are done, and the app itself.</summary>
internal sealed partial class SettingsForm
{
    private TabPage BuildGeneralPage()
    {
        var page = NewPage(Strings.T("Settings.General"));
        var section = new SettingsSection(page, Announce);

        section.Group(Strings.T("Group.DownloadFolder"));
        var outputBox = NewTextBox(_settings.OutputFolder, "Download folder");
        section.AddLabeled(Strings.T("Set.OutputFolder"), outputBox);
        section.AddControl(BrowseButton("ChooseFolder", Strings.T("Set.ChooseFolder"), outputBox, newFolder: true));
        _apply.Add(() => _settings.OutputFolder = outputBox.Text.Trim());

        section.Group(Strings.T("Group.WhenFinished"));
        var open = section.AddCheck(Strings.T("Set.OpenWhenDone"), _settings.OpenFolderWhenFinished,
            "Open download folder when finished");
        _apply.Add(() => _settings.OpenFolderWhenFinished = open.Checked);

        var notify = section.AddCheck(Strings.T("Set.Notify"), _settings.NotifyEachDownload,
            "Windows notification, with its sound, when a book has downloaded");
        _apply.Add(() => _settings.NotifyEachDownload = notify.Checked);

        section.Group(Strings.T("Group.App"));
        var remember = section.AddCheck(Strings.T("Set.RememberProvider"), _settings.RememberLastProvider,
            "Remember the last provider");
        _apply.Add(() => _settings.RememberLastProvider = remember.Checked);

        var updates = section.AddCheck(Strings.T("Set.CheckUpdates"), _settings.CheckForUpdates,
            "Check for updates when the app starts");
        _apply.Add(() => _settings.CheckForUpdates = updates.Checked);

        section.AddNote(string.Format(Strings.T("Set.DataFolder"), AppSettings.SettingsFolder));
        return page;
    }
}
