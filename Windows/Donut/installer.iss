#define MyAppName "Donut Convert"
#define MyAppVersion "1.3.0"
#define MyAppPublisher "Donut Convert"
#define MyAppExeName "Donut.exe"
#ifndef Runtime
  #define Runtime "win-x64"
#endif

[Setup]
AppId={{D9E1E0D7-4D71-4C1D-8C51-2E1D5D2EEA91}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Donut file conversion utility
VersionInfoProductName={#MyAppName}
VersionInfoTextVersion={#MyAppVersion}
SetupIconFile=publish\{#Runtime}\Donut.ico
DefaultDirName={autopf}\Donut Convert
DefaultGroupName=Donut Convert
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=Install Donut Convert
; ZIP is intentionally used instead of solid LZMA: installation is much faster
; on Windows, while the self-contained runtime remains unchanged.
Compression=zip
SolidCompression=no
WizardStyle=modern
DisableWelcomePage=no
DisableDirPage=no
DisableReadyPage=no
DisableFinishedPage=no
Uninstallable=yes
CloseApplications=yes
RestartApplications=no
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "publish\{#Runtime}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Donut Convert"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Donut Convert"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Donut Convert"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DonutConvert"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[UninstallDelete]
; Remove the complete installed application folder, including files created by an update.
Type: filesandordirs; Name: "{app}"
; Donut currently stores no user data, but clean these locations if a future version does.
Type: filesandordirs; Name: "{userappdata}\Donut"
Type: filesandordirs; Name: "{localappdata}\Donut"
