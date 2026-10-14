using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Interface text in every language the app speaks. Keys are stable names; a
/// missing translation falls back to English, and a missing key falls back to
/// the key itself so nothing ever renders as blank.
/// </summary>
internal static partial class Strings
{
    /// <summary>Languages the interface can be shown in.</summary>
    public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        new("en", "English"),
        new("hi", "हिन्दी"),
        new("es", "Español"),
        new("bn", "বাংলা"),
        new("mr", "मराठी"),
        new("ta", "தமிழ்"),
        new("ur", "اردو"),
    ];

    /// <summary>
    /// The languages kept in their own files (Strings.bn.cs and so on) are added to
    /// the table once, before any text is looked up.
    /// </summary>
    static Strings()
    {
        Merge("bn", Bengali);
        Merge("mr", Marathi);
        Merge("ta", Tamil);
        Merge("ur", Urdu);
    }

    /// <summary>
    /// What is wrong with the translations, for the self-test: a translation for a key
    /// that does not exist (a typo, so it would never be shown), or a key translated
    /// into Hindi but missing in one of the other interface languages.
    /// </summary>
    internal static IEnumerable<string> TranslationProblems()
    {
        foreach (var (code, translations) in new[] { ("bn", Bengali), ("mr", Marathi), ("ta", Tamil), ("ur", Urdu) })
        {
            foreach (var key in translations.Keys.Where(key => !Table.ContainsKey(key)))
                yield return $"{code}: \"{key}\" is not a text key";

            foreach (var key in Table.Where(row => row.Value.ContainsKey("hi") && !row.Value.ContainsKey(code)).Select(row => row.Key))
                yield return $"{code}: \"{key}\" is not translated";

            foreach (var (key, text) in translations)
            {
                if (!Table.TryGetValue(key, out var row) || !row.TryGetValue("en", out var english))
                    continue;

                // {0}, {1}… must survive translation, or string.Format throws.
                for (var i = 0; i < 4; i++)
                    if (english.Contains($"{{{i}}}") != text.Contains($"{{{i}}}"))
                        yield return $"{code}: \"{key}\" does not keep {{{i}}}";
            }
        }
    }

    private static void Merge(string code, Dictionary<string, string> translations)
    {
        foreach (var (key, text) in translations)
        {
            if (Table.TryGetValue(key, out var row))
                row[code] = text;
        }
    }

    private static string _current = "en";

    /// <summary>Short code of the language currently in use.</summary>
    public static string Current => _current;

    public static void SetLanguage(string? code)
    {
        var normalized = Normalize(code);
        _current = normalized;
        System.Globalization.CultureInfo.CurrentUICulture =
            System.Globalization.CultureInfo.GetCultureInfo(_current);
    }

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "en";
        var trimmed = code.Trim().ToLowerInvariant();
        return AvailableLanguages.Any(l => l.Code == trimmed) ? trimmed : "en";
    }

    /// <summary>True when the selected language is right-to-left (none today, but ready).</summary>
    public static bool IsRightToLeft => _current is "ar" or "he" or "fa" or "ur";

    /// <summary>
    /// Lays a window out right to left for a right-to-left language (Urdu), and left
    /// to right otherwise. Every window calls this as it is built.
    /// </summary>
    public static void ApplyDirection(Form form)
    {
        form.RightToLeft = IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        form.RightToLeftLayout = IsRightToLeft;
    }

    /// <summary>
    /// The plain text a screen reader speaks: keyboard-mnemonic ampersands
    /// ("S&earch") removed. Accessible names are always simple, spoken words.
    /// </summary>
    public static string Plain(string? text) => (text ?? string.Empty).Replace("&", string.Empty);

    /// <summary>Translate a key for the current language.</summary>
    public static string T(string key)
    {
        if (Table.TryGetValue(key, out var translations))
        {
            if (translations.TryGetValue(_current, out var text))
                return text;
            if (translations.TryGetValue("en", out var english))
                return english;
        }
        return key;
    }

    /// <summary>Translate a key and format it with the given arguments.</summary>
    public static string T(string key, params object[] args) =>
        string.Format(T(key), args);

    private static readonly Dictionary<string, Dictionary<string, string>> Table = new(StringComparer.Ordinal)
    {
        // ---------------- application / menus ----------------
        ["App.Title"] = new() { ["en"] = "Audiobook Downloader", ["hi"] = "ऑडियोबुक डाउनलोडर", ["es"] = "Descargador de audiolibros" },
        ["Menu.Application"] = new() { ["en"] = "&Application", ["hi"] = "&ऐप", ["es"] = "&Aplicación" },
        ["Menu.Help"] = new() { ["en"] = "&Help", ["hi"] = "&सहायता", ["es"] = "A&yuda" },
        ["Menu.Settings"] = new() { ["en"] = "&Settings…", ["hi"] = "&सेटिंग्स…", ["es"] = "&Configuración…" },
        ["Menu.About"] = new() { ["en"] = "&About…", ["hi"] = "&परिचय…", ["es"] = "Acerca &de…" },
        ["Menu.Shortcuts"] = new() { ["en"] = "&Keyboard shortcuts", ["hi"] = "&कीबोर्ड शॉर्टकट", ["es"] = "Atajos de &teclado" },
        ["Menu.Exit"] = new() { ["en"] = "E&xit", ["hi"] = "बंद &करें", ["es"] = "&Salir" },
        ["Menu.Refresh"] = new() { ["en"] = "&Reload list", ["hi"] = "सूची फिर से &लोड करें", ["es"] = "&Recargar lista" },

        // ---------------- main window ----------------
        ["Main.Provider"] = new() { ["en"] = "Download &provider:", ["hi"] = "डाउनलोड &प्रदाता:", ["es"] = "&Proveedor de descarga:" },
        ["Main.ProviderAccessible"] = new()
        {
            ["en"] = "Download provider",
            ["hi"] = "डाउनलोड प्रदाता",
            ["es"] = "Proveedor de descargas",
        },
        ["Main.ProviderStatus"] = new() { ["en"] = "Provider status", ["hi"] = "प्रदाता की स्थिति", ["es"] = "Estado del proveedor" },
        ["Main.SignIn"] = new() { ["en"] = "Sign &in to Audible…", ["hi"] = "Audible में साइन &इन करें…", ["es"] = "&Iniciar sesión en Audible…" },
        ["Main.SignOut"] = new() { ["en"] = "Sign &out of Audible", ["hi"] = "Audible से साइन &आउट करें", ["es"] = "Cerrar &sesión de Audible" },
        ["Main.Search"] = new() { ["en"] = "&Search books:", ["hi"] = "पुस्तकें &खोजें:", ["es"] = "&Buscar libros:" },
        ["Main.SearchAccessible"] = new() { ["en"] = "Search books", ["hi"] = "पुस्तकें खोजें", ["es"] = "Buscar libros" },
        ["Main.SearchButton"] = new() { ["en"] = "S&earch", ["hi"] = "&खोजें", ["es"] = "B&uscar" },
        // No mnemonic: the language list is reached with the Alt + U shortcut below,
        // and an underlined letter here would claim Alt + L for the same job as the
        // activity log.
        // The list's caption says what it holds: the Audible library you own, or what
        // a free catalogue found for your search.
        ["Main.Books"] = new() { ["en"] = "Search results (&books):", ["hi"] = "खोज परिणाम (&पुस्तकें):", ["es"] = "Resultados (&libros):" },
        ["Main.BooksAudible"] = new() { ["en"] = "Your Audible library (&books):", ["hi"] = "आपकी Audible लाइब्रेरी (&पुस्तकें):", ["es"] = "Su biblioteca de Audible (&libros):" },
        ["Main.Download"] = new() { ["en"] = "&Download", ["hi"] = "&डाउनलोड करें", ["es"] = "&Descargar" },
        ["Main.Cancel"] = new() { ["en"] = "&Cancel download", ["hi"] = "डाउनलोड &रद्द करें", ["es"] = "&Cancelar la descarga" },
        ["Main.OpenFolder"] = new() { ["en"] = "&Open download folder", ["hi"] = "डाउनलोड &फ़ोल्डर खोलें", ["es"] = "&Abrir la carpeta de descargas" },

        // ---------------- the download queue's own menu ----------------
        //
        // The queue's actions are on the list's context menu (the menu key, Shift F10
        // or a right click) and on shortcuts, so the window itself carries only the two
        // actions the main job needs.

        // Titles of the three groups on the main window: 1. Find books,
        // 2. Downloads, 3. Status.
        ["Main.Group.Books"] = new() { ["en"] = "1. Books", ["hi"] = "1. पुस्तकें", ["es"] = "1. Libros" },
        ["Main.Group.Queue"] = new() { ["en"] = "2. Download", ["hi"] = "2. डाउनलोड", ["es"] = "2. Descarga" },
        ["Main.Group.Status"] = new() { ["en"] = "3. Status", ["hi"] = "3. स्थिति", ["es"] = "3. Estado" },
        ["Main.Progress"] = new() { ["en"] = "Download progress", ["hi"] = "डाउनलोड प्रगति", ["es"] = "Progreso de la descarga" },
        ["Main.Status"] = new() { ["en"] = "Status", ["hi"] = "स्थिति", ["es"] = "Estado" },

        // ---------------- common ----------------
        ["Common.OK"] = new() { ["en"] = "&OK", ["hi"] = "ठी&क है", ["es"] = "&Aceptar" },
        ["Common.Cancel"] = new() { ["en"] = "&Cancel", ["hi"] = "&रद्द करें", ["es"] = "&Cancelar" },
        ["Common.Close"] = new() { ["en"] = "&Close", ["hi"] = "&बंद करें", ["es"] = "&Cerrar" },
        ["Common.Apply"] = new() { ["en"] = "&Apply", ["hi"] = "&लागू करें", ["es"] = "Apl&icar" },
        ["Common.Browse"] = new() { ["en"] = "B&rowse…", ["hi"] = "&ब्राउज़ करें…", ["es"] = "E&xaminar…" },

        // ---------------- settings ----------------
        ["Settings.Title"] = new() { ["en"] = "Settings", ["hi"] = "सेटिंग्स", ["es"] = "Configuración" },
        ["Settings.General"] = new() { ["en"] = "General", ["hi"] = "सामान्य", ["es"] = "General" },
        ["Settings.Downloads"] = new() { ["en"] = "Downloads", ["hi"] = "डाउनलोड", ["es"] = "Descargas" },
        ["Settings.Audible"] = new() { ["en"] = "Audible account", ["hi"] = "Audible खाता", ["es"] = "Cuenta de Audible" },
        ["Settings.Language"] = new() { ["en"] = "Language", ["hi"] = "भाषा", ["es"] = "Idioma" },
        ["Settings.Accessibility"] = new() { ["en"] = "Accessibility", ["hi"] = "सुगम्यता", ["es"] = "Accesibilidad" },

        ["Set.OutputFolder"] = new() { ["en"] = "Download &folder:", ["hi"] = "डाउनलोड &फ़ोल्डर:", ["es"] = "Carpeta de &descargas:" },
        ["Set.OpenWhenDone"] = new() { ["en"] = "Open &the download folder when downloads finish", ["hi"] = "&डाउनलोड पूरे होने पर डाउनलोड फ़ोल्डर खोलें", ["es"] = "Abrir &la carpeta de descargas al terminar" },
        ["Set.RememberProvider"] = new() { ["en"] = "Remember the &last provider", ["hi"] = "पिछला प्रदाता &याद रखें", ["es"] = "&Recordar el último proveedor" },
        ["Set.DeleteIntermediate"] = new() { ["en"] = "&Delete the encrypted original after successful decryption", ["hi"] = "&डिक्रिप्शन के बाद मूल एन्क्रिप्टेड फ़ाइल हटाएँ", ["es"] = "Eliminar el &original cifrado tras descifrar" },
        ["Set.RememberSignIn"] = new() { ["en"] = "&Remember the Audible sign-in on this computer", ["hi"] = "इस कंप्यूटर पर Audible साइन-इन &याद रखें", ["es"] = "&Recordar el inicio de sesión de Audible en este equipo" },
        ["Set.LoadLibrary"] = new() { ["en"] = "Load my &library automatically after signing in", ["hi"] = "सा&इन इन के बाद लाइब्रेरी अपने आप लोड करें", ["es"] = "Cargar mi &biblioteca tras iniciar sesión" },
        ["Set.Country"] = new() { ["en"] = "Au&dible country:", ["hi"] = "Audible &देश:", ["es"] = "&País de Audible:" },
        ["Set.AccountStatus"] = new() { ["en"] = "Account status", ["hi"] = "खाता स्थिति", ["es"] = "Estado de la cuenta" },
        ["Set.SignIn"] = new() { ["en"] = "&Sign in to Audible…", ["hi"] = "Audible में &साइन इन करें…", ["es"] = "Iniciar &sesión en Audible…" },
        ["Set.SignInAccessible"] = new() { ["en"] = "Sign in to Audible", ["hi"] = "Audible में साइन इन करें", ["es"] = "Iniciar sesión en Audible" },
        ["Set.SwitchAccountAccessible"] = new() { ["en"] = "Switch Audible account", ["hi"] = "Audible खाता बदलें", ["es"] = "Cambiar de cuenta de Audible" },
        ["Set.SignOut"] = new() { ["en"] = "Sign out and &forget this account", ["hi"] = "साइन &आउट करें और खाता भूल जाएँ", ["es"] = "Cerrar sesión &y olvidar esta cuenta" },
        ["Set.SignOutAccessible"] = new() { ["en"] = "Sign out", ["hi"] = "साइन आउट करें", ["es"] = "Cerrar sesión" },
        ["Set.UiLanguage"] = new() { ["en"] = "&Interface language:", ["hi"] = "&इंटरफ़ेस भाषा:", ["es"] = "Idioma &de la interfaz:" },
        ["Set.AnnounceStatus"] = new() { ["en"] = "Announce &status changes through the screen reader", ["hi"] = "स्क्रीन रीडर से &स्थिति परिवर्तन बताएँ", ["es"] = "Anunciar los cambios de &estado con el lector de pantalla" },
        ["Set.AnnounceProgress"] = new() { ["en"] = "Announce download &progress milestones", ["hi"] = "डाउनलोड &प्रगति बताएँ", ["es"] = "Anunciar los hitos del &progreso" },
        ["Set.AccessibilityNote"] = new()
        {
            ["en"] = "This app never speaks through its own voice. Everything is exposed to Windows " +
                   "accessibility so your own screen reader, such as NVDA or Narrator, reads it automatically.",
            ["hi"] = "यह ऐप अपनी आवाज़ में कुछ नहीं बोलता। सब कुछ Windows सुगम्यता को दिया जाता है, " +
                   "जिससे NVDA या नैरेटर जैसे आपके स्क्रीन रीडर इसे अपने आप पढ़ते हैं।",
            ["es"] = "Esta aplicación nunca habla con su propia voz. Todo se expone a la accesibilidad " +
                   "de Windows para que su lector de pantalla, como NVDA o Narrador, lo lea automáticamente.",
        },
        ["Set.DataFolder"] = new() { ["en"] = "Settings and log are stored in:", ["hi"] = "सेटिंग्स और लॉग यहाँ रखे जाते हैं:", ["es"] = "La configuración y el registro están en:" },

        // ---------------- main window: provider state ----------------
        ["Main.NotSignedIn"] = new() { ["en"] = "Not signed in.", ["hi"] = "साइन इन नहीं हैं।", ["es"] = "Sin sesión iniciada." },
        ["Main.HasDrm"] = new() { ["en"] = "DRM will be removed automatically (personal backups only).", ["hi"] = "DRM अपने आप हट जाएगा (केवल निजी बैकअप)।", ["es"] = "El DRM se eliminará automáticamente (solo copias personales)." },
        ["Main.NoDrm"] = new() { ["en"] = "No DRM — files are saved as they arrive.", ["hi"] = "कोई DRM नहीं — फ़ाइलें वैसे ही सहेजी जाती हैं।", ["es"] = "Sin DRM: los archivos se guardan tal cual." },
        ["Main.Loading"] = new() { ["en"] = "Loading…", ["hi"] = "लोड हो रहा है…", ["es"] = "Cargando…" },
        ["Main.BooksFound"] = new() { ["en"] = "{0} books found.", ["hi"] = "{0} पुस्तकें मिलीं।", ["es"] = "Se encontraron {0} libros." },
        ["Main.BooksFoundIn"] = new() { ["en"] = "{0} books found in {1}.", ["hi"] = "{1} में {0} पुस्तकें मिलीं।", ["es"] = "Se encontraron {0} libros en {1}." },
        ["Main.NoBooks"] = new() { ["en"] = "No books matched. Change the search text or the language.", ["hi"] = "कोई पुस्तक नहीं मिली। खोज पाठ या भाषा बदलें।", ["es"] = "Ningún libro coincide. Cambie el texto de búsqueda o el idioma." },
        ["Main.ListFailed"] = new() { ["en"] = "The list could not be loaded. See the activity log.", ["hi"] = "सूची लोड नहीं हो सकी। गतिविधि लॉग देखें।", ["es"] = "No se pudo cargar la lista. Consulte el registro de actividad." },
        ["Main.ListFailedStatus"] = new() { ["en"] = "Could not list books: {0}", ["hi"] = "पुस्तकें सूचीबद्ध नहीं हो सकीं: {0}", ["es"] = "No se pudieron listar los libros: {0}" },
        ["Main.SelectFirst"] = new() { ["en"] = "Select a book first.", ["hi"] = "पहले कोई पुस्तक चुनें।", ["es"] = "Seleccione primero un libro." },
        ["Main.Cancelling"] = new() { ["en"] = "Cancelling the active download…", ["hi"] = "सक्रिय डाउनलोड रद्द हो रहा है…", ["es"] = "Cancelando la descarga activa…" },
        ["Main.AllFinishedOk"] = new() { ["en"] = "All downloads finished: {0} downloaded successfully.", ["hi"] = "सभी डाउनलोड पूरे: {0} सफलतापूर्वक डाउनलोड हुए।", ["es"] = "Todas las descargas terminaron: {0} completadas." },
        ["Main.AllFinishedMixed"] = new() { ["en"] = "All downloads finished: {0} completed, {1} failed, {2} cancelled.", ["hi"] = "सभी डाउनलोड पूरे: {0} पूर्ण, {1} विफल, {2} रद्द।", ["es"] = "Todas las descargas terminaron: {0} completadas, {1} fallidas, {2} canceladas." },
        ["Main.SettingsSaved"] = new() { ["en"] = "Settings saved. Downloads go to {0}", ["hi"] = "सेटिंग्स सहेज दी गईं। डाउनलोड यहाँ जाते हैं: {0}", ["es"] = "Configuración guardada. Las descargas van a {0}" },
        ["Main.AllLanguages"] = new() { ["en"] = "All languages", ["hi"] = "सभी भाषाएँ", ["es"] = "Todos los idiomas" },
        ["Main.ProgressAnnouncement"] = new() { ["en"] = "Downloading {0}, {1} percent.", ["hi"] = "{0} डाउनलोड हो रहा है, {1} प्रतिशत।", ["es"] = "Descargando {0}, {1} por ciento." },
        ["Main.OpenFolderFailed"] = new() { ["en"] = "Could not open the download folder: {0}", ["hi"] = "डाउनलोड फ़ोल्डर नहीं खोला जा सका: {0}", ["es"] = "No se pudo abrir la carpeta de descargas: {0}" },

        // ---------------- Audible signed-out state ----------------
        ["Audible.SignedOutShort"] = new() { ["en"] = "Not signed in to Audible.", ["hi"] = "Audible में साइन इन नहीं हैं।", ["es"] = "No ha iniciado sesión en Audible." },
        ["Audible.SignedOutHint"] = new()
        {
            ["en"] = "Signed out. Press \"Sign in to Audible\" (Alt+I) to choose your country and sign in.",
            ["hi"] = "साइन आउट हैं। देश चुनने और साइन इन करने के लिए “Audible में साइन इन करें” दबाएँ।",
            ["es"] = "Sin sesión. Pulse \"Iniciar sesión en Audible\" (Alt+I) para elegir su país e iniciar sesión.",
        },
        ["Audible.SignedOutStatus"] = new()
        {
            ["en"] = "Not signed in to Audible. Press Sign in to Audible, next to the provider list, to choose your country and sign in.",
            ["hi"] = "Audible में साइन इन नहीं हैं। देश चुनने और साइन इन करने के लिए प्रदाता सूची के पास “Audible में साइन इन करें” दबाएँ।",
            ["es"] = "No ha iniciado sesión en Audible. Pulse Iniciar sesión para elegir su país e iniciar sesión.",
        },
        ["Audible.PressSearch"] = new()
        {
            ["en"] = "Signed in to Audible. Press Search to list your library.",
            ["hi"] = "Audible में साइन इन हो गए। लाइब्रेरी देखने के लिए खोजें दबाएँ।",
            ["es"] = "Sesión iniciada en Audible. Pulse Buscar para ver su biblioteca.",
        },
        ["Audible.SwitchConfirm"] = new()
        {
            ["en"] = "Sign out of the current account and sign in with a different one?\n\n" +
                   "No downloaded files are removed.",
            ["hi"] = "वर्तमान खाते से साइन आउट करके दूसरे खाते से साइन इन करें?\n\nडाउनलोड की गई फ़ाइलें नहीं हटेंगी।",
            ["es"] = "¿Cerrar la sesión actual e iniciarla con otra cuenta?\n\nNo se elimina ningún archivo descargado.",
        },
        ["Audible.SignInBeforeDownload"] = new()
        {
            ["en"] = "Sign in to Audible before downloading Audible books.",
            ["hi"] = "Audible की पुस्तकें डाउनलोड करने से पहले साइन इन करें।",
            ["es"] = "Inicie sesión en Audible antes de descargar sus libros.",
        },

        // ---------------- unexpected problems ----------------
        // Diagnostics stay in English on purpose: they are technical, and they are
        // what a bug report will quote back.
        ["Main.Tasks.Provider"] = new() { ["en"] = "loading books from the selected provider" },
        ["Main.Tasks.Search"] = new() { ["en"] = "searching" },
        ["Main.Tasks.Settings"] = new() { ["en"] = "opening Settings" },
        ["Main.Tasks.Reload"] = new() { ["en"] = "reloading the book list" },
        ["Audible.SignInTask"] = new() { ["en"] = "signing in to Audible" },
        ["Main.UnexpectedError"] = new() { ["en"] = "Something went wrong while {0}: {1}" },
        ["Main.UnexpectedErrorTitle"] = new() { ["en"] = "Unexpected problem", ["hi"] = "अनपेक्षित समस्या", ["es"] = "Problema inesperado" },
        ["Main.UnexpectedErrorDetail"] = new()
        {
            ["en"] = "Something went wrong while {0}.\n\n{1}\n\nThe full details were written to:\n{2}\n\n" +
                   "Nothing that was already downloaded was lost. Please try again.",
        },
        ["Fatal.Title"] = new() { ["en"] = "Audiobook Downloader hit an unexpected problem", ["hi"] = "ऐप में एक अनपेक्षित समस्या आई", ["es"] = "La aplicación encontró un problema inesperado" },
        ["Fatal.Message"] = new()
        {
            ["en"] = "The application ran into an unexpected {0}.\n\n{1}\n\nDetails have been written to:\n{2}\n\n" +
                   "Press OK to carry on. If this keeps happening, the log above will help track it down.",
        },
        ["Fatal.NoDetails"] = new() { ["en"] = "(no details available)" },

        // ---------------- keyboard help ----------------
        ["Help.Title"] = new() { ["en"] = "Keyboard shortcuts", ["hi"] = "कीबोर्ड शॉर्टकट", ["es"] = "Atajos de teclado" },
        ["Help.Intro"] = new()
        {
            ["en"] = "The main window keeps only what the main job needs: choose a source, find a book, download it and watch it " +
                   "happen. Everything else is here — on a shortcut, in the application menu (press Alt), or in Settings. " +
                   "The shortcuts this window offers through the underlined letters on its own labels are listed at the end.",
            ["hi"] = "मुख्य विंडो में वही रहता है जो मुख्य काम के लिए चाहिए: स्रोत चुनें, पुस्तक खोजें, डाउनलोड करें और उसे होते देखें। " +
                   "बाकी सब यहीं है — किसी शॉर्टकट पर, ऐप मेन्यू में (Alt दबाएँ), या सेटिंग्स में। " +
                   "इस विंडो के लेबल पर रेखांकित अक्षरों वाले शॉर्टकट अंत में दिए हैं।",
            ["es"] = "La ventana principal conserva solo lo que necesita el trabajo principal: elegir un origen, buscar un libro, " +
                   "descargarlo y verlo avanzar. Todo lo demás está aquí: en un atajo, en el menú de la aplicación (pulse Alt) " +
                   "o en la configuración. Los atajos que ofrece esta ventana con las letras subrayadas de sus etiquetas van al final.",
        },

        // ---------------- keyboard shortcuts ----------------
        //
        // One sentence per shortcut, listed by the help screen in the order they are
        // defined in Shortcuts.All. Written as sentences rather than as control labels,
        // because they are read out on their own, without a window around them.
        ["Shortcut.ShortcutList"] = new()
        {
            ["en"] = "Show this list of keyboard shortcuts.",
            ["hi"] = "कीबोर्ड शॉर्टकट की यह सूची दिखाएँ।",
            ["es"] = "Mostrar esta lista de atajos de teclado.",
        },
        ["Shortcut.Settings"] = new()
        {
            ["en"] = "Open Settings.",
            ["hi"] = "सेटिंग्स खोलें।",
            ["es"] = "Abrir la configuración.",
        },
        ["Shortcut.ReloadBooks"] = new()
        {
            ["en"] = "Reload the list of books from the current provider.",
            ["hi"] = "मौजूदा प्रदाता से पुस्तकों की सूची फिर से लोड करें।",
            ["es"] = "Recargar la lista de libros del proveedor actual.",
        },
        ["Shortcut.Folder"] = new()
        {
            ["en"] = "Open the download folder.",
            ["hi"] = "डाउनलोड फ़ोल्डर खोलें।",
            ["es"] = "Abrir la carpeta de descargas.",
        },
        ["Shortcut.Quit"] = new()
        {
            ["en"] = "Quit the application.",
            ["hi"] = "ऐप बंद करें।",
            ["es"] = "Cerrar la aplicación.",
        },
        ["Shortcut.SearchHistory"] = new()
        {
            ["en"] = "Open your previous searches for the current provider.",
            ["hi"] = "मौजूदा प्रदाता के लिए अपनी पिछली खोजें खोलें।",
            ["es"] = "Abrir sus búsquedas anteriores para el proveedor actual.",
        },
        ["Shortcut.MoveFocus"] = new()
        {
            ["en"] = "Move to the next or previous control.",
            ["hi"] = "अगले या पिछले नियंत्रण पर जाएँ।",
            ["es"] = "Ir al control siguiente o anterior.",
        },
        ["Shortcut.AppMenu"] = new()
        {
            ["en"] = "Open the application menu: settings, download folder, reload, quit and help.",
            ["hi"] = "ऐप मेन्यू खोलें: सेटिंग्स, डाउनलोड फ़ोल्डर, फिर से लोड, बंद करना और सहायता।",
            ["es"] = "Abrir el menú de la aplicación: configuración, carpeta de descargas, recargar, salir y ayuda.",
        },
        ["Shortcut.NextSection"] = new()
        {
            ["en"] = "Move to the next section inside the Settings dialog.",
            ["hi"] = "सेटिंग्स डायलॉग के अगले भाग पर जाएँ।",
            ["es"] = "Ir a la sección siguiente del cuadro de configuración.",
        },
        ["Shortcut.PreviousSection"] = new()
        {
            ["en"] = "Move to the previous section inside the Settings dialog.",
            ["hi"] = "सेटिंग्स डायलॉग के पिछले भाग पर जाएँ।",
            ["es"] = "Ir a la sección anterior del cuadro de configuración.",
        },
        ["Shortcut.CloseDialog"] = new()
        {
            ["en"] = "Close the current dialog.",
            ["hi"] = "मौजूदा डायलॉग बंद करें।",
            ["es"] = "Cerrar el cuadro de diálogo actual.",
        },
        ["Shortcut.SearchBoxEnter"] = new()
        {
            ["en"] = "In the search box: search with the text you typed.",
            ["hi"] = "खोज बॉक्स में: आपने जो लिखा है उससे खोजें।",
            ["es"] = "En el cuadro de búsqueda: buscar con el texto que escribió.",
        },
        ["Shortcut.SelectSeveral"] = new()
        {
            ["en"] = "In a list: select several rows, to download or remove them together.",
            ["hi"] = "किसी सूची में: कई पंक्तियाँ चुनें, ताकि उन्हें एक साथ डाउनलोड या हटा सकें।",
            ["es"] = "En una lista: seleccionar varias filas para descargarlas o quitarlas juntas.",
        },
        ["Shortcut.OpenBook"] = new()
        {
            ["en"] = "In the book list: download the book you are on.",
            ["hi"] = "पुस्तक सूची में: जिस पुस्तक पर हैं उसे डाउनलोड करें।",
            ["es"] = "En la lista de libros: descargar el libro en el que está.",
        },

        // ---------------- book details, export, duplicates ----------------
        ["Menu.Export"] = new() { ["en"] = "&Export book list…", ["hi"] = "पुस्तक सूची &निर्यात करें…", ["es"] = "&Exportar la lista de libros…" },
        ["Export.Filter"] = new()
        {
            ["en"] = "CSV file (*.csv)|*.csv",
            ["hi"] = "CSV फ़ाइल (*.csv)|*.csv",
            ["es"] = "Archivo CSV (*.csv)|*.csv",
        },
        ["Main.NothingToExport"] = new() { ["en"] = "The book list is empty, so there is nothing to export.", ["hi"] = "पुस्तक सूची खाली है, निर्यात करने को कुछ नहीं है।", ["es"] = "La lista de libros está vacía; no hay nada que exportar." },
        ["Main.Exported"] = new() { ["en"] = "Exported {0} books to {1}", ["hi"] = "{0} पुस्तकें यहाँ निर्यात की गईं: {1}", ["es"] = "Se exportaron {0} libros a {1}" },
        ["Main.ExportFailed"] = new() { ["en"] = "Could not export the book list: {0}", ["hi"] = "पुस्तक सूची निर्यात नहीं हो सकी: {0}", ["es"] = "No se pudo exportar la lista: {0}" },
        ["Main.AllSelected"] = new() { ["en"] = "All {0} books selected.", ["hi"] = "सभी {0} पुस्तकें चुनी गईं।", ["es"] = "Seleccionados los {0} libros." },
        ["Main.AlreadyDownloaded"] = new() { ["en"] = "already downloaded", ["hi"] = "पहले से डाउनलोड", ["es"] = "ya descargado" },
        ["Main.DuplicateTitle"] = new() { ["en"] = "Already downloaded", ["hi"] = "पहले से डाउनलोड", ["es"] = "Ya descargado" },
        ["Main.DuplicateOne"] = new()
        {
            ["en"] = "\"{0}\" is already in your download folder.\n\nDownload it again? A second copy is saved next to the first; nothing is overwritten.",
            ["hi"] = "\"{0}\" पहले से आपके डाउनलोड फ़ोल्डर में है।\n\nफिर से डाउनलोड करें? दूसरी कॉपी पहली के पास सहेजी जाएगी; कुछ भी अधिलेखित नहीं होगा।",
            ["es"] = "\"{0}\" ya está en su carpeta de descargas.\n\n¿Descargarlo de nuevo? Se guarda una segunda copia junto a la primera; no se sobrescribe nada.",
        },
        ["Main.DuplicateMany"] = new()
        {
            ["en"] = "{0} of the {1} selected books are already in your download folder.\n\n" +
                     "Yes: download all of them, making second copies.\nNo: skip the ones already downloaded.\nCancel: download nothing.",
            ["hi"] = "चुनी गई {1} में से {0} पुस्तकें पहले से आपके डाउनलोड फ़ोल्डर में हैं।\n\n" +
                     "हाँ: सभी डाउनलोड करें, दूसरी कॉपी बनाकर।\nनहीं: पहले से डाउनलोड हुई छोड़ दें।\nरद्द करें: कुछ भी डाउनलोड न करें।",
            ["es"] = "{0} de los {1} libros seleccionados ya están en su carpeta de descargas.\n\n" +
                     "Sí: descargarlos todos, con segundas copias.\nNo: omitir los ya descargados.\nCancelar: no descargar nada.",
        },
        ["Main.NothingQueued"] = new() { ["en"] = "Nothing was added to the download queue.", ["hi"] = "डाउनलोड सूची में कुछ नहीं जोड़ा गया।", ["es"] = "No se añadió nada a la cola de descargas." },
        ["Details.WindowTitle"] = new() { ["en"] = "Book details", ["hi"] = "पुस्तक विवरण", ["es"] = "Detalles del libro" },
        ["Details.Title"] = new() { ["en"] = "Title", ["hi"] = "शीर्षक", ["es"] = "Título" },
        ["Details.Author"] = new() { ["en"] = "Author", ["hi"] = "लेखक", ["es"] = "Autor" },
        ["Details.Narrator"] = new() { ["en"] = "Narrator", ["hi"] = "वाचक", ["es"] = "Narrador" },
        ["Details.Length"] = new() { ["en"] = "Length", ["hi"] = "अवधि", ["es"] = "Duración" },
        ["Details.LengthValue"] = new() { ["en"] = "{0} hours {1} minutes", ["hi"] = "{0} घंटे {1} मिनट", ["es"] = "{0} horas {1} minutos" },
        ["Details.Language"] = new() { ["en"] = "Language", ["hi"] = "भाषा", ["es"] = "Idioma" },
        ["Details.Source"] = new() { ["en"] = "Source", ["hi"] = "स्रोत", ["es"] = "Origen" },
        ["Details.Id"] = new() { ["en"] = "Identifier", ["hi"] = "पहचान", ["es"] = "Identificador" },
        ["Details.File"] = new() { ["en"] = "Downloaded file", ["hi"] = "डाउनलोड की गई फ़ाइल", ["es"] = "Archivo descargado" },
        ["Set.OrganizeByAuthor"] = new()
        {
            ["en"] = "Save &each book in a folder named after its author",
            ["hi"] = "हर पुस्तक को &उसके लेखक के नाम वाले फ़ोल्डर में सहेजें",
            ["es"] = "Guardar cada &libro en una carpeta con el nombre de su autor",
        },
        ["Set.WarnDuplicates"] = new()
        {
            ["en"] = "As&k before downloading a book that is already downloaded",
            ["hi"] = "पहले &से डाउनलोड पुस्तक को फिर डाउनलोड करने से पहले पूछें",
            ["es"] = "Preguntar antes de descargar &un libro ya descargado",
        },
        ["Shortcut.Export"] = new()
        {
            ["en"] = "Export the book list to a CSV file that opens in Excel or any text editor.",
            ["hi"] = "पुस्तक सूची को CSV फ़ाइल में निर्यात करें, जो Excel या किसी भी टेक्स्ट एडिटर में खुलती है।",
            ["es"] = "Exportar la lista de libros a un archivo CSV que se abre en Excel o en cualquier editor.",
        },
        ["Shortcut.BookDetails"] = new()
        {
            ["en"] = "Show everything known about the selected book: author, narrator, length, language and file.",
            ["hi"] = "चुनी गई पुस्तक का पूरा विवरण दिखाएँ: लेखक, वाचक, अवधि, भाषा और फ़ाइल।",
            ["es"] = "Mostrar todo lo que se sabe del libro seleccionado: autor, narrador, duración, idioma y archivo.",
        },
        ["Main.NotDownloadedYet"] = new()
        {
            ["en"] = "\"{0}\" has not been downloaded yet.",
            ["hi"] = "\"{0}\" अभी डाउनलोड नहीं हुई है।",
            ["es"] = "\"{0}\" aún no se ha descargado.",
        },
        ["Shortcut.ShowFile"] = new()
        {
            ["en"] = "Show the selected book's downloaded file in File Explorer.",
            ["hi"] = "चुनी गई पुस्तक की डाउनलोड की गई फ़ाइल File Explorer में दिखाएँ।",
            ["es"] = "Mostrar en el Explorador de archivos el archivo descargado del libro seleccionado.",
        },
        ["Shortcut.SelectAll"] = new()
        {
            ["en"] = "Select every book in the list, to download them all at once.",
            ["hi"] = "सूची की सभी पुस्तकें चुनें, ताकि सब एक साथ डाउनलोड हों।",
            ["es"] = "Seleccionar todos los libros de la lista para descargarlos a la vez.",
        },

        // ---------------- player, view menu, updates, new settings ----------------
        ["Player.Title"] = new() { ["en"] = "Player: {0}", ["hi"] = "प्लेयर: {0}", ["es"] = "Reproductor: {0}" },
        ["Player.BookTitle"] = new() { ["en"] = "Book", ["hi"] = "पुस्तक", ["es"] = "Libro" },
        ["Player.Chapter"] = new() { ["en"] = "Chapter", ["hi"] = "अध्याय", ["es"] = "Capítulo" },
        ["Player.Time"] = new() { ["en"] = "Time", ["hi"] = "समय", ["es"] = "Tiempo" },
        ["Player.Position"] = new() { ["en"] = "Position", ["hi"] = "स्थिति", ["es"] = "Posición" },
        ["Player.Play"] = new() { ["en"] = "&Play", ["hi"] = "&चलाएँ", ["es"] = "&Reproducir" },
        ["Player.Pause"] = new() { ["en"] = "&Pause", ["hi"] = "&रोकें", ["es"] = "&Pausa" },
        ["Player.Back"] = new() { ["en"] = "&Back 30 seconds", ["hi"] = "30 सेकंड &पीछे", ["es"] = "&Atrás 30 segundos" },
        ["Player.Forward"] = new() { ["en"] = "&Forward 30 seconds", ["hi"] = "30 सेकंड &आगे", ["es"] = "A&delante 30 segundos" },
        ["Player.Previous"] = new() { ["en"] = "Pre&vious chapter", ["hi"] = "पि&छला अध्याय", ["es"] = "Capítulo a&nterior" },
        ["Player.Next"] = new() { ["en"] = "&Next chapter", ["hi"] = "&अगला अध्याय", ["es"] = "Capítulo &siguiente" },
        ["Player.Speed"] = new() { ["en"] = "&Speed:", ["hi"] = "&गति:", ["es"] = "&Velocidad:" },
        ["Player.Status"] = new() { ["en"] = "Player status", ["hi"] = "प्लेयर की स्थिति", ["es"] = "Estado del reproductor" },
        ["Player.KeysHint"] = new() { ["en"] = "Keys: Space on the position slider plays or pauses. Ctrl+Left and Ctrl+Right jump 30 seconds, Ctrl+Shift+Left and Right change chapter, Ctrl+Up and Down change the speed, Ctrl+T says the time.", ["hi"] = "कुंजियाँ: स्थिति स्लाइडर पर स्पेस चलाता या रोकता है। Ctrl+Left और Ctrl+Right 30 सेकंड कूदते हैं, Ctrl+Shift+Left और Right अध्याय बदलते हैं, Ctrl+Up और Down गति बदलते हैं, Ctrl+T समय बताता है।", ["es"] = "Teclas: Espacio en el control de posición reproduce o pausa. Ctrl+Izquierda y Ctrl+Derecha saltan 30 segundos, Ctrl+Mayús+Izquierda y Derecha cambian de capítulo, Ctrl+Arriba y Abajo cambian la velocidad, Ctrl+T dice el tiempo." },
        ["Player.Failed"] = new() { ["en"] = "This book could not be played: {0}", ["hi"] = "यह पुस्तक चलाई नहीं जा सकी: {0}", ["es"] = "No se pudo reproducir este libro: {0}" },
        ["Player.NoAudio"] = new() { ["en"] = "there is no audio file in it.", ["hi"] = "इसमें कोई ऑडियो फ़ाइल नहीं है।", ["es"] = "no contiene ningún archivo de audio." },
        ["Player.Opening"] = new() { ["en"] = "Opening…", ["hi"] = "खोला जा रहा है…", ["es"] = "Abriendo…" },
        ["Player.Resumed"] = new() { ["en"] = "Resuming at {0}.", ["hi"] = "{0} से जारी।", ["es"] = "Continuando en {0}." },
        ["Player.Playing"] = new() { ["en"] = "Playing.", ["hi"] = "चल रहा है।", ["es"] = "Reproduciendo." },
        ["Player.PausedAt"] = new() { ["en"] = "Paused at {0}.", ["hi"] = "{0} पर रोका गया।", ["es"] = "En pausa en {0}." },
        ["Player.Ended"] = new() { ["en"] = "Finished. Next time the book starts from the beginning.", ["hi"] = "समाप्त। अगली बार पुस्तक शुरू से चलेगी।", ["es"] = "Terminado. La próxima vez el libro empezará desde el principio." },
        ["Player.SpeedNow"] = new() { ["en"] = "Speed {0}.", ["hi"] = "गति {0}।", ["es"] = "Velocidad {0}." },
        ["Player.PositionText"] = new() { ["en"] = "{0} of {1}", ["hi"] = "{1} में से {0}", ["es"] = "{0} de {1}" },
        ["Player.TrackText"] = new() { ["en"] = "Chapter {0} of {1}: {2}", ["hi"] = "अध्याय {0} / {1}: {2}", ["es"] = "Capítulo {0} de {1}: {2}" },
        ["Menu.View"] = new() { ["en"] = "&View", ["hi"] = "&दृश्य", ["es"] = "&Ver" },
        ["Menu.SortTitle"] = new() { ["en"] = "Sort by &title", ["hi"] = "&शीर्षक से क्रमबद्ध करें", ["es"] = "Ordenar por &título" },
        ["Menu.SortAuthor"] = new() { ["en"] = "Sort by &author", ["hi"] = "&लेखक से क्रमबद्ध करें", ["es"] = "Ordenar por &autor" },
        ["Menu.SortLength"] = new() { ["en"] = "Sort by &length", ["hi"] = "&अवधि से क्रमबद्ध करें", ["es"] = "Ordenar por &duración" },
        ["Menu.SortAdded"] = new() { ["en"] = "Sort by date &added (newest first)", ["hi"] = "&जोड़ने की तारीख से (नई पहले)", ["es"] = "Ordenar por &fecha (más recientes primero)" },
        ["Menu.HideDownloaded"] = new() { ["en"] = "&Hide books already downloaded", ["hi"] = "पहले से डाउनलोड पुस्तकें &छिपाएँ", ["es"] = "&Ocultar los libros ya descargados" },
        ["Menu.Play"] = new() { ["en"] = "&Play selected book…", ["hi"] = "चुनी पुस्तक &चलाएँ…", ["es"] = "&Reproducir el libro seleccionado…" },
        ["Menu.CheckUpdates"] = new() { ["en"] = "Check for &updates…", ["hi"] = "&अपडेट जाँचें…", ["es"] = "Buscar &actualizaciones…" },
        ["Main.HiddenCount"] = new() { ["en"] = "{0} books shown; {1} already downloaded are hidden (Ctrl+H shows them).", ["hi"] = "{0} पुस्तकें दिख रही हैं; पहले से डाउनलोड {1} छिपी हैं (Ctrl+H से दिखाएँ)।", ["es"] = "Se muestran {0} libros; {1} ya descargados están ocultos (Ctrl+H los muestra)." },
        ["Main.AllHidden"] = new() { ["en"] = "Every book here is already downloaded. Press Ctrl+H to show them.", ["hi"] = "यहाँ की हर पुस्तक पहले से डाउनलोड है। दिखाने के लिए Ctrl+H दबाएँ।", ["es"] = "Todos estos libros ya están descargados. Pulse Ctrl+H para mostrarlos." },
        ["Main.SortedBy"] = new() { ["en"] = "Sorted by {0}.", ["hi"] = "{0} से क्रमबद्ध।", ["es"] = "Ordenado por {0}." },
        ["Main.SortNameTitle"] = new() { ["en"] = "title", ["hi"] = "शीर्षक", ["es"] = "título" },
        ["Main.SortNameAuthor"] = new() { ["en"] = "author", ["hi"] = "लेखक", ["es"] = "autor" },
        ["Main.SortNameLength"] = new() { ["en"] = "length", ["hi"] = "अवधि", ["es"] = "duración" },
        ["Main.SortNameAdded"] = new() { ["en"] = "date added", ["hi"] = "जोड़ने की तारीख", ["es"] = "fecha" },
        ["Main.DownloadedShown"] = new() { ["en"] = "Books already downloaded are shown.", ["hi"] = "पहले से डाउनलोड पुस्तकें दिखाई जा रही हैं।", ["es"] = "Se muestran los libros ya descargados." },
        ["Main.DownloadedHidden"] = new() { ["en"] = "Books already downloaded are hidden.", ["hi"] = "पहले से डाउनलोड पुस्तकें छिपा दी गईं।", ["es"] = "Se ocultan los libros ya descargados." },
        ["Main.NotPlayable"] = new() { ["en"] = "\"{0}\" is not audio. It was opened in its own program.", ["hi"] = "\"{0}\" ऑडियो नहीं है। इसे इसके अपने प्रोग्राम में खोला गया।", ["es"] = "\"{0}\" no es audio. Se abrió con su propio programa." },
        ["Main.Unzipping"] = new() { ["en"] = "Unzipping \"{0}\" so it can be played…", ["hi"] = "\"{0}\" को चलाने के लिए अनज़िप किया जा रहा है…", ["es"] = "Descomprimiendo \"{0}\" para reproducirlo…" },
        ["Main.UnzipFailed"] = new() { ["en"] = "Could not unzip the book: {0}", ["hi"] = "पुस्तक अनज़िप नहीं हो सकी: {0}", ["es"] = "No se pudo descomprimir el libro: {0}" },
        ["Main.UpdateAvailableTitle"] = new() { ["en"] = "Update available", ["hi"] = "अपडेट उपलब्ध", ["es"] = "Actualización disponible" },
        ["Main.UpdateAvailable"] = new() { ["en"] = "Version {0} of Audiobook Downloader is available. You have version {1}.\n\nOpen the download page now?", ["hi"] = "ऑडियोबुक डाउनलोडर का संस्करण {0} उपलब्ध है। आपके पास संस्करण {1} है।\n\nक्या अभी डाउनलोड पेज खोलें?", ["es"] = "La versión {0} está disponible. Usted tiene la versión {1}.\n\n¿Abrir ahora la página de descarga?" },
        ["Main.UpdateStatus"] = new() { ["en"] = "Version {0} is available. Choose Help, Check for updates to get it.", ["hi"] = "संस्करण {0} उपलब्ध है। पाने के लिए सहायता, अपडेट जाँचें चुनें।", ["es"] = "La versión {0} está disponible. Elija Ayuda, Buscar actualizaciones para obtenerla." },
        ["Main.UpToDate"] = new() { ["en"] = "You have the latest version ({0}).", ["hi"] = "आपके पास नवीनतम संस्करण ({0}) है।", ["es"] = "Tiene la versión más reciente ({0})." },
        ["Main.UpdateFailed"] = new() { ["en"] = "Could not check for updates: {0}", ["hi"] = "अपडेट नहीं जाँचे जा सके: {0}", ["es"] = "No se pudo buscar actualizaciones: {0}" },
        ["Main.ResumedSignIn"] = new() { ["en"] = "Signed in to Audible with your saved sign-in.", ["hi"] = "सहेजे गए साइन-इन से Audible में साइन इन हो गए।", ["es"] = "Sesión iniciada en Audible con el inicio de sesión guardado." },
        ["Main.NewPurchases"] = new() { ["en"] = "{0} new Audible book(s) found and added to the download queue.", ["hi"] = "{0} नई Audible पुस्तक(एँ) मिलीं और डाउनलोड सूची में जोड़ी गईं।", ["es"] = "Se encontraron {0} libros nuevos de Audible y se añadieron a la cola." },
        ["Shortcut.Play"] = new() { ["en"] = "Play the selected book in the built-in player, from where you stopped last time.", ["hi"] = "चुनी गई पुस्तक को अंतर्निहित प्लेयर में वहीं से चलाएँ जहाँ पिछली बार रुके थे।", ["es"] = "Reproducir el libro seleccionado en el reproductor integrado, desde donde lo dejó." },
        ["Shortcut.SortTitle"] = new() { ["en"] = "Sort the book list by title.", ["hi"] = "पुस्तक सूची को शीर्षक से क्रमबद्ध करें।", ["es"] = "Ordenar la lista por título." },
        ["Shortcut.SortAuthor"] = new() { ["en"] = "Sort the book list by author.", ["hi"] = "पुस्तक सूची को लेखक से क्रमबद्ध करें।", ["es"] = "Ordenar la lista por autor." },
        ["Shortcut.SortLength"] = new() { ["en"] = "Sort the book list by length, longest first.", ["hi"] = "पुस्तक सूची को अवधि से क्रमबद्ध करें, सबसे लंबी पहले।", ["es"] = "Ordenar la lista por duración, la más larga primero." },
        ["Shortcut.SortAdded"] = new() { ["en"] = "Sort the book list by the date added, newest first.", ["hi"] = "पुस्तक सूची को जोड़ने की तारीख से क्रमबद्ध करें, नई पहले।", ["es"] = "Ordenar la lista por fecha, los más recientes primero." },
        ["Shortcut.HideDownloaded"] = new() { ["en"] = "Hide or show the books that are already downloaded.", ["hi"] = "पहले से डाउनलोड पुस्तकें छिपाएँ या दिखाएँ।", ["es"] = "Ocultar o mostrar los libros ya descargados." },
        ["Format.M4b"] = new() { ["en"] = "M4B (lossless, keeps chapters)", ["hi"] = "M4B (बिना गुणवत्ता हानि, अध्याय सहित)", ["es"] = "M4B (sin pérdida, con capítulos)" },
        ["Format.Mp3"] = new() { ["en"] = "MP3 (plays everywhere; slower to make)", ["hi"] = "MP3 (हर जगह चलता है; बनने में धीमा)", ["es"] = "MP3 (se reproduce en todas partes; más lento)" },
        ["Set.SaveCover"] = new() { ["en"] = "Save &the cover picture next to each book", ["hi"] = "&हर पुस्तक के पास कवर चित्र सहेजें", ["es"] = "Guardar la imagen de &portada junto a cada libro" },
        ["Set.EmbedInfo"] = new() { ["en"] = "Write the description, series and cover &into the book file", ["hi"] = "विवरण, सीरीज़ और कवर पुस्तक फ़ाइल &में लिखें", ["es"] = "Escribir la descripción, la serie y la portada &en el archivo" },
        ["Set.Parallel"] = new() { ["en"] = "Do&wnloads at the same time:", ["hi"] = "&एक साथ डाउनलोड:", ["es"] = "Descargas &simultáneas:" },
        ["Set.SpeedLimit"] = new() { ["en"] = "Download speed &limit:", ["hi"] = "डाउनलोड ग&ति सीमा:", ["es"] = "Límite de &velocidad:" },
        ["Speed.None"] = new() { ["en"] = "No limit", ["hi"] = "कोई सीमा नहीं", ["es"] = "Sin límite" },
        ["Set.IncludePodcasts"] = new() { ["en"] = "List &podcast episodes from my library", ["hi"] = "मेरी लाइब्रेरी के &पॉडकास्ट एपिसोड दिखाएँ", ["es"] = "&Mostrar los episodios de podcast de mi biblioteca" },
        ["Set.AutoDownload"] = new() { ["en"] = "Download new purchases automatically when &the app starts", ["hi"] = "ऐप शुरू होने पर नई खरीदारी &अपने आप डाउनलोड करें", ["es"] = "Descargar &las compras nuevas automáticamente al iniciar" },
        ["Set.CheckUpdates"] = new() { ["en"] = "Check for &updates when the app starts", ["hi"] = "ऐप शुरू होने पर &अपडेट जाँचें", ["es"] = "&Buscar actualizaciones al iniciar" },
        ["Details.Series"] = new() { ["en"] = "Series", ["hi"] = "सीरीज़", ["es"] = "Serie" },
        ["Details.Description"] = new() { ["en"] = "Description", ["hi"] = "विवरण", ["es"] = "Descripción" },
        ["Details.Added"] = new() { ["en"] = "Added", ["hi"] = "जोड़ा गया", ["es"] = "Añadido" },

        // ---------------- DRM removal, opt-in ----------------

        // ---------------- player extras, store, accounts, backup, statistics ----------------
        ["Player.AddBookmark"] = new() { ["en"] = "Add boo&kmark…", ["hi"] = "बु&कमार्क जोड़ें…", ["es"] = "Añadir &marcador…" },
        ["Player.Bookmarks"] = new() { ["en"] = "Book&marks…", ["hi"] = "बुक&मार्क…", ["es"] = "&Lista de marcadores…" },
        ["Player.Sleep"] = new() { ["en"] = "Sl&eep timer:", ["hi"] = "&स्लीप टाइमर:", ["es"] = "&Temporizador:" },
        ["Player.SleepOff"] = new() { ["en"] = "Off", ["hi"] = "बंद", ["es"] = "Desactivado" },
        ["Player.SleepChapter"] = new() { ["en"] = "At the end of the chapter", ["hi"] = "अध्याय के अंत में", ["es"] = "Al final del capítulo" },
        ["Player.SleepMinutes"] = new() { ["en"] = "{0} minutes", ["hi"] = "{0} मिनट", ["es"] = "{0} minutos" },
        ["Player.SkipSilence"] = new() { ["en"] = "Skip &long silences", ["hi"] = "&लंबी चुप्पी छोड़ें", ["es"] = "Saltar l&os silencios largos" },
        ["Player.SleepCancelled"] = new() { ["en"] = "Sleep timer cancelled.", ["hi"] = "स्लीप टाइमर रद्द।", ["es"] = "Temporizador cancelado." },
        ["Player.SleepAtChapter"] = new() { ["en"] = "Playback stops at the end of this chapter.", ["hi"] = "इस अध्याय के अंत में प्लेबैक रुकेगा।", ["es"] = "La reproducción se detendrá al final de este capítulo." },
        ["Player.SleepSet"] = new() { ["en"] = "Sleep timer {0} minutes: playback stops at {1}.", ["hi"] = "स्लीप टाइमर {0} मिनट: प्लेबैक {1} पर रुकेगा।", ["es"] = "Temporizador de {0} minutos: se detendrá a las {1}." },
        ["Player.SleepDone"] = new() { ["en"] = "Sleep timer: paused at {0}. Good night.", ["hi"] = "स्लीप टाइमर: {0} पर रोका गया। शुभ रात्रि।", ["es"] = "Temporizador: en pausa en {0}. Buenas noches." },
        ["Player.SilenceUnsupported"] = new() { ["en"] = "Silences can only be skipped in M4B and M4A files.", ["hi"] = "चुप्पी केवल M4B और M4A फ़ाइलों में छोड़ी जा सकती है।", ["es"] = "Los silencios solo se pueden saltar en archivos M4B y M4A." },
        ["Player.SilenceFinding"] = new() { ["en"] = "Finding the long silences in this book…", ["hi"] = "इस पुस्तक की लंबी चुप्पियाँ खोजी जा रही हैं…", ["es"] = "Buscando los silencios largos de este libro…" },
        ["Player.SilenceFound"] = new() { ["en"] = "{0} long silences will be skipped.", ["hi"] = "{0} लंबी चुप्पियाँ छोड़ी जाएँगी।", ["es"] = "Se saltarán {0} silencios largos." },
        ["Bookmark.AddTitle"] = new() { ["en"] = "Add bookmark", ["hi"] = "बुकमार्क जोड़ें", ["es"] = "Añadir marcador" },
        ["Bookmark.NoteLabel"] = new() { ["en"] = "Note for the bookmark at {0} (optional):", ["hi"] = "{0} पर बुकमार्क के लिए नोट (वैकल्पिक):", ["es"] = "Nota para el marcador en {0} (opcional):" },
        ["Bookmark.Added"] = new() { ["en"] = "Bookmark added at {0}.", ["hi"] = "{0} पर बुकमार्क जोड़ा गया।", ["es"] = "Marcador añadido en {0}." },
        ["Bookmark.None"] = new() { ["en"] = "This book has no bookmarks yet. Press Ctrl+B to add one.", ["hi"] = "इस पुस्तक में अभी कोई बुकमार्क नहीं है। जोड़ने के लिए Ctrl+B दबाएँ।", ["es"] = "Este libro aún no tiene marcadores. Pulse Ctrl+B para añadir uno." },
        ["Bookmark.ListTitle"] = new() { ["en"] = "Bookmarks", ["hi"] = "बुकमार्क", ["es"] = "Marcadores" },
        ["Bookmark.Go"] = new() { ["en"] = "&Go to", ["hi"] = "&जाएँ", ["es"] = "&Ir" },
        ["Bookmark.Delete"] = new() { ["en"] = "&Delete", ["hi"] = "&हटाएँ", ["es"] = "&Eliminar" },
        ["Bookmark.InChapter"] = new() { ["en"] = "chapter {0}", ["hi"] = "अध्याय {0}", ["es"] = "capítulo {0}" },
        ["Bookmark.Went"] = new() { ["en"] = "Bookmark at {0}. {1}", ["hi"] = "{0} पर बुकमार्क। {1}", ["es"] = "Marcador en {0}. {1}" },
        ["Menu.SortSeries"] = new() { ["en"] = "Sort by &series", ["hi"] = "&सीरीज़ से क्रमबद्ध करें", ["es"] = "Ordenar por &serie" },
        ["Menu.Stats"] = new() { ["en"] = "Listening &statistics…", ["hi"] = "सुनने के &आँकड़े…", ["es"] = "&Estadísticas de escucha…" },
        ["Menu.BackupNow"] = new() { ["en"] = "&Back up downloaded books now", ["hi"] = "डाउनलोड की गई पुस्तकों का अभी &बैकअप लें", ["es"] = "Hacer copia de &seguridad ahora" },
        ["BookMenu.Name"] = new() { ["en"] = "Book", ["hi"] = "पुस्तक", ["es"] = "Libro" },
        ["BookMenu.Details"] = new() { ["en"] = "&Book details…", ["hi"] = "पुस्तक &विवरण…", ["es"] = "Detalles del &libro…" },
        ["Main.SortNameSeries"] = new() { ["en"] = "series", ["hi"] = "सीरीज़", ["es"] = "serie" },
        ["Main.SignedInAs"] = new() { ["en"] = "Signed in as {0} ({1}).", ["hi"] = "{0} के रूप में साइन इन ({1})।", ["es"] = "Sesión iniciada como {0} ({1})." },
        ["Backup.NoFolder"] = new() { ["en"] = "Choose a backup folder first, in Settings, Downloads.", ["hi"] = "पहले सेटिंग्स, डाउनलोड में बैकअप फ़ोल्डर चुनें।", ["es"] = "Elija antes una carpeta de copia en Configuración, Descargas." },
        ["Backup.Running"] = new() { ["en"] = "Backing up to {0}…", ["hi"] = "{0} में बैकअप हो रहा है…", ["es"] = "Copiando a {0}…" },
        ["Backup.Done"] = new() { ["en"] = "{0} file(s) backed up to {1}.", ["hi"] = "{0} फ़ाइलें {1} में बैकअप हुईं।", ["es"] = "Se copiaron {0} archivo(s) a {1}." },
        ["Stats.Title"] = new() { ["en"] = "Listening statistics", ["hi"] = "सुनने के आँकड़े", ["es"] = "Estadísticas de escucha" },
        ["Stats.Today"] = new() { ["en"] = "Today", ["hi"] = "आज", ["es"] = "Hoy" },
        ["Stats.Week"] = new() { ["en"] = "Last 7 days", ["hi"] = "पिछले 7 दिन", ["es"] = "Últimos 7 días" },
        ["Stats.Month"] = new() { ["en"] = "Last 30 days", ["hi"] = "पिछले 30 दिन", ["es"] = "Últimos 30 días" },
        ["Stats.Total"] = new() { ["en"] = "In total", ["hi"] = "कुल", ["es"] = "En total" },
        ["Stats.Streak"] = new() { ["en"] = "Days in a row", ["hi"] = "लगातार दिन", ["es"] = "Días seguidos" },
        ["Stats.Finished"] = new() { ["en"] = "Books finished", ["hi"] = "पूरी की गई पुस्तकें", ["es"] = "Libros terminados" },
        ["Stats.FinishedMonth"] = new() { ["en"] = "Books finished in the last 30 days", ["hi"] = "पिछले 30 दिनों में पूरी की गई पुस्तकें", ["es"] = "Libros terminados en los últimos 30 días" },
        ["Stats.Duration"] = new() { ["en"] = "{0} hours {1} minutes", ["hi"] = "{0} घंटे {1} मिनट", ["es"] = "{0} horas {1} minutos" },
        ["Details.NextInSeries"] = new() { ["en"] = "Next in the series", ["hi"] = "सीरीज़ में अगली", ["es"] = "Siguiente de la serie" },
        ["Shortcut.SortSeries"] = new() { ["en"] = "Sort the book list by series, in reading order.", ["hi"] = "पुस्तक सूची को सीरीज़ से, पढ़ने के क्रम में क्रमबद्ध करें।", ["es"] = "Ordenar la lista por serie, en orden de lectura." },
        ["Shortcut.Stats"] = new() { ["en"] = "Show how much you have listened: today, this week, this month, and your streak.", ["hi"] = "दिखाएँ कि आपने कितना सुना: आज, इस सप्ताह, इस महीने, और लगातार दिन।", ["es"] = "Mostrar cuánto ha escuchado: hoy, esta semana, este mes y su racha." },
        ["Set.Accounts"] = new() { ["en"] = "Account in &use:", ["hi"] = "&उपयोग में खाता:", ["es"] = "Cuenta en &uso:" },
        ["Set.AddAccount"] = new() { ["en"] = "Add a&nother account…", ["hi"] = "दूसरा खा&ता जोड़ें…", ["es"] = "Añadir &otra cuenta…" },
        ["Set.SwitchedTo"] = new() { ["en"] = "Now using {0}.", ["hi"] = "अब {0} का उपयोग हो रहा है।", ["es"] = "Ahora se usa {0}." },
        ["Set.NewAccount"] = new() { ["en"] = "New account", ["hi"] = "नया खाता", ["es"] = "Cuenta nueva" },
        ["Set.Backup"] = new() { ["en"] = "Copy every finished book to a &backup folder", ["hi"] = "हर पूरी पुस्तक को बैकअप &फ़ोल्डर में कॉपी करें", ["es"] = "Copiar cada libro &terminado a una carpeta de copia" },
        ["Set.BackupFolder"] = new() { ["en"] = "Back&up folder:", ["hi"] = "बैकअप फ़ोल्डर का स्था&न:", ["es"] = "Ca&rpeta de copia:" },
        ["Set.ChooseBackupFolder"] = new() { ["en"] = "Choose the backup folder (a OneDrive folder is uploaded to the cloud by OneDrive)", ["hi"] = "बैकअप फ़ोल्डर चुनें (OneDrive फ़ोल्डर को OneDrive क्लाउड पर अपलोड करता है)", ["es"] = "Elija la carpeta de copia (OneDrive sube a la nube una carpeta de OneDrive)" },
        ["Set.AnnounceChapters"] = new() { ["en"] = "Say &each chapter's name when the player reaches it", ["hi"] = "प्लेयर नए &अध्याय पर पहुँचे तो उसका नाम बताएँ", ["es"] = "&Decir el nombre de cada capítulo al llegar a él" },
        ["Set.ProgressBeeps"] = new() { ["en"] = "&Beep as a download progresses, like NVDA's progress bars", ["hi"] = "NVDA की प्रगति पट्टी की तरह डाउनलोड के साथ &बीप बजाएँ", ["es"] = "Emitir un &bip según avanza la descarga, como las barras de progreso de NVDA" },
        ["Group.DownloadFolder"] = new() { ["en"] = "Download folder", ["hi"] = "डाउनलोड फ़ोल्डर", ["es"] = "Carpeta de descargas" },
        ["Group.WhenFinished"] = new() { ["en"] = "When downloads finish", ["hi"] = "डाउनलोड पूरे होने पर", ["es"] = "Al terminar las descargas" },
        ["Group.App"] = new() { ["en"] = "Using the app", ["hi"] = "ऐप का उपयोग", ["es"] = "Uso de la aplicación" },
        ["Group.Files"] = new() { ["en"] = "Files and folders", ["hi"] = "फ़ाइलें और फ़ोल्डर", ["es"] = "Archivos y carpetas" },
        ["Group.Backup"] = new() { ["en"] = "Backup", ["hi"] = "बैकअप", ["es"] = "Copia de seguridad" },
        ["Group.Speed"] = new() { ["en"] = "Speed", ["hi"] = "गति", ["es"] = "Velocidad" },
        ["Group.Account"] = new() { ["en"] = "Account", ["hi"] = "खाता", ["es"] = "Cuenta" },
        ["Group.Library"] = new() { ["en"] = "Library", ["hi"] = "लाइब्रेरी", ["es"] = "Biblioteca" },
        ["Group.AppLanguage"] = new() { ["en"] = "Language of the app", ["hi"] = "ऐप की भाषा", ["es"] = "Idioma de la aplicación" },
        ["Group.BookLanguages"] = new() { ["en"] = "Language of the books listed", ["hi"] = "दिखाई जाने वाली किताबों की भाषा", ["es"] = "Idioma de los libros que se muestran" },
        ["Group.Announcements"] = new() { ["en"] = "Screen reader announcements", ["hi"] = "स्क्रीन रीडर घोषणाएँ", ["es"] = "Anuncios del lector de pantalla" },
        ["Group.Sounds"] = new() { ["en"] = "Sounds", ["hi"] = "ध्वनियाँ", ["es"] = "Sonidos" },
        ["Set.LanguageAudible"] = new() { ["en"] = "A&udible:", ["hi"] = "&Audible:", ["es"] = "A&udible:" },
        ["Set.LanguageLibriVox"] = new() { ["en"] = "&LibriVox:", ["hi"] = "&LibriVox:", ["es"] = "&LibriVox:" },
        ["Set.LanguageGutenberg"] = new() { ["en"] = "Project &Gutenberg:", ["hi"] = "Project &Gutenberg:", ["es"] = "Project &Gutenberg:" },
        ["Main.BookDownloaded"] = new() { ["en"] = "Download successful: {0}", ["hi"] = "डाउनलोड सफल: {0}", ["es"] = "Descarga correcta: {0}" },
        ["Notify.Title"] = new() { ["en"] = "Download complete", ["hi"] = "डाउनलोड पूरा", ["es"] = "Descarga completada" },
        ["Set.Notify"] = new() { ["en"] = "Show a Windows &notification, with its sound, when a book has downloaded", ["hi"] = "किताब डाउनलोड होने पर Windows &सूचना उसकी ध्वनि के साथ दिखाएँ", ["es"] = "Mostrar una &notificación de Windows, con su sonido, al descargar cada libro" },
        ["Format.ArchiveZip"] = new() { ["en"] = "The item's files in a ZIP file", ["hi"] = "ZIP फ़ाइल में आइटम की सभी फ़ाइलें", ["es"] = "Los archivos del elemento en un archivo ZIP" },
        ["Format.LibriVoxZip"] = new() { ["en"] = "MP3 chapters in a ZIP file", ["hi"] = "ZIP फ़ाइल में MP3 अध्याय", ["es"] = "Capítulos MP3 en un archivo ZIP" },
        ["Format.Mp3Chapters"] = new() { ["en"] = "MP3, one file per chapter", ["hi"] = "MP3, हर अध्याय की अलग फ़ाइल", ["es"] = "MP3, un archivo por capítulo" },
        ["Format.M4bChapters"] = new() { ["en"] = "M4B, one file per chapter", ["hi"] = "M4B, हर अध्याय की अलग फ़ाइल", ["es"] = "M4B, un archivo por capítulo" },
        ["DownloadFormat.Now"] = new() { ["en"] = "&Download now", ["hi"] = "अभी &डाउनलोड करें", ["es"] = "&Descargar ahora" },
        ["DownloadFormat.Label"] = new() { ["en"] = "&Format:", ["hi"] = "&फ़ॉर्मैट:", ["es"] = "&Formato:" },
        ["DownloadFormat.Books"] = new() { ["en"] = "{0} books", ["hi"] = "{0} किताबें", ["es"] = "{0} libros" },
        ["DownloadFormat.Title"] = new() { ["en"] = "Download {0}", ["hi"] = "{0} डाउनलोड करें", ["es"] = "Descargar {0}" },
        ["Main.BookFailed"] = new() { ["en"] = "{0} could not be downloaded: {1}", ["hi"] = "{0} डाउनलोड नहीं हो सकी: {1}", ["es"] = "No se pudo descargar {0}: {1}" },
        ["Main.DownloadsStarted"] = new() { ["en"] = "Downloading {0} books, one after another.", ["hi"] = "{0} किताबें एक-एक करके डाउनलोड हो रही हैं।", ["es"] = "Descargando {0} libros, uno tras otro." },
        ["Main.DownloadStarted"] = new() { ["en"] = "Downloading {0}.", ["hi"] = "{0} डाउनलोड हो रही है।", ["es"] = "Descargando {0}." },
        ["BookMenu.Download"] = new() { ["en"] = "&Download", ["hi"] = "&डाउनलोड करें", ["es"] = "&Descargar" },
        ["Main.ProgressPercent"] = new() { ["en"] = "{0} percent", ["hi"] = "{0} प्रतिशत", ["es"] = "{0} por ciento" },
        ["Drm.NotPurchasedTitle"] = new() { ["en"] = "Not a book you bought", ["hi"] = "यह आपकी खरीदी हुई किताब नहीं है", ["es"] = "No es un libro que haya comprado" },
        ["Drm.NotPurchased"] = new() { ["en"] = "You have not bought these books. They are in your library only through your Audible membership (Audible Plus):\n\n{0}\n\nDownloading them removes their DRM, so you get ordinary files that play anywhere. Removing DRM from a book you have not bought may be illegal in your country. If you go ahead, you do so entirely at your own responsibility: the developer of this app is not responsible for anything that may happen to you because of it.\n\nDownload them and remove their DRM?", ["hi"] = "ये किताबें आपने खरीदी नहीं हैं। ये सिर्फ़ आपकी Audible सदस्यता (Audible Plus) की वजह से आपकी लाइब्रेरी में हैं:\n\n{0}\n\nडाउनलोड करने पर इनका DRM हटा दिया जाएगा, ताकि आपको सामान्य फ़ाइलें मिलें जो कहीं भी चलें। बिना खरीदी किताब से DRM हटाना आपके देश में गैरकानूनी हो सकता है। अगर आप आगे बढ़ते हैं, तो यह पूरी तरह आपकी अपनी ज़िम्मेदारी है: इसकी वजह से आपके साथ जो कुछ भी हो, उसके लिए इस ऐप का डेवलपर ज़िम्मेदार नहीं है।\n\nक्या इन्हें डाउनलोड करके इनका DRM हटाना है?", ["es"] = "No ha comprado estos libros. Están en su biblioteca solo por su suscripción de Audible (Audible Plus):\n\n{0}\n\nAl descargarlos se les quita el DRM, para que tenga archivos normales que se reproducen en cualquier lugar. Quitar el DRM de un libro que no ha comprado puede ser ilegal en su país. Si continúa, lo hace bajo su entera responsabilidad: el desarrollador de esta aplicación no es responsable de nada que pueda ocurrirle por ello.\n\n¿Descargarlos y quitarles el DRM?" },

        // ---------------- keyboard help ----------------
        ["Help.Keys"] = new() { ["en"] = "Keys", ["hi"] = "कीज़", ["es"] = "Teclas" },
        ["Help.What"] = new() { ["en"] = "What it does", ["hi"] = "क्या करता है", ["es"] = "Qué hace" },
        ["Help.Close"] = new() { ["en"] = "&Close", ["hi"] = "&बंद करें", ["es"] = "&Cerrar" },
        ["Help.Label"] = new() { ["en"] = "Keyboard shortcuts", ["hi"] = "कीबोर्ड शॉर्टकट", ["es"] = "Atajos de teclado" },
        ["Help.Group.Application"] = new()
        {
            ["en"] = "In the application",
            ["hi"] = "ऐप्लिकेशन में",
            ["es"] = "En la aplicación",
        },
        ["Help.Group.Window"] = new()
        {
            ["en"] = "In the main window",
            ["hi"] = "मुख्य विंडो में",
            ["es"] = "En la ventana principal",
        },
        ["Help.Group.Dialogs"] = new()
        {
            ["en"] = "In dialogs and lists",
            ["hi"] = "डायलॉग और सूचियों में",
            ["es"] = "En cuadros de diálogo y listas",
        },
        ["Help.Group.WindowKeys"] = new()
        {
            ["en"] = "Alt keys this window offers on its own labels",
            ["hi"] = "इस विंडो के अपने लेबल पर मिलने वाली Alt कुंजियाँ",
            ["es"] = "Teclas Alt que ofrece esta ventana en sus propias etiquetas",
        },

        // ---------------- settings: page descriptions ----------------
        ["Settings.Sections"] = new() { ["en"] = "Settings sections", ["hi"] = "सेटिंग्स भाग", ["es"] = "Secciones de configuración" },
        ["Settings.Ready"] = new() { ["en"] = "Ready.", ["hi"] = "तैयार।", ["es"] = "Listo." },
        ["Settings.Status"] = new() { ["en"] = "Settings status", ["hi"] = "सेटिंग्स की स्थिति", ["es"] = "Estado de la configuración" },
        ["Settings.Save"] = new() { ["en"] = "Save settings", ["hi"] = "सेटिंग्स सहेजें", ["es"] = "Guardar la configuración" },
        ["Settings.Cancel"] = new() { ["en"] = "Cancel changes", ["hi"] = "बदलाव रद्द करें", ["es"] = "Cancelar los cambios" },
        ["Settings.Apply"] = new() { ["en"] = "Apply settings", ["hi"] = "सेटिंग्स लागू करें", ["es"] = "Aplicar la configuración" },
        ["Settings.Saved"] = new() { ["en"] = "Settings saved.", ["hi"] = "सेटिंग्स सहेज दी गईं।", ["es"] = "Configuración guardada." },

        // ---------------- settings: general / downloads ----------------
        ["Set.ChooseFolder"] = new() { ["en"] = "Choose the folder where downloads are saved", ["hi"] = "वह फ़ोल्डर चुनें जहाँ डाउनलोड सहेजे जाएँ", ["es"] = "Elija la carpeta donde se guardan las descargas" },
        ["Downloads.Note"] = new() { ["en"] = "Every Audible book has its DRM removed, losslessly and entirely on your computer. Audible issues download licences only for titles in your own library.", ["hi"] = "हर Audible किताब का DRM बिना गुणवत्ता घटाए, पूरी तरह आपके कंप्यूटर पर हटाया जाता है। Audible केवल आपकी अपनी लाइब्रेरी की किताबों के लिए डाउनलोड लाइसेंस देता है।", ["es"] = "A cada libro de Audible se le quita el DRM sin pérdida de calidad y por completo en su equipo. Audible solo emite licencias de descarga para los títulos de su propia biblioteca." },

        // ---------------- Audible countries ----------------
        //
        // Full country names, because this is what the country selector shows and
        // what a screen reader reads out. The store codes ("us", "in") are how the
        // sign-in is sent, never how a country is named to a person.
        ["Country.us"] = new() { ["en"] = "United States", ["hi"] = "संयुक्त राज्य अमेरिका", ["es"] = "Estados Unidos" },
        ["Country.uk"] = new() { ["en"] = "United Kingdom", ["hi"] = "यूनाइटेड किंगडम", ["es"] = "Reino Unido" },
        ["Country.ca"] = new() { ["en"] = "Canada", ["hi"] = "कनाडा", ["es"] = "Canadá" },
        ["Country.au"] = new() { ["en"] = "Australia", ["hi"] = "ऑस्ट्रेलिया", ["es"] = "Australia" },
        ["Country.in"] = new() { ["en"] = "India", ["hi"] = "भारत", ["es"] = "India" },
        ["Country.de"] = new() { ["en"] = "Germany", ["hi"] = "जर्मनी", ["es"] = "Alemania" },
        ["Country.fr"] = new() { ["en"] = "France", ["hi"] = "फ़्रांस", ["es"] = "Francia" },
        ["Country.es"] = new() { ["en"] = "Spain", ["hi"] = "स्पेन", ["es"] = "España" },
        ["Country.it"] = new() { ["en"] = "Italy", ["hi"] = "इटली", ["es"] = "Italia" },
        ["Country.jp"] = new() { ["en"] = "Japan", ["hi"] = "जापान", ["es"] = "Japón" },
        ["Country.br"] = new() { ["en"] = "Brazil", ["hi"] = "ब्राज़ील", ["es"] = "Brasil" },
        ["Country.Label"] = new() { ["en"] = "Country", ["hi"] = "देश", ["es"] = "País" },
        ["Country.Instructions"] = new()
        {
            ["en"] = "Choose the country your Audible account belongs to, or type the first letters of its name. " +
                   "Your library, the books you can download and the prices all come from that country's store.",
            ["hi"] = "वह देश चुनें जिससे आपका Audible खाता जुड़ा है, या उसके नाम के शुरुआती अक्षर लिखें। आपकी लाइब्रेरी, " +
                   "डाउनलोड होने योग्य पुस्तकें और कीमतें उसी देश के स्टोर से आती हैं।",
            ["es"] = "Elija el país al que pertenece su cuenta de Audible, o escriba las primeras letras de su " +
                   "nombre. Su biblioteca, los libros que puede descargar y los precios provienen de la tienda " +
                   "de ese país.",
        },
        ["Country.Waiting"] = new()
        {
            ["en"] = "Choose a country to continue. The Sign in button appears once a country is chosen.",
            ["hi"] = "आगे बढ़ने के लिए देश चुनें। देश चुनने के बाद ही “साइन इन” बटन दिखेगा।",
            ["es"] = "Elija un país para continuar. El botón Iniciar sesión aparece cuando haya elegido un país.",
        },
        ["Country.Chosen"] = new()
        {
            ["en"] = "{0} chosen. Press Sign in to continue.",
            ["hi"] = "{0} चुना गया। जारी रखने के लिए “साइन इन” दबाएँ।",
            ["es"] = "{0} seleccionado. Pulse Iniciar sesión para continuar.",
        },
        ["Country.NoMatch"] = new()
        {
            ["en"] = "No country matches “{0}”. Keep typing, or press Backspace to start the name again.",
            ["hi"] = "“{0}” से कोई देश नहीं मिला। लिखते रहें, या नाम फिर से शुरू करने के लिए बैकस्पेस दबाएँ।",
            ["es"] = "Ningún país coincide con “{0}”. Siga escribiendo o pulse Retroceso para empezar el nombre de nuevo.",
        },
        ["Country.SignIn"] = new() { ["en"] = "&Sign in", ["hi"] = "&साइन इन", ["es"] = "&Iniciar sesión" },
        ["Country.SignInAccessible"] = new() { ["en"] = "Sign in", ["hi"] = "साइन इन", ["es"] = "Iniciar sesión" },
        ["Country.Title"] = new() { ["en"] = "Sign in to Audible", ["hi"] = "Audible में साइन इन करें", ["es"] = "Iniciar sesión en Audible" },

        // ---------------- settings: Audible account ----------------
        ["Set.SwitchAccount"] = new() { ["en"] = "S&witch account…", ["hi"] = "&खाता बदलें…", ["es"] = "Cambiar &de cuenta…" },
        ["Audible.Note"] = new()
        {
            ["en"] = "No Audible book, story, recommendation or library item is shown unless you are signed in. " +
                   "Nothing is remembered between runs except the sign-in itself.",
            ["hi"] = "साइन इन नहीं होने पर Audible की कोई पुस्तक, कहानी, सुझाव या लाइब्रेरी आइटम नहीं दिखता। " +
                   "साइन-इन के अलावा कुछ भी सहेजा नहीं जाता।",
            ["es"] = "No se muestra ningún libro, historia, recomendación ni elemento de la biblioteca de Audible " +
                   "si no ha iniciado sesión. Nada se recuerda entre ejecuciones salvo el inicio de sesión.",
        },
        ["Audible.SigningIn"] = new()
        {
            ["en"] = "Signing in to Audible. Finish the sign-in in the window that opened; it closes by itself.",
            ["hi"] = "Audible में साइन इन हो रहा है। जो विंडो खुली है उसमें साइन इन पूरा करें; वह अपने आप बंद हो जाएगी।",
            ["es"] = "Iniciando sesión en Audible. Complete el inicio de sesión en la ventana que se abrió; se cerrará sola.",
        },
        ["Audible.SignedIn"] = new()
        {
            ["en"] = "Signed in to Audible. Your library can now be listed.",
            ["hi"] = "Audible में साइन इन हो गए। अब आपकी लाइब्रेरी दिखाई जा सकती है।",
            ["es"] = "Sesión iniciada en Audible. Ya puede ver su biblioteca.",
        },
        ["Audible.SignedInRegion"] = new()
        {
            ["en"] = "Signed in to Audible, region {0}.",
            ["hi"] = "Audible में साइन इन, क्षेत्र {0}।",
            ["es"] = "Sesión iniciada en Audible, región {0}.",
        },
        ["Audible.SignedOut"] = new()
        {
            ["en"] = "Not signed in to Audible. No Audible books are shown until you sign in.",
            ["hi"] = "Audible में साइन इन नहीं हैं। साइन इन करने तक कोई Audible पुस्तक नहीं दिखेगी।",
            ["es"] = "No ha iniciado sesión en Audible. No se mostrará ningún libro hasta que lo haga.",
        },
        ["Audible.SignedOutSavedSignIn"] = new()
        {
            ["en"] = "Not signed in, but a saved sign-in is present. Press Sign in to use it.",
            ["hi"] = "साइन इन नहीं हैं, पर सहेजा गया साइन-इन मौजूद है। उपयोग करने के लिए साइन इन दबाएँ।",
            ["es"] = "Sin sesión iniciada, pero hay un inicio de sesión guardado. Pulse Iniciar sesión para usarlo.",
        },
        ["Audible.SignInCancelled"] = new()
        {
            ["en"] = "Audible sign-in was cancelled. You are still signed out.",
            ["hi"] = "Audible साइन-इन रद्द कर दिया गया। आप अब भी साइन आउट हैं।",
            ["es"] = "Se canceló el inicio de sesión de Audible. Sigue sin sesión.",
        },
        ["Audible.SignInFailed"] = new()
        {
            ["en"] = "Audible sign-in failed: {0}",
            ["hi"] = "Audible साइन-इन विफल: {0}",
            ["es"] = "Falló el inicio de sesión de Audible: {0}",
        },
        ["Audible.SignInFailedTitle"] = new() { ["en"] = "Audible sign-in failed", ["hi"] = "Audible साइन-इन विफल", ["es"] = "Falló el inicio de sesión de Audible" },
        ["Audible.SignOutConfirm"] = new()
        {
            ["en"] = "Sign out of Audible and delete the saved sign-in from this computer?\n\n" +
                   "No downloaded files are removed, but Audible books will be hidden until you sign in again.",
            ["hi"] = "Audible से साइन आउट करें और इस कंप्यूटर से सहेजा गया साइन-इन हटाएँ?\n\n" +
                   "डाउनलोड की गई फ़ाइलें नहीं हटेंगी, पर साइन इन करने तक Audible पुस्तकें छिपी रहेंगी।",
            ["es"] = "¿Cerrar la sesión de Audible y borrar el inicio de sesión guardado de este equipo?\n\n" +
                   "No se elimina ningún archivo descargado, pero los libros de Audible se ocultarán hasta que vuelva a iniciar sesión.",
        },
        ["Audible.SignOutTitle"] = new() { ["en"] = "Sign out of Audible", ["hi"] = "Audible से साइन आउट करें", ["es"] = "Cerrar la sesión de Audible" },
        ["Audible.SignedOutAndForgotten"] = new()
        {
            ["en"] = "Signed out of Audible and deleted the saved sign-in. No Audible content will be shown.",
            ["hi"] = "Audible से साइन आउट और सहेजा गया साइन-इन हटा दिया गया। कोई Audible सामग्री नहीं दिखेगी।",
            ["es"] = "Sesión cerrada y inicio de sesión guardado eliminado. No se mostrará contenido de Audible.",
        },

        // ---------------- settings: Gutenberg / language / accessibility ----------------
        ["Format.Audio"] = new() { ["en"] = "Audio (MP3) when available, otherwise EPUB", ["hi"] = "उपलब्ध हो तो ऑडियो (MP3), वरना EPUB", ["es"] = "Audio (MP3) si está disponible; si no, EPUB" },
        ["Format.Epub"] = new() { ["en"] = "EPUB", ["hi"] = "EPUB", ["es"] = "EPUB" },
        ["Format.Html"] = new() { ["en"] = "HTML", ["hi"] = "HTML", ["es"] = "HTML" },
        ["Format.Text"] = new() { ["en"] = "Plain text", ["hi"] = "सादा पाठ", ["es"] = "Texto sin formato" },
        ["Language.Note"] = new()
        {
            ["en"] = "Some technical messages are always shown in English.",
            ["hi"] = "कुछ तकनीकी संदेश हमेशा अंग्रेज़ी में दिखाए जाते हैं।",
            ["es"] = "Algunos mensajes técnicos siempre se muestran en inglés.",
        },

        // ---------------- about --------------
        ["About.Title"] = new() { ["en"] = "About Audiobook Downloader", ["hi"] = "ऑडियोबुक डाउनलोडर के बारे में", ["es"] = "Acerca de Descargador de audiolibros" },
        ["About.Heading"] = new() { ["en"] = "Application name", ["hi"] = "ऐप का नाम", ["es"] = "Nombre de la aplicación" },
        ["About.Version"] = new() { ["en"] = "Version {0}", ["hi"] = "संस्करण {0}", ["es"] = "Versión {0}" },
        ["About.Summary"] = new()
        {
            ["en"] = "A Windows desktop app for downloading audiobooks and books to your computer, " +
                   "with optional DRM removal for books you own.",
            ["hi"] = "ऑडियोबुक और पुस्तकें अपने कंप्यूटर पर डाउनलोड करने वाला Windows ऐप, " +
                   "अपनी खरीदी पुस्तकों से DRM हटाने की सुविधा के साथ।",
            ["es"] = "Una aplicación de Windows para descargar audiolibros y libros a su equipo, " +
                   "con eliminación opcional de DRM para libros propios.",
        },
        ["About.Providers"] = new() { ["en"] = "Available sources:", ["hi"] = "उपलब्ध स्रोत:", ["es"] = "Fuentes disponibles:" },
        ["About.Accessibility"] = new()
        {
            ["en"] = "Accessibility: every control is named, labelled and keyboard reachable. " +
                   "The app exposes status changes through the Windows accessibility APIs, so your " +
                   "screen reader reads them. It never speaks with a built-in voice of its own.",
            ["hi"] = "सुगम्यता: हर नियंत्रण का नाम है, लेबल है और कीबोर्ड से पहुँचा जा सकता है। " +
                   "ऐप स्थिति परिवर्तन Windows सुगम्यता API से बताता है, जिन्हें आपका स्क्रीन रीडर पढ़ता है। " +
                   "यह अपनी कोई आवाज़ नहीं बनाता।",
            ["es"] = "Accesibilidad: cada control tiene nombre, etiqueta y se alcanza con el teclado. " +
                   "La aplicación expone los cambios de estado con las API de accesibilidad de Windows, " +
                   "de modo que su lector de pantalla los lee. Nunca usa una voz propia.",
        },
        ["About.SettingsFolder"] = new() { ["en"] = "Settings and log folder:", ["hi"] = "सेटिंग्स और लॉग फ़ोल्डर:", ["es"] = "Carpeta de configuración y registro:" },
        ["About.Licence"] = new()
        {
            ["en"] = "For personal use. Only back up content you own and never share decrypted files.",
            ["hi"] = "निजी उपयोग के लिए। केवल अपनी सामग्री का बैकअप लें और डिक्रिप्ट की गई फ़ाइलें साझा न करें।",
            ["es"] = "Para uso personal. Haga copia solo de contenido propio y nunca comparta archivos descifrados.",
        },
        ["About.Text"] = new() { ["en"] = "About this application", ["hi"] = "इस ऐप के बारे में", ["es"] = "Acerca de esta aplicación" },
        ["About.Close"] = new() { ["en"] = "Close the About dialog", ["hi"] = "परिचय संवाद बंद करें", ["es"] = "Cerrar el cuadro Acerca de" },

        // ---------------- activity log ----------------

        // ---------------- progress live region ----------------
        ["Main.ProgressStatus"] = new()
        {
            ["en"] = "Download progress status",
            ["hi"] = "डाउनलोड प्रगति स्थिति",
            ["es"] = "Estado del progreso de descarga",
        },
        ["Main.ProgressStatusText"] = new() { ["en"] = "{0} — {1}%", ["hi"] = "{0} — {1}%", ["es"] = "{0} — {1}%" },

        // ---------------- Audible sign-in window ----------------
        // Amazon's own page opens inside the app; the window recognises the
        // redirect that ends the sign-in and closes itself. No address is ever
        // copied or typed by hand, so no string here asks for one.
        ["Login.Title"] = new()
        {
            ["en"] = "Sign in to Audible",
            ["hi"] = "Audible में साइन इन करें",
            ["es"] = "Iniciar sesión en Audible",
        },
        ["Login.Instructions"] = new()
        {
            ["en"] = "Sign in to your Amazon account on the page below. This window closes by itself as soon as Amazon sends you back to the app.",
            ["hi"] = "नीचे दिए पेज पर अपने Amazon खाते में साइन इन करें। Amazon द्वारा ऐप पर वापस भेजते ही यह विंडो अपने आप बंद हो जाएगी।",
            ["es"] = "Inicie sesión en su cuenta de Amazon en la página de abajo. Esta ventana se cierra sola en cuanto Amazon la devuelva a la aplicación.",
        },
        ["Login.InstructionsAccessible"] = new()
        {
            ["en"] = "Sign-in instructions",
            ["hi"] = "साइन-इन निर्देश",
            ["es"] = "Instrucciones de inicio de sesión",
        },
        ["Login.StatusAccessible"] = new()
        {
            ["en"] = "Sign-in status",
            ["hi"] = "साइन-इन स्थिति",
            ["es"] = "Estado del inicio de sesión",
        },
        ["Login.Starting"] = new()
        {
            ["en"] = "Opening Amazon's sign-in page…",
            ["hi"] = "Amazon का साइन-इन पेज खोला जा रहा है…",
            ["es"] = "Abriendo la página de inicio de sesión de Amazon…",
        },
        ["Login.Waiting"] = new()
        {
            ["en"] = "Waiting for you to finish signing in. This window closes by itself when you are done.",
            ["hi"] = "साइन इन पूरा करने की प्रतीक्षा है। पूरा होने पर यह विंडो अपने आप बंद हो जाएगी।",
            ["es"] = "Esperando a que termine de iniciar sesión. Esta ventana se cerrará sola cuando termine.",
        },
        ["Login.SignedIn"] = new()
        {
            ["en"] = "Signed in. Returning to the app…",
            ["hi"] = "साइन इन हो गया। ऐप पर वापस जा रहे हैं…",
            ["es"] = "Sesión iniciada. Volviendo a la aplicación…",
        },
        ["Login.PageFailed"] = new()
        {
            ["en"] = "That page could not be loaded. Press Reload the page to try again.",
            ["hi"] = "वह पेज लोड नहीं हो सका। दोबारा कोशिश के लिए पेज दोबारा लोड करें दबाएँ।",
            ["es"] = "No se pudo cargar esa página. Pulse Volver a cargar la página para intentarlo de nuevo.",
        },
        ["Login.PageAccessible"] = new()
        {
            ["en"] = "Amazon sign-in page",
            ["hi"] = "Amazon साइन-इन पेज",
            ["es"] = "Página de inicio de sesión de Amazon",
        },
        ["Login.Reload"] = new()
        {
            ["en"] = "&Reload the page",
            ["hi"] = "&पेज दोबारा लोड करें",
            ["es"] = "&Volver a cargar la página",
        },
        ["Login.ReloadAccessible"] = new()
        {
            ["en"] = "Reload the page",
            ["hi"] = "पेज दोबारा लोड करें",
            ["es"] = "Volver a cargar la página",
        },
        ["Login.CancelAccessible"] = new()
        {
            ["en"] = "Cancel",
            ["hi"] = "रद्द करें",
            ["es"] = "Cancelar",
        },
        ["Login.NoBrowser"] = new()
        {
            ["en"] = "The sign-in window needs the Microsoft WebView2 runtime, which comes with Microsoft Edge. " +
                     "Install the \"Microsoft Edge WebView2 Runtime\", then sign in again.",
            ["hi"] = "साइन-इन विंडो के लिए Microsoft WebView2 रनटाइम चाहिए, जो Microsoft Edge के साथ आता है। " +
                     "\"Microsoft Edge WebView2 Runtime\" इंस्टॉल करें, फिर दोबारा साइन इन करें।",
            ["es"] = "La ventana de inicio de sesión necesita el entorno de ejecución Microsoft WebView2, que viene con Microsoft Edge. " +
                     "Instale \"Microsoft Edge WebView2 Runtime\" y vuelva a iniciar sesión.",
        },
        ["Login.WindowFailed"] = new()
        {
            ["en"] = "The sign-in window could not be opened. {0}",
            ["hi"] = "साइन-इन विंडो खोली नहीं जा सकी। {0}",
            ["es"] = "No se pudo abrir la ventana de inicio de sesión. {0}",
        },

        // ---------------- decryption key dialog ----------------
    };
}
