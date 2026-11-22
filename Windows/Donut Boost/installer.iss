#define MyAppName "Donut Boost"
#define MyAppVersion "1.0.4"
#define MyAppPublisher "Donut"
#define MyAppURL "https://user-cariv.github.io/Donut/boost.html"
#define MyAppExeName "DonutBoost.exe"
#ifndef Runtime
  #define Runtime "win-x64"
#endif

[Setup]
AppId={{8B41E112-6893-4E84-8FE3-78A7F4160408}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Donut Boost - Multi-Connection & Speed Bonding Downloader
VersionInfoProductName={#MyAppName}
VersionInfoTextVersion={#MyAppVersion}
SetupIconFile=Assets\Donut.ico
DefaultDirName={autopf}\Donut Boost
DefaultGroupName=Donut Boost
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=Install Donut Boost
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
DisableDirPage=no
DisableReadyPage=no
DisableFinishedPage=no
Uninstallable=yes
CloseApplications=yes
RestartApplications=no
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=Donut Boost
UninstallDisplayIcon={app}\{#MyAppExeName}

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "publish\{#Runtime}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.old,*.old.*,*.pdb"

[Icons]
Name: "{autoprograms}\Donut Boost"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Donut.ico"
Name: "{autodesktop}\Donut Boost"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Donut.ico"; Tasks: desktopicon

[Run]
; Launch Donut Boost upon setup completion using ShellExecute so elevation manifests succeed without code 740
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Donut Boost"; Flags: shellexec nowait postinstall skipifsilent

[UninstallRun]
; Cleanly reset proxy and any registry modifications
Filename: "{cmd}"; Parameters: "/c ""powershell -NoProfile -Command Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -Name ProxyEnable -Value 0 -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated; RunOnceId: "ResetProxy"

[UninstallDelete]
; Completely remove application directory on uninstall
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\DonutBoost"
Type: filesandordirs; Name: "{userappdata}\DonutBoost"
