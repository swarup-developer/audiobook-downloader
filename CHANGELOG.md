# Changelog

All notable changes to **Audiobook Downloader** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Jump to: [Unreleased](#unreleased) · [1.1.0](#110) · [1.0.0](#100)

> Release dates are given in **UTC**. The first public build finished at
> 2026-09-17 19:23 UTC, which was already 2026-09-18 in the maintainer's local
> time zone (+05:30).

---

## [Unreleased]

Reliability work on the download engine, so nothing that is already on disk can be
lost and an interrupted transfer is not thrown away, plus LibriVox search, an
Audible sign-in that finishes by itself, and the removal of the YouTube and Spotify
sources.

### Added

- **Already-downloaded books are marked** in the book list ("already downloaded"), and
  the app asks before downloading one again: download all, skip the ones you have, or
  cancel. Can be turned off in Settings, Downloads.
- **Book details (Ctrl+I):** author, narrator, length, language, source, identifier and,
  once downloaded, the file's location, in one message your screen reader reads in full.
- **Show the downloaded file (Ctrl+Shift+O)** in File Explorer, highlighted.
- **Export the book list (Ctrl+E, Application menu)** to a UTF-8 CSV file that opens in
  Excel with Hindi and accented names intact.
- **Select all books (Ctrl+A in the book list).**
- **Organise downloads by author** (Settings, Downloads): each book goes into a folder
  named after its author.
- **Enter in the book list downloads the selected books.** The help always said so, but
  nothing answered the key.

- **One keyboard shortcut table, and a help screen built from it.** Every shortcut the
  application has is declared once — the keys, what it does, and who answers it — and:
  <kbd>F1</kbd> opens a two-column list read row by row by a screen reader, grouped into
  *in the application*, *in the main window*, *in dialogs and lists*, and the
  <kbd>Alt</kbd> keys the window offers on its own labels. That last group is read from
  the window's live labels, so it can no longer describe a control that is not there —
  which is exactly how the old hand-written list kept naming removed buttons.
- **The queue's own actions are shortcuts as well as menu items** — retry the selected
  failed or cancelled row with <kbd>Ctrl</kbd>+<kbd>R</kbd>, remove one that has not
  started with <kbd>Ctrl</kbd>+<kbd>Delete</kbd>, and clear the finished rows with
  <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Delete</kbd>. The same actions sit on the queue's
  context menu, and neither the shortcut nor the menu item can drift from the other:
  both call the same method.
- The self-test now fails the build if two shortcuts answer the same keys, if a
  documented shortcut has nothing to do, if a shortcut or help heading is not written
  in every interface language, if the menu's own keys differ from the documented ones,
  if a labelled control the window has taken off it is still offered as a shortcut, or
  if the download folder and the decryption-key help are not reachable without a mouse.
- **The first sign-in asks which country your Audible account belongs to, before offering
  the sign-in at all.** Choosing the country is part of signing in rather than a detour
  through Settings: the dialog opens with the country list and nothing else, the **Sign
  in** button is not on screen until a country has been chosen, and the choice is saved
  the moment it is made — so closing the dialog without signing in still remembers it.
  When the sign-in succeeds the dialog closes, the main window is back in front of you
  signed in, and the library loads. Changing the country later needs no sign-in: it is
  on its own in Settings, under **Audible country**.
- **An unfinished batch of downloads outlives the app.** The queue is written to
  `queue.json` beside your settings as it changes, holding only what is still to do, and
  the next run brings it back: the books that were still to come are queued again in
  the same order, the one that was interrupted resumes from its partial file instead of
  starting over, and the number of downloads being picked up is announced rather than
  work beginning in silence. Nothing is resumed that should not be — a book you
  cancelled, or one that failed, comes back as it was left, with its reason, waiting for
  **Retry** — and a book that finished is not remembered at all, because it is already on
  disk.
- **The country list is searched by typing.** Typing the first letters of a country's
  name finds it — "ind" for India, "united k" for the United Kingdom, or a later word
  of a name — and a mistyped name still finds the country it meant, in whatever
  interface language you read, with the accents left off if your keyboard has none. The
  list is deliberately not an edit box: it goes on showing and announcing a country's
  full name, so half-typed text can never be read out as though it were a country. A
  typed name that matches nothing is said out loud rather than ignored, and taking the
  name back puts the list where it was.
- **The Audible sign-in now happens inside the app.** Amazon's own sign-in page opens
  in a window the app owns (the Microsoft Edge WebView2 runtime, already part of
  Windows), and the device cookies Audible issues for that sign-in are handed to the
  page, so Amazon sees the very device the download licence will belong to.
- The self-test now checks the sign-in window's one rule — close on Amazon's redirect
  address, and on no other — address by address, offline.
- `--self-test-live` reads the real LibriVox catalogue as part of the self-test, so a
  change to the site's API is caught before users see an empty book list.
- The self-test now **fails if the app carries a speech engine** — referenced, loaded,
  defined or shipped beside it. The app is meant to be read by your own screen reader,
  never by a voice of its own, and this keeps that true.
- **Simple, minimal accessibility on every control.** Accessible names are now plain
  spoken words — "Search", "Settings", "Close" — instead of keyboard-mnemonic text
  such as "S&earch", and the long second-paragraph descriptions are gone: every
  control keeps just a simple name, an explicit role and its tab position, which is
  all a screen reader reads. Nothing lost coverage: every interactive control in every
  dialog is still named and reachable, and the self-test now fails the build if any
  accessible name contains an ampersand, any control sets a description, or any
  control (menu items included) is left without a name.

### Changed

- **The main window now carries only the controls the main job needs.** Choose a
  source, find a book, download it and watch it happen: that is the whole window now.
  The **Help**, **How to get keys**, **Open download folder**, **Settings**, **Retry**,
  **Remove from queue** and **Clear finished** buttons are gone from it — not disabled,
  removed. Everything they did is still reachable, on the keyboard shortcut that does
  the same thing, on the application menu (which names each action and shows its key),
  or on the download queue's own context menu.
- **A shortcut the application already had but never answered now works:** the
  **Open download folder** and **How to get keys** menu items were in the menu bar with
  <kbd>Ctrl</kbd>+<kbd>O</kbd> and <kbd>Ctrl</kbd>+<kbd>K</kbd> beside them and no
  action behind either. Both now do what the menu says.
- **Signing in to Audible ends by itself.** The sign-in window watches where Amazon
  sends the page, takes the redirect that carries the authorization code, closes and
  creates the session — so the flow no longer depends on the user noticing a web page
  and carrying its address back to the app. With "Load the Audible library after
  signing in" on, the book list fills in as soon as the session exists.
- **The main window is organised into three numbered groups**, so every control has
  one obvious place and the reading order matches the tab order:
  **1. Find books** (provider, search, book language for Project Gutenberg, book
  list) → **2. Downloads** (all action buttons, progress bar, download queue) →
  **3. Status** (status line, activity log). The nine action buttons no longer sit
  in one loose row above everything else, and the caption labels no longer float
  unattached between the lists.
- **The book-language selector is shown only for Project Gutenberg**, the one source
  that can filter by language: it now sits on its own "Book language" row inside
  "1. Find books" and the row disappears completely for every other provider
  (previously it sat hidden-but-present inside the search row for all of them).
- The self-test now covers the download engine as well: file naming, resuming after
  a broken connection, and cancelling. It uses a temporary local server, so it still
  runs completely offline.

### Removed

- **YouTube and Spotify are gone from the app.** The YouTube provider, the Spotify
  → YouTube match, the `yt-dlp` wrapper that downloaded and converted their audio,
  the **YouTube and yt-dlp** page in Settings (its path, audio format, format
  selector, extra arguments, metadata/thumbnail embedding, browser cookies, cookies
  file, proxy and search-results options), the provider entries in the About window,
  and the settings file entries behind them were all removed. Downloads are now made
  of the sources listed in the README: Audible, LibriVox, Internet Archive, Project
  Gutenberg and your own local AAX/AAXC files. Nothing else about the app changed.
- **The address-copying step is gone.** The dialog that asked for the copied
  `amazon.com/ap/maplanding` address, its instructions and its translations were
  removed, along with the code that opened an outside browser. No address is ever
  copied, pasted or typed by hand.
- **Dead translation entries are gone.** Forty-one keys in `Strings.cs` that no code
  referenced any more — the leftover description strings from before accessible
  descriptions were dropped, plus a few older labels nothing sets — were deleted, so
  every key in the table is one the app actually uses. The 219 remaining keys are all
  reachable from the code.

### Fixed

- **Audible books show their titles and authors.** The library was requested with too
  few response groups, so Amazon sent no title and every book was listed by its ASIN
  (and as "Unknown author"). Titles, authors, narrators, length and language now arrive.
  Podcast parent entries, which have no audio and always failed with "License response
  not OK", are no longer listed, and a refused licence now says why in plain words.
- **Arrow keys work in the sign-in country list.** Every Up/Down press chose a country
  and moved focus to the Sign in button, so the next arrow never reached the list. Focus
  now stays on the list (and starts there); Enter signs in. The status no longer
  re-announces each country over the screen reader.
- **Signing in and out happens on the main window.** The button next to the provider list
  reads "Sign in to Audible…" (Alt+I) or "Sign out of Audible" (Alt+O); downloading while
  signed out opens the sign-in instead of sending you into Settings.
- **LibriVox search works again.** The API now refuses pages larger than 500 rows with
  HTTP 400, which the app took for the end of the catalogue, so every search found
  nothing. Pages are 500 rows, fetched six at a time (the full ~22,000-title catalogue
  reads in about 20 seconds, once a week), and only a 404 ends the read.
- **Internet Archive search works.** The archive sends authors as a list, which made
  the parser throw and the search look empty. All words must now match, and the most
  downloaded items come first.
- **Network failures are reported, not shown as "no books matched"** (Gutenberg,
  Internet Archive, LibriVox).
- **Choosing a provider or language no longer fires a search on every arrow key**, and a
  slow earlier search can no longer overwrite the answer to a newer one. Search text is
  cleared when you switch provider, so one catalogue's search no longer empties another's list.
- **Book language works for Audible and LibriVox too**, not just Project Gutenberg, and
  the list shows plain names ("Hindi") instead of "Hindi (hi)".
- The book list's caption says what it holds: "Your Audible library" or "Search results".
- Settings drop-downs no longer announce each value a second time over the screen reader.

- **Countries are named, not coded.** The country selector was built from Audible's store
  codes, so it displayed — and a screen reader announced — "us", "uk" and "in". Somebody
  picking India heard "in". Both selectors, the new sign-in dialog and the one in
  Settings, now hold countries with their full names, translated like the rest of the
  interface, and the account-status line names the country as well instead of the code.
  The self-test fails if a store would be announced by its code anywhere in the app, in
  any interface language.
- **Signing in now registers the account on every screen at once.** The live Audible
  session is the one thing the account button, the Settings screen and the book list all
  depend on, and it now changes in exactly one place and tells every screen about it.
  Before, each screen decided for itself when to look at the session, so a session could
  exist while a screen went on offering "Sign in" — and only the screen that happened to
  start the sign-in knew to look again. The Settings screen also now updates the
  **spoken** name of its button, which it never did: once signed in, a screen reader was
  still told "Sign in to Audible" over an account that was already signed in. That is
  the same defect as a button that still says "Sign in", and it is invisible to
  anybody testing the dialog by eye.
- **A book with no title no longer appears as "[null]".** The Audible library answers
  `[null]` from its own title property when Amazon sends an item with no title at all,
  so that value is never null and the app's fallback to the book's identifier never ran:
  the library list read "[null] - Author". A title is now built from the real fields
  only — the title and subtitle, or the ASIN when Amazon sent no title, and plain words
  when there is neither. Credits with blank names ("Smith, , Jones") are left out the
  same way, and no row can show a blank or a placeholder even if a provider hands one
  over, because the row itself falls back to the identifier and then to plain words.
  Both are checked offline by the self-test, which builds rows from what Amazon really
  sends — including the library's own `[null]` placeholder — and fails if any of them
  would read as anything but a title or its identifier.
- **The Audible sign-in now really does end by itself: the shipped build finally
  contains the flow that does it.** `publish/AudiobookDownload.exe` was still built
  from the source as it stood *before* the in-app sign-in existed, so the app people
  actually ran opened Amazon in the **outside browser** and then asked for the address
  back in a paste box — a sign-in that could never return to the app on its own. The
  published app has been rebuilt from the current source, so signing in opens Amazon's
  page in a window the app owns and the app finishes the sign-in from the redirect.
  The publish recipe is now written down in the README, because an artifact that has
  quietly fallen behind the source is a bug that no test in the tree can see.
- **The sign-in window no longer leaves you on Amazon's page after you have signed in.**
  Recognition rested on the reply arriving at one particular path on one particular
  kind of address, and on it arriving as a navigation *starting* — so a reply sent to a
  different landing page, or delivered as a page finished loading, or with the
  parameters after the `#`, was ignored and the window simply sat there. The reply is
  now taken whenever an Amazon or Audible store address carries a non-empty
  authorization code, from the query or the fragment, on a navigation, on a page load
  or when the address changes; and the window also asks the browser what it is showing,
  so no single missed event can strand the user. The address handed to the Audible
  library is built from the code alone, which is all the library reads, so a code that
  arrived after the `#` is handed over as something the library accepts rather than
  refusing the sign-in as an invalid address.
- **The sign-in window closes without hanging.** It used to close inside the browser's
  own navigation callback, tearing the browser down while it was still handing the
  event over. The close is now posted to the message queue and happens once that
  callback has returned.
- **The sign-in page opens only when it is actually needed.** A saved sign-in is tried
  first, and the window is opened only when there is nothing on disk or what is there is
  no longer accepted — so signing in again never opens a page for an account that is
  already usable.
- **A runtime that refuses the throwaway browser profile no longer costs you the
  sign-in.** The window falls back to a plain profile instead of reporting that it could
  not be opened; the Amazon cookies are wiped as the window closes either way.
- **The sign-in redirect is now checked against the Audible library itself, not against
  a hand-written list.** The self-test reads the return address the library builds for
  every one of the eleven stores the app offers and checks that the reply coming back to
  each one is recognised *and* readable by the library, offline. If the library moves a
  landing address or adds a store, the self-test fails here instead of the user being
  stranded on a web page after a sign-in that worked.
- **The published app no longer contains the old speech engine.** The 1.0.0 build in
  `publish/` was still the pre-removal binary and bundled `System.Speech`; the shipped
  app has been rebuilt from the current source (1.1.0), and the stale build folders
  that still carried `System.Speech.dll` have been removed. The application contains
  no speech engine: nothing references, loads or defines one.
- **The window now follows Windows high contrast**, including switching it on while the
  app is running. Three places used a fixed colour — dark slate grey for the hint text
  and white for the activity log — which could be unreadable on a high-contrast
  background. Every colour now comes from the Windows palette.
- **Hiding the activity log no longer leaves an empty strip** where the log used to be.
  Its table row is collapsed, and shown again if you turn the log back on.
- **The activity log shortcuts work again.** Once the log was hidden in Settings,
  "Alt L — go to the activity log" did nothing at all, because an invisible control
  cannot take focus. <kbd>Ctrl</kbd> + <kbd>L</kbd> now shows or hides the log (and
  remembers the choice), and <kbd>Alt</kbd> + <kbd>L</kbd> shows it before going to it.
- **Progress is announced on its own live region** instead of the status line, so a
  download reaching a quarter, a half or three quarters no longer replaces the last
  status message.
- **The Audible sign-in prompt and the decryption-key prompt are translated.** They were
  the last two dialogs written in English only; their text, labels and error messages
  are now in every interface language — including the sign-in window that replaced the
  paste box.
- **A sign-in window that cannot open says why.** If the Microsoft Edge WebView2 Runtime
  is missing, the app names it and says what to install, rather than reporting the
  attempt as "cancelled".
- **LibriVox search now covers the whole catalogue.** It used to search only the first
  100 titles the site returned, and a misspelled field name in the request meant that
  list was usually empty — so searching for almost any LibriVox book found nothing.
  The whole catalogue (about 22,000 titles) is now read in a few paged requests and
  saved, so every later search is immediate and complete.

- **Downloads can no longer overwrite a file that is already in the folder.** If you
  download the same title twice, the second copy is saved with " (2)" added to its
  name instead of silently replacing the first one. The same applies to the
  DRM-free copy produced after decryption, and even to a file that appears in the
  folder while a download is still running: both are kept.
- **An interrupted download resumes where it stopped** instead of starting again
  from the beginning. If the connection drops, or you cancel, what has already
  arrived is kept in a ".part" file, and retrying the item continues from there.
- **A dropped connection is retried automatically**, up to four times with a
  growing wait between attempts, and a connection that stops sending data for a
  minute is treated as dead so the retry can happen instead of hanging forever.
- **A partly written file can no longer be mistaken for a finished one.** A file
  only appears under its real name once the whole download has arrived, so a crash
  or a power cut cannot leave a truncated file that looks complete.
- **Cancelling a download is no longer reported as a failure.** The queue shows it
  as cancelled rather than failed, keeps the partial file, and a later retry
  resumes it.
- **An unexpected problem inside the application no longer closes it.** The details
  are written to the log file and a single plain message is shown, so a fault
  during a long download does not take the rest of your work with it. Failures in
  the background are logged too.
- A yt-dlp download that is interrupted also resumes rather than starting again.

---

## [1.1.0] — 2026-09-18

A large release: Settings now holds every option, Audible requires a real sign-in
before anything is shown, YouTube is downloaded with yt-dlp, Project Gutenberg
brings books in dozens of languages, and the whole interface is exposed properly
to screen readers such as NVDA.

### Added

- **Application menu** — press <kbd>Alt</kbd> for **Settings**, **About** and
  **Quit**, plus a Help menu with About and Keyboard shortcuts. Fully keyboard
  accessible and read correctly by screen readers.
- **About dialog** with version, available sources, accessibility notes and the
  folder where your settings and log live.
- **A complete Settings dialog**, organised into sections so nothing is scattered
  around the app any more:
  - **General** — download folder, folder for your existing AAX/AAXC files, open
    the folder when finished, Windows notification, confirmation before removing a
    queue item, remember the last provider.
  - **Downloads** — remove DRM, delete the encrypted original after decryption.
  - **Audible account** — account status, sign in, sign out, region and
    "remember this sign-in".
  - **YouTube and yt-dlp** — path to yt-dlp, audio format, format selector, extra
    arguments, embedded metadata, embedded cover art, cookies from a browser,
    cookies file, proxy, number of search results, plus **Test**, **Update** and
    **Find automatically** buttons.
  - **Project Gutenberg** — default language and preferred download format.
  - **Language** — the language of the application interface.
  - **Accessibility** — announce status changes, announce progress milestones,
    keep the activity log visible.
  - **Apply** button, so you can save without closing the dialog.
- **YouTube downloads now use yt-dlp.** Searching, downloading and audio
  conversion all go through yt-dlp, and the yt-dlp options above are yours to
  configure.
- **Project Gutenberg support** — search and download free books in **all 57
  languages Gutenberg publishes, including English and Hindi**, with an accessible
  language selector on the main window that works with the keyboard and screen
  readers.
- **Interface language** — English, हिन्दी (Hindi) and Español (Spanish).
- **Download progress bar**, with progress announced by your screen reader.
- **Book-language selector** on the main window for sources that support languages.
- **Clear error messages** when yt-dlp is missing, out of date, or configured
  wrongly, explaining exactly what to do — on screen, in the status line and in
  the activity log.
- **A built-in self-test** — run
  `AudiobookDownloader.WinForms.exe --self-test` to check that every dialog opens
  and every control is properly exposed to accessibility. It reports problems and
  exits with a non-zero code if it finds any.
- **Keyboard shortcuts** for Settings (<kbd>Ctrl</kbd>+<kbd>,</kbd>), reloading the
  list (<kbd>F5</kbd>), the book-language list (<kbd>Alt</kbd>+<kbd>U</kbd>),
  the activity log (<kbd>Ctrl</kbd>+<kbd>L</kbd>) and quitting
  (<kbd>Ctrl</kbd>+<kbd>Q</kbd>).

### Changed

- **Audible account controls moved into Settings → Audible account.** Signing in,
  signing out, account status and region now live in one place instead of on the
  main window.
- The main window's account button no longer signs you in directly; it opens the
  Audible account page in Settings, which is the single place your account is
  managed.
- **Help now opens reliably** — from the Help menu, the Help button,
  <kbd>F1</kbd> or the keyboard shortcut list.
- The main status line, the Settings status line and the Audible and yt-dlp status
  lines are now **accessibility live regions**, so screen readers announce changes
  as they happen, including errors.
- The YouTube provider is listed as **"YouTube (audio via yt-dlp)"** so it is clear
  what performs the download.
- The Audible sign-in window now opens in front of the app and gives focus back
  when it closes.
- Menus and dialogs return keyboard focus to the control you came from.

### Fixed

- **Audible no longer shows anything when you are not signed in.** No cached,
  sample, fake or previously loaded books, stories, recommendations or library
  items are displayed. The Audible screen shows only the signed-out state and a way
  to sign in.
- The Audible library is never re-used across sessions; signing out clears it.
- The **help dialog now opens** — previously it could fail to appear.
- YouTube searches with no results, and yt-dlp failures, are reported instead of
  looking like an empty list.
- Opening any dialog no longer risks losing your place in the window; focus is
  restored when it closes.

### Removed

- **The built-in Windows speech voice has been removed**, along with the "speak
  status updates aloud" option. A voice of the app's own competed with screen
  readers such as NVDA. The app now exposes proper accessibility information
  through the operating system instead, so **the screen reader you already use
  reads the interface by itself**.
- The `YoutubeExplode` library. All YouTube work is done by yt-dlp; the app does
  not implement its own downloader.

### Upgrading from 1.0.0

- Your existing settings are kept. Nothing needs to be re-entered.
- The "speak status updates aloud" option is gone. Use **Settings →
  Accessibility** to control announcements instead.
- If you used Audible, sign in again from **Settings → Audible account**.
- For YouTube, make sure **yt-dlp** is installed (`pip install -U yt-dlp`) or set
  its full path in **Settings → YouTube and yt-dlp**.

### Notes

- **ffmpeg** is only needed if you choose mp3, opus or wav output. The default m4a
  output needs nothing extra.
- Decryption still happens entirely on your computer, and Audible still issues
  licences only for books in your own library.

---

## [1.0.0] — 2026-09-17

Initial release.

- Provider dropdown: Audible, LibriVox, Internet Archive, YouTube, Spotify →
  YouTube match, and local AAX/AAXC files.
- Sign in to Audible in the browser to list and download the books you own.
- Automatic lossless DRM removal to `.m4b` for AAXC and AAX files.
- Batch downloads with a queue, per-book progress, cancel, retry and removal.
- Durable activity log at `%AppData%\AudiobookDownloader\log.txt`.
- Downloads saved to `Desktop\Audiobooks` by default.

---

[Unreleased]: #unreleased
[1.1.0]: #110
[1.0.0]: #100
