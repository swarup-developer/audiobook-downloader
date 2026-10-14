using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using AudiobookDownloader.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// A build-in check that every dialog can be constructed and that every
/// interactive control is properly exposed to accessibility: a spoken name, a
/// role, and a unique position in its container's tab order.
/// </summary>
/// <remarks>
/// Run it with: AudiobookDownloader.WinForms.exe --self-test [report-path]
/// It returns exit code 0 when everything passes and 1 when it does not, and it
/// never downloads anything or touches the network.
/// Add --self-test-live to also read the real LibriVox catalogue (slow, and it
/// does use the network).
/// </remarks>
internal static class SelfTest
{
    private static readonly StringBuilder Report = new();
    private static int _failures;

    private const int WmChar = 0x0102;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    public static int Run(string? reportPath, bool live = false)
    {
        var path = string.IsNullOrWhiteSpace(reportPath)
            ? Path.Combine(Path.GetTempPath(), "audiobook-selftest.txt")
            : reportPath;

        var settings = AppSettings.Load();

        Line($"Audiobook Downloader self-test — {DateTime.Now:u}");
        Line($"Interface language: {Strings.Current}");
        Line("");

        CheckForm("Main window", () => new MainForm());
        CheckForm("Settings", () => new SettingsForm(settings, new AudibleProvider { AppSettingsInstance = settings }));

        var previousLanguage = Strings.Current;
        foreach (var language in Strings.AvailableLanguages)
        {
            Strings.SetLanguage(language.Code);
            CheckForm($"Settings ({language.Code})",
                () => new SettingsForm(settings, new AudibleProvider { AppSettingsInstance = settings }));
        }
        Strings.SetLanguage(previousLanguage);

        CheckForm("About", () => new AboutForm());
        CheckForm("Keyboard help", NewKeyboardHelpForm);
        CheckForm("Sign-in window", NewSignInForm);
        CheckForm("Country sign-in", NewCountrySignInForm);
        CheckForm("Key prompt (AAXC)", () => new KeyPromptForm("book.aaxc", askKeyIv: true, askActivationBytes: false));
        CheckForm("Key prompt (AAX)", () => new KeyPromptForm("book.aax", askKeyIv: false, askActivationBytes: true));

        CheckProviderCatalog();
        CheckSignInRedirect();
        CheckBookRows();
        CheckAudibleCredits();
        CheckSignedInPresentation();
        CheckCountries();
        CheckCountrySignInFlow();
        CheckCountrySearch();
        CheckCountryTyping();
        CheckCountryKeystrokes();
        CheckQueueMessages();
        CheckShortcuts();
        SelfTestDownloads.Run(Line, Fail);
        SelfTestQueue.Run(Line, Fail);
        SelfTestCatalog.Run(Line, Fail, live);
        SelfTestUi.Run(Line, Fail);
        CheckLiveRegion();

        Line("");

        CheckSimpleNames();

        Line("");
        Line(_failures == 0 ? "PASS: no problems found." : $"FAIL: {_failures} problem(s) found.");

        var text = Report.ToString();
        try { File.WriteAllText(path, text); } catch { /* console output is enough */ }
        Console.Write(text);

        return _failures == 0 ? 0 : 1;
    }

    private static void CheckForm(string name, Func<Form> factory)
    {
        Line($"== {name} ==");
        Form? form = null;
        try
        {
            form = factory();
            form.CreateControl();

            var controls = Descendants(form).ToList();
            Line($"   controls: {controls.Count}");

            foreach (var control in controls)
            {
                if (IsInteractive(control))
                    CheckAccessibleName(control, name);

                if (control.TabStop && control.Visible)
                    CheckDuplicateTabIndex(control, name);
            }

            foreach (var item in form.MainMenuStrip?.Items.Cast<ToolStripItem>() ?? [])
                CheckMenuTree(item, name);
        }
        catch (Exception ex)
        {
            Fail($"{name}: could not be constructed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            form?.Dispose();
        }

        Line("");
    }

    private static void CheckMenuTree(ToolStripItem item, string formName)
    {
        // Separators are decoration, not controls.
        if (item is ToolStripSeparator)
            return;

        if (string.IsNullOrWhiteSpace(item.AccessibleName))
            Fail($"{formName}: menu item \"{item.Text}\" has no accessible name");

        if (item is ToolStripMenuItem menuItem)
            foreach (var child in menuItem.DropDownItems.Cast<ToolStripItem>())
                CheckMenuTree(child, formName);
    }

    private static void CheckAccessibleName(Control control, string formName)
    {
        // The spin-edit inside a NumericUpDown is an implementation detail; the
        // NumericUpDown itself is the element a screen reader reads.
        if (control.Parent is NumericUpDown or UpDownBase)
            return;

        var name = control.AccessibleName;

        if (string.IsNullOrWhiteSpace(name))
        {
            Fail($"{formName}: {Describe(control)} has no accessible name " +
                 $"(text \"{control.Text}\")");
            return;
        }

        if (name.Contains('&'))
            Fail($"{formName}: {Describe(control)} has an ampersand in its accessible name " +
                 $"\"{name}\"; the spoken name must be plain text");

        if (LooksLikeUnresolvedKey(name))
            Fail($"{formName}: {Describe(control)} shows an unresolved text key: \"{name}\"");
    }

    /// <summary>
    /// Minimal accessibility, applied to every form: every accessible name is
    /// simple plain text (never a keyboard mnemonic like "S&earch"), and no
    /// control carries an AccessibleDescription — the name and the role are all
    /// a screen reader needs, so it never reads a long second paragraph.
    /// </summary>
    private static void CheckSimpleNames()
    {
        Line("== Simple accessible names ==");

        foreach (var (formName, factory) in AllForms())
        {
            try
            {
                using var form = factory();
                form.CreateControl();

                foreach (var control in Descendants(form))
                {
                    if (control.Visible && !string.IsNullOrEmpty(control.AccessibleDescription))
                        Fail($"{formName}: {Describe(control)} sets an AccessibleDescription; " +
                             "the simple name and role are all a screen reader reads");

                    if (IsInteractive(control) && control.Visible)
                        CheckAccessibleName(control, formName);
                }

                foreach (var item in form.MainMenuStrip?.Items.Cast<ToolStripItem>() ?? [])
                    CheckMenuItemIsPlain(item, formName);
            }
            catch (Exception ex)
            {
                Fail($"{formName}: could not be re-checked: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Line("");
    }

    private static void CheckMenuItemIsPlain(ToolStripItem item, string formName)
    {
        if (item is ToolStripSeparator)
            return;

        var spoken = item.AccessibleName;
        if (string.IsNullOrWhiteSpace(spoken))
            Fail($"{formName}: menu item \"{item.Text}\" has no accessible name");
        else if (spoken.Contains('&'))
            Fail($"{formName}: menu item has the mnemonic \"{spoken}\" as its accessible name");

        if (item is ToolStripMenuItem menuItem)
            foreach (var child in menuItem.DropDownItems.Cast<ToolStripItem>())
                CheckMenuItemIsPlain(child, formName);
    }

    private static IEnumerable<(string Name, Func<Form> Build)> AllForms()
    {
        var settings = AppSettings.Load();
        yield return ("Main window", () => new MainForm());
        yield return ("Settings", () => new SettingsForm(settings, new AudibleProvider { AppSettingsInstance = settings }));
        yield return ("About", () => new AboutForm());
        yield return ("Keyboard help", NewKeyboardHelpForm);
        yield return ("Sign-in window", NewSignInForm);
        yield return ("Country sign-in", NewCountrySignInForm);
        yield return ("Key prompt (AAXC)", () => new KeyPromptForm("book.aaxc", askKeyIv: true, askActivationBytes: false));
        yield return ("Key prompt (AAX)", () => new KeyPromptForm("book.aax", askKeyIv: false, askActivationBytes: true));
    }

    private static void CheckDuplicateTabIndex(Control control, string formName)
    {
        if (control.Parent is not { } parent)
            return;

        var siblings = parent.Controls.Cast<Control>()
            .Where(c => c.TabStop && c.Visible)
            .GroupBy(c => c.TabIndex)
            .Where(g => g.Count() > 1);

        foreach (var group in siblings)
            Fail($"{formName}: {Describe(control)} shares tab position {group.Key} with " +
                 $"{string.Join(", ", group.Select(c => Describe(c)).Where(d => d != Describe(control)))}");
    }

    /// <summary>Text that looks like a localization key (for example "Set.OutputFolder").</summary>
    private static bool LooksLikeUnresolvedKey(string text)
    {
        var trimmed = text.Trim();
        var dot = trimmed.IndexOf('.');
        if (dot <= 0 || dot >= trimmed.Length - 1)
            return false;

        var left = trimmed[..dot];
        var right = trimmed[(dot + 1)..];
        return left.Length > 1 && right.Length > 2
               && left.All(c => char.IsLetter(c))
               && right.All(c => char.IsLetterOrDigit(c))
               && char.IsUpper(left[0]);
    }

    private static void CheckProviderCatalog()
    {
        Line("== Providers ==");
        var settings = AppSettings.Load();

        var providers = new IBookProvider[]
        {
            new AudibleProvider { AppSettingsInstance = settings },
            new LibriVoxProvider(),
            new InternetArchiveProvider(),
            new GutenbergProvider(settings),
            new LocalFilesProvider(settings, null),
        };

        foreach (var provider in providers)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(provider.Id)) missing.Add("Id");
            if (string.IsNullOrWhiteSpace(provider.DisplayName)) missing.Add("DisplayName");
            if (string.IsNullOrWhiteSpace(provider.Description)) missing.Add("Description");

            if (missing.Count > 0)
                Fail($"{provider.GetType().Name}: missing {string.Join(", ", missing)}");
            else
                Line($"   {provider.Id}: {provider.DisplayName}");

            if (provider is GutenbergProvider gutenberg)
            {
                var languages = ((ILanguageFilter)gutenberg).SupportedLanguages;
                Line($"   Gutenberg languages: {languages.Count}");
                if (languages.All(l => l.Code != "en"))
                    Fail("Project Gutenberg: English is missing from the language list");
                if (languages.All(l => l.Code != "hi"))
                    Fail("Project Gutenberg: Hindi is missing from the language list");
                if (languages.Any(l => string.IsNullOrWhiteSpace(l.Code) || string.IsNullOrWhiteSpace(l.DisplayName)))
                    Fail("Project Gutenberg: a language option has no code or name");
            }

            // Signed out: Audible must never hand out library content.
            if (provider is AudibleProvider audible && audible.IsConnected)
                Fail("Audible: reports a live session before anyone signed in");
        }

        Line("");
    }

    /// <summary>
    /// The automatic Audible sign-in turns on recognising the address Amazon sends
    /// the browser back to once the account holder is signed in. Getting it wrong
    /// one way leaves the user stranded on a web page after a sign-in that worked;
    /// wrong the other way closes the window on a sign-in that never happened. So
    /// it is checked address by address, and then against the addresses the
    /// Audible library itself asks Amazon to return to.
    /// </summary>
    private static void CheckSignInRedirect()
    {
        Line("== Audible sign-in redirect ==");

        (string Address, bool IsReply)[] addresses =
        [
            // The reply: the store's own address, carrying the code.
            ("https://www.amazon.com/ap/maplanding?openid.oa2.authorization_code=ANcdef123", true),
            ("https://www.amazon.co.uk/ap/maplanding?openid.oa2.authorization_code=AN%2Bcd", true),
            ("https://www.audible.com/ap/maplanding?openid.oa2.authorization_code=ANcdef123", true),
            ("https://www.amazon.com/ap/maplanding?foo=1&openid.oa2.authorization_code=ANcdef123&bar=2", true),
            ("https://www.amazon.com/ap/maplanding/?openid.oa2.authorization_code=ANcdef123", true),
            // The code after the hash, which a reader that only looked at the query would miss.
            ("https://www.audible.com/ap/maplanding#openid.oa2.authorization_code=ANcdef123", true),

            // Never a reply: no code to take, or not the store at all.
            ("https://www.amazon.com/ap/signin?openid.oa2.response_type=code", false),
            ("https://www.amazon.com/ap/maplanding?openid.oa2.authorization_code=", false),
            ("https://www.amazon.com/ap/maplanding?openid.oa2.authorization_code=%20", false),
            ("https://www.amazon.com/gp/library", false),
            ("https://example.com/ap/maplanding?openid.oa2.authorization_code=ANcdef123", false),
            ("https://www.amazon.com.evil.example/ap/maplanding?openid.oa2.authorization_code=ANcdef123", false),
            ("https://notamazon.com/ap/maplanding?openid.oa2.authorization_code=ANcdef123", false),
            ("file:///C:/ap/maplanding?openid.oa2.authorization_code=ANcdef123", false),
            ("about:blank", false),
            ("not an address", false),
            ("", false),
            (null!, false),
        ];

        var wrong = 0;
        foreach (var (address, isReply) in addresses)
        {
            var recognised = AudibleSignInRedirect.CarriesAuthorizationCode(address);
            if (recognised == isReply)
                continue;

            wrong++;
            Fail(recognised
                ? $"the sign-in window would close on \"{address}\", which carries no authorization code"
                : $"the sign-in window would ignore \"{address}\", so the sign-in could never finish");
        }

        if (wrong == 0)
            Line($"   {addresses.Length} addresses recognised exactly: the sign-in ends on the reply, never sooner");

        CheckSignInReplyForEveryRegion();
        Line("");
    }

    /// <summary>
    /// Takes the addresses the Audible library itself hands to Amazon — one per
    /// store the app offers — and checks the reply that comes back to each one is
    /// recognised, and that the address the window then hands over is one the
    /// library can read the code out of.
    /// </summary>
    /// <remarks>
    /// This is what keeps the app in step with the library: <c>openid.return_to</c>
    /// is built from the library's own list of stores, so if a landing address
    /// moves or a store is added, the self-test says so instead of the user being
    /// stranded on a web page after a sign-in that actually worked.
    /// </remarks>
    private static void CheckSignInReplyForEveryRegion()
    {
        const string code = "ANcdef123";
        var checkedRegions = 0;

        foreach (var region in AudibleStores.Codes)
        {
            var locale = AudibleApi.Localization.Get(region);
            if (string.IsNullOrWhiteSpace(locale.TopDomain))
            {
                Fail($"the Audible library does not know the \"{region}\" store the app offers");
                continue;
            }

            var loginUrl = new AudibleApi.Authorization.RegistrationOptions().OAuthUrl(locale);
            var returnTo = QueryValue(loginUrl.Query, "openid.return_to");
            if (returnTo is null)
            {
                Fail($"the sign-in link for the \"{region}\" store does not say where to return to");
                continue;
            }

            var reply = $"{returnTo}?{AudibleSignInRedirect.AuthorizationCodeParameter}={code}";
            if (AudibleSignInRedirect.AuthorizationCode(reply) != code)
            {
                Fail($"a sign-in finished on the \"{region}\" store would not be recognised: {reply}");
                continue;
            }

            var handedOver = AudibleSignInRedirect.ResponseUrl(reply, code);
            if (AudibleApi.Authorization.OAuth2.Parse(handedOver)?.Code != code)
            {
                Fail($"the \"{region}\" reply address would be refused by the Audible library: {handedOver}");
                continue;
            }

            checkedRegions++;
        }

        if (checkedRegions == AudibleStores.Codes.Length)
            Line($"   {checkedRegions} stores: each reply is recognised and read by the Audible library");

        // A code that arrives after the hash still has to reach the library, which
        // reads the query and nothing else.
        const string fragmentReply = "https://www.amazon.com/ap/maplanding#openid.oa2.authorization_code=ANhash";
        if (AudibleSignInRedirect.AuthorizationCode(fragmentReply) is not { } fromFragment ||
            AudibleApi.Authorization.OAuth2.Parse(AudibleSignInRedirect.ResponseUrl(fragmentReply, fromFragment))?.Code != fromFragment)
            Fail("a code that arrives after the hash would not reach the Audible library");
        else
            Line("   a code that arrives after the hash still reaches the Audible library");
    }

    /// <summary>The value of one parameter in a query string, or null when it is absent.</summary>
    private static string? QueryValue(string? query, string name)
    {
        if (string.IsNullOrEmpty(query))
            return null;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == name)
                return Uri.UnescapeDataString(parts[1]);
        }

        return null;
    }

    /// <summary>
    /// No book row may ever read "null", an empty string, or a placeholder.
    /// </summary>
    /// <remarks>
    /// This is the shape of the bug that reached the user. The Audible library
    /// answers "[null]" from its own title property when Amazon sends an item with
    /// no title, so that property is never null and a fallback written as
    /// <c>?? identifier</c> never runs: the row reads "[null] - Author".
    /// </remarks>
    private static void CheckBookRows()
    {
        Line("== Book rows ==");

        (string Title, string Author, string Id, string Expected)[] rows =
        [
            ("The Hobbit", "J. R. R. Tolkien", "B012345", "The Hobbit - J. R. R. Tolkien"),
            ("[null]", "J. R. R. Tolkien", "B012345", "B012345 - J. R. R. Tolkien"),
            ("null", "J. R. R. Tolkien", "B012345", "B012345 - J. R. R. Tolkien"),
            ("", "J. R. R. Tolkien", "B012345", "B012345 - J. R. R. Tolkien"),
            ("   ", "J. R. R. Tolkien", "B012345", "B012345 - J. R. R. Tolkien"),
            ("The Hobbit", "", "B012345", "The Hobbit - Unknown author"),
            ("The Hobbit", "[null]", "B012345", "The Hobbit - Unknown author"),
            ("The Hobbit", "   ", "B012345", "The Hobbit - Unknown author"),
            ("", "", "", "Unknown title - Unknown author"),
            ("[null]", "[null]", "", "Unknown title - Unknown author"),
        ];

        var wrong = 0;
        foreach (var (title, author, id, expected) in rows)
        {
            var shown = new BookInfo("test", id, title, author, string.Empty, 0, null, null, null).ToString();
            if (shown == expected)
                continue;

            wrong++;
            Fail($"a book row would read \"{shown}\" instead of \"{expected}\"");
        }

        if (wrong == 0)
            Line($"   {rows.Length} rows: a missing title or author reads as the book's identifier or plain words, never \"null\"");

        Line("");
    }

    /// <summary>
    /// What the Audible provider puts in a row, given what Amazon really sends:
    /// items with no title, and credited people with no name. The provider's own
    /// mapping is exercised, offline, with no session.
    /// </summary>
    private static void CheckAudibleCredits()
    {
        Line("== Audible titles and credits ==");

        var wrong = 0;
        var checkedMappings = 0;

        void Expect(string what, string actual, string expected)
        {
            checkedMappings++;

            if (actual == expected)
                return;

            wrong++;
            Fail($"the Audible {what} would read \"{actual}\" instead of \"{expected}\"");
        }

        Expect("title with a subtitle",
            AudibleProvider.DisplayTitle(new AudibleApi.Common.Item { Title = "Dune", Subtitle = "Book One" }),
            "Dune: Book One");

        Expect("title with a blank subtitle",
            AudibleProvider.DisplayTitle(new AudibleApi.Common.Item { Title = "Dune", Subtitle = "  " }),
            "Dune");

        // Amazon sends items with no title at all, and the library then answers
        // "[null]" from its own title property. The identifier is all there is.
        Expect("title for an item Amazon sent with none",
            AudibleProvider.DisplayTitle(new AudibleApi.Common.Item { Title = null, Asin = "B012345" }),
            "B012345");

        Expect("title for an item with neither title nor identifier",
            AudibleProvider.DisplayTitle(new AudibleApi.Common.Item { Title = "[null]", Asin = null }),
            BookInfo.UnknownTitle);

        Expect("credits for authors with blank names",
            AudibleProvider.DisplayCredits(
                [new AudibleApi.Common.Person { Name = "Ursula K. Le Guin" }, new AudibleApi.Common.Person { Name = "  " }, null!],
                BookInfo.UnknownAuthor),
            "Ursula K. Le Guin");

        Expect("credits when nobody is named",
            AudibleProvider.DisplayCredits([], BookInfo.UnknownAuthor),
            BookInfo.UnknownAuthor);

        Expect("credits when Amazon sends no people at all",
            AudibleProvider.DisplayCredits(null, BookInfo.UnknownAuthor),
            BookInfo.UnknownAuthor);

        if (wrong == 0)
            Line($"   {checkedMappings} mappings: an item with no title falls back to its identifier, and blank credits are left out");

        Line("");
    }

    /// <summary>
    /// The Settings screen has to say whether the Audible account is signed in, and
    /// say it differently in each state — in every language, and in the spoken name
    /// a screen reader reads, not only in the visible text.
    /// </summary>
    /// <remarks>
    /// A name that still reads "Sign in to Audible" over a signed-in account is the
    /// same defect as a button that still says "Sign in", and it is invisible to
    /// anybody testing the dialog by eye.
    /// </remarks>
    private static void CheckSignedInPresentation()
    {
        Line("== Signed-in account presentation ==");

        var previousLanguage = Strings.Current;
        var wrong = 0;

        foreach (var language in Strings.AvailableLanguages)
        {
            Strings.SetLanguage(language.Code);

            var signedOut = Strings.T("Set.SignIn");
            var signedOutSpoken = Strings.T("Set.SignInAccessible");
            var signedIn = Strings.T("Set.SwitchAccount");
            var signedInSpoken = Strings.T("Set.SwitchAccountAccessible");

            if (new[] { signedOut, signedOutSpoken, signedIn, signedInSpoken }.Any(LooksLikeUnresolvedKey))
            {
                wrong++;
                Fail($"in {language.Code}, the account button shows an unresolved text key: " +
                     $"\"{signedIn}\" / \"{signedInSpoken}\"");
                continue;
            }

            if (signedIn == signedOut || signedInSpoken == signedOutSpoken)
            {
                wrong++;
                Fail($"in {language.Code}, the account button reads the same whether or not the account is signed in");
                continue;
            }

            if (signedInSpoken.Contains('&'))
            {
                wrong++;
                Fail($"in {language.Code}, the signed-in account button has an ampersand in its spoken name: \"{signedInSpoken}\"");
            }
        }

        Strings.SetLanguage(previousLanguage);

        if (wrong == 0)
            Line($"   {Strings.AvailableLanguages.Count} languages: a signed-in account cannot look signed out, spoken or shown");

        Line("");
    }

    /// <summary>
    /// What the queue says when it brings unfinished downloads back has to be said in
    /// every language, and has to name how many there are.
    /// </summary>
    /// <remarks>
    /// These are not on any control, so no dialog check would ever see them: they are
    /// spoken through the status live region at start-up, which is exactly when a
    /// user who is not watching the screen has nothing else to go on.
    /// </remarks>
    private static void CheckQueueMessages()
    {
        Line("== Unfinished downloads, said out loud ==");

        var previousLanguage = Strings.Current;
        var wrong = 0;

        Strings.SetLanguage("en");
        string englishResuming, englishRestored;

        try
        {
            englishResuming = Strings.T("Main.ResumingQueue", 3);
            englishRestored = Strings.T("Main.RestoredQueue", 3);
        }
        catch (Exception ex)
        {
            Fail($"the queue's messages could not be built: {ex.GetType().Name}: {ex.Message}");
            Strings.SetLanguage(previousLanguage);
            return;
        }

        foreach (var language in Strings.AvailableLanguages)
        {
            Strings.SetLanguage(language.Code);

            foreach (var (key, expected, english) in new[]
                     {
                         ("Main.ResumingQueue", Strings.T("Main.ResumingQueue", 3), englishResuming),
                         ("Main.RestoredQueue", Strings.T("Main.RestoredQueue", 3), englishRestored),
                     })
            {
                if (LooksLikeUnresolvedKey(expected))
                {
                    wrong++;
                    Fail($"in {language.Code}, \"{key}\" is not a sentence at all: \"{expected}\"");
                }
                else if (!expected.Contains('3', StringComparison.Ordinal))
                {
                    wrong++;
                    Fail($"in {language.Code}, \"{key}\" does not say how many downloads it is about");
                }
                else if (language.Code != "en" && string.Equals(expected, english, StringComparison.Ordinal))
                {
                    wrong++;
                    Fail($"in {language.Code}, \"{key}\" is still the English text: \"{expected}\"");
                }
            }
        }

        Strings.SetLanguage(previousLanguage);

        if (wrong == 0)
        {
            Line("   2 messages in " + Strings.AvailableLanguages.Count +
                 " languages: the returned batch is announced, with how many downloads it is about");
        }

        Line("");
    }

    /// <summary>
    /// The country list has to read as countries.
    /// </summary>
    /// <remarks>
    /// A selector built from the store codes announces "in" where a person should hear
    /// "India", and nothing about the screen looks wrong to anybody testing it by eye —
    /// which is why it went unnoticed for so long. So it is checked by name, in every
    /// interface language, and every name is checked against its own code.
    /// </remarks>
    private static void CheckCountries()
    {
        Line("== Audible countries ==");

        var previousLanguage = Strings.Current;
        var wrong = 0;

        foreach (var language in Strings.AvailableLanguages)
        {
            Strings.SetLanguage(language.Code);
            var spoken = new List<string>();

            foreach (var code in AudibleStores.Codes)
            {
                var name = new CountryOption(code, Strings.T(AudibleStores.NameKey(code))).ToString();

                if (LooksLikeUnresolvedKey(name))
                {
                    wrong++;
                    Fail($"in {language.Code}, the \"{code}\" store has no country name in the string table");
                    continue;
                }

                if (name.Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    wrong++;
                    Fail($"in {language.Code}, the \"{code}\" store would be announced by its code, \"{name}\"");
                    continue;
                }

                if (name.Contains('&'))
                {
                    wrong++;
                    Fail($"in {language.Code}, \"{name}\" contains an ampersand; a country name is plain text");
                    continue;
                }

                if (spoken.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    wrong++;
                    Fail($"in {language.Code}, two stores would both be announced as \"{name}\"");
                    continue;
                }

                spoken.Add(name);
            }
        }

        Strings.SetLanguage(previousLanguage);

        if (wrong == 0)
            Line($"   {AudibleStores.Codes.Length} countries in {Strings.AvailableLanguages.Count} languages: " +
                 "every store is announced as its country, never as its code");

        Line("");
    }

    /// <summary>
    /// The first sign-in asks for the country before it offers the sign-in, and
    /// choosing a country is reported at once so that it can be saved.
    /// </summary>
    /// <remarks>
    /// The dialog is driven the way a person drives it — through its own country list
    /// — and is never connected, so the self-test stays offline. It is given a
    /// settings object of its own, because the self-test must not write the real
    /// settings file of whoever runs it.
    /// </remarks>
    private static void CheckCountrySignInFlow()
    {
        Line("== Country-first sign-in ==");

        var settings = new AppSettings();
        var audible = new AudibleProvider { AppSettingsInstance = settings };
        var reported = new List<string>();

        var wrong = 0;
        void Complain(string message)
        {
            wrong++;
            Fail(message);
        }

        using var dialog = new AudibleCountryForm(settings, audible, reported.Add);

        if (dialog.SignInOffered)
            Complain("the sign-in is offered before a country has been chosen");

        var offered = dialog.CountryList.Items.Cast<CountryOption>()
            .Select(country => country.ToString())
            .ToList();

        if (offered.Count != AudibleStores.Codes.Length)
            Complain($"the country list offers {offered.Count} countries instead of {AudibleStores.Codes.Length}");

        if (offered.Any(LooksLikeUnresolvedKey))
            Complain("the country list shows an unresolved text key instead of a country name");

        if (offered.Any(name => name.Contains('&')))
            Complain("a country in the list has an ampersand in it; a country name is plain text");

        dialog.Choose("in");

        if (!dialog.SignInOffered)
            Complain("the sign-in is still not offered after a country was chosen");

        if (reported.Count != 1 || reported[0] != "in")
            Complain($"choosing India reported {reported.Count} country(ies) to save instead of just India");

        if (settings.AudibleLocale != "in" || !settings.AudibleCountryChosen)
            Complain($"choosing India left the account's store as \"{settings.AudibleLocale}\"");

        foreach (var control in new Control[] { dialog.CountryList, dialog.SignInButton, dialog.CancelControl })
        {
            var name = control.AccessibleName;

            if (string.IsNullOrWhiteSpace(name))
                Complain($"the sign-in dialog's {control.GetType().Name} has no spoken name");
            else if (name.Contains('&'))
                Complain($"the sign-in dialog's {control.GetType().Name} has an ampersand in its spoken name: \"{name}\"");
        }

        if (wrong == 0)
        {
            Line($"   {offered.Count} countries are offered, and no sign-in button to press until one is chosen");
            Line($"   the list is announced as \"{dialog.CountryList.AccessibleName}\", and the choice is reported for saving the moment it is made");
        }

        Line("");
    }

    /// <summary>
    /// A country is found from what is typed into the list, and an incomplete or
    /// mistyped name still finds the country it meant.
    /// </summary>
    /// <remarks>
    /// Every spelling is checked against the names the list actually shows, in every
    /// interface language, and the shortest letters that still name one country are
    /// worked out from the list itself rather than written down here — so a country
    /// added later is checked the moment it appears, and two countries sharing the
    /// first letters cannot quietly resolve to the wrong one.
    /// </remarks>
    private static void CheckCountrySearch()
    {
        Line("== Country search ==");

        var previousLanguage = Strings.Current;
        var wrong = 0;

        foreach (var language in Strings.AvailableLanguages)
        {
            Strings.SetLanguage(language.Code);

            var countries = Countries.All();
            var spellings = 0;

            foreach (var country in countries)
            {
                var ways = new List<(string Typed, string Because)>
                {
                    (country.Name, "its whole name"),
                    (ShortestOwnPrefix(countries, country), "just the letters that already name it"),
                    (CountrySearch.Normalize(country.Name), "its name typed without its accents"),
                };

                if (MistypedName(country.Name) is { } mistyped)
                    ways.Add((mistyped, "its name with a letter wrong"));

                foreach (var (typed, because) in ways)
                {
                    spellings++;

                    var found = CountrySearch.Resolve(countries, typed);

                    if (found != country)
                    {
                        wrong++;
                        Fail($"in {language.Code}, typing \"{typed}\" ({because}) finds " +
                             $"{found?.Name ?? "no country"} instead of \"{country.Name}\"");
                    }
                }
            }

            // A name that is no country at all must not land on one it is merely
            // close to.
            foreach (var nonsense in new[] { "zzz", "qqqqqq", "12345", "wxyz" })
            {
                if (CountrySearch.Resolve(countries, nonsense) is { } country)
                {
                    wrong++;
                    Fail($"in {language.Code}, typing \"{nonsense}\", which names no country, " +
                         $"finds \"{country.Name}\"");
                }
            }

            if (wrong == 0)
                Line($"   {countries.Count} countries in {language.Code}, {spellings} spellings: " +
                     "the whole name, the fewest letters that name it, its name without accents, " +
                     "and a mistyped name all find their country");
        }

        Strings.SetLanguage(previousLanguage);
        Line("");
    }

    /// <summary>
    /// Typing into the sign-in dialog's country list chooses the country, brings the
    /// sign-in on screen and reports the choice for saving — and never puts half a
    /// typed name on screen as though it were a country.
    /// </summary>
    /// <remarks>
    /// The dialog is driven by typing its own list, the way a person drives it, and is
    /// never connected, so the self-test stays offline. The names typed are the English
    /// ones, so the check sets the interface language itself rather than depending on
    /// the language whoever runs it happens to use.
    /// </remarks>
    private static void CheckCountryTyping()
    {
        Line("== Typing a country into the sign-in dialog ==");

        var previousLanguage = Strings.Current;
        Strings.SetLanguage("en");

        try
        {
            var settings = new AppSettings();
            var audible = new AudibleProvider { AppSettingsInstance = settings };
            var reported = new List<string>();
            var wrong = 0;

            void Complain(string message)
            {
                wrong++;
                Fail(message);
            }

            using var dialog = new AudibleCountryForm(settings, audible, reported.Add);
            var list = dialog.CountryList;

            (string Typed, string Country, string Because)[] ways =
            [
                ("ind", "in", "the first letters of India"),
                ("japn", "jp", "Japan with two letters the wrong way round"),
                ("grmany", "de", "Germany with a letter missing"),
                ("united k", "uk", "the United Kingdom by its second word"),
                ("ital", "it", "the first letters of Italy"),
            ];

            foreach (var (typed, code, because) in ways)
            {
                list.ForgetTyping();        // the pause between one country and the next

                if (list.TypeCountry(typed) is null)
                    Complain($"typing \"{typed}\" ({because}) found no country at all");

                if (list.SelectedCountry?.Code != code)
                    Complain($"typing \"{typed}\" ({because}) chose " +
                             $"\"{list.SelectedCountry?.Code ?? "nothing"}\" instead of \"{code}\"");

                if (list.Text != Countries.Name(code))
                    Complain($"after typing \"{typed}\" the list shows \"{list.Text}\" " +
                             $"instead of \"{Countries.Name(code)}\"");
            }

            if (!dialog.SignInOffered)
                Complain("a country typed into the list did not bring the sign-in on screen");

            if (!reported.Contains("uk"))
                Complain("a country typed into the list was not reported for saving");

            // A wrong start is not a dead end: the letters at the front are dropped
            // and what is left is looked up again.
            list.ForgetTyping();
            list.TypeCountry("ind");
            list.TypeCountry("spain");

            if (list.SelectedCountry?.Code != "es")
                Complain($"typing on past a wrong start chose " +
                         $"\"{list.SelectedCountry?.Code ?? "nothing"}\" instead of \"es\"");

            // A word that names no country leaves the choice where it was, and says so.
            list.ForgetTyping();
            list.TypeCountry("zzzz");

            if (list.SelectedCountry?.Code != "es")
                Complain($"typing something that names no country changed the choice to " +
                         $"\"{list.SelectedCountry?.Code ?? "nothing"}\"");

            if (dialog.Status != Strings.T("Country.NoMatch", list.SearchText))
                Complain($"typing something that names no country says \"{dialog.Status}\" " +
                         "instead of saying that nothing matched");

            // Taking the word back leaves the dialog saying what it said before, and
            // a new name can be typed straight away.
            foreach (var _ in "zzzz")
                list.TypeCountry("\b");

            if (list.SearchText.Length != 0)
                Complain($"the typed word is still \"{list.SearchText}\" after taking it back");

            if (list.TypeCountry("japan")?.Code != "jp")
                Complain("a country could not be typed after taking an earlier name back");

            if (wrong == 0)
            {
                Line("   a country is found from its first letters, from a later word of its name, " +
                     "and from a mistyped name");
                Line("   typing on past a wrong start finds the country being named, and a word that " +
                     "names no country chooses nothing and says so");
            }
        }
        finally
        {
            Strings.SetLanguage(previousLanguage);
        }

        Line("");
    }

    /// <summary>
    /// What a keyboard really sends the country list: one window message per letter.
    /// </summary>
    /// <remarks>
    /// The search is driven above by calling into it, which would go on passing even
    /// if the list let Windows do the searching itself. Windows' own type-ahead
    /// matches the first letters of a name and gives up on a mistyped one — typing
    /// "indai" would move the list on to Australia — so the letters are sent as the
    /// messages a keyboard sends, and the list's own answer has to come back.
    /// </remarks>
    private static void CheckCountryKeystrokes()
    {
        Line("== Country list keystrokes ==");

        var previousLanguage = Strings.Current;
        Strings.SetLanguage("en");

        try
        {
            using var host = new Form { ShowInTaskbar = false };
            host.CreateControl();

            using var list = new CountryComboBox { AccessibleName = Strings.T("Country.Label") };
            host.Controls.Add(list);
            list.Fill(Countries.All(), null);
            list.CreateControl();

            var wrong = 0;

            void Complain(string message)
            {
                wrong++;
                Fail(message);
            }

            if (list.SelectedCountry is not null)
                Complain("the country list starts on a country nobody chose");

            // A mistyped "India", letter by letter.
            SendKeystrokes(list, "indai");

            if (list.SelectedCountry?.Code != "in")
                Complain("the keystrokes of \"indai\" left the list on " +
                         $"\"{list.SelectedCountry?.Code ?? "nothing"}\" instead of \"in\"");

            // A space is part of a country's name — "United Kingdom" — so it must not
            // be taken as a key that presses a button.
            list.ForgetTyping();
            SendKeystrokes(list, "united k");

            if (list.SelectedCountry?.Code != "uk")
                Complain("the keystrokes of \"united k\" left the list on " +
                         $"\"{list.SelectedCountry?.Code ?? "nothing"}\" instead of \"uk\"");

            if (!TakesTyping(list, Keys.Space))
                Complain("the list does not count a space as typing, so an open list would take " +
                         "it as the key that presses it instead of a letter of the name");

            if (TakesTyping(list, Keys.Tab))
                Complain("the list counts Tab as typing, so it could never be tabbed away from");

            if (wrong == 0)
                Line("   letters and the space in a country's name arrive as keystrokes, and a " +
                     "mistyped name stays on the country it means");
        }
        finally
        {
            Strings.SetLanguage(previousLanguage);
        }

        Line("");
    }

    /// <summary>Sends the letters of a name to the list as one keystroke message each.</summary>
    private static void SendKeystrokes(CountryComboBox list, string typed)
    {
        foreach (var character in typed)
            SendMessage(list.Handle, WmChar, (IntPtr)character, IntPtr.Zero);
    }

    /// <summary>
    /// Asks the list what the message loop asks it before a key is let through: is
    /// this a key the user types? The space that joins "United" to "States" has to be
    /// one, and Tab must not be, or nobody could tab away from the list.
    /// </summary>
    /// <remarks>
    /// It is asked through the protected member the loop itself calls, because a key
    /// that is refused never becomes a character: there is nothing left to observe
    /// from outside once it has been taken as a key that presses a button.
    /// </remarks>
    private static bool TakesTyping(Control control, Keys key)
    {
        var isInputKey = typeof(Control).GetMethod(
            "IsInputKey", BindingFlags.Instance | BindingFlags.NonPublic);

        return isInputKey?.Invoke(control, [key]) as bool? ?? false;
    }

    /// <summary>
    /// The fewest letters that name one country and no other, taken from the list the
    /// user is looking at: "In" is enough for India, while "Es" in Spanish is not
    /// enough for Spain because it also begins the United States.
    /// </summary>
    private static string ShortestOwnPrefix(IReadOnlyList<CountryOption> countries, CountryOption country)
    {
        var name = CountrySearch.Normalize(country.Name);

        for (var length = 1; length < name.Length; length++)
        {
            var prefix = name[..length];

            var named = countries.Count(other => CountrySearch.Normalize(other.Name)
                .StartsWith(prefix, StringComparison.Ordinal));

            if (named == 1 && CountrySearch.Resolve(countries, prefix) == country)
                return prefix;
        }

        return name;
    }

    /// <summary>
    /// A plausible mistyping of a country's name: two letters of its middle the wrong
    /// way round (“Indai” for India), or a letter left out (“Grmany”). A middle
    /// letter is chosen so the name still starts the way the person typed it, and null
    /// comes back for a name too short to mistype without becoming another name.
    /// </summary>
    private static string? MistypedName(string name)
    {
        var normalized = CountrySearch.Normalize(name);
        var letters = normalized.ToCharArray();

        var middle = letters.Length / 2;
        while (middle > 0 && !char.IsLetter(letters[middle]))
            middle--;

        if (middle <= 0 || middle >= letters.Length - 1)
            return null;

        var swap = char.IsLetter(letters[middle]) && char.IsLetter(letters[middle + 1]);

        if (swap)
            (letters[middle], letters[middle + 1]) = (letters[middle + 1], letters[middle]);

        var mistyped = swap ? new string(letters) : normalized.Remove(middle, 1);

        return mistyped == normalized ? null : mistyped;
    }

    /// <summary>
    /// The keyboard shortcut system: one key means one thing, every shortcut is
    /// answered and translated, the keys the window lists on its own labels do not
    /// collide with it, the application menu and the queue menu carry the same keys
    /// the help promises, the main window carries only the controls the main job
    /// needs, and the help screen itself shows a row for every one of them.
    /// </summary>
    /// <remarks>
    /// Written to fail when something is <em>removed</em> as much as when something is
    /// added: the homepage check is an allow-list, so a utility button has to be taken
    /// off the window deliberately rather than appearing again unnoticed.
    /// </remarks>
    private static void CheckShortcuts()
    {
        Line("== Keyboard shortcuts ==");

        CheckNoClashingKeys();
        CheckShortcutsAreAnswered();
        CheckShortcutDescriptions();
        CheckMnemonicsDoNotClash();
        CheckMenuCarriesTheKeys();
        CheckHomepageIsFocused();
        CheckHelpListsEverything();

        Line("");
    }

    /// <summary>Two actions must never answer the same key combination.</summary>
    private static void CheckNoClashingKeys()
    {
        var clashes = Shortcuts.All
            .Where(shortcut => shortcut.Keys != Keys.None)
            .GroupBy(shortcut => shortcut.Keys)
            .Where(group => group.Count() > 1)
            .Select(group => $"{Shortcuts.Describe(group.Key)} ({string.Join(", ", group.Select(s => s.Action))})")
            .ToList();

        if (clashes.Count > 0)
            Fail($"two shortcuts answer the same keys: {string.Join("; ", clashes)}");
        else
            Line($"   {Shortcuts.All.Count} shortcuts: every key combination answers exactly one action");
    }

    /// <summary>
    /// Every shortcut the help says belongs to the window must really be answered by
    /// the window, and every menu shortcut must name a real key.
    /// </summary>
    private static void CheckShortcutsAreAnswered()
    {
        var unanswered = Shortcuts.All
            .Where(shortcut => shortcut.Owner == ShortcutOwner.Window
                               && !MainForm.AnsweredActions.Contains(shortcut.Action))
            .Select(shortcut => $"{Shortcuts.Describe(shortcut)} — {shortcut.Action}")
            .ToList();

        if (unanswered.Count > 0)
            Fail($"the help promises shortcuts the window does not answer: {string.Join("; ", unanswered)}");
        else
            Line($"   every one of the {MainForm.AnsweredActions.Count} window shortcuts is answered " +
                 "by the window that documents it");

        var empty = Shortcuts.All
            .Where(shortcut => string.IsNullOrWhiteSpace(Shortcuts.Describe(shortcut))
                               || Shortcuts.Describe(shortcut).Contains("None", StringComparison.Ordinal))
            .Select(shortcut => shortcut.Action)
            .ToList();

        if (empty.Count > 0)
            Fail($"these shortcuts would be read out as nothing, or as \"None\": {string.Join(", ", empty)}");
    }

    /// <summary>
    /// Every shortcut's description, and every heading the help screen shows, must be
    /// written in the person's own language rather than left in English or unresolved.
    /// </summary>
    private static void CheckShortcutDescriptions()
    {
        var previous = Strings.Current;
        var missing = new List<string>();

        try
        {
            foreach (var language in Strings.AvailableLanguages)
            {
                Strings.SetLanguage(language.Code);

                foreach (var key in Shortcuts.All.Select(shortcut => shortcut.DescriptionKey)
                             .Concat([
                                 "Help.Title", "Help.Intro", "Help.Keys", "Help.What", "Help.Label",
                                 "Help.Group.Application", "Help.Group.Window", "Help.Group.Dialogs",
                                 "Help.Group.WindowKeys",
                             ]))
                {
                    var text = Strings.T(key);

                    if (string.IsNullOrWhiteSpace(text) || text == key)
                        missing.Add($"{key} ({language.Code})");
                }
            }
        }
        finally
        {
            Strings.SetLanguage(previous);
        }

        if (missing.Count > 0)
            Fail($"the shortcut list is not written in every language: {string.Join(", ", missing)}");
        else
            Line($"   every shortcut and heading is written in all " +
                 $"{Strings.AvailableLanguages.Count} interface languages");
    }

    /// <summary>
    /// The Alt keys the window offers on its own labels are read from the live window,
    /// which is only safe if none of them is a shortcut the window answers itself:
    /// one press must not do two things.
    /// </summary>
    private static void CheckMnemonicsDoNotClash()
    {
        using var window = new MainForm();
        window.CreateControl();

        var offered = Shortcuts.Mnemonics(window);

        if (offered.Count == 0)
        {
            Fail("the window offers no Alt keys at all, so the last part of the help would be empty");
            return;
        }

        var clash = new List<string>();
        var answered = Shortcuts.All
            .Where(shortcut => shortcut.Owner == ShortcutOwner.Window
                               && (shortcut.Keys & Keys.Alt) == Keys.Alt)
            .Select(shortcut => Shortcuts.Describe(shortcut))
            .ToList();

        foreach (var (keys, what) in offered)
        {
            if (answered.Contains(keys))
                clash.Add($"{keys} ({what})");
        }

        if (clash.Count > 0)
            Fail($"these Alt keys are offered by a label and answered by the window as well, " +
                 $"so one press would do two things: {string.Join("; ", clash)}");
        else
            Line($"   {offered.Count} Alt keys are read from the window's own labels " +
                 $"({string.Join(", ", offered.Select(item => item.Keys))}), and none of them is " +
                 "also a shortcut");

        var unpronounceable = offered.Where(item => item.What.Contains('&') || item.What.Trim().Length == 0).ToList();

        if (unpronounceable.Count > 0)
            Fail("an Alt key would be announced with a mnemonic or with nothing: " +
                 string.Join(", ", unpronounceable.Select(item => item.Keys)));

        CheckHiddenControlsAreLeftOut();
    }

    /// <summary>
    /// A control the application has taken off the window must not still be listed as
    /// an Alt key: that is exactly how the removed buttons kept being documented. The
    /// bearer here holds one labelled control and one the application has hidden.
    /// </summary>
    private static void CheckHiddenControlsAreLeftOut()
    {
        using var bearer = new Panel();

        var offered = new Label
        {
            Text = "&Search books",
            AccessibleName = "Search books",
            AutoSize = true,
        };

        var takenOff = new Label
        {
            Text = "&Keys and folder",
            AccessibleName = "Keys and folder",
            AutoSize = true,
            Visible = false,
        };

        // The bearer is never parented to a window, so it reports its own visibility
        // and the hidden control is hidden for the same reason a removed button is.
        bearer.Controls.Add(offered);
        bearer.Controls.Add(takenOff);

        if (!bearer.Visible)
        {
            Fail("a control outside a window cannot tell its own visibility, so the shortcut " +
                 "list cannot leave out what the window has taken off it");
            return;
        }

        var keys = Shortcuts.Mnemonics(bearer).Select(item => item.Keys).ToList();

        if (!keys.Contains("Alt + S"))
            Fail("the shortcut list leaves out a control that is on the window");
        else if (keys.Any(key => key == "Alt + K"))
            Fail("the shortcut list still offers an Alt key for a control that has been " +
                 "taken off the window");
        else
            Line("   a control the window has taken off it is not offered as a shortcut any more");
    }

    /// <summary>
    /// The keys the application menu itself holds must be exactly the keys the help
    /// lists for it: a menu item whose shortcut moved, and a help screen that did not,
    /// is how a documented key stops working.
    /// </summary>
    private static void CheckMenuCarriesTheKeys()
    {
        using var window = new MainForm();
        window.CreateControl();

        var inMenu = MenuShortcuts(window).ToList();
        var promised = Shortcuts.All
            .Where(shortcut => shortcut.Owner == ShortcutOwner.Menu)
            .Select(shortcut => shortcut.Keys)
            .ToList();

        var missing = promised.Except(inMenu).Select(Shortcuts.Describe).ToList();
        var extra = inMenu.Except(promised).Select(Shortcuts.Describe).ToList();

        if (missing.Count > 0)
            Fail($"the help lists menu shortcuts the menu does not have: {string.Join(", ", missing)}");
        else if (extra.Count > 0)
            Fail($"the menu has shortcuts the help does not list: {string.Join(", ", extra)}");
        else
            Line($"   the application menu carries exactly the {inMenu.Count} keys the help lists for it");

        // F1 is how the list is opened from anywhere, including from a dialog.
        if (!inMenu.Contains(Keys.F1))
            Fail("no menu item answers F1, so the shortcut list cannot be opened from the keyboard alone");

        CheckQueueMenuShowsTheKeys(window);
    }

    /// <summary>
    /// The download queue shows its actions' keys beside them. A menu that says
    /// "Ctrl + R" while the application answers something else is worse than one that
    /// says nothing, so the displayed text is compared with the shortcut table.
    /// </summary>
    private static void CheckQueueMenuShowsTheKeys(MainForm window)
    {
        var queue = Descendants(window).OfType<ListBox>()
            .FirstOrDefault(list => list.Name == "queueList");

        if (queue?.ContextMenuStrip is not { } menu)
        {
            Fail("the download queue has no menu of its own, so Retry, Remove and Clear " +
                 "finished cannot be reached without a mouse");
            return;
        }

        var expected = new Dictionary<string, string>
        {
            ["queueMenuRetry"] = Describe(ShortcutAction.RetryQueueItem),
            ["queueMenuRemove"] = Describe(ShortcutAction.RemoveQueueItem),
            ["queueMenuClearFinished"] = Describe(ShortcutAction.ClearFinishedItems),
        };

        var wrong = new List<string>();

        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            if (!expected.TryGetValue(item.Name ?? string.Empty, out var keys))
                continue;

            if (!string.Equals(item.ShortcutKeyDisplayString, keys, StringComparison.Ordinal))
                wrong.Add($"{item.Name} shows \"{item.ShortcutKeyDisplayString}\" rather than \"{keys}\"");
        }

        if (wrong.Count > 0)
            Fail($"the queue's menu shows the wrong keys: {string.Join("; ", wrong)}");
        else
            Line("   the queue's menu shows the same keys the application answers");
    }

    /// <summary>The keys the shortcut table gives one action, as the help would show them.</summary>
    private static string Describe(ShortcutAction action) =>
        Shortcuts.Describe(Shortcuts.All.First(shortcut => shortcut.Action == action));

    /// <summary>Every shortcut key the window's own menu strip holds.</summary>
    private static IEnumerable<Keys> MenuShortcuts(Form form)
    {
        if (form.MainMenuStrip is not { } strip)
            yield break;

        foreach (var keys in Items(strip.Items))
            yield return keys;

        static IEnumerable<Keys> Items(ToolStripItemCollection items)
        {
            foreach (var item in items.Cast<ToolStripItem>())
            {
                if (item is ToolStripMenuItem { ShortcutKeys: not Keys.None } menu)
                    yield return menu.ShortcutKeys;

                if (item is ToolStripMenuItem parent)
                    foreach (var child in Items(parent.DropDownItems))
                        yield return child;
            }
        }
    }

    /// <summary>
    /// The main window carries only the controls the main job needs. The list below is
    /// deliberate: a button that is not on it is a button somebody has to add here on
    /// purpose, which is the point of moving the rest onto shortcuts and menus.
    /// </summary>
    private static void CheckHomepageIsFocused()
    {
        using var window = new MainForm();
        window.CreateControl();

        string[] permanent =
        [
            Strings.T("Main.SearchButton"),
            Strings.T("Main.Download"),
            Strings.T("Main.Cancel"),
            // The account is the one thing the window cannot make a shortcut of: it
            // says whether anyone is signed in, so it has to be on screen.
            Strings.T("Main.SignIn"),
            Strings.T("Main.SignOut"),
            // Only ever shown while it has something to say.
            Strings.T("Main.GotIt"),
        ];

        for (var i = 0; i < permanent.Length; i++)
            permanent[i] = Strings.Plain(permanent[i]).Trim();

        var buttons = Descendants(window).OfType<ButtonBase>()
            .Select(button => Strings.Plain(button.AccessibleName ?? button.Text).Trim())
            .ToList();

        var unexpected = buttons.Where(name => !permanent.Contains(name)).Distinct().ToList();

        if (unexpected.Count > 0)
            Fail($"the main window carries controls that are not part of the main job: " +
                 $"{string.Join(", ", unexpected.Select(name => $"\"{name}\""))}");
        else
            Line($"   the main window offers {buttons.Distinct().Count()} buttons — the search, " +
                 "download and cancel actions and the account — and nothing else");
    }

    /// <summary>
    /// The help screen must really list every shortcut and every Alt key the window
    /// offers, in the person's own language, in rows a screen reader can read out.
    /// </summary>
    private static void CheckHelpListsEverything()
    {
        using var window = new MainForm();
        window.CreateControl();

        var offered = Shortcuts.Mnemonics(window).Count;
        var previous = Strings.Current;

        try
        {
            foreach (var language in Strings.AvailableLanguages)
            {
                Strings.SetLanguage(language.Code);

                using var help = NewKeyboardHelpForm(window);
                help.CreateControl();

                var list = Descendants(help).OfType<ListView>().FirstOrDefault();

                if (list is null)
                {
                    Fail($"the shortcut list has no rows to read in {language.Code}");
                    return;
                }

                var expected = Shortcuts.All.Count + offered;

                if (list.Items.Count != expected)
                {
                    Fail($"the shortcut list shows {list.Items.Count} rows in {language.Code} " +
                         $"rather than the {expected} shortcuts the application has");
                    return;
                }

                var unreadable = list.Items.Cast<ListViewItem>()
                    .Where(item => item.SubItems.Count < 2
                                   || string.IsNullOrWhiteSpace(item.SubItems[0].Text)
                                   || string.IsNullOrWhiteSpace(item.SubItems[1].Text)
                                   || LooksLikeUnresolvedKey(item.SubItems[1].Text))
                    .Select(item => item.Text)
                    .ToList();

                if (unreadable.Count > 0)
                {
                    Fail($"a shortcut row reads as a key rather than in words in {language.Code}: " +
                         string.Join("; ", unreadable));
                    return;
                }

                if (list.Items.Cast<ListViewItem>().Any(item => item.Group is null))
                {
                    Fail($"a shortcut row sits outside every group in {language.Code}, so nothing " +
                         "says which part of the application it belongs to");
                    return;
                }
            }

            Line($"   the shortcut list shows all {Shortcuts.All.Count} shortcuts and all " +
                 $"{offered} Alt keys of the window, in words, in every language");
        }
        catch (Exception ex)
        {
            Fail($"the shortcut list could not be read: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Strings.SetLanguage(previous);
        }
    }

    /// <summary>
    /// Prove the screen-reader plumbing really works: a live region must be able
    /// to raise the operating-system accessibility event for a text change. This
    /// is what makes NVDA read status updates without any built-in voice.
    /// </summary>
    private static void CheckLiveRegion()
    {
        Line("== Accessibility live regions ==");
        try
        {
            using var host = new Form { ShowInTaskbar = false };
            host.CreateControl();

            var label = new LiveRegionLabel { Text = "Ready.", AccessibleName = "Status" };
            host.Controls.Add(label);
            label.CreateControl();

            label.Text = "Testing announcements.";
            label.AccessibilityObject.RaiseLiveRegionChanged();
            Line("   status live region raised a change event");
        }
        catch (Exception ex)
        {
            Fail($"the status live region cannot announce changes: {ex.GetType().Name}: {ex.Message}");
        }

        Line("");
    }

    /// <summary>
    /// The Audible sign-in window exactly as the app builds it, but never shown:
    /// an address to open and the device cookies Audible issues for it. Building
    /// it contacts nothing — the embedded browser starts only when the window is
    /// shown — so the self-test stays offline.
    /// </summary>
    internal static AudibleSignInForm NewSignInForm() => new(new AudibleSignInRequest(
        "https://www.amazon.com/ap/signin?openid.mode=checkid_setup",
        new CookieCollection { new Cookie("frc", "device", "/ap", ".amazon.com") }));

    /// <summary>
    /// The shortcut list exactly as the app builds it, describing a window that is
    /// never shown: reading a window's own labels for its Alt keys needs the window,
    /// but nothing about it has to be on screen.
    /// </summary>
    internal static KeyboardHelpForm NewKeyboardHelpForm(Control window) => new(window);

    /// <summary>
    /// The same list for the checks that only want a form to look at: a throwaway
    /// window stands in for the main one, so those checks describe no Alt keys.
    /// </summary>
    internal static KeyboardHelpForm NewKeyboardHelpForm() => new(new Form());

    /// <summary>
    /// The country-first sign-in dialog exactly as the app builds it, but never
    /// shown and never connected: nothing is signed in until the button is pressed,
    /// so the self-test stays offline.
    /// </summary>
    internal static AudibleCountryForm NewCountrySignInForm()
    {
        var settings = new AppSettings();
        return new AudibleCountryForm(settings, new AudibleProvider { AppSettingsInstance = settings });
    }

    private static bool IsInteractive(Control control) =>
        control is ButtonBase or TextBoxBase or ComboBox or ListBox or ListView or NumericUpDown
            or ProgressBar or TrackBar or DateTimePicker or TabControl or WebView2
        || control is Form { MainMenuStrip: not null };

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child))
                yield return grandChild;
        }
    }

    private static string Describe(Control control)
    {
        var type = control.GetType().Name;
        var text = string.IsNullOrWhiteSpace(control.Text) ? "" : $" \"{control.Text.Replace("&", "")}\"";
        return $"{type}{text}";
    }

    private static void Line(string text) => Report.AppendLine(text);

    private static void Fail(string message)
    {
        _failures++;
        Report.AppendLine($"   FAIL: {message}");
    }
}
