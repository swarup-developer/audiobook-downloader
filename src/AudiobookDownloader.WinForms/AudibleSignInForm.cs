using System.Net;
using AudiobookDownloader.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// The Audible sign-in window: Amazon's own page, shown in a browser the app owns
/// so the sign-in finishes inside the app instead of leaving the user on a web
/// site.
/// </summary>
/// <remarks>
/// Amazon ends a successful sign-in by sending the browser back with an
/// authorization code in the address. That address is what the Audible library
/// needs and it exists for that one navigation only, so the window watches every
/// navigation — top-level pages and frames alike — and asks the browser what it
/// is showing as well, then takes the address the moment it appears and closes
/// itself. Nothing is ever copied or typed by hand, and no further page has to be
/// visited once the sign-in has succeeded.
/// The browser runs in a throwaway profile under LocalAppData: the Amazon web
/// session must not outlive the sign-in, and the only thing the app keeps is its
/// own Audible identity file.
/// </remarks>
internal sealed class AudibleSignInForm : Form
{
    /// <summary>Where the throwaway browser profile lives.</summary>
    private static string UserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AudiobookDownloader",
        "AudibleSignIn");

    private readonly AudibleSignInRequest _request;
    private readonly WebView2 _browser;
    private readonly LiveRegionLabel _status;
    private readonly Button _reloadButton;
    private readonly Button _cancelButton;
    private readonly System.Windows.Forms.Timer _watch;

    private bool _started;
    private bool _closePosted;
    private string? _responseUrl;

    /// <summary>
    /// Set when the window itself could not be used, and null when the account
    /// holder simply closed it. The caller turns it into the sign-in error the
    /// user reads, so a missing browser is never reported as "cancelled".
    /// </summary>
    public string? FailureMessage { get; private set; }

    public AudibleSignInForm(AudibleSignInRequest request)
    {
        _request = request;

        Text = Strings.T("Login.Title");
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(940, 720);
        MinimumSize = new Size(620, 500);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        Strings.ApplyDirection(this);
        AccessibleName = Strings.T("Login.Title");
        AccessibleRole = AccessibleRole.Dialog;

        var instructions = new Label
        {
            Text = Strings.T("Login.Instructions"),
            AutoSize = true,
            MaximumSize = new Size(880, 0),
        };
        instructions.AccessibleName = Strings.T("Login.InstructionsAccessible");

        _status = new LiveRegionLabel
        {
            Text = Strings.T("Login.Starting"),
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            AccessibleName = Strings.T("Login.StatusAccessible"),
        };

        _browser = new WebView2
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 320),
            TabIndex = 0,
            AccessibleName = Strings.T("Login.PageAccessible"),
            AccessibleRole = AccessibleRole.Document,
            // A throwaway InPrivate profile: in-private mode must not use the
            // default profile, so this window brings one of its own.
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = UserDataFolder,
                ProfileName = "audible-sign-in",
                IsInPrivateModeEnabled = true,
                Language = Strings.Current,
            },
        };

        // The browser's own events are the main way the reply is caught, but a
        // reply that arrives without raising the one event the window happened to
        // be watching would leave the account holder on a finished sign-in. This
        // asks the browser what it is showing, so the reply is taken whichever way
        // it arrives.
        _watch = new System.Windows.Forms.Timer { Interval = 700 };
        _watch.Tick += (_, _) => TakeReply(_browser.Source?.ToString());

        _reloadButton = new Button
        {
            Text = Strings.T("Login.Reload"),
            AutoSize = true,
            TabIndex = 1,
            AccessibleName = Strings.T("Login.ReloadAccessible"),
        };
        _reloadButton.Click += (_, _) => Reload();

        _cancelButton = new Button
        {
            Text = Strings.T("Common.Cancel"),
            AutoSize = true,
            TabIndex = 2,
            DialogResult = DialogResult.Cancel,
            AccessibleName = Strings.T("Login.CancelAccessible"),
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
        };
        buttons.Controls.Add(_reloadButton);
        buttons.Controls.Add(_cancelButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(instructions, 0, 0);
        layout.Controls.Add(_status, 0, 1);
        layout.Controls.Add(_browser, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);

        CancelButton = _cancelButton;

        Load += async (_, _) => await StartBrowserAsync();
    }

    /// <summary>
    /// Throw the Amazon session away as the window closes. It was only ever needed
    /// to finish the sign-in, and dropping it means switching accounts always
    /// starts from a clean page.
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _watch.Stop();

        try { _browser.CoreWebView2?.CookieManager.DeleteAllCookies(); }
        catch { /* the profile is throwaway anyway */ }

        base.OnFormClosed(e);
    }

    /// <summary>
    /// Shows the window modally and returns the address Amazon redirected to, or
    /// null when the account holder closed the window without signing in. An
    /// owner is required so the window is centred on it, appears in front of it,
    /// and gives focus back to it when it closes.
    /// </summary>
    public string? ShowAndGetResponseUrl(IWin32Window? owner)
    {
        if (owner is null)
            ShowDialog();
        else
            ShowDialog(owner);

        return _responseUrl;
    }

    /// <summary>Opens the sign-in page and starts watching where Amazon sends it.</summary>
    private async Task StartBrowserAsync()
    {
        if (_started)
            return;

        _started = true;

        try
        {
            Directory.CreateDirectory(UserDataFolder);

            await EnsureBrowserAsync();

            if (IsDisposed || Disposing || _browser.CoreWebView2 is null)
                return;

            var core = _browser.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;

            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.SourceChanged += OnSourceChanged;
            core.FrameCreated += OnFrameCreated;
            core.ProcessFailed += (_, _) => SetStatus(Strings.T("Login.PageFailed"));

            SeedSignInCookies(core.CookieManager);

            SetStatus(Strings.T("Login.Waiting"));
            _watch.Start();
            core.Navigate(_request.LoginUrl);
        }
        catch (Exception ex)
        {
            Fail(ex is WebView2RuntimeNotFoundException
                ? Strings.T("Login.NoBrowser")
                : string.Format(Strings.T("Login.WindowFailed"), ex.Message));
        }
    }

    /// <summary>
    /// Starts the browser, falling back to a plain profile when the runtime will
    /// not give this window the throwaway one.
    /// </summary>
    /// <remarks>
    /// The cookies are wiped as the window closes whichever profile is used, so
    /// the throwaway profile and InPrivate mode are a second line of defence
    /// rather than the only one. A runtime that refuses to combine them must not
    /// cost the account holder the sign-in itself.
    /// </remarks>
    private async Task EnsureBrowserAsync()
    {
        try
        {
            await _browser.EnsureCoreWebView2Async();
            return;
        }
        catch (Exception ex) when (ex is not WebView2RuntimeNotFoundException)
        {
            Log.Warn($"Audible: the throwaway sign-in profile was refused ({ex.Message}); " +
                     "retrying with a plain profile.");
        }

        _browser.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = UserDataFolder,
            Language = Strings.Current,
        };

        await _browser.EnsureCoreWebView2Async();
    }

    /// <summary>
    /// Puts the device cookies Audible issued for this sign-in into the browser
    /// before the page opens. They identify the very device the book licence is
    /// later issued to, so Amazon must see them.
    /// </summary>
    private void SeedSignInCookies(CoreWebView2CookieManager manager)
    {
        foreach (Cookie cookie in _request.SignInCookies)
        {
            if (string.IsNullOrWhiteSpace(cookie.Name))
                continue;

            try
            {
                // A leading dot is not significant in a cookie domain (RFC 6265).
                var domain = (cookie.Domain ?? string.Empty).TrimStart('.');
                var path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path;
                var added = manager.CreateCookie(cookie.Name, cookie.Value ?? string.Empty, domain, path);
                added.IsHttpOnly = cookie.HttpOnly;
                added.IsSecure = cookie.Secure;
                added.SameSite = CoreWebView2CookieSameSiteKind.Lax;
                manager.AddOrUpdateCookie(added);
            }
            catch (Exception ex)
            {
                Log.Warn($"Audible: could not hand the \"{cookie.Name}\" cookie to the sign-in page: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The moment Amazon sends the browser to the reply, take it and close: the
    /// reply is the whole point of this window. The navigation is cancelled so the
    /// account holder never has to look at Amazon's "you can close this page" dead
    /// end before the finish they already completed.
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (AudibleSignInRedirect.AuthorizationCode(e.Uri) is null)
            return;

        e.Cancel = true; // the code is all the app needs; the page itself can wait
        TakeReply(e.Uri);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // A reply that arrives as a page finishes loading, rather than as a
        // navigation starting, is just as much a reply.
        if (TakeReply(_browser.Source?.ToString()))
            return;

        SetStatus(Strings.T(e.IsSuccess ? "Login.Waiting" : "Login.PageFailed"));
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e) =>
        TakeReply(_browser.Source?.ToString());

    /// <summary>
    /// Takes the reply when this address carries one, and says whether the window
    /// now has it — whether this call is the one that took it or an earlier call
    /// did. Every route the reply can arrive by ends here.
    /// </summary>
    private bool TakeReply(string? address)
    {
        if (_responseUrl is not null)
            return true;

        if (IsDisposed || Disposing)
            return false;

        if (AudibleSignInRedirect.AuthorizationCode(address) is not { } code)
            return false;

        _responseUrl = AudibleSignInRedirect.ResponseUrl(address!, code);
        Log.Info("Audible: Amazon sent the sign-in reply back; closing the sign-in window.");

        SetStatus(Strings.T("Login.SignedIn"));
        CloseSoon();
        return true;
    }

    /// <summary>
    /// Closes the window from the message queue rather than from the browser
    /// callback that found the reply: tearing the browser down while it is still
    /// handing an event over is how a WebView2 window hangs, and a window that
    /// hangs is a sign-in that never gets back to the app.
    /// </summary>
    private void CloseSoon()
    {
        if (_closePosted || IsDisposed || Disposing || !IsHandleCreated)
            return;

        _closePosted = true;
        BeginInvoke(new Action(() =>
        {
            if (!IsDisposed && !Disposing)
                DialogResult = DialogResult.OK;
        }));
    }

    /// <summary>
    /// Amazon sometimes makes its final hop inside a frame, so frames are watched
    /// exactly like the main page.
    /// </summary>
    private void OnFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e) =>
        e.Frame.NavigationStarting += OnNavigationStarting;

    private void Reload()
    {
        if (_browser.CoreWebView2 is not null)
        {
            _browser.Reload();
            return;
        }

        // The browser never started (a slow antivirus, no runtime until now):
        // give it another go instead of leaving a dead window behind.
        _started = false;
        FailureMessage = null;
        _ = StartBrowserAsync();
    }

    private void SetStatus(string text)
    {
        if (IsDisposed || Disposing)
            return;

        _status.Text = text;
        _status.Announce();
    }

    /// <summary>
    /// The window cannot be used at all: tell the caller why and close, so the
    /// user reads one clear explanation instead of a stuck dialog.
    /// </summary>
    private void Fail(string message)
    {
        FailureMessage = message;
        Log.Error($"Audible: the sign-in window could not be opened: {message}");
        DialogResult = DialogResult.Abort;
    }
}
