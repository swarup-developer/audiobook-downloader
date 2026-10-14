using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Interface text in every language the app speaks. Keys are stable names; a
/// missing translation falls back to English, and a missing key falls back to
/// the key itself so nothing ever renders as blank.
/// </summary>
internal static class Strings
{
    /// <summary>Languages the interface can be shown in.</summary>
    public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        new("en", "English"),
        new("hi", "हिन्दी"),
        new("es", "Español"),
    ];

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
        ["Main.Provider"] = new() { ["en"] = "&Provider:", ["hi"] = "&प्रदाता:", ["es"] = "&Proveedor:" },
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
        ["Main.Language"] = new() { ["en"] = "Book language:", ["hi"] = "पुस्तक भाषा:", ["es"] = "Idioma de los libros:" },
        ["Main.LanguageAccessible"] = new() { ["en"] = "Book language", ["hi"] = "पुस्तक की भाषा", ["es"] = "Idioma de los libros" },
        // The list's caption says what it holds: the Audible library you own, or what
        // a free catalogue found for your search.
        ["Main.Books"] = new() { ["en"] = "Search results (&books):", ["hi"] = "खोज परिणाम (&पुस्तकें):", ["es"] = "Resultados (&libros):" },
        ["Main.BooksAudible"] = new() { ["en"] = "Your Audible library (&books):", ["hi"] = "आपकी Audible लाइब्रेरी (&पुस्तकें):", ["es"] = "Su biblioteca de Audible (&libros):" },
        ["Main.Download"] = new() { ["en"] = "&Download selected", ["hi"] = "चयनित &डाउनलोड करें", ["es"] = "&Descargar seleccionados" },
        ["Main.Cancel"] = new() { ["en"] = "&Cancel active", ["hi"] = "सक्रिय &रद्द करें", ["es"] = "&Cancelar la activa" },
        ["Main.Keys"] = new() { ["en"] = "How to get &keys…", ["hi"] = "&कीज़ कैसे पाएँ…", ["es"] = "Cómo obtener las &claves…" },
        ["Main.OpenFolder"] = new() { ["en"] = "&Open download folder", ["hi"] = "डाउनलोड &फ़ोल्डर खोलें", ["es"] = "&Abrir la carpeta de descargas" },

        // ---------------- the download queue's own menu ----------------
        //
        // The queue's actions are on the list's context menu (the menu key, Shift F10
        // or a right click) and on shortcuts, so the window itself carries only the two
        // actions the main job needs.
        ["QueueMenu.Retry"] = new() { ["en"] = "&Retry", ["hi"] = "&पुनः प्रयास", ["es"] = "&Reintentar" },
        ["QueueMenu.Remove"] = new() { ["en"] = "Re&move", ["hi"] = "&हटाएँ", ["es"] = "&Quitar" },
        ["QueueMenu.ClearFinished"] = new() { ["en"] = "Clear &finished", ["hi"] = "पूर्ण &साफ़ करें", ["es"] = "Borrar los &terminados" },

        // Titles of the three groups on the main window: 1. Find books,
        // 2. Downloads, 3. Status.
        ["Main.Group.Books"] = new() { ["en"] = "1. Find books", ["hi"] = "1. पुस्तकें खोजें", ["es"] = "1. Buscar libros" },
        ["Main.Group.Queue"] = new() { ["en"] = "2. Downloads", ["hi"] = "2. डाउनलोड", ["es"] = "2. Descargas" },
        ["Main.Group.Status"] = new() { ["en"] = "3. Status", ["hi"] = "3. स्थिति", ["es"] = "3. Estado" },
        ["Main.QueueList"] = new() { ["en"] = "Download queue", ["hi"] = "डाउनलोड सूची", ["es"] = "Cola de descargas" },
        ["Main.Progress"] = new() { ["en"] = "Download progress", ["hi"] = "डाउनलोड प्रगति", ["es"] = "Progreso de la descarga" },
        ["Main.Status"] = new() { ["en"] = "Status", ["hi"] = "स्थिति", ["es"] = "Estado" },
        ["Main.Log"] = new() { ["en"] = "Activity log", ["hi"] = "गतिविधि लॉग", ["es"] = "Registro de actividad" },

        // ---------------- common ----------------
        ["Common.OK"] = new() { ["en"] = "&OK", ["hi"] = "ठी&क है", ["es"] = "&Aceptar" },
        ["Common.Cancel"] = new() { ["en"] = "&Cancel", ["hi"] = "&रद्द करें", ["es"] = "&Cancelar" },
        ["Common.Close"] = new() { ["en"] = "&Close", ["hi"] = "&बंद करें", ["es"] = "&Cerrar" },
        ["Common.Apply"] = new() { ["en"] = "&Apply", ["hi"] = "&लागू करें", ["es"] = "&Aplicar" },
        ["Common.Browse"] = new() { ["en"] = "B&rowse…", ["hi"] = "&ब्राउज़ करें…", ["es"] = "E&xaminar…" },

        // ---------------- settings ----------------
        ["Settings.Title"] = new() { ["en"] = "Settings", ["hi"] = "सेटिंग्स", ["es"] = "Configuración" },
        ["Settings.General"] = new() { ["en"] = "General", ["hi"] = "सामान्य", ["es"] = "General" },
        ["Settings.Downloads"] = new() { ["en"] = "Downloads", ["hi"] = "डाउनलोड", ["es"] = "Descargas" },
        ["Settings.Audible"] = new() { ["en"] = "Audible account", ["hi"] = "Audible खाता", ["es"] = "Cuenta de Audible" },
        ["Settings.Gutenberg"] = new() { ["en"] = "Project Gutenberg", ["hi"] = "प्रोजेक्ट गुटेनबर्ग", ["es"] = "Project Gutenberg" },
        ["Settings.Language"] = new() { ["en"] = "Language", ["hi"] = "भाषा", ["es"] = "Idioma" },
        ["Settings.Accessibility"] = new() { ["en"] = "Accessibility", ["hi"] = "सुगम्यता", ["es"] = "Accesibilidad" },

        ["Set.OutputFolder"] = new() { ["en"] = "Download &folder:", ["hi"] = "डाउनलोड &फ़ोल्डर:", ["es"] = "Carpeta de &descargas:" },
        ["Set.LocalFolder"] = new() { ["en"] = "Folder with your &AAX or AAXC files:", ["hi"] = "आपकी &AAX या AAXC फ़ाइलों का फ़ोल्डर:", ["es"] = "Carpeta con sus archivos &AAX o AAXC:" },
        ["Set.OpenWhenDone"] = new() { ["en"] = "&Open the download folder when the queue finishes", ["hi"] = "सूची पूरी होने पर डाउनलोड &फ़ोल्डर खोलें", ["es"] = "&Abrir la carpeta cuando termine la cola" },
        ["Set.Toast"] = new() { ["en"] = "Show a Windows &notification when downloads finish", ["hi"] = "डाउनलोड पूरा होने पर Windows &सूचना दिखाएँ", ["es"] = "Mostrar una &notificación de Windows al terminar" },
        ["Set.ConfirmRemove"] = new() { ["en"] = "&Ask before removing an item from the queue", ["hi"] = "सूची से हटाने से पहले &पूछें", ["es"] = "&Preguntar antes de quitar de la cola" },
        ["Set.RememberProvider"] = new() { ["en"] = "&Remember the last provider", ["hi"] = "पिछला प्रदाता &याद रखें", ["es"] = "&Recordar el último proveedor" },
        ["Set.RemoveDrm"] = new() { ["en"] = "&Remove DRM from Audible files (personal backups of books you own)", ["hi"] = "Audible फ़ाइलों से &DRM हटाएँ (अपनी खरीदी पुस्तकों का निजी बैकअप)", ["es"] = "&Quitar DRM de los archivos de Audible (copias personales de libros propios)" },
        ["Set.DeleteIntermediate"] = new() { ["en"] = "&Delete the encrypted original after successful decryption", ["hi"] = "डिक्रिप्शन के बाद मूल एन्क्रिप्टेड फ़ाइल &हटाएँ", ["es"] = "&Eliminar el original cifrado tras descifrar" },
        ["Set.RememberSignIn"] = new() { ["en"] = "&Remember the Audible sign-in on this computer", ["hi"] = "इस कंप्यूटर पर Audible साइन-इन &याद रखें", ["es"] = "&Recordar el inicio de sesión de Audible en este equipo" },
        ["Set.LoadLibrary"] = new() { ["en"] = "Load my &library automatically after signing in", ["hi"] = "साइन इन के बाद &लाइब्रेरी अपने आप लोड करें", ["es"] = "Cargar mi &biblioteca tras iniciar sesión" },
        ["Set.Country"] = new() { ["en"] = "Audible &country:", ["hi"] = "Audible &देश:", ["es"] = "&País de Audible:" },
        ["Set.AccountStatus"] = new() { ["en"] = "Account status", ["hi"] = "खाता स्थिति", ["es"] = "Estado de la cuenta" },
        ["Set.SignIn"] = new() { ["en"] = "&Sign in to Audible…", ["hi"] = "Audible में &साइन इन करें…", ["es"] = "&Iniciar sesión en Audible…" },
        ["Set.SignInAccessible"] = new() { ["en"] = "Sign in to Audible", ["hi"] = "Audible में साइन इन करें", ["es"] = "Iniciar sesión en Audible" },
        ["Set.SwitchAccountAccessible"] = new() { ["en"] = "Switch Audible account", ["hi"] = "Audible खाता बदलें", ["es"] = "Cambiar de cuenta de Audible" },
        ["Set.SignOut"] = new() { ["en"] = "Sign &out and forget this account", ["hi"] = "साइन &आउट करें और खाता भूल जाएँ", ["es"] = "Cerrar &sesión y olvidar esta cuenta" },
        ["Set.SignOutAccessible"] = new() { ["en"] = "Sign out", ["hi"] = "साइन आउट करें", ["es"] = "Cerrar sesión" },
        ["Set.DefaultLanguage"] = new() { ["en"] = "Default &language for Gutenberg searches:", ["hi"] = "गुटेनबर्ग खोज की डिफ़ॉल्ट &भाषा:", ["es"] = "&Idioma predeterminado para las búsquedas:" },
        ["Set.PreferredFormat"] = new() { ["en"] = "Preferred &format:", ["hi"] = "पसंदीदा &प्रारूप:", ["es"] = "&Formato preferido:" },
        ["Set.UiLanguage"] = new() { ["en"] = "&Interface language:", ["hi"] = "&इंटरफ़ेस भाषा:", ["es"] = "Idioma de la &interfaz:" },
        ["Set.AnnounceStatus"] = new() { ["en"] = "Announce &status changes through the screen reader", ["hi"] = "स्क्रीन रीडर से &स्थिति परिवर्तन बताएँ", ["es"] = "Anunciar los cambios de &estado con el lector de pantalla" },
        ["Set.AnnounceProgress"] = new() { ["en"] = "Announce download &progress milestones", ["hi"] = "डाउनलोड &प्रगति बताएँ", ["es"] = "Anunciar los hitos del &progreso" },
        ["Set.KeepLog"] = new() { ["en"] = "Keep the &activity log visible", ["hi"] = "&गतिविधि लॉग दिखाई देता रहे", ["es"] = "Mantener visible el &registro de actividad" },
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
        ["Main.QueueRunning"] = new() { ["en"] = "Queue running: {0} completed, {1} failed.", ["hi"] = "सूची चल रही है: {0} पूर्ण, {1} विफल।", ["es"] = "Cola en marcha: {0} completados, {1} fallidos." },
        ["Main.QueueIdle"] = new() { ["en"] = "Queue idle: {0} completed, {1} failed, {2} pending.", ["hi"] = "सूची निष्क्रिय: {0} पूर्ण, {1} विफल, {2} प्रतीक्षित।", ["es"] = "Cola inactiva: {0} completados, {1} fallidos, {2} pendientes." },
        ["Main.SelectFirst"] = new() { ["en"] = "Select a book first.", ["hi"] = "पहले कोई पुस्तक चुनें।", ["es"] = "Seleccione primero un libro." },
        ["Main.AddedToQueue"] = new() { ["en"] = "{0} added to the download queue.", ["hi"] = "{0} डाउनलोड सूची में जोड़े गए।", ["es"] = "{0} añadidos a la cola de descargas." },
        ["Main.ResumingQueue"] = new()
        {
            ["en"] = "Resuming {0} unfinished download(s) where they left off. Press Cancel to stop.",
            ["hi"] = "{0} अधूरे डाउनलोड वहीं से जारी हो रहे हैं जहाँ वे रुके थे। रोकने के लिए “रद्द करें” दबाएँ।",
            ["es"] = "Se reanudan {0} descarga(s) sin terminar donde se quedaron. Pulse Cancelar para detenerlas.",
        },
        ["Main.RestoredQueue"] = new()
        {
            ["en"] = "The last run left {0} unfinished download(s) in the queue. Press Retry to try one again.",
            ["hi"] = "पिछली बार {0} अधूरे डाउनलोड सूची में रह गए थे। फिर से कोशिश करने के लिए “पुनः प्रयास” दबाएँ।",
            ["es"] = "La última sesión dejó {0} descarga(s) sin terminar en la cola. Pulse Reintentar para volver a intentarlo.",
        },
        ["Main.Cancelling"] = new() { ["en"] = "Cancelling the active download…", ["hi"] = "सक्रिय डाउनलोड रद्द हो रहा है…", ["es"] = "Cancelando la descarga activa…" },
        ["Main.SelectFailedFirst"] = new() { ["en"] = "Select a failed or cancelled item in the queue first.", ["hi"] = "पहले सूची में विफल या रद्द आइटम चुनें।", ["es"] = "Seleccione primero un elemento fallido o cancelado." },
        ["Main.OnlyFailedRetry"] = new() { ["en"] = "Only failed or cancelled items can be retried.", ["hi"] = "केवल विफल या रद्द आइटम फिर से चलाए जा सकते हैं।", ["es"] = "Solo se pueden reintentar los elementos fallidos o cancelados." },
        ["Main.Retrying"] = new() { ["en"] = "Retrying {0}.", ["hi"] = "{0} का पुनः प्रयास हो रहा है।", ["es"] = "Reintentando {0}." },
        ["Main.SelectPendingFirst"] = new() { ["en"] = "Select a pending item in the queue first.", ["hi"] = "पहले सूची में प्रतीक्षित आइटम चुनें।", ["es"] = "Seleccione primero un elemento pendiente." },
        ["Main.OnlyPendingRemove"] = new() { ["en"] = "Only items that have not started downloading can be removed.", ["hi"] = "केवल वे आइटम हटाए जा सकते हैं जो शुरू नहीं हुए।", ["es"] = "Solo se pueden quitar los elementos que no han empezado." },
        ["Main.RemoveConfirm"] = new() { ["en"] = "Remove \"{0}\" from the download queue?\n\nIt has not started downloading yet, so nothing will be lost.", ["hi"] = "\"{0}\" को डाउनलोड सूची से हटाएँ?\n\nइसका डाउनलोड शुरू नहीं हुआ, इसलिए कुछ भी नहीं खोएगा।", ["es"] = "¿Quitar \"{0}\" de la cola de descargas?\n\nAún no ha empezado, así que no se perderá nada." },
        ["Main.RemoveConfirmTitle"] = new() { ["en"] = "Confirm removal", ["hi"] = "हटाने की पुष्टि", ["es"] = "Confirmar la eliminación" },
        ["Main.RemovalCancelled"] = new() { ["en"] = "Removal cancelled.", ["hi"] = "हटाना रद्द किया गया।", ["es"] = "Eliminación cancelada." },
        ["Main.Removed"] = new() { ["en"] = "Removed {0} from the queue.", ["hi"] = "{0} को सूची से हटा दिया।", ["es"] = "Se quitó {0} de la cola." },
        ["Main.FinishedCleared"] = new() { ["en"] = "Finished items removed from the queue.", ["hi"] = "पूर्ण आइटम सूची से हटा दिए गए।", ["es"] = "Elementos terminados quitados de la cola." },
        ["Main.AllFinishedOk"] = new() { ["en"] = "All downloads finished: {0} downloaded successfully.", ["hi"] = "सभी डाउनलोड पूरे: {0} सफलतापूर्वक डाउनलोड हुए।", ["es"] = "Todas las descargas terminaron: {0} completadas." },
        ["Main.AllFinishedMixed"] = new() { ["en"] = "All downloads finished: {0} completed, {1} failed, {2} cancelled.", ["hi"] = "सभी डाउनलोड पूरे: {0} पूर्ण, {1} विफल, {2} रद्द।", ["es"] = "Todas las descargas terminaron: {0} completadas, {1} fallidas, {2} canceladas." },
        ["Main.AllFinishedTitle"] = new() { ["en"] = "All downloads finished", ["hi"] = "सभी डाउनलोड पूरे", ["es"] = "Todas las descargas terminaron" },
        ["Main.Notification"] = new() { ["en"] = "Notification", ["hi"] = "सूचना", ["es"] = "Notificación" },
        ["Main.NotificationMessage"] = new() { ["en"] = "Notification message", ["hi"] = "सूचना संदेश", ["es"] = "Mensaje de notificación" },
        ["Main.GotIt"] = new() { ["en"] = "&Got it", ["hi"] = "समझ &गया", ["es"] = "&Entendido" },
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
        ["Main.Tasks.Language"] = new() { ["en"] = "changing the book language" },
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
        ["Shortcut.Keys"] = new()
        {
            ["en"] = "Explain how to get the decryption keys for AAX and AAXC files.",
            ["hi"] = "AAX और AAXC फ़ाइलों की डिक्रिप्शन कीज़ कैसे पाएँ, यह बताएँ।",
            ["es"] = "Explicar cómo obtener las claves de descifrado de los archivos AAX y AAXC.",
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
        ["Shortcut.Log"] = new()
        {
            ["en"] = "Show or hide the activity log. Your choice is remembered.",
            ["hi"] = "गतिविधि लॉग दिखाएँ या छिपाएँ। आपका चुनाव याद रखा जाएगा।",
            ["es"] = "Mostrar u ocultar el registro de actividad. Se recordará su elección.",
        },
        ["Shortcut.GoToLog"] = new()
        {
            ["en"] = "Go to the activity log, showing it first if it is hidden.",
            ["hi"] = "गतिविधि लॉग पर जाएँ; छिपा हो तो पहले उसे दिखाएँ।",
            ["es"] = "Ir al registro de actividad, mostrándolo primero si está oculto.",
        },
        ["Shortcut.Retry"] = new()
        {
            ["en"] = "Retry the selected failed or cancelled item in the download queue.",
            ["hi"] = "डाउनलोड सूची में चुने गए विफल या रद्द आइटम का फिर से प्रयास करें।",
            ["es"] = "Reintentar el elemento fallido o cancelado seleccionado en la cola de descargas.",
        },
        ["Shortcut.Remove"] = new()
        {
            ["en"] = "Remove the selected item that has not started downloading yet.",
            ["hi"] = "चुना गया वह आइटम हटाएँ जिसका डाउनलोड शुरू नहीं हुआ।",
            ["es"] = "Quitar el elemento seleccionado que aún no ha empezado a descargarse.",
        },
        ["Shortcut.ClearFinished"] = new()
        {
            ["en"] = "Clear completed and cancelled items from the download queue.",
            ["hi"] = "डाउनलोड सूची से पूर्ण और रद्द आइटम साफ़ करें।",
            ["es"] = "Borrar de la cola los elementos terminados y cancelados.",
        },
        ["Shortcut.SearchHistory"] = new()
        {
            ["en"] = "Open your previous searches for the current provider.",
            ["hi"] = "मौजूदा प्रदाता के लिए अपनी पिछली खोजें खोलें।",
            ["es"] = "Abrir sus búsquedas anteriores para el proveedor actual.",
        },
        ["Shortcut.BookLanguage"] = new()
        {
            ["en"] = "Go to the book language list (Project Gutenberg only).",
            ["hi"] = "पुस्तक भाषा सूची पर जाएँ (केवल Project Gutenberg)।",
            ["es"] = "Ir a la lista de idiomas de los libros (solo Project Gutenberg).",
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
            ["en"] = "Save each book in a folder named after its &author",
            ["hi"] = "हर पुस्तक को उसके &लेखक के नाम वाले फ़ोल्डर में सहेजें",
            ["es"] = "Guardar cada libro en una carpeta con el nombre de su &autor",
        },
        ["Set.WarnDuplicates"] = new()
        {
            ["en"] = "As&k before downloading a book that is already downloaded",
            ["hi"] = "पहले से डाउनलोड पुस्तक को फिर डाउनलोड करने से पहले &पूछें",
            ["es"] = "Pre&guntar antes de descargar un libro ya descargado",
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
        ["Settings.GeneralDescription"] = new()
        {
            ["en"] = "Everyday options: where files are saved and how the app behaves.",
            ["hi"] = "सामान्य विकल्प: फ़ाइलें कहाँ सहेजी जाएँ और ऐप कैसे व्यवहार करे।",
            ["es"] = "Opciones generales: dónde se guardan los archivos y cómo se comporta la aplicación.",
        },
        ["Settings.DownloadsDescription"] = new()
        {
            ["en"] = "How downloaded files are handled, including DRM removal.",
            ["hi"] = "डाउनलोड की गई फ़ाइलों को कैसे संभाला जाए, DRM हटाने सहित।",
            ["es"] = "Cómo se gestionan los archivos descargados, incluida la eliminación de DRM.",
        },
        ["Settings.AudibleDescription"] = new()
        {
            ["en"] = "Sign in, sign out and check your Audible account. Audible books are only ever shown while you are signed in.",
            ["hi"] = "साइन इन करें, साइन आउट करें और अपना Audible खाता देखें। Audible की पुस्तकें केवल साइन इन रहने पर दिखती हैं।",
            ["es"] = "Inicie y cierre sesión y consulte su cuenta de Audible. Los libros de Audible solo se muestran con la sesión iniciada.",
        },
        ["Settings.GutenbergDescription"] = new()
        {
            ["en"] = "Where Project Gutenberg searches start, and which file format to prefer.",
            ["hi"] = "प्रोजेक्ट गुटेनबर्ग की खोज कहाँ से शुरू हो और कौन-सा प्रारूप पसंदीदा हो।",
            ["es"] = "Dónde comienzan las búsquedas de Project Gutenberg y qué formato preferir.",
        },
        ["Settings.LanguageDescription"] = new()
        {
            ["en"] = "Which language the application itself is shown in.",
            ["hi"] = "ऐप किस भाषा में दिखाया जाए।",
            ["es"] = "En qué idioma se muestra la aplicación.",
        },
        ["Settings.AccessibilityDescription"] = new()
        {
            ["en"] = "How the app works with your screen reader.",
            ["hi"] = "ऐप आपके स्क्रीन रीडर के साथ कैसे काम करता है।",
            ["es"] = "Cómo funciona la aplicación con su lector de pantalla.",
        },
        ["Settings.Ready"] = new() { ["en"] = "Ready.", ["hi"] = "तैयार।", ["es"] = "Listo." },
        ["Settings.Status"] = new() { ["en"] = "Settings status", ["hi"] = "सेटिंग्स की स्थिति", ["es"] = "Estado de la configuración" },
        ["Settings.Save"] = new() { ["en"] = "Save settings", ["hi"] = "सेटिंग्स सहेजें", ["es"] = "Guardar la configuración" },
        ["Settings.Cancel"] = new() { ["en"] = "Cancel changes", ["hi"] = "बदलाव रद्द करें", ["es"] = "Cancelar los cambios" },
        ["Settings.Apply"] = new() { ["en"] = "Apply settings", ["hi"] = "सेटिंग्स लागू करें", ["es"] = "Aplicar la configuración" },
        ["Settings.Saved"] = new() { ["en"] = "Settings saved.", ["hi"] = "सेटिंग्स सहेज दी गईं।", ["es"] = "Configuración guardada." },

        // ---------------- settings: general / downloads ----------------
        ["Set.OutputFolderDescription"] = new()
        {
            ["en"] = "The folder downloads are saved into. Press Browse to choose another.",
            ["hi"] = "डाउनलोड इस फ़ोल्डर में सहेजे जाते हैं। दूसरा चुनने के लिए ब्राउज़ दबाएँ।",
            ["es"] = "La carpeta donde se guardan las descargas. Pulse Examinar para cambiarla.",
        },
        ["Set.ChooseFolder"] = new() { ["en"] = "Choose the folder where downloads are saved", ["hi"] = "वह फ़ोल्डर चुनें जहाँ डाउनलोड सहेजे जाएँ", ["es"] = "Elija la carpeta donde se guardan las descargas" },
        ["Set.LocalFolderDescription"] = new()
        {
            ["en"] = "The folder the Local files provider scans for AAX and AAXC files you already own.",
            ["hi"] = "वह फ़ोल्डर जहाँ लोकल फ़ाइलें प्रदाता आपकी AAX और AAXC फ़ाइलें खोजता है।",
            ["es"] = "La carpeta donde el proveedor de archivos locales busca sus AAX y AAXC.",
        },
        ["Set.ChooseLocalFolder"] = new() { ["en"] = "Choose the folder holding your AAX or AAXC files", ["hi"] = "वह फ़ोल्डर चुनें जिसमें आपकी AAX या AAXC फ़ाइलें हैं", ["es"] = "Elija la carpeta con sus archivos AAX o AAXC" },
        ["Downloads.Note"] = new()
        {
            ["en"] = "DRM removal is lossless and happens entirely on your computer. Only back up books you own: " +
                   "Audible issues licences only for titles in your own library.",
            ["hi"] = "DRM हटाना लॉसलेस है और पूरी तरह आपके कंप्यूटर पर होता है। केवल अपनी खरीदी पुस्तकों का बैकअप लें: " +
                   "Audible केवल आपकी लाइब्रेरी की पुस्तकों के लिए लाइसेंस देता है।",
            ["es"] = "La eliminación de DRM es sin pérdida y ocurre en su equipo. Haga copia solo de libros propios: " +
                   "Audible solo emite licencias para títulos de su biblioteca.",
        },

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
        ["Set.SwitchAccount"] = new() { ["en"] = "Switch &account…", ["hi"] = "&खाता बदलें…", ["es"] = "&Cambiar de cuenta…" },
        ["Audible.Note"] = new()
        {
            ["en"] = "No Audible book, story, recommendation or library item is shown unless you are signed in. " +
                   "Nothing is remembered between runs except the sign-in itself.",
            ["hi"] = "साइन इन नहीं होने पर Audible की कोई पुस्तक, कहानी, सुझाव या लाइब्रेरी आइटम नहीं दिखता। " +
                   "साइन-इन के अलावा कुछ भी सहेजा नहीं जाता।",
            ["es"] = "No se muestra ningún libro, historia, recomendación ni elemento de la biblioteca de Audible " +
                   "si no ha iniciado sesión. Nada se recuerda entre ejecuciones salvo el inicio de sesión.",
        },
        ["Audible.KeysHelp"] = new() { ["en"] = "How to get decryption keys", ["hi"] = "डिक्रिप्शन कीज़ कैसे पाएँ", ["es"] = "Cómo obtener las claves de descifrado" },
        ["Audible.KeysHelpText"] = new()
        {
            ["en"] = "Legacy AAX files use 8 hex digits called activation bytes.\n\n" +
                   "Modern AAXC files use a 32-hex-digit key and a 32-hex-digit IV.\n\n" +
                   "When you download from your own Audible account through this app, the keys are fetched " +
                   "automatically, so you never need to type them. For files you already have, the quickest " +
                   "route is to download the same title through the app once; the keys are then remembered for that file.",
            ["hi"] = "पुरानी AAX फ़ाइलें 8 हेक्स अंकों वाली एक्टिवेशन बाइट्स इस्तेमाल करती हैं।\n\n" +
                   "आधुनिक AAXC फ़ाइलें 32 हेक्स अंकों की की और 32 हेक्स अंकों की IV इस्तेमाल करती हैं।\n\n" +
                   "इस ऐप से अपने Audible खाते से डाउनलोड करते समय कीज़ अपने आप आ जाती हैं, आपको टाइप नहीं करनी पड़तीं। " +
                   "पहले से मौजूद फ़ाइलों के लिए, उसी पुस्तक को एक बार ऐप से डाउनलोड कर लें; कीज़ याद रखी जाएँगी।",
            ["es"] = "Los archivos AAX antiguos usan 8 dígitos hexadecimales llamados bytes de activación.\n\n" +
                   "Los archivos AAXC modernos usan una clave de 32 dígitos hexadecimales y un IV de 32.\n\n" +
                   "Al descargar desde su propia cuenta de Audible con esta aplicación, las claves se obtienen " +
                   "automáticamente. Para archivos que ya tiene, lo más rápido es descargar el mismo título una vez con la aplicación.",
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
        ["Set.DefaultLanguageDescription"] = new()
        {
            ["en"] = "The language the Gutenberg search box starts in. You can change it on the main window too.",
            ["hi"] = "गुटेनबर्ग खोज किस भाषा में शुरू हो। इसे मुख्य विंडो में भी बदल सकते हैं।",
            ["es"] = "El idioma inicial del cuadro de búsqueda de Gutenberg. También puede cambiarlo en la ventana principal.",
        },
        ["Set.PreferredFormatDescription"] = new()
        {
            ["en"] = "Audio MP3 when a title has one, otherwise this format.",
            ["hi"] = "यदि MP3 ऑडियो उपलब्ध हो तो वह, वरना यह प्रारूप।",
            ["es"] = "Audio MP3 cuando el título lo tenga; si no, este formato.",
        },
        ["Format.Audio"] = new() { ["en"] = "Audio (MP3) when available, otherwise EPUB", ["hi"] = "उपलब्ध हो तो ऑडियो (MP3), वरना EPUB", ["es"] = "Audio (MP3) si está disponible; si no, EPUB" },
        ["Format.Epub"] = new() { ["en"] = "EPUB", ["hi"] = "EPUB", ["es"] = "EPUB" },
        ["Format.Html"] = new() { ["en"] = "HTML", ["hi"] = "HTML", ["es"] = "HTML" },
        ["Format.Text"] = new() { ["en"] = "Plain text", ["hi"] = "सादा पाठ", ["es"] = "Texto sin formato" },
        ["Gutenberg.Note"] = new()
        {
            ["en"] = "Project Gutenberg is free and needs no account. Every language Gutenberg publishes can be searched, " +
                   "including English, Hindi and dozens more.",
            ["hi"] = "प्रोजेक्ट गुटेनबर्ग निःशुल्क है और खाता नहीं चाहता। गुटेनबर्ग की हर भाषा में खोज हो सकती है, " +
                   "जिसमें अंग्रेज़ी, हिन्दी और कई और भाषाएँ शामिल हैं।",
            ["es"] = "Project Gutenberg es gratuito y no necesita cuenta. Se puede buscar en todos los idiomas que publica " +
                   "Gutenberg, incluidos inglés, hindi y muchos más.",
        },
        ["Set.UiLanguageDescription"] = new()
        {
            ["en"] = "Choose the interface language. The change applies when you press OK.",
            ["hi"] = "इंटरफ़ेस भाषा चुनें। बदलाव ठीक है दबाने पर लागू होगा।",
            ["es"] = "Elija el idioma de la interfaz. El cambio se aplica al pulsar Aceptar.",
        },
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
        ["Main.LogShown"] = new()
        {
            ["en"] = "Activity log shown.",
            ["hi"] = "गतिविधि लॉग दिखा दिया गया।",
            ["es"] = "Registro de actividad mostrado.",
        },
        ["Main.LogHidden"] = new()
        {
            ["en"] = "Activity log hidden. Press Control L to show it again.",
            ["hi"] = "गतिविधि लॉग छिपा दिया गया। दोबारा दिखाने के लिए Control L दबाएँ।",
            ["es"] = "Registro de actividad oculto. Pulse Control L para mostrarlo de nuevo.",
        },

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
        ["Key.Title"] = new()
        {
            ["en"] = "Decryption keys needed",
            ["hi"] = "डिक्रिप्शन कुंजियाँ चाहिए",
            ["es"] = "Se necesitan claves de descifrado",
        },
        ["Key.File"] = new() { ["en"] = "File: {0}", ["hi"] = "फ़ाइल: {0}", ["es"] = "Archivo: {0}" },
        ["Key.FileAccessible"] = new() { ["en"] = "File", ["hi"] = "फ़ाइल", ["es"] = "Archivo" },
        ["Key.ExplanationAccessible"] = new()
        {
            ["en"] = "Explanation",
            ["hi"] = "विवरण",
            ["es"] = "Explicación",
        },
        ["Key.ExplainKeyIv"] = new()
        {
            ["en"] = "AAXC files need a key and an IV (32 hex digits each).",
            ["hi"] = "AAXC फ़ाइलों के लिए key और IV चाहिए (हर एक 32 हेक्स अंक)।",
            ["es"] = "Los archivos AAXC necesitan una clave y un IV (32 dígitos hexadecimales cada uno).",
        },
        ["Key.ExplainActivation"] = new()
        {
            ["en"] = "AAX files need 8 hex digits called activation bytes.",
            ["hi"] = "AAX फ़ाइलों के लिए 8 हेक्स अंक चाहिए, जिन्हें activation bytes कहते हैं।",
            ["es"] = "Los archivos AAX necesitan 8 dígitos hexadecimales llamados bytes de activación.",
        },
        ["Key.KeyLabel"] = new() { ["en"] = "&Key:", ["hi"] = "&Key:", ["es"] = "&Clave:" },
        ["Key.KeyAccessible"] = new()
        {
            ["en"] = "Decryption key",
            ["hi"] = "डिक्रिप्शन key",
            ["es"] = "Clave de descifrado",
        },
        ["Key.IvLabel"] = new() { ["en"] = "&IV:", ["hi"] = "&IV:", ["es"] = "&IV:" },
        ["Key.IvAccessible"] = new()
        {
            ["en"] = "Initialization vector",
            ["hi"] = "इनिशियलाइज़ेशन वेक्टर (IV)",
            ["es"] = "Vector de inicialización (IV)",
        },
        ["Key.ActivationLabel"] = new()
        {
            ["en"] = "&Activation bytes:",
            ["hi"] = "&Activation bytes:",
            ["es"] = "Bytes de &activación:",
        },
        ["Key.ActivationAccessible"] = new()
        {
            ["en"] = "Activation bytes",
            ["hi"] = "Activation bytes",
            ["es"] = "Bytes de activación",
        },
        ["Key.OkAccessible"] = new()
        {
            ["en"] = "OK",
            ["hi"] = "ठीक है",
            ["es"] = "Aceptar",
        },
        ["Key.CancelAccessible"] = new()
        {
            ["en"] = "Cancel",
            ["hi"] = "रद्द करें",
            ["es"] = "Cancelar",
        },
        ["Key.BadKeyIv"] = new()
        {
            ["en"] = "The key and IV must each be exactly 32 hex digits (0-9, a-f).",
            ["hi"] = "key और IV में से हर एक ठीक 32 हेक्स अंक होना चाहिए (0-9, a-f)।",
            ["es"] = "La clave y el IV deben tener exactamente 32 dígitos hexadecimales (0-9, a-f).",
        },
        ["Key.BadKeyIvTitle"] = new()
        {
            ["en"] = "Check the key",
            ["hi"] = "कुंजी जाँचें",
            ["es"] = "Compruebe la clave",
        },
        ["Key.BadActivation"] = new()
        {
            ["en"] = "Activation bytes must be exactly 8 hex digits (0-9, a-f).",
            ["hi"] = "activation bytes ठीक 8 हेक्स अंक होने चाहिए (0-9, a-f)।",
            ["es"] = "Los bytes de activación deben ser exactamente 8 dígitos hexadecimales (0-9, a-f).",
        },
        ["Key.BadActivationTitle"] = new()
        {
            ["en"] = "Check the activation bytes",
            ["hi"] = "activation bytes जाँचें",
            ["es"] = "Compruebe los bytes de activación",
        },
    };
}
