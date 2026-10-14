using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Settings, Downloads: files and folders, backup and speed. The format is asked
/// for when Download is pressed.
/// </summary>
internal sealed partial class SettingsForm
{
    private TabPage BuildDownloadsPage()
    {
        var page = NewPage(Strings.T("Settings.Downloads"));
        var section = new SettingsSection(page, Announce);

        section.Group(Strings.T("Group.Files"));
        var byAuthor = section.AddCheck(Strings.T("Set.OrganizeByAuthor"), _settings.OrganizeByAuthor,
            "Save each book in a folder named after its author");
        _apply.Add(() => _settings.OrganizeByAuthor = byAuthor.Checked);

        var duplicates = section.AddCheck(Strings.T("Set.WarnDuplicates"), _settings.WarnAboutDuplicates,
            "Ask before downloading a book that is already downloaded");
        _apply.Add(() => _settings.WarnAboutDuplicates = duplicates.Checked);

        var delete = section.AddCheck(Strings.T("Set.DeleteIntermediate"), _settings.DeleteIntermediateFile,
            "Delete the encrypted original after decryption");
        _apply.Add(() => _settings.DeleteIntermediateFile = delete.Checked);

        var embed = section.AddCheck(Strings.T("Set.EmbedInfo"), _settings.EmbedBookInfo,
            "Write the description, series and cover into the book file");
        _apply.Add(() => _settings.EmbedBookInfo = embed.Checked);

        var cover = section.AddCheck(Strings.T("Set.SaveCover"), _settings.SaveCoverImage,
            "Save the cover picture next to each book");
        _apply.Add(() => _settings.SaveCoverImage = cover.Checked);

        // Every finished book is copied here too. A OneDrive folder is uploaded to
        // the cloud by OneDrive itself.
        section.Group(Strings.T("Group.Backup"));
        var backup = section.AddCheck(Strings.T("Set.Backup"), _settings.BackupEnabled,
            "Copy every finished book to a backup folder");
        var backupFolder = NewTextBox(
            string.IsNullOrWhiteSpace(_settings.BackupFolder) ? DownloadManager.SuggestedBackupFolder() ?? "" : _settings.BackupFolder,
            Strings.Plain(Strings.T("Set.BackupFolder")).TrimEnd(':'));
        section.AddLabeled(Strings.T("Set.BackupFolder"), backupFolder);
        section.AddControl(BrowseButton("ChooseBackupFolder", Strings.T("Set.ChooseBackupFolder"), backupFolder, newFolder: true));
        _apply.Add(() =>
        {
            _settings.BackupEnabled = backup.Checked;
            _settings.BackupFolder = backupFolder.Text.Trim();
        });

        section.Group(Strings.T("Group.Speed"));
        string[] parallel = ["1", "2", "3", "4"];
        var parallelCombo = NewCombo(parallel, Math.Clamp(_settings.MaxParallelDownloads, 1, 4).ToString(),
            Strings.Plain(Strings.T("Set.Parallel")).TrimEnd(':'));
        section.AddLabeled(Strings.T("Set.Parallel"), parallelCombo);
        _apply.Add(() => _settings.MaxParallelDownloads = parallelCombo.SelectedIndex + 1);

        int[] limits = [0, 256, 512, 1024, 2048, 5120, 10240];
        string LimitText(int kb) => kb == 0 ? Strings.T("Speed.None")
            : kb >= 1024 ? $"{kb / 1024} MB/s" : $"{kb} KB/s";
        var current = limits.Contains(_settings.SpeedLimitKBps) ? _settings.SpeedLimitKBps : 0;
        var speedCombo = NewCombo([.. limits.Select(LimitText)], LimitText(current),
            Strings.Plain(Strings.T("Set.SpeedLimit")).TrimEnd(':'));
        section.AddLabeled(Strings.T("Set.SpeedLimit"), speedCombo);
        _apply.Add(() => _settings.SpeedLimitKBps = limits[Math.Max(0, speedCombo.SelectedIndex)]);

        section.AddNote(Strings.T("Downloads.Note"));
        return page;
    }
}
