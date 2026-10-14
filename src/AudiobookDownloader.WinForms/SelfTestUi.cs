using System.Reflection;
using AudiobookDownloader.Core;
using System.Windows.Forms.Automation;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Checks the window behaviour that a screenshot cannot show: that progress and
/// status announce through separate live regions, that hiding the activity log
/// leaves no empty strip behind, and that the sign-in and key dialogs really are
/// translated rather than left in English.
/// </summary>
internal static class SelfTestUi
{
    public static void Run(Action<string> line, Action<string> fail)
    {
        CheckNoSpeechEngine(line, fail);
        CheckThemeFollowsWindows(line, fail);
        CheckLiveRegionsAreSeparate(line, fail);
        CheckNoActivityLog(line, fail);
        CheckSounds(line, fail);
        CheckSettingsTabsKeepFocus(line, fail);
        CheckSettingsLayout(line, fail);
        CheckNoBookLanguageOnMainWindow(line, fail);
        CheckBookMenuAndDetails(line, fail);
        CheckDialogsAreLocalized(line, fail);
    }

    /// <summary>
    /// The app must never read the interface aloud with a voice of its own, so no
    /// speech engine may be referenced, loaded or defined: that would talk over the
    /// user's own screen reader. Accessibility goes through the operating system's
    /// accessibility tree instead, and NVDA or Narrator does the talking.
    /// </summary>
    private static void CheckNoSpeechEngine(Action<string> line, Action<string> fail)
    {
        line("== No built-in voice ==");

        var app = typeof(SelfTestUi).Assembly;
        var offenders = new List<string>();

        foreach (var referenced in app.GetReferencedAssemblies())
        {
            if (IsSpeechEngine(referenced.Name))
                offenders.Add($"{referenced.Name} (referenced)");
        }

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (IsSpeechEngine(loaded.GetName().Name))
                offenders.Add($"{loaded.GetName().Name} (loaded)");
        }

        try
        {
            foreach (var type in app.GetTypes())
            {
                if (type.Name.Contains("Speech", StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{type.FullName} (type)");
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            offenders.AddRange(ex.Types
                .Where(t => t is not null && t.Name.Contains("Speech", StringComparison.OrdinalIgnoreCase))
                .Select(t => $"{t!.FullName} (type)"));
        }

        // A speech engine sitting beside the app could still be loaded by name at
        // run time, so a stray copy counts as carrying one.
        try
        {
            var folder = AppContext.BaseDirectory;
            if (Directory.Exists(folder))
            {
                offenders.AddRange(Directory
                    .EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .Where(IsSpeechEngine)
                    .Select(name => $"{name} (shipped beside the app)"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked or unreadable folder is not evidence of a speech engine.
        }

        if (offenders.Count > 0)
            fail($"the app carries a speech engine ({string.Join(", ", offenders.Distinct())}), so it " +
                 "could speak over the user's own screen reader");
        else
            line("   no speech engine is referenced, loaded or defined; the screen reader does the talking");

        line("");
    }

    /// <summary>Names of the speech engines this app must never depend on.</summary>
    private static bool IsSpeechEngine(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Assembly names arrive bare; file names arrive with ".dll" attached.
        var bare = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

        return bare.Equals("System.Speech", StringComparison.OrdinalIgnoreCase)
               || bare.Equals("Microsoft.Speech", StringComparison.OrdinalIgnoreCase)
               || bare.Equals("Windows.Media.SpeechSynthesis", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The colours the window picks for itself must come from the Windows palette
    /// and be applied through the theme helper, which re-applies them when Windows
    /// changes its colours. A fixed colour — dark slate grey for hints — is
    /// unreadable on a high-contrast background.
    /// </summary>
    private static void CheckThemeFollowsWindows(Action<string> line, Action<string> fail)
    {
        line("== Following the Windows theme ==");

        using var form = new MainForm();
        form.CreateControl();

        var providerStatus = Descendants(form).OfType<Label>()
            .FirstOrDefault(l => l.AccessibleName == Strings.T("Main.ProviderStatus"));
        if (providerStatus is null)
        {
            fail("the status hint could not be found in the window");
            return;
        }

        if (providerStatus.ForeColor != SystemTheme.HintText)
            fail($"hint text uses the fixed colour {providerStatus.ForeColor.Name} rather than the " +
                 "Windows palette, so high contrast would leave it unreadable");
        else
            line($"   hints follow the Windows palette " +
                 $"(high contrast is {(SystemTheme.HighContrast ? "on" : "off")})");

        line("");
    }

    /// <summary>
    /// Progress used to be announced on the status line, so a download reaching a
    /// milestone wiped out whatever the last status message was. Each now has a
    /// region of its own.
    /// </summary>
    private static void CheckLiveRegionsAreSeparate(Action<string> line, Action<string> fail)
    {
        line("== Progress and status announcements ==");

        using var form = new MainForm();
        form.CreateControl();

        var regions = Descendants(form).OfType<LiveRegionLabel>().ToList();
        var names = regions.Select(r => r.AccessibleName ?? "(unnamed)").ToList();

        if (regions.Count < 2)
            fail($"the window has {regions.Count} live region(s); progress needs one of its own, " +
                 "separate from the status line");
        else if (names.Distinct().Count() != names.Count)
            fail($"two live regions share a name ({string.Join(", ", names)}), so a screen reader " +
                 "cannot tell them apart");
        else if (!regions.Any(r => r.AccessibleName == Strings.T("Main.ProgressStatus")))
            fail("the progress live region has no accessible name");
        else if (regions.Any(r => r.LiveSetting == AutomationLiveSetting.Off))
            fail("a live region is switched off, so nothing will be announced");
        else
            line($"   separate live regions: {string.Join(", ", names)}");

        line("");
    }

    /// <summary>
    /// The main window shows the download's percentage and status, not a scrolling
    /// log: nothing there may be a multi-line text box.
    /// </summary>
    private static void CheckNoActivityLog(Action<string> line, Action<string> fail)
    {
        line("== No activity log on the main window ==");

        using var form = new MainForm();
        form.CreateControl();

        if (Descendants(form).OfType<TextBox>().Any(t => t.Multiline))
            fail("the main window still has a multi-line log box");
        else
            line("   no log box; progress is the progress bar and its percentage");
        line("");
    }

    /// <summary>
    /// The progress beeps follow NVDA's pitches, and the beep is real WAV data
    /// Windows can play. Nothing is played here.
    /// </summary>
    private static void CheckSounds(Action<string> line, Action<string> fail)
    {
        line("== Progress beeps ==");

        if (Sounds.ProgressBeepFrequency(0) != 110 || Sounds.ProgressBeepFrequency(50) != 440 ||
            Sounds.ProgressBeepFrequency(100) != 1760)
            fail($"progress beeps are pitched {Sounds.ProgressBeepFrequency(0)}, {Sounds.ProgressBeepFrequency(50)} " +
                 $"and {Sounds.ProgressBeepFrequency(100)} Hz at 0, 50 and 100 percent, not NVDA's 110, 440 and 1760");

        foreach (var (name, wave, seconds) in new[]
                 {
                     ("beep", Sounds.BeepWave(440), 0.04),
                 })
        {
            var riff = System.Text.Encoding.ASCII.GetString(wave, 0, 4) == "RIFF" &&
                       System.Text.Encoding.ASCII.GetString(wave, 8, 4) == "WAVE" &&
                       BitConverter.ToInt32(wave, 4) == wave.Length - 8;
            var length = (wave.Length - 44) / 2 / 44100.0;
            var loudest = 0;
            for (var i = 44; i + 1 < wave.Length; i += 2)
                loudest = Math.Max(loudest, Math.Abs((int)BitConverter.ToInt16(wave, i)));

            if (!riff)
                fail($"the {name} is not a valid WAV file");
            else if (Math.Abs(length - seconds) > 0.01)
                fail($"the {name} lasts {length:0.000} s instead of {seconds:0.000} s");
            else if (loudest < 3000 || loudest == short.MaxValue)
                fail($"the {name} is {(loudest < 3000 ? "almost silent" : "clipped")}");
            else
                line($"   {name}: {length:0.00} s, valid WAV");
        }
        line("");
    }

    /// <summary>
    /// Left and Right on the Settings section tabs change the section and leave
    /// focus on the tabs; it used to jump into the page, so the next arrow press
    /// moved through the page's controls instead of to the next section.
    /// </summary>
    private static void CheckSettingsTabsKeepFocus(Action<string> line, Action<string> fail)
    {
        line("== Settings section tabs keep focus ==");

        var settings = new AppSettings { SavePath = Path.Combine(Path.GetTempPath(), "selftest-settings-tabs.json") };
        using var form = new SettingsForm(settings, new AudibleProvider { AppSettingsInstance = settings })
        {
            ShowInTaskbar = false,
            Opacity = 0,
        };
        form.Show();
        Application.DoEvents();

        var tabs = Descendants(form).OfType<TabControl>().Single();
        tabs.Focus();
        Application.DoEvents();
        for (var i = 1; i < tabs.TabCount; i++)
        {
            // A real Right arrow press, as the keyboard sends it.
            SendMessage(tabs.Handle, 0x0100 /* WM_KEYDOWN */, 0x27 /* VK_RIGHT */, 0);
            SendMessage(tabs.Handle, 0x0101 /* WM_KEYUP */, 0x27, 0);
            Application.DoEvents();
            if (tabs.SelectedIndex != i)
            {
                fail($"Right arrow on the section tabs went to section {tabs.SelectedIndex + 1}, not {i + 1}");
                form.Close();
                line("");
                return;
            }
            if (!tabs.Focused)
            {
                fail($"moving to the \"{tabs.SelectedTab?.Text}\" section took focus off the section tabs " +
                     $"(it went to {form.ActiveControl?.AccessibleName ?? "nothing"})");
                form.Close();
                line("");
                return;
            }
        }
        line($"   focus stayed on the tabs through all {tabs.TabCount} sections");

        // From inside a page (Ctrl + Tab, or anything but an arrow on the tabs),
        // focus goes into the new page.
        tabs.SelectedIndex = 0;
        Application.DoEvents();
        var first = Descendants(tabs.SelectedTab!).FirstOrDefault(c => c != tabs.SelectedTab && c.CanSelect && c.TabStop);
        first?.Select();
        Application.DoEvents();
        var before = form.ActiveControl;
        tabs.SelectedIndex = 1;
        Application.DoEvents();
        var now = form.ActiveControl;
        if (before is null || before == tabs)
            fail($"could not put focus inside the first section (it is on {before?.AccessibleName ?? "nothing"})");
        else if (now is null || now == tabs || !tabs.SelectedTab!.Contains(now))
            fail($"changing section from inside a page left focus on {now?.AccessibleName ?? "nothing"}, " +
                 "not in the new page");
        else
            line($"   from inside a page, focus moves into the new section ({now.AccessibleName})");

        form.Close();
        line("");
    }

    /// <summary>
    /// Settings has five tabs, each made of named groups, and in every language no
    /// two Alt letters on one tab are the same (a clash makes Alt + that letter
    /// go to the first one only).
    /// </summary>
    private static void CheckSettingsLayout(Action<string> line, Action<string> fail)
    {
        line("== Settings tabs, groups and Alt keys ==");

        var previous = Strings.Current;
        try
        {
            foreach (var language in Strings.AvailableLanguages)
            {
                Strings.SetLanguage(language.Code);
                var settings = new AppSettings { SavePath = Path.Combine(Path.GetTempPath(), "selftest-settings-layout.json") };
                using var form = new SettingsForm(settings, new AudibleProvider { AppSettingsInstance = settings });
                form.CreateControl();

                var tabs = Descendants(form).OfType<TabControl>().Single();
                if (tabs.TabCount != 5)
                    fail($"{language.Code}: Settings has {tabs.TabCount} tabs instead of 5");

                // OK, Cancel and Apply sit under every tab, so their letters count on each.
                var footer = Descendants(form).OfType<Button>().Where(b => !tabs.Contains(b)).ToList();
                foreach (TabPage page in tabs.TabPages)
                {
                    var groups = Descendants(page).OfType<GroupBox>().ToList();
                    if (groups.Count == 0)
                        fail($"{language.Code}: the \"{page.Text}\" tab has no groups");
                    foreach (var group in groups.Where(g => string.IsNullOrWhiteSpace(g.Text)))
                        fail($"{language.Code}: a group on the \"{page.Text}\" tab has no name");

                    var letters = Descendants(page).Concat(footer)
                        .Where(c => c is Label or CheckBox or Button && c.Text.Contains('&'))
                        .Select(c => (Control: c, Letter: MnemonicOf(c.Text)))
                        .Where(x => x.Letter is not null)
                        .GroupBy(x => x.Letter, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() > 1);
                    foreach (var clash in letters)
                        fail($"{language.Code}: on the \"{page.Text}\" tab Alt+{clash.Key} belongs to " +
                             string.Join(" and ", clash.Select(x => $"\"{Strings.Plain(x.Control.Text)}\"")));
                }
            }
            line($"   5 tabs in named groups, no clashing Alt keys, in all {Strings.AvailableLanguages.Count} languages");
        }
        finally
        {
            Strings.SetLanguage(previous);
        }
        line("");
    }

    /// <summary>The letter after a single "&amp;", or null ("&amp;&amp;" is a plain ampersand).</summary>
    private static string? MnemonicOf(string text)
    {
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '&')
                continue;
            if (text[i + 1] == '&')
            {
                i++;
                continue;
            }
            return text[i + 1].ToString().ToUpperInvariant();
        }
        return null;
    }

    /// <summary>
    /// The book language is chosen in Settings, Language, for every source that has
    /// one; the main window no longer has a list that comes and goes with the source.
    /// </summary>
    private static void CheckNoBookLanguageOnMainWindow(Action<string> line, Action<string> fail)
    {
        line("== Book language lives in Settings ==");

        using var form = new MainForm();
        form.CreateControl();
        var combos = Descendants(form).OfType<ComboBox>().Select(c => c.Name).ToList();
        if (combos.Contains("languageCombo"))
            fail("the main window still has a book-language list");

        var settings = new AppSettings { SavePath = Path.Combine(Path.GetTempPath(), "selftest-settings-language.json") };
        using var dialog = new SettingsForm(settings, new AudibleProvider { AppSettingsInstance = settings },
            SettingsForm.SectionLanguage);
        dialog.CreateControl();
        var page = Descendants(dialog).OfType<TabControl>().Single().TabPages[SettingsForm.SectionLanguage];
        var lists = Descendants(page).OfType<ComboBox>().ToList();
        if (lists.Count != 4)
            fail($"Settings, Language has {lists.Count} lists instead of 4 (the app, Audible, LibriVox, Gutenberg)");
        else if (lists.Skip(1).Any(l => l.Items.Count < 2))
            fail("a book-language list in Settings has no languages to choose from");
        else
            line($"   the main window has no language list; Settings, Language has the app's and 3 book languages " +
                 $"({string.Join(", ", lists.Skip(1).Select(l => l.Items.Count))} choices)");
        line("");
    }

    /// <summary>
    /// The Applications key on a book offers Download, Play and Book details; the
    /// details open as text read line by line with the arrows; the progress bar can
    /// take focus, so Download can send focus there; and there is no queue list.
    /// </summary>
    private static void CheckBookMenuAndDetails(Action<string> line, Action<string> fail)
    {
        line("== Book menu, details and progress ==");

        using var form = new MainForm();
        form.CreateControl();

        var books = Descendants(form).OfType<ListBox>().Single(l => l.Name == "libraryList");
        var items = books.ContextMenuStrip?.Items.OfType<ToolStripMenuItem>().Select(i => i.Name ?? "").ToList() ?? [];
        if (items.FirstOrDefault() != "bookMenuDownload" || items.Count != 3)
            fail($"the book list's menu should be Download, Play, Book details; it has {items.Count} item(s)");
        else
            line("   the book list's menu: Download, Play, Book details");

        if (Descendants(form).OfType<ListBox>().Any(l => l.Name == "queueList"))
            fail("the main window still has a download queue list");

        var bar = Descendants(form).OfType<ProgressBar>().Single();
        if (bar is not FocusableProgressBar || !bar.TabStop)
            fail("the progress bar cannot take focus, so Download cannot send the screen reader to it");
        else
            line("   the progress bar can take focus");

        using var format = new DownloadFormatForm("A Book",
            [("m4b", "M4B"), ("mp3", "MP3"), ("m4b-chapters", "M4B per chapter"), ("mp3-chapters", "MP3 per chapter")],
            "mp3-chapters");
        format.CreateControl();
        var list = Descendants(format).OfType<ComboBox>().Single();
        if (list.Items.Count != 4 || format.ChosenCode != "mp3-chapters" || format.AcceptButton is null)
            fail("the format window does not offer the formats, start on the last choice, or answer Enter");
        else
            line("   Download asks for the format: 4 Audible formats, the last choice first, Enter downloads");

        using var details = new BookDetailsForm("A Book", ["Title: A Book", "Author: Someone", "Length: 1 hour"]);
        details.CreateControl();
        var box = Descendants(details).OfType<TextBox>().Single();
        if (!box.Multiline || !box.ReadOnly || box.Lines.Length != 3)
            fail($"book details are not a read-only text of one line per fact ({box.Lines.Length} lines)");
        else
            line("   book details: read-only text, one fact per line, read with Up and Down");
        line("");
    }

    /// <summary>
    /// The sign-in, country and decryption-key dialogs are the ones a signed-out
    /// user meets first. Each string is compared against the same dialog in Hindi.
    /// </summary>
    private static void CheckDialogsAreLocalized(Action<string> line, Action<string> fail)
    {
        line("== Sign-in, country and key dialogs in every language ==");

        var previous = Strings.Current;
        try
        {
            (string Name, Func<Form> Build)[] dialogs =
            [
                ("Sign-in window", SelfTest.NewSignInForm),
                ("Country sign-in", SelfTest.NewCountrySignInForm),
            ];

            foreach (var (name, build) in dialogs)
            {
                Strings.SetLanguage("en");
                using var english = build();
                english.CreateControl();

                Strings.SetLanguage("hi");
                using var hindi = build();
                hindi.CreateControl();

                var englishTexts = Texts(english);
                var hindiTexts = Texts(hindi);

                if (englishTexts.Count != hindiTexts.Count)
                {
                    fail($"{name}: the dialog does not build the same way in Hindi and English");
                    continue;
                }

                if (englishTexts.Count == 0)
                {
                    fail($"{name}: no text could be read from the dialog");
                    continue;
                }

                var translated = englishTexts
                    .Zip(hindiTexts)
                    .Count(pair => !string.Equals(pair.First, pair.Second, StringComparison.Ordinal));

                if (translated < 5)
                    fail($"{name}: only {translated} of {englishTexts.Count} strings are translated, " +
                         "so it is still English-only");
                else
                    line($"   {name}: {translated} of {englishTexts.Count} strings translated");
            }
        }
        finally
        {
            Strings.SetLanguage(previous);
        }

        line("");
    }

    /// <summary>Everything a screen reader would read out of the dialog, in order.</summary>
    private static List<string> Texts(Form form)
    {
        var texts = new List<string> { form.Text };

        foreach (var control in Descendants(form))
        {
            if (control is not (Label or ButtonBase or TextBoxBase))
                continue;

            texts.Add(control.AccessibleName
                      ?? (control is TextBoxBase ? string.Empty : control.Text));
        }

        return texts;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child))
                yield return grandChild;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
