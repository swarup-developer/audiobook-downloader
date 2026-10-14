; Installer for Audiobook Downloader, built with Inno Setup 6 on GitHub Actions.
;
;   iscc /DAppVersion=1.3.0 /DSourceExe=path\to\AudiobookDownload.exe /Oout installer\AudiobookDownloader.iss
;
; It installs for the current user by default (no administrator prompt), so the
; app's own updater can run a newer setup quietly; "Install for all users" is offered
; on the first page for anyone who wants it in Program Files. The app keeps its
; settings in %AppData%\AudiobookDownloader, which uninstalling leaves alone.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\AudiobookDownload.exe"
#endif

#define AppName "Audiobook Downloader"
#define AppExe "AudiobookDownload.exe"
#define AppUrl "https://github.com/swarup-developer/audiobook-downloader"

[Setup]
; Never change AppId: it is how a newer setup recognises an installed copy.
AppId={{6F0C2B7E-3A41-4C8E-9B5D-1E7A2F4C9D21}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=swarup-developer
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputBaseFilename=AudiobookDownloader-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; An update closes the running app first, and the app restarts when setup ends.
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Shown as "Launch Audiobook Downloader" at the end of a normal install, and run by
; itself after a quiet update (the app's updater), so the new version starts.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall
