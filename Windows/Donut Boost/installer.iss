#define MyAppName "Donut Boost"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.10"
#endif
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
VersionInfoDescription=Donut Boost - Network Monitor and Direct Downloader
VersionInfoProductName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
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
; Keep the brand icon available for the Start Menu and desktop shortcuts.
Source: "Assets\Donut.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "browser-extension\chrome\*"; DestDir: "{app}\browser-extension\chrome"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Donut Boost"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Donut.ico"
Name: "{autodesktop}\Donut Boost"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Donut.ico"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\donutboost"; ValueType: string; ValueName: ""; ValueData: "URL:Donut Boost download link"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\donutboost"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\donutboost\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\donutboost\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
; Run the app with the signed-in user's normal privileges after setup.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Donut Boost"; Flags: shellexec runasoriginaluser nowait postinstall skipifsilent
; In-app updates are silent; reopen the updated app for the user after setup completes.
Filename: "{app}\{#MyAppExeName}"; Flags: shellexec runasoriginaluser nowait; Check: WizardSilent

[UninstallDelete]
; Completely remove application directory on uninstall
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\DonutBoost"
Type: filesandordirs; Name: "{userappdata}\DonutBoost"
