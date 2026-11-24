#define MyAppName "Donut Boost"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.11"
#endif
#define MyAppPublisher "Donut"
#define MyAppURL "https://user-cariv.github.io/Donut/boost.html"
#define MyAppExeName "DonutBoost.exe"
#define ExtensionSetupFolder "Select this folder to unpack in chrome extension"
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

[Dirs]
; Existing unpacked installations still load their files from this path.
Name: "{app}\browser-extension\chrome"; Attribs: hidden

[Files]
Source: "publish\{#Runtime}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs uninsrestartdelete; Excludes: "*.old,*.old.*,*.pdb"
; Keep the brand icon available for the Start Menu and desktop shortcuts.
Source: "Assets\Donut.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "browser-extension\chrome\*"; DestDir: "{app}\browser-extension\chrome"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "browser-extension\chrome\*"; DestDir: "{app}\browser-extension\{#ExtensionSetupFolder}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{app}\browser-extension\Open Chrome Extensions"; Filename: "{code:GetChromePath}"; Parameters: "chrome://extensions/"; Check: ChromeIsInstalled
Name: "{autoprograms}\Donut Boost"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Donut.ico"
Name: "{autodesktop}\Donut Boost"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Donut.ico"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\donutboost"; ValueType: string; ValueName: ""; ValueData: "URL:Donut Boost download link"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\donutboost"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\donutboost\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\donutboost\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKCU; Subkey: "Software\DonutBoost"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{{D090B005-7A8E-4F23-9112-E59C778B5D01}"; ValueName: "Anchor"; Flags: uninsdeletevalue uninsdeletekeyifempty

[Run]
; Run the app with the signed-in user's normal privileges after setup.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Donut Boost"; Flags: shellexec runasoriginaluser nowait postinstall skipifsilent
; In-app updates are silent; reopen the updated app for the user after setup completes.
Filename: "{app}\{#MyAppExeName}"; Flags: shellexec runasoriginaluser nowait; Check: WizardSilent

[UninstallDelete]
; Inno removes installed files and its own executable from the install folder.
Type: files; Name: "{app}\DonutBoost.exe.old*"
Type: files; Name: "{app}\DonutBoost.dll.old*"
Type: files; Name: "{userprograms}\Donut Boost.lnk"
Type: files; Name: "{userdesktop}\Donut Boost.lnk"
Type: filesandordirs; Name: "{localappdata}\DonutBoost"
Type: filesandordirs; Name: "{userappdata}\DonutBoost"
Type: filesandordirs; Name: "{localappdata}\Donut\TorrentCache"
Type: filesandordirs; Name: "{localappdata}\Temp\DonutBoost_Update"

[Code]
function GetChromePath(Param: String): String;
var
  Key: String;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe';
  if RegQueryStringValue(HKCU, Key, '', Result) and FileExists(Result) then Exit;
  if RegQueryStringValue(HKLM64, Key, '', Result) and FileExists(Result) then Exit;
  if RegQueryStringValue(HKLM32, Key, '', Result) and FileExists(Result) then Exit;
  Result := '';
end;

function ChromeIsInstalled(): Boolean;
begin
  Result := GetChromePath('') <> '';
end;

function InitializeUninstall(): Boolean;
begin
  Result := False;
  if CheckForMutexes('DonutBoost_SingleInstance_AppMutex') then
  begin
    if not UninstallSilent then
      MsgBox('Close Donut Boost and run the uninstaller again.', mbInformation, MB_OK);
    Exit;
  end;
  if FileExists(ExpandConstant('{localappdata}\DonutBoost\network-route-metrics.json')) then
  begin
    if not UninstallSilent then
      MsgBox('Open Donut Boost and select Restore route settings before uninstalling.', mbInformation, MB_OK);
    Exit;
  end;
  Result := True;
end;
