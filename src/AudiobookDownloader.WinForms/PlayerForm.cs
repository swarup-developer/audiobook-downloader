using AudiobookDownloader.Core;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// A small audiobook player: play and pause, jump back and forward 30 seconds,
/// move between chapters, change the speed, set a sleep timer, keep bookmarks with
/// notes, skip long silences, and pick up where you stopped last time. Built on the
/// Windows media player, so it plays M4B, M4A and MP3 without anything extra installed.
/// </summary>
/// <remarks>
/// Everything is a named control with a mnemonic, and the keys that matter while
/// listening work from anywhere in the window:
/// Space (on the position slider) or the Play/Pause media key, Ctrl + Left and
/// Ctrl + Right to jump 30 seconds, Ctrl + Shift + Left and Right for the previous
/// and next chapter, Ctrl + Up and Down for the speed, Ctrl + T to hear the time,
/// Ctrl + B to add a bookmark and Ctrl + Shift + B to see them.
///
/// A chapter is a chapter file in a folder of them, or a chapter inside a single
/// M4B; either way the player moves between them the same way and says each one's
/// name when it starts.
/// </remarks>
internal sealed class PlayerForm : Form
{
    private static readonly double[] Speeds = [0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0];

    /// <summary>Sleep timer choices in minutes; -1 is "at the end of the chapter".</summary>
    private static readonly int[] SleepChoices = [0, 5, 10, 15, 30, 45, 60, 90, -1];

    /// <summary>The kinds of file the player opens.</summary>
    public static readonly string[] AudioExtensions = [".m4b", ".m4a", ".mp3", ".aac", ".ogg", ".wav", ".flac", ".wma"];

    private static readonly TimeSpan Jump = TimeSpan.FromSeconds(30);

    /// <summary>Silences shorter than this are left alone; longer ones are jumped over.</summary>
    private static readonly TimeSpan LongSilence = TimeSpan.FromSeconds(1.2);

    /// <summary>Silence found in each file, kept for the session so it is worked out once.</summary>
    private static readonly Dictionary<string, List<(TimeSpan Start, TimeSpan End)>> SilenceCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly AppSettings _settings;
    private readonly string _path;
    private readonly IReadOnlyList<string> _tracks;

    private readonly Label _titleLabel;
    private readonly Label _trackLabel;
    private readonly Label _positionLabel;
    private readonly TrackBar _seek;
    private readonly Button _play;
    private readonly Button _back;
    private readonly Button _forward;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _addBookmark;
    private readonly Button _bookmarks;
    private readonly ComboBox _speed;
    private readonly ComboBox _sleep;
    private readonly CheckBox _skipSilence;
    private readonly Button _close;
    private readonly LiveRegionLabel _status;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _fastTick = new() { Interval = 150 };

    private MediaPlayer? _player;
    private MediaPlaybackList? _list;
    private bool _restorePending = true;
    private int _ticksSinceSave;
    private int _currentTrack;

    /// <summary>Chapters inside the file being played, when it is one M4B with several.</summary>
    private IReadOnlyList<(string Title, TimeSpan Start)> _inFileChapters = [];
    private int _currentChapter = -1;

    private DateTime? _sleepAt;
    private bool _sleepAtChapterEnd;

    /// <summary>The position slider, play button and status, for the self-test.</summary>
    internal Button PlayButton => _play;

    internal IReadOnlyList<string> Tracks => _tracks;

    /// <summary>Plays silently; the self-test uses it so checking the player makes no sound.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Muted { get; init; }

    internal bool IsPlaying => _player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    internal TimeSpan Position => _player?.PlaybackSession.Position ?? TimeSpan.Zero;

    internal string StatusText => _status.Text;

    /// <summary>The chapters found inside the file, for the self-test.</summary>
    internal int InFileChapterCount => _inFileChapters.Count;

    /// <summary>The audio files a book consists of: the file itself, or a chapter folder's files in order.</summary>
    public static IReadOnlyList<string> TracksIn(string path)
    {
        if (File.Exists(path))
            return AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) ? [path] : [];

        if (!Directory.Exists(path))
            return [];

        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public PlayerForm(AppSettings settings, string path, string title)
    {
        _settings = settings;
        _path = path;
        _tracks = TracksIn(path);

        Text = string.Format(Strings.T("Player.Title"), title);
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = true;
        MaximizeBox = false;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Font;
        Strings.ApplyDirection(this);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;

        _titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Font = new Font(Font.FontFamily, Font.Size + 2f, FontStyle.Bold),
            AccessibleName = Strings.T("Player.BookTitle"),
            AccessibleRole = AccessibleRole.StaticText,
        };

        _trackLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Visible = _tracks.Count > 1,
            AccessibleName = Strings.T("Player.Chapter"),
            AccessibleRole = AccessibleRole.StaticText,
        };

        _positionLabel = new Label
        {
            AutoSize = true,
            Text = "0:00",
            AccessibleName = Strings.T("Player.Time"),
            AccessibleRole = AccessibleRole.StaticText,
        };

        _seek = new TrackBar
        {
            Minimum = 0,
            Maximum = 1,
            TickStyle = TickStyle.None,
            SmallChange = 10,
            LargeChange = 60,
            Width = 600,
            TabIndex = 0,
            AccessibleName = Strings.T("Player.Position"),
        };
        _seek.Scroll += (_, _) => SeekTo(TimeSpan.FromSeconds(_seek.Value));

        _play = NewButton("Player.Play", 1, TogglePlay);
        _back = NewButton("Player.Back", 2, () => SkipBy(-Jump));
        _forward = NewButton("Player.Forward", 3, () => SkipBy(Jump));
        _previous = NewButton("Player.Previous", 4, () => MoveChapter(-1));
        _next = NewButton("Player.Next", 5, () => MoveChapter(+1));
        _previous.Visible = _next.Visible = _tracks.Count > 1;
        _addBookmark = NewButton("Player.AddBookmark", 6, AddBookmark);
        _bookmarks = NewButton("Player.Bookmarks", 7, ShowBookmarks);

        _speed = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 90,
            TabIndex = 9,
            AccessibleName = Strings.Plain(Strings.T("Player.Speed")).TrimEnd(':'),
            AccessibleRole = AccessibleRole.ComboBox,
        };
        foreach (var speed in Speeds)
            _speed.Items.Add($"{speed:0.##}x");
        _speed.SelectedIndex = NearestSpeedIndex(settings.PlaybackSpeed);
        _speed.SelectedIndexChanged += (_, _) => ApplySpeed();

        _sleep = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190,
            TabIndex = 11,
            AccessibleName = Strings.Plain(Strings.T("Player.Sleep")).TrimEnd(':'),
            AccessibleRole = AccessibleRole.ComboBox,
        };
        foreach (var minutes in SleepChoices)
            _sleep.Items.Add(minutes switch
            {
                0 => Strings.T("Player.SleepOff"),
                -1 => Strings.T("Player.SleepChapter"),
                _ => string.Format(Strings.T("Player.SleepMinutes"), minutes),
            });
        _sleep.SelectedIndex = 0;
        _sleep.SelectionChangeCommitted += (_, _) => SetSleepTimer();
        _sleep.DropDownClosed += (_, _) => SetSleepTimer();
        _sleep.Leave += (_, _) => SetSleepTimer();

        _skipSilence = new CheckBox
        {
            Text = Strings.T("Player.SkipSilence"),
            AutoSize = true,
            TabIndex = 12,
            Checked = settings.SkipSilence,
            AccessibleName = Strings.Plain(Strings.T("Player.SkipSilence")),
        };
        _skipSilence.CheckedChanged += (_, _) => ToggleSkipSilence();

        _close = new Button
        {
            Text = Strings.T("Common.Close"),
            AutoSize = true,
            TabIndex = 13,
            DialogResult = DialogResult.Cancel,
            AccessibleName = Strings.Plain(Strings.T("Common.Close")),
        };
        CancelButton = _close;

        _status = new LiveRegionLabel
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Text = Strings.T("Player.KeysHint"),
            AccessibleName = Strings.T("Player.Status"),
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.AddRange([_play, _back, _forward, _previous, _next, _addBookmark, _bookmarks]);

        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        options.Controls.Add(new Label { Text = Strings.T("Player.Speed"), AutoSize = true, TabIndex = 8, Margin = new Padding(3, 7, 3, 3) });
        options.Controls.Add(_speed);
        options.Controls.Add(new Label { Text = Strings.T("Player.Sleep"), AutoSize = true, TabIndex = 10, Margin = new Padding(12, 7, 3, 3) });
        options.Controls.Add(_sleep);
        options.Controls.Add(_skipSilence);
        options.Controls.Add(_close);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var control in new Control[] { _titleLabel, _trackLabel, _positionLabel, _seek, buttons, options, _status })
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control);
        }
        Controls.Add(layout);

        _tick.Tick += (_, _) => OnTick();
        _fastTick.Tick += (_, _) => JumpOverSilence();
        Shown += (_, _) => Open();
        FormClosing += (_, _) => Shutdown();
        ActiveControl = _play;
    }

    private Button NewButton(string key, int tabIndex, Action action)
    {
        var button = new Button
        {
            Text = Strings.T(key),
            AutoSize = true,
            TabIndex = tabIndex,
            AccessibleName = Strings.Plain(Strings.T(key)).TrimEnd('…'),
            AccessibleRole = AccessibleRole.PushButton,
        };
        button.Click += (_, _) => action();
        return button;
    }

    // ------------------------------------------------------------------
    // Opening and closing
    // ------------------------------------------------------------------

    private void Open()
    {
        if (_tracks.Count == 0)
        {
            SetStatus(string.Format(Strings.T("Player.Failed"), Strings.T("Player.NoAudio")));
            return;
        }

        try
        {
            _player = new MediaPlayer { AutoPlay = false, IsMuted = Muted };
            _player.MediaOpened += (_, _) => OnUi(OnOpened);
            _player.MediaEnded += (_, _) => OnUi(OnEnded);
            _player.MediaFailed += (_, e) => OnUi(() => SetStatus(string.Format(Strings.T("Player.Failed"), e.ErrorMessage)));
            _player.PlaybackSession.PlaybackStateChanged += (_, _) => OnUi(UpdatePlayButton);

            if (_tracks.Count == 1)
            {
                _player.Source = MediaSource.CreateFromUri(new Uri(_tracks[0]));
                LoadInFileChapters(_tracks[0]);
            }
            else
            {
                _list = new MediaPlaybackList();
                foreach (var track in _tracks)
                    _list.Items.Add(new MediaPlaybackItem(MediaSource.CreateFromUri(new Uri(track))));

                _currentTrack = Math.Clamp(SavedTrack(), 0, _tracks.Count - 1);
                _list.StartingItem = _list.Items[_currentTrack];
                _list.CurrentItemChanged += (_, _) => OnUi(OnTrackChanged);
                _player.Source = _list;
            }

            SetStatus(Strings.T("Player.Opening"));
            UpdateTrackLabel();

            if (_skipSilence.Checked)
                StartSilenceSkipping();
        }
        catch (Exception ex)
        {
            Log.Error($"Player: could not open {_path}: {ex.Message}");
            SetStatus(string.Format(Strings.T("Player.Failed"), ex.Message));
        }
    }

    /// <summary>
    /// Reads the chapter list inside a single M4B or M4A, in the background, so the
    /// previous and next chapter buttons work inside it and each chapter is named.
    /// </summary>
    private void LoadInFileChapters(string file)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension is not (".m4b" or ".m4a"))
            return;

        Task.Run(() =>
        {
            var chapters = Mp4Chapters.Read(file);
            if (chapters.Count < 2)
                return;

            OnUi(() =>
            {
                _inFileChapters = chapters;
                _previous.Visible = _next.Visible = true;
                _trackLabel.Visible = true;
                UpdateChapterFromPosition(announce: false);
            });
        });
    }

    private void OnOpened()
    {
        if (_player is null)
            return;

        ApplySpeed(save: false);
        UpdateDuration();

        // Only the first file opened resumes; later chapters start at their beginning.
        if (_restorePending)
        {
            _restorePending = false;
            var saved = TimeSpan.FromSeconds(_settings.PlaybackPositions.GetValueOrDefault(_path));
            var length = _player.PlaybackSession.NaturalDuration;
            if (saved > TimeSpan.FromSeconds(5) && (length == TimeSpan.Zero || saved < length - TimeSpan.FromSeconds(5)))
            {
                _player.PlaybackSession.Position = saved;
                SetStatus(string.Format(Strings.T("Player.Resumed"), Clock(saved)));
            }
            else
            {
                SetStatus(Strings.T("Player.Playing"));
            }
        }

        _player.Play();
        _tick.Start();
    }

    private void OnTrackChanged()
    {
        if (_list?.CurrentItem is not { } item)
            return;

        _currentTrack = Math.Max(0, _list.Items.IndexOf(item));
        UpdateTrackLabel();
        UpdateDuration();

        if (_sleepAtChapterEnd)
        {
            SleepNow();
            return;
        }

        if (_settings.AnnounceChapters)
            SetStatus(_trackLabel.Text);
    }

    private void OnEnded()
    {
        // The whole book has been heard, so next time it starts from the beginning,
        // and it counts as finished in the listening statistics.
        _settings.PlaybackPositions.Remove(_path);
        _settings.PlaybackPositions.Remove(_path + "|track");
        _settings.FinishedBooks[_path] = DateTime.Now.ToString("yyyy-MM-dd");
        _settings.Save();
        SetStatus(Strings.T("Player.Ended"));
        UpdatePlayButton();
    }

    private void Shutdown()
    {
        _tick.Stop();
        _fastTick.Stop();
        SavePosition();

        try { _player?.Pause(); } catch { /* closing anyway */ }
        _player?.Dispose();
        _player = null;
        _tick.Dispose();
        _fastTick.Dispose();
    }

    // ------------------------------------------------------------------
    // Controls
    // ------------------------------------------------------------------

    private void TogglePlay()
    {
        if (_player is null)
            return;

        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            _player.Pause();
            SavePosition();
            SetStatus(string.Format(Strings.T("Player.PausedAt"), Clock(_player.PlaybackSession.Position)));
        }
        else
        {
            _player.Play();
            SetStatus(Strings.T("Player.Playing"));
        }
    }

    private void SkipBy(TimeSpan amount)
    {
        if (_player is null)
            return;

        var session = _player.PlaybackSession;
        var target = session.Position + amount;
        if (target < TimeSpan.Zero)
            target = TimeSpan.Zero;
        if (session.NaturalDuration > TimeSpan.Zero && target > session.NaturalDuration)
            target = session.NaturalDuration - TimeSpan.FromSeconds(1);

        SeekTo(target);
        SetStatus(Clock(target));
    }

    private void SeekTo(TimeSpan position)
    {
        if (_player is null)
            return;

        _player.PlaybackSession.Position = position;
        UpdatePosition();
        UpdateChapterFromPosition(announce: false);
    }

    /// <summary>Previous or next chapter: inside the file when it has chapters, else the next file.</summary>
    private void MoveChapter(int step)
    {
        if (_player is null)
            return;

        if (_inFileChapters.Count > 1)
        {
            var position = _player.PlaybackSession.Position;
            var current = ChapterAt(position);
            int target;
            if (step < 0)
                // Like any player: more than a few seconds in, "previous" restarts the chapter.
                target = current >= 0 && position - _inFileChapters[current].Start > TimeSpan.FromSeconds(5) ? current : current - 1;
            else
                target = current + 1;

            if (target < 0 || target >= _inFileChapters.Count)
                return;

            SeekTo(_inFileChapters[target].Start);
            _currentChapter = target;
            UpdateTrackLabel();
            SetStatus(_trackLabel.Text);
            return;
        }

        if (_list is null)
            return;

        if (step < 0)
        {
            if (_player.PlaybackSession.Position > TimeSpan.FromSeconds(5) || _currentTrack == 0)
            {
                SeekTo(TimeSpan.Zero);
                SetStatus(_trackLabel.Text);
                return;
            }

            _list.MovePrevious();
        }
        else if (_currentTrack < _list.Items.Count - 1)
        {
            _list.MoveNext();
        }
    }

    private void ApplySpeed(bool save = true)
    {
        var speed = Speeds[Math.Clamp(_speed.SelectedIndex, 0, Speeds.Length - 1)];
        _settings.PlaybackSpeed = speed;

        if (_player is not null)
            _player.PlaybackSession.PlaybackRate = speed;

        if (save)
            _settings.Save();
    }

    private void ChangeSpeed(int step)
    {
        var index = Math.Clamp(_speed.SelectedIndex + step, 0, Speeds.Length - 1);
        if (index == _speed.SelectedIndex)
            return;

        _speed.SelectedIndex = index;   // ApplySpeed runs from the list's own event
        SetStatus(string.Format(Strings.T("Player.SpeedNow"), _speed.SelectedItem));
    }

    // ------------------------------------------------------------------
    // Sleep timer
    // ------------------------------------------------------------------

    /// <summary>
    /// Takes the sleep choice once it has been made (not on every arrow press), and
    /// says when playback will stop.
    /// </summary>
    private void SetSleepTimer()
    {
        var minutes = SleepChoices[Math.Clamp(_sleep.SelectedIndex, 0, SleepChoices.Length - 1)];
        var wasSet = _sleepAt is not null || _sleepAtChapterEnd;

        _sleepAtChapterEnd = minutes == -1;
        _sleepAt = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : null;

        if (minutes == 0)
        {
            if (wasSet)
                SetStatus(Strings.T("Player.SleepCancelled"));
        }
        else if (minutes == -1)
        {
            SetStatus(Strings.T("Player.SleepAtChapter"));
        }
        else
        {
            SetStatus(string.Format(Strings.T("Player.SleepSet"), minutes, _sleepAt!.Value.ToString("t")));
        }
    }

    private void SleepNow()
    {
        _sleepAt = null;
        _sleepAtChapterEnd = false;
        _sleep.SelectedIndex = 0;

        _player?.Pause();
        SavePosition();
        SetStatus(string.Format(Strings.T("Player.SleepDone"), Clock(Position)));
    }

    // ------------------------------------------------------------------
    // Bookmarks
    // ------------------------------------------------------------------

    private List<Bookmark> BookmarkList()
    {
        if (!_settings.Bookmarks.TryGetValue(_path, out var list))
            _settings.Bookmarks[_path] = list = [];
        return list;
    }

    /// <summary>Marks the current place, with a note if one is typed. Ctrl + B.</summary>
    private void AddBookmark()
    {
        if (_player is null)
            return;

        var position = Position;
        var note = TextPrompt.Ask(this, Strings.T("Bookmark.AddTitle"),
            string.Format(Strings.T("Bookmark.NoteLabel"), Clock(position)), "");
        if (note is null)
            return;

        BookmarkList().Add(new Bookmark { Track = _currentTrack, Seconds = Math.Round(position.TotalSeconds, 1), Note = note.Trim() });
        _settings.Save();
        SetStatus(string.Format(Strings.T("Bookmark.Added"), Clock(position)));
    }

    /// <summary>The bookmarks of this book: go to one, or delete it. Ctrl + Shift + B.</summary>
    private void ShowBookmarks()
    {
        var list = BookmarkList();
        if (list.Count == 0)
        {
            SetStatus(Strings.T("Bookmark.None"));
            return;
        }

        using var dialog = new BookmarksForm(list, _tracks.Count > 1 ? _tracks : null);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Chosen is { } chosen)
            GoTo(chosen);

        _settings.Save();
    }

    private void GoTo(Bookmark mark)
    {
        if (_player is null)
            return;

        if (_list is not null && mark.Track != _currentTrack && mark.Track < _list.Items.Count)
        {
            _list.MoveTo((uint)mark.Track);
            _currentTrack = mark.Track;
        }

        SeekTo(TimeSpan.FromSeconds(mark.Seconds));
        _player.Play();
        SetStatus(string.Format(Strings.T("Bookmark.Went"), Clock(TimeSpan.FromSeconds(mark.Seconds)),
            string.IsNullOrWhiteSpace(mark.Note) ? "" : mark.Note));
    }

    // ------------------------------------------------------------------
    // Skipping silence
    // ------------------------------------------------------------------

    private void ToggleSkipSilence()
    {
        _settings.SkipSilence = _skipSilence.Checked;
        _settings.Save();

        if (_skipSilence.Checked)
            StartSilenceSkipping();
        else
            _fastTick.Stop();
    }

    /// <summary>
    /// Finds the long silences in each file once, in the background, then jumps over
    /// them while playing. Only M4B and M4A files can be read this way.
    /// </summary>
    private void StartSilenceSkipping()
    {
        var readable = _tracks.Where(t => Path.GetExtension(t).ToLowerInvariant() is ".m4b" or ".m4a").ToList();
        if (readable.Count == 0)
        {
            SetStatus(Strings.T("Player.SilenceUnsupported"));
            return;
        }

        var missing = readable.Where(t => { lock (SilenceCache) return !SilenceCache.ContainsKey(t); }).ToList();
        if (missing.Count > 0)
            SetStatus(Strings.T("Player.SilenceFinding"));

        Task.Run(() =>
        {
            var total = 0;
            foreach (var file in missing)
            {
                var spans = FindSilences(file);
                lock (SilenceCache) SilenceCache[file] = spans;
                total += spans.Count;
            }

            OnUi(() =>
            {
                if (missing.Count > 0)
                    SetStatus(string.Format(Strings.T("Player.SilenceFound"), total));
                if (_skipSilence.Checked)
                    _fastTick.Start();
            });
        });
    }

    /// <summary>The silences in one file that are long enough to skip.</summary>
    internal static List<(TimeSpan Start, TimeSpan End)> FindSilences(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var mp4 = new AAXClean.Mp4File(stream, stream.Length);
            var detect = AAXClean.Codecs.Mp4FileExtensions.DetectSilenceAsync(mp4, -35, LongSilence, null);
            detect.Start();
            var found = detect.OperationTask.GetAwaiter().GetResult();
            return found.Select(s => (s.SilenceStart, s.SilenceEnd)).ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"Player: silences could not be found in {Path.GetFileName(file)}: {ex.Message}");
            return [];
        }
    }

    private void JumpOverSilence()
    {
        if (_player is null || !IsPlaying || !_skipSilence.Checked)
            return;

        var file = _tracks[Math.Clamp(_currentTrack, 0, _tracks.Count - 1)];
        List<(TimeSpan Start, TimeSpan End)>? spans;
        lock (SilenceCache)
            SilenceCache.TryGetValue(file, out spans);
        if (spans is null)
            return;

        var position = Position;
        foreach (var (start, end) in spans)
        {
            // A short pause is kept at each end, so speech never sounds cut off.
            if (position >= start + TimeSpan.FromSeconds(0.3) && position < end - TimeSpan.FromSeconds(0.4))
            {
                _player.PlaybackSession.Position = end - TimeSpan.FromSeconds(0.25);
                return;
            }
        }
    }

    // ------------------------------------------------------------------
    // Keys
    // ------------------------------------------------------------------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.MediaPlayPause:
            case Keys.Space when ActiveControl == _seek:
                TogglePlay();
                return true;
            case Keys.Control | Keys.Left:
                SkipBy(-Jump);
                return true;
            case Keys.Control | Keys.Right:
                SkipBy(Jump);
                return true;
            case Keys.Control | Keys.Shift | Keys.Left:
            case Keys.MediaPreviousTrack:
                MoveChapter(-1);
                return true;
            case Keys.Control | Keys.Shift | Keys.Right:
            case Keys.MediaNextTrack:
                MoveChapter(+1);
                return true;
            case Keys.Control | Keys.Up:
                ChangeSpeed(+1);
                return true;
            case Keys.Control | Keys.Down:
                ChangeSpeed(-1);
                return true;
            case Keys.Control | Keys.T:
                SetStatus(_inFileChapters.Count > 1 || _tracks.Count > 1
                    ? $"{_positionLabel.Text}. {_trackLabel.Text}"
                    : _positionLabel.Text);
                return true;
            case Keys.Control | Keys.B:
                AddBookmark();
                return true;
            case Keys.Control | Keys.Shift | Keys.B:
                ShowBookmarks();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------------
    // Display
    // ------------------------------------------------------------------

    private void OnTick()
    {
        UpdatePosition();
        UpdateChapterFromPosition(announce: true);

        if (IsPlaying)
        {
            // One second of listening, for the listening statistics.
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            _settings.ListeningLog[today] = _settings.ListeningLog.GetValueOrDefault(today) + 1;
        }

        if (_sleepAt is { } at && DateTime.Now >= at && IsPlaying)
            SleepNow();

        if (++_ticksSinceSave >= 15)
        {
            _ticksSinceSave = 0;
            SavePosition();
        }
    }

    private int ChapterAt(TimeSpan position)
    {
        var index = -1;
        for (var i = 0; i < _inFileChapters.Count; i++)
        {
            if (_inFileChapters[i].Start <= position + TimeSpan.FromMilliseconds(500))
                index = i;
            else
                break;
        }
        return index;
    }

    /// <summary>Follows the chapters inside the file; a new one is named, and ends a sleep "at chapter end".</summary>
    private void UpdateChapterFromPosition(bool announce)
    {
        if (_inFileChapters.Count < 2 || _player is null)
            return;

        var chapter = ChapterAt(Position);
        if (chapter == _currentChapter)
            return;

        var first = _currentChapter < 0;
        _currentChapter = chapter;
        UpdateTrackLabel();

        if (!announce || first)
            return;

        if (_sleepAtChapterEnd)
        {
            SleepNow();
            return;
        }

        if (_settings.AnnounceChapters && IsPlaying)
            SetStatus(_trackLabel.Text);
    }

    private void UpdateDuration()
    {
        if (_player is null)
            return;

        var length = _player.PlaybackSession.NaturalDuration;
        _seek.Maximum = Math.Max(1, (int)length.TotalSeconds);
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (_player is null)
            return;

        var session = _player.PlaybackSession;
        var seconds = (int)Math.Clamp(session.Position.TotalSeconds, 0, _seek.Maximum);

        // Only moved by the timer while the slider is not being used, so arrowing
        // along it is not fought by the clock.
        if (!_seek.Focused || session.PlaybackState != MediaPlaybackState.Playing)
            _seek.Value = seconds;

        _positionLabel.Text = string.Format(Strings.T("Player.PositionText"),
            Clock(session.Position), Clock(session.NaturalDuration));
    }

    private void UpdatePlayButton()
    {
        var key = IsPlaying ? "Player.Pause" : "Player.Play";
        _play.Text = Strings.T(key);
        _play.AccessibleName = Strings.Plain(Strings.T(key));
    }

    private void UpdateTrackLabel()
    {
        if (_inFileChapters.Count > 1 && _currentChapter >= 0)
        {
            _trackLabel.Text = string.Format(Strings.T("Player.TrackText"),
                _currentChapter + 1, _inFileChapters.Count, _inFileChapters[_currentChapter].Title);
            _previous.Enabled = true;
            _next.Enabled = _currentChapter < _inFileChapters.Count - 1;
            return;
        }

        if (_tracks.Count <= 1)
            return;

        _trackLabel.Text = string.Format(Strings.T("Player.TrackText"),
            _currentTrack + 1, _tracks.Count, Path.GetFileNameWithoutExtension(_tracks[_currentTrack]));
        _previous.Enabled = true;
        _next.Enabled = _currentTrack < _tracks.Count - 1;
    }

    private void SetStatus(string text)
    {
        if (IsDisposed)
            return;

        _status.Text = text;
        _status.Announce();
    }

    // ------------------------------------------------------------------
    // Remembering where you were
    // ------------------------------------------------------------------

    private int SavedTrack() =>
        (int)_settings.PlaybackPositions.GetValueOrDefault(_path + "|track");

    private void SavePosition()
    {
        if (_player is null)
            return;

        try
        {
            var position = _player.PlaybackSession.Position.TotalSeconds;
            if (position < 1 && _currentTrack == 0)
                return;

            _settings.PlaybackPositions[_path] = Math.Round(position, 1);
            if (_tracks.Count > 1)
                _settings.PlaybackPositions[_path + "|track"] = _currentTrack;
            _settings.Save();
        }
        catch (Exception ex)
        {
            Log.Warn($"Player: could not remember the position: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        try { BeginInvoke(action); }
        catch (InvalidOperationException) { /* the window is closing */ }
    }

    private static int NearestSpeedIndex(double speed)
    {
        var best = 2;
        for (var i = 0; i < Speeds.Length; i++)
            if (Math.Abs(Speeds[i] - speed) < Math.Abs(Speeds[best] - speed))
                best = i;
        return best;
    }

    /// <summary>"1:05:09" or "5:09", the way a person reads a time.</summary>
    internal static string Clock(TimeSpan time) =>
        time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
}
