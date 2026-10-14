# Audiobook Downloader

Audiobook Downloader is a free Windows app for downloading audiobooks and books to
your computer. It can back up the Audible books you own, and it can search and
download free public-domain books from LibriVox, the Internet Archive and Project
Gutenberg. A built-in player plays them, and remembers where you stopped.

The app is built for keyboard and screen reader users first. Every control has a
clear name, everything works without a mouse, and status changes are announced by
your own screen reader (NVDA, JAWS or Narrator). The app never talks with a voice of
its own.

> **Personal use only.** The Audible part of this app is for books you have bought.
> Audible only issues download licences for books in your own library, and this app
> does not get around that. DRM removal is off until you turn it on yourself. Do not
> share decrypted files.

## Contents

- [Features](#features)
- [Download and install](#download-and-install)
- [Getting started](#getting-started)
- [Book sources](#book-sources)
- [The player](#the-player)
- [Removing DRM](#removing-drm)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Settings](#settings)
- [Interface languages](#interface-languages)
- [Screen reader support](#screen-reader-support)
- [Privacy](#privacy)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Contributing](#contributing)
- [Licence](#licence)

## Features

- **Five book sources in one list:** your Audible library, LibriVox, the Internet
  Archive, Project Gutenberg, and AAX/AAXC files already on your computer.
- **Your Audible library, properly listed.** Titles, authors, narrators, length,
  language, series and description. Podcast episodes in your library are listed one
  by one, so they can be downloaded too.
- **Signed in once, signed in for good.** A remembered Audible sign-in is used
  automatically when the app starts, with no button to press.
- **New purchases download themselves** (optional). When the app starts, books you
  bought since the last time are added to the download queue.
- **DRM removal you choose.** Off by default. Turn it on in Settings and every
  Audible book is fully decrypted on your own computer, checked, and saved as an
  ordinary M4B or MP3 file. Books downloaded earlier with their DRM can be unlocked
  later without downloading them again.
- **M4B or MP3, whole or by chapter.** Save one lossless M4B with chapters, one MP3,
  or one file per chapter in a folder of its own.
- **Book details inside the file.** The description, series, narrator and cover are
  written into the file, so any player shows them. The cover can also be saved as a
  picture next to the book.
- **Built-in player.** Play, pause, jump 30 seconds, change chapter, change speed
  from 0.5x to 3x, and pick up where you stopped last time. LibriVox zip files are
  unzipped for you.
- **Search that works the way you expect.** Every word you type has to match, so
  "harry stone" finds "Harry Potter and the Philosopher's Stone". Previous searches
  are remembered for each source.
- **Filter by language** on Audible, LibriVox and Project Gutenberg.
- **Sort the list** by title, author, length or date added, and hide the books you
  already have.
- **Already-downloaded books are marked**, and the app asks before downloading one
  again.
- **Faster, gentler downloads.** Download up to four books at once, and set a speed
  limit if you share your connection.
- **Nothing gets lost.** A download never overwrites an existing file. An interrupted
  transfer resumes where it stopped, a dropped connection is retried automatically,
  and the queue survives closing the app.
- **Export your list** to a CSV file that opens in Excel.
- **Seven interface languages:** English, Hindi, Spanish, Bengali, Marathi, Tamil and
  Urdu (shown right to left).
- **Tells you about new versions** when they are released.
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

The menu bar (press Alt) has three menus: **Application**, **View** and **Help**.

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
Settings. Next time you start the app you are already signed in. To sign out, press
the same button, which now reads **Sign out of Audible**.

### Download free books

1. In **Provider**, choose LibriVox, Internet Archive or Project Gutenberg. No
   account is needed.
2. Type what you are looking for in **Search books** and press Enter.
3. Optionally choose a **Book language** (Alt+U).
4. Select books and press Enter.

## Book sources

| Source | What you get | Account | Format |
|--------|--------------|---------|--------|
| Audible (your library) | Books and podcast episodes you own | Amazon account | `.aaxc` as downloaded, or DRM-free `.m4b` / `.mp3` when DRM removal is on |
| LibriVox | Over 20,000 free public-domain audiobooks read by volunteers | None | `.zip` of MP3 chapters |
| Internet Archive | Free audio collections, most popular first | None | `.zip` of the item's files |
| Project Gutenberg | Over 70,000 free public-domain books | None | EPUB, HTML, text or MP3 |
| Local files | Your own `.aax` and `.aaxc` files | None | DRM-free `.m4b` / `.mp3` |

The first LibriVox search reads the whole catalogue once (about 20 seconds) and keeps
it for a week, so later searches are instant.

## The player

Select a downloaded book and press **Ctrl+P** (or Application, Play selected book).
A finished row of the download queue can be played the same way.

| Keys in the player | What they do |
|--------------------|--------------|
| Space on the position slider, or the Play/Pause media key | Play or pause |
| Left and Right arrows on the position slider | Move 10 seconds |
| Ctrl+Left, Ctrl+Right | Jump back or forward 30 seconds |
| Ctrl+Shift+Left, Ctrl+Shift+Right | Previous or next chapter file |
| Ctrl+Up, Ctrl+Down | Faster or slower |
| Ctrl+T | Say the current time |
| Escape | Close the player; the position is remembered |

A LibriVox zip is unzipped into a folder next to it the first time you play it. An
e-book opens in the program Windows uses for it.

## Removing DRM

DRM removal is **off** until you turn it on. While it is off, Audible books are saved
exactly as Audible sends them (`.aaxc`), and the keys that unlock them are remembered.

To turn it on, open Settings (Ctrl+,), go to **Downloads** and tick **Remove DRM from
Audible books**. The app explains what this means and asks you to confirm that you
will only use it for books you own, for your own backup.

With it on:

- Every Audible book is decrypted on your computer. Nothing is sent anywhere.
- The result is checked: it must play for as long as the original. A copy that falls
  short is discarded and the encrypted original is kept.
- The app offers to unlock the Audible books that are already in your download folder
  with their DRM. You can do the same any time from Application, **Remove DRM from
  downloaded books**.
- The encrypted original is deleted after a successful decryption, unless you turn
  that off.

| Format | What unlocks it | Result |
|--------|-----------------|--------|
| AAXC (current Audible files) | A key and IV issued with each download licence | Lossless `.m4b`, or `.mp3` |
| AAX (older Audible files) | Your account's activation bytes | Lossless `.m4b`, or `.mp3` |

Decryption uses [AAXClean](https://github.com/Mbucari/AAXClean) and does not need
ffmpeg. Removing DRM may not be allowed where you live; you are responsible for using
this as your local law allows.

## Keyboard shortcuts

Press F1 at any time for the full list in your own language.

| Keys | What it does |
|------|--------------|
| F1 | Show all keyboard shortcuts |
| Alt | Open the menu bar |
| Ctrl+, | Settings |
| F5 | Reload the book list |
| Ctrl+P | Play the selected book |
| Ctrl+O | Open the download folder |
| Ctrl+E | Export the book list to a CSV file |
| Ctrl+K | How to get decryption keys for AAX and AAXC files |
| Ctrl+1, Ctrl+2, Ctrl+3, Ctrl+4 | Sort by title, author, length, date added |
| Ctrl+H | Hide or show books already downloaded |
| Ctrl+Q or Alt+F4 | Quit |
| Alt+P | Go to the provider list |
| Alt+I | Sign in to Audible |
| Alt+S | Go to the search box |
| Alt+Down | Open your previous searches |
| Alt+U | Go to the book language list |
| Alt+B | Go to the book list |
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
  or showing a Windows notification when downloads finish, confirmations, and checking
  for updates.
- **Downloads:** DRM removal, deleting the encrypted original, M4B or MP3, one file
  per chapter, writing the book's details into the file, saving the cover picture,
  author folders, asking before downloading a book twice, downloads at the same time
  (1 to 4) and a speed limit.
- **Audible account:** sign in, sign out, country, remembering the sign-in, loading
  the library after signing in, podcast episodes, and downloading new purchases
  automatically.
- **Project Gutenberg:** default language and preferred format.
- **Language:** the language of the app itself.
- **Accessibility:** which status changes and progress milestones are announced, and
  whether the activity log stays visible.

## Interface languages

English, Hindi (हिन्दी), Spanish (Español), Bengali (বাংলা), Marathi (मराठी),
Tamil (தமிழ்) and Urdu (اردو). Choose one in Settings, Language. Urdu is laid out right
to left. A few technical error messages are always in English, so they can be quoted
in a bug report.

## Screen reader support

- Every control has a short, plain name and its real role, in a sensible tab order.
- The status line and the progress line are separate live regions. Your screen reader
  announces what happens, including errors, and progress at 25, 50 and 75 percent.
- Lists announce changes only once. The app does not repeat what your screen reader
  already reads when you arrow through a list, and moving through a list never starts
  a search on every key press.
- Opening a dialog moves focus into it, and closing it puts focus back where it was.
- The app contains no speech engine. All speech comes from your own screen reader.

## Privacy

- Your Amazon password is typed only into Amazon's own page. The app never sees it.
- The sign-in is stored only on your computer, in
  `%AppData%\AudiobookDownloader\audible-identity.json`. Sign out, or turn off
  "Remember the Audible sign-in", to delete it.
- No Audible books are shown, cached or stored while you are signed out.
- The update check reads the public list of releases on GitHub and sends nothing
  about you. It can be turned off in Settings, General.
- The app has no analytics and sends nothing anywhere else except to the book sources
  you use.
- Settings, the download queue, playback positions and a log file are kept in
  `%AppData%\AudiobookDownloader`.

## Troubleshooting

**The sign-in window does not open.** Install the Microsoft Edge WebView2 Runtime from
Microsoft's website, then press Sign in to Audible again.

**"Audible refused to issue a download licence".** The title is no longer in your
library (for example an Audible Plus title that was removed, or a returned book), or
it belongs to a different Audible country than the one you signed in to. Sign out and
sign in again with the right country.

**Audible books download as .aaxc files.** DRM removal is off. See
[Removing DRM](#removing-drm).

**A search finds nothing.** Check the Book language list: a language filter may be
hiding results. Also check View, Hide books already downloaded (Ctrl+H). Project
Gutenberg has very few books in some languages, including Hindi.

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

The self-test runs offline, makes no sound and never touches your own settings. It
checks the following and exits with an error if anything is wrong:

- every window, in all seven interface languages, for accessible names, roles and
  tab order;
- that every text is translated, every placeholder kept, and every translation key
  exists;
- the keyboard shortcut table against the menus and the help screen;
- the country search in the sign-in, in every language;
- the Audible sign-in redirect for every store;
- the download engine: no overwrites, resuming, cancelling, the queue surviving a
  restart, and recognising books that are already downloaded;
- the player: it really plays a file, remembers where it stopped and resumes there;
- the LibriVox catalogue reader.

Add `--self-test-live` to also read the real LibriVox catalogue over the internet.

### Making a release

Releases are built on GitHub, never on a personal computer. Every push and pull
request is built and self-tested by the **Build** workflow. To publish a version:

1. Add a section for it to `CHANGELOG.md` (for example `## [1.2.0] — 2026-10-14`)
   and push it. That section becomes the release notes.
2. Start the **Release** workflow, either by pushing a tag:

   ```
   git tag v1.2.0
   git push origin v1.2.0
   ```

   or on GitHub under Actions, Release, Run workflow, typing the version.

The workflow builds the single-file `AudiobookDownload.exe`, runs the self-test on
that exact file in an empty folder, and only then publishes the release with the exe
attached. If the self-test finds a problem, nothing is released.

### Project layout

```
src/AudiobookDownloader.Core      book sources, models, download, decryption and update check
src/AudiobookDownloader.WinForms  the accessible interface, player, Settings, translations, self-test
```

To add a book source, implement `IBookProvider` and register it in `MainForm`. A
source that can filter by language also implements `ILanguageFilter`, and the main
window then shows the language list for it.

## Contributing

Bug reports, translations and pull requests are welcome. Please:

- run the self-test before sending a pull request;
- keep every new control keyboard-reachable and give it a plain accessible name;
- add new interface text to `Localization/Strings.cs` in English, Hindi and Spanish,
  and to each `Localization/Strings.*.cs` language file. The self-test lists anything
  missing.

See [CHANGELOG.md](CHANGELOG.md) for what changed in each release.

## Licence

Audiobook Downloader is free software under the
[GNU Affero General Public License, version 3](LICENSE). It uses AudibleApi
(AGPL 3.0) and AAXClean (GPL 3.0), among others; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Thanks to [Libation](https://github.com/rmcrackan/Libation), whose AudibleApi and
AAXClean make the Audible part possible, and to [LibriVox](https://librivox.org), the
[Internet Archive](https://archive.org), [Project Gutenberg](https://www.gutenberg.org)
and [Gutendex](https://gutendex.com).

## Disclaimer

This project is not affiliated with Audible, Amazon, LibriVox, the Internet Archive or
Project Gutenberg. Audible is a trademark of Audible, Inc. Use the Audible features
only for books you own and only as your local laws allow.
