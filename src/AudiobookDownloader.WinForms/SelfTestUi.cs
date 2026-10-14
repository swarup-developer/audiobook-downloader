using System.Reflection;
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
        CheckActivityLogRowCollapses(line, fail);
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
    /// changes its colours. A fixed colour — dark slate grey for hints, white for
    /// the log — is unreadable on a high-contrast background.
    /// </summary>
    private static void CheckThemeFollowsWindows(Action<string> line, Action<string> fail)
    {
        line("== Following the Windows theme ==");

        using var form = new MainForm();
        form.CreateControl();

        var providerStatus = Descendants(form).OfType<Label>()
            .FirstOrDefault(l => l.AccessibleName == Strings.T("Main.ProviderStatus"));
        var log = Descendants(form).OfType<TextBox>().FirstOrDefault(t => t.Multiline);

        if (providerStatus is null || log is null)
        {
            fail("the status hint or the activity log could not be found in the window");
            return;
        }

        if (providerStatus.ForeColor != SystemTheme.HintText)
            fail($"hint text uses the fixed colour {providerStatus.ForeColor.Name} rather than the " +
                 "Windows palette, so high contrast would leave it unreadable");
        else if (log.BackColor != SystemTheme.ReadOnlyBackground)
            fail($"the activity log uses the fixed colour {log.BackColor.Name} rather than the " +
                 "Windows palette");
        else
            line($"   hints and the activity log follow the Windows palette " +
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
    /// The activity log sits in its own table row. Hiding the text box used to
    /// leave the row's height behind as an empty 100-pixel strip, and the
    /// shortcut that was meant to bring the log back did nothing because an
    /// invisible control cannot take focus.
    /// </summary>
    private static void CheckActivityLogRowCollapses(Action<string> line, Action<string> fail)
    {
        line("== Activity log row ==");

        using var form = new MainForm();
        form.CreateControl();

        var log = Descendants(form).OfType<TextBox>().FirstOrDefault(t => t.Multiline);

        // The log lives in a row of its own inside whatever layout holds it
        // (today the status group's table), so find that layout by its child.
        var table = log?.Parent as TableLayoutPanel;

        if (table is null || log is null)
        {
            fail("the activity log or the layout holding it could not be found");
            return;
        }

        var row = -1;
        for (var i = 0; i < table.RowCount; i++)
        {
            if (table.GetControlFromPosition(0, i) == log)
                row = i;
        }

        if (row < 0)
        {
            fail("the activity log is not in a row of its own");
            return;
        }

        form.SetActivityLogVisible(false);
        var hidden = table.RowStyles[row].Height;
        var logHidden = !log.Visible;

        form.SetActivityLogVisible(true);
        var shown = table.RowStyles[row].Height;

        if (!logHidden)
            fail("hiding the activity log left it on screen");
        else if (hidden != 0F)
            fail($"hiding the activity log left {hidden:0} pixels of empty space behind");
        else if (shown <= 0F)
            fail("showing the activity log again gave it no height");
        else
            line($"   hiding the log collapses its row to 0, showing it restores {shown:0} pixels");

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
                ("Key prompt", () => new KeyPromptForm("book.aaxc", askKeyIv: true, askActivationBytes: false)),
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
}
