using AudiobookDownloader.Core;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// A small audiobook player: play and pause, jump back and forward 30 seconds,
/// move between chapter files, change the speed, and pick up where you stopped
/// last time. Built on the Windows media player, so it plays M4B, M4A and MP3
/// without anything extra installed.
/// </summary>
/// <remarks>
/// Everything is a named control with a mnemonic, and the keys that matter while
/// listening work from anywhere in the window:
/// Space (on the position slider) or the Play/Pause media key, Ctrl + Left and
/// Ctrl + Right to jump 30 seconds, Ctrl + Shift + Left and Right for the previous
/// and next chapter, Ctrl + Up and Down for the speed, and Ctrl + T to hear the time.
/// </remarks>
internal sealed class PlayerForm : Form
{
    private static readonly double[] Speeds = [0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0];

    /// <summary>The kinds of file the player opens.</summary>
    public static readonly string[] AudioExtensions = [".m4b", ".m4a", ".mp3", ".aac", ".ogg", ".wav", ".flac", ".wma"];

    private static readonly TimeSpan Jump = TimeSpan.FromSeconds(30);

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
    private readonly ComboBox _speed;
    private readonly Button _close;
    private readonly LiveRegionLabel _status;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    private MediaPlayer? _player;
    private MediaPlaybackList? _list;
    private bool _restorePending = true;
    private int _ticksSinceSave;
    private int _currentTrack;

    /// <summary>The position slider, play button and status, for the self-test.</summary>
    internal Button PlayButton => _play;

    internal IReadOnlyList<string> Tracks => _tracks;

    /// <summary>Plays silently; the self-test uses it so checking the player makes no sound.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Muted { get; init; }

    internal bool IsPlaying => _player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    internal TimeSpan Position => _player?.PlaybackSession.Position ?? TimeSpan.Zero;

    internal string StatusText => _status.Text;

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
        ClientSize = new Size(620, 300);
        KeyPreview = true;
        Strings.ApplyDirection(this);

        _titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            MaximumSize = new Size(590, 0),
            Font = new Font(Font.FontFamily, Font.Size + 2f, FontStyle.Bold),
            AccessibleName = Strings.T("Player.BookTitle"),
            AccessibleRole = AccessibleRole.StaticText,
        };

        _trackLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(590, 0),
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
            Dock = DockStyle.Fill,
            TabIndex = 0,
            AccessibleName = Strings.T("Player.Position"),
        };
        _seek.Scroll += (_, _) => SeekTo(TimeSpan.FromSeconds(_seek.Value));

        _play = NewButton("Player.Play", 1, TogglePlay);
        _back = NewButton("Player.Back", 2, () => SkipBy(-Jump));
        _forward = NewButton("Player.Forward", 3, () => SkipBy(Jump));
        _previous = NewButton("Player.Previous", 4, () => MoveTrack(-1));
        _next = NewButton("Player.Next", 5, () => MoveTrack(+1));
        _previous.Visible = _next.Visible = _tracks.Count > 1;

        _speed = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
            TabIndex = 6,
            AccessibleName = Strings.Plain(Strings.T("Player.Speed")).TrimEnd(':'),
            AccessibleRole = AccessibleRole.ComboBox,
        };
        foreach (var speed in Speeds)
            _speed.Items.Add($"{speed:0.##}x");
        _speed.SelectedIndex = NearestSpeedIndex(settings.PlaybackSpeed);
        _speed.SelectedIndexChanged += (_, _) => ApplySpeed();

        _close = new Button
        {
            Text = Strings.T("Common.Close"),
            AutoSize = true,
            TabIndex = 7,
            DialogResult = DialogResult.Cancel,
            AccessibleName = Strings.Plain(Strings.T("Common.Close")),
        };
        CancelButton = _close;

        _status = new LiveRegionLabel
        {
            AutoSize = true,
            MaximumSize = new Size(590, 0),
            Text = Strings.T("Player.KeysHint"),
            AccessibleName = Strings.T("Player.Status"),
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.AddRange([_play, _back, _forward, _previous, _next]);

        var speedRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        speedRow.Controls.Add(new Label
        {
            Text = Strings.T("Player.Speed"),
            AutoSize = true,
            Margin = new Padding(3, 7, 3, 3),
        });
        speedRow.Controls.Add(_speed);
        speedRow.Controls.Add(_close);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        foreach (var control in new Control[] { _titleLabel, _trackLabel, _positionLabel, _seek, buttons, speedRow, _status })
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control);
        }
        Controls.Add(layout);

        _tick.Tick += (_, _) => OnTick();
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
            AccessibleName = Strings.Plain(Strings.T(key)),
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
        }
        catch (Exception ex)
        {
            Log.Error($"Player: could not open {_path}: {ex.Message}");
            SetStatus(string.Format(Strings.T("Player.Failed"), ex.Message));
        }
    }

    private void OnOpened()
    {
        if (_player is null)
            return;

        ApplySpeed(announce: false);
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
        SetStatus(_trackLabel.Text);
    }

    private void OnEnded()
    {
        // The whole book has been heard, so next time it starts from the beginning.
        _settings.PlaybackPositions.Remove(_path);
        _settings.PlaybackPositions.Remove(_path + "|track");
        _settings.Save();
        SetStatus(Strings.T("Player.Ended"));
        UpdatePlayButton();
    }

    private void Shutdown()
    {
        _tick.Stop();
        SavePosition();

        try { _player?.Pause(); } catch { /* closing anyway */ }
        _player?.Dispose();
        _player = null;
        _tick.Dispose();
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
    }

    private void MoveTrack(int step)
    {
        if (_list is null)
            return;

        if (step < 0)
        {
            // Like any player: more than a few seconds in, "previous" restarts the chapter.
            if (_player!.PlaybackSession.Position > TimeSpan.FromSeconds(5) || _currentTrack == 0)
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

    private void ApplySpeed(bool announce = true)
    {
        var speed = Speeds[Math.Clamp(_speed.SelectedIndex, 0, Speeds.Length - 1)];
        _settings.PlaybackSpeed = speed;

        if (_player is not null)
            _player.PlaybackSession.PlaybackRate = speed;

        if (announce)
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
                MoveTrack(-1);
                return true;
            case Keys.Control | Keys.Shift | Keys.Right:
            case Keys.MediaNextTrack:
                MoveTrack(+1);
                return true;
            case Keys.Control | Keys.Up:
                ChangeSpeed(+1);
                return true;
            case Keys.Control | Keys.Down:
                ChangeSpeed(-1);
                return true;
            case Keys.Control | Keys.T:
                SetStatus(_positionLabel.Text);
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

        if (++_ticksSinceSave >= 15)
        {
            _ticksSinceSave = 0;
            SavePosition();
        }
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
        var playing = _player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        var key = playing ? "Player.Pause" : "Player.Play";
        _play.Text = Strings.T(key);
        _play.AccessibleName = Strings.Plain(Strings.T(key));
    }

    private void UpdateTrackLabel()
    {
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
