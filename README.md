# Audiobook Downloader

Audiobook Downloader is a free Windows app for downloading audiobooks and books to
your computer. It can back up the Audible books you own as ordinary M4B files, and it
can search and download free public-domain books from LibriVox, the Internet Archive
and Project Gutenberg.

The app is built for keyboard and screen reader users first. Every control has a
clear name, everything works without a mouse, and status changes are announced by
your own screen reader (NVDA, JAWS or Narrator). The app never talks with a voice of
its own.

> **Personal use only.** The Audible part of this app is for making personal backups
> of books you have bought. Audible only issues download licences for books in your
> own library, and this app does not get around that. Do not share decrypted files.

## Contents

- [Features](#features)
- [Download and install](#download-and-install)
- [Getting started](#getting-started)
- [Book sources](#book-sources)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Settings](#settings)
- [Screen reader support](#screen-reader-support)
- [How DRM removal works](#how-drm-removal-works)
- [Privacy](#privacy)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Contributing](#contributing)
- [Roadmap](#roadmap)

## Features

- **Five book sources in one list:** your Audible library, LibriVox, the Internet
  Archive, Project Gutenberg, and AAX/AAXC files already on your computer.
- **Audible backups.** Sign in with your Amazon account, see your whole library with
  titles, authors, narrators and length, and download books as DRM-free `.m4b`
  files. Decryption is lossless and happens on your own computer.
- **Search that works the way you expect.** Every word you type has to match, so
  "harry stone" finds "Harry Potter and the Philosopher's Stone". Previous searches
  are remembered for each source.
- **Filter by language.** Audible, LibriVox and Project Gutenberg can be narrowed to
  one language, such as Hindi, English or German.
- **Already-downloaded books are marked.** A book whose file is already in your
  download folder says "already downloaded" in the list. The app asks before
  downloading it again.
- **Batch downloads.** Select several books, or all of them with Ctrl+A, and press
  Download once.
- **Nothing gets lost.** A download never overwrites an existing file. An interrupted
  transfer resumes where it stopped, a dropped connection is retried automatically,
  and the queue survives closing the app.
- **Book details.** Ctrl+I shows everything known about a book: author, narrator,
  length, language, source and, once downloaded, where the file is.
- **Export your list.** Ctrl+E saves the current book list as a CSV file that opens in
  Excel or any text editor. Handy for keeping a catalogue of your Audible library.
- **Tidy folders.** Optionally save each book in a folder named after its author.
- **Interface in English, Hindi and Spanish.**
- **Follows Windows high contrast** while the app is running, without a restart.

## Download and install

1. Download `AudiobookDownload.exe` from the
   [Releases](https://github.com/swarup-developer/audiobook-downloader/releases)
   page.
2. Put it in any folder and run it. There is no installer, and you do not need to
   install .NET.

**Requirements**

- Windows 10 or Windows 11, 64-bit.
- Microsoft Edge WebView2 Runtime, used for the Audible sign-in window. It is
  already part of Windows 11 and comes with Microsoft Edge on Windows 10. If it is
  missing, the app tells you so.

Windows SmartScreen may warn you the first time you run the file, because it is not
signed with a code-signing certificate. Choose "More info", then "Run anyway".

## Getting started

The main window has three groups, from top to bottom:

1. **Find books**: choose a source, search, and pick books from the list.
2. **Downloads**: Download selected, Cancel active, the progress bar and the download
   queue.
3. **Status**: the status line, which your screen reader announces, and the activity
   log.

### Download your Audible books

1. In **Provider**, choose "Audible (your library)".
2. Press **Sign in to Audible** (Alt+I), right next to the provider list.
3. Choose your country. Use the Up and Down arrows, or type the first letters of
   its name: "ind" finds India. Press Enter.
4. Amazon's own sign-in page opens inside the app. Sign in as usual. The window
   closes by itself when Amazon has accepted the sign-in.
5. Your library appears in the list. Select one or more books and press Enter or
   **Download selected** (Alt+D).

Downloaded books are saved to `Desktop\Audiobooks` unless you change it in
Settings. The sign-in is remembered, so next time your library loads straight away.
To sign out, press the same button, which now reads **Sign out of Audible**.

### Download free books

1. In **Provider**, choose LibriVox, Internet Archive or Project Gutenberg. No
   account is needed.
2. Type what you are looking for in **Search books** and press Enter.
3. Optionally choose a **Book language** (Alt+U).
4. Select books and press Enter.

## Book sources

| Source | What you get | Account | Format |
|--------|--------------|---------|--------|
| Audible (your library) | Books you own | Amazon account | `.m4b` (DRM removed) |
| LibriVox | Over 20,000 free public-domain audiobooks read by volunteers | None | `.zip` of MP3 chapters |
| Internet Archive | Free audio collections, most popular first | None | `.zip` of the item's files |
| Project Gutenberg | Over 70,000 free public-domain books | None | EPUB, HTML, text or MP3 |
| Local files | Your own `.aax` and `.aaxc` files | None | `.m4b` (DRM removed) |

The first LibriVox search reads the whole catalogue once (about 20 seconds) and keeps
it for a week, so later searches are instant.

## Keyboard shortcuts

Press F1 at any time for the full list in your own language.

| Keys | What it does |
|------|--------------|
| F1 | Show all keyboard shortcuts |
| Alt | Open the application menu |
| Ctrl+, | Settings |
| F5 | Reload the book list |
| Ctrl+O | Open the download folder |
| Ctrl+E | Export the book list to a CSV file |
| Ctrl+K | How to get decryption keys for AAX and AAXC files |
| Ctrl+Q or Alt+F4 | Quit |
| Alt+P | Go to the provider list |
| Alt+S | Go to the search box |
| Alt+Down | Open your previous searches |
| Alt+U | Go to the book language list |
| Alt+B | Go to the book list |
| Alt+I | Sign in to Audible |
| Enter in the book list | Download the selected books |
| Ctrl+A in the book list | Select every book |
| Ctrl+I | Details of the selected book |
| Ctrl+Shift+O | Show the selected book's file in File Explorer |
| Alt+D | Download selected |
| Ctrl+R | Retry the selected failed or cancelled download |
| Ctrl+Delete | Remove a download that has not started |
| Ctrl+Shift+Delete | Clear finished downloads |
| Ctrl+L | Show or hide the activity log |
| Alt+L | Go to the activity log |

The download queue also has a context menu (Applications key or Shift+F10) with
Retry, Remove and Clear finished.

## Settings

Open Settings with Ctrl+, and move between its sections with Ctrl+Tab.

- **General:** download folder, folder for your own AAX/AAXC files, opening the folder
  or showing a Windows notification when downloads finish, and confirmations.
- **Downloads:** remove DRM from Audible files, delete the encrypted original after
  decrypting, save each book in an author folder, and ask before downloading a book
  that is already downloaded.
- **Audible account:** sign in, sign out, change country, remember the sign-in, and
  load the library automatically after signing in.
- **Project Gutenberg:** default language and preferred format.
- **Language:** the language of the app itself.
- **Accessibility:** which status changes and progress milestones are announced, and
  whether the activity log stays visible.

## Screen reader support

- Every control has a short, plain name and its real role, in a sensible tab order.
- The status line and the progress line are separate live regions. Your screen reader
  announces what happens, including errors, and progress at 25, 50 and 75 percent.
- Lists announce changes only once. The app does not repeat what your screen reader
  already reads when you arrow through a list.
- Opening a dialog moves focus into it, and closing it puts focus back where it was.
- The app contains no speech engine. All speech comes from your own screen reader.

## How DRM removal works

| Format | What unlocks it | Result |
|--------|-----------------|--------|
| AAXC (current Audible files) | A key and IV issued with each download licence | Lossless `.m4b` |
| AAX (older Audible files) | Your account's activation bytes | Lossless `.m4b` |
| Everything else | Nothing, the files are already DRM-free | Saved as they are |

When you download through your own Audible account, the keys are fetched for you. For
AAX or AAXC files you already have, the app asks for the keys once and remembers them.
Decryption uses [AAXClean](https://github.com/Mbucari/AAXClean) and does not need
ffmpeg.

## Privacy

- Your Amazon password is typed only into Amazon's own page. The app never sees it.
- The sign-in is stored only on your computer, in
  `%AppData%\AudiobookDownloader\audible-identity.json`. Sign out, or turn off
  "Remember the Audible sign-in", to delete it.
- No Audible books are shown, cached or stored while you are signed out.
- The app has no analytics and sends nothing anywhere except to the book sources you
  use.
- Settings, the download queue and a log file are kept in
  `%AppData%\AudiobookDownloader`.

## Troubleshooting

**The sign-in window does not open.** Install the Microsoft Edge WebView2 Runtime from
Microsoft's website, then press Sign in to Audible again.

**"Audible refused to issue a download licence".** The title is no longer in your
library (for example an Audible Plus title that was removed, or a returned book), or
it belongs to a different Audible country than the one you signed in to. Sign out and
sign in again with the right country.

**A search finds nothing.** Check the Book language list: a language filter may be
hiding results. Project Gutenberg has very few books in some languages, including
Hindi.

**Decryption failed.** For your own AAX or AAXC files, check the key and IV (32 hex
digits each) or the activation bytes (8 hex digits). The encrypted original is always
kept when decryption fails.

**Anything else.** The activity log (Alt+L) and the log file at
`%AppData%\AudiobookDownloader\log.txt` say what happened. Please include the relevant
lines when you open an issue.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```
dotnet build AudiobookDownloader.sln
dotnet run --project src/AudiobookDownloader.WinForms
```

To make the single-file `AudiobookDownload.exe`:

```
dotnet publish src/AudiobookDownloader.WinForms/AudiobookDownloader.WinForms.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out
```

Then rename `out\AudiobookDownloader.WinForms.exe` to `AudiobookDownload.exe`.

### Self-test

```
dotnet run --project src/AudiobookDownloader.WinForms -- --self-test
```

The self-test runs offline. It checks the following and exits with an error if
anything is wrong:

- every dialog, in every interface language, for accessible names, roles and tab
  order;
- the keyboard shortcut table against the menu and the help screen;
- the country search in the sign-in;
- the Audible sign-in redirect for every store;
- the download engine: no overwrites, resuming, cancelling, and the queue surviving a
  restart;
- the LibriVox catalogue reader.

Add `--self-test-live` to also read the real LibriVox catalogue over the internet.

### Project layout

```
src/AudiobookDownloader.Core      book sources, models, download and decryption engine
src/AudiobookDownloader.WinForms  the accessible interface, Settings, translations, self-test
```

To add a book source, implement `IBookProvider` and register it in `MainForm`. A
source that can filter by language also implements `ILanguageFilter`, and the main
window then shows the language list for it.

## Contributing

Bug reports, translations and pull requests are welcome. Please:

- run the self-test before sending a pull request;
- keep every new control keyboard-reachable and give it a plain accessible name;
- add new interface text to `Localization/Strings.cs` in English, Hindi and Spanish.

See [CHANGELOG.md](CHANGELOG.md) for what changed in each release.

## Roadmap

Ideas being considered for future versions:

- Podcast episodes from your Audible library.
- Splitting a book into one file per chapter, and choosing MP3 output.
- Downloading new Audible purchases automatically when the app starts.
- Built-in playback with remembered position.
- More interface languages.

## Acknowledgements

- [AudibleApi](https://github.com/rmcrackan/AudibleApi) and
  [AAXClean](https://github.com/Mbucari/AAXClean), from the
  [Libation](https://github.com/rmcrackan/Libation) project.
- [LibriVox](https://librivox.org), the [Internet Archive](https://archive.org),
  [Project Gutenberg](https://www.gutenberg.org) and
  [Gutendex](https://gutendex.com).

## Disclaimer

This project is not affiliated with Audible, Amazon, LibriVox, the Internet Archive or
Project Gutenberg. Audible is a trademark of Audible, Inc. Use the Audible features
only for books you own and only as your local laws allow.
