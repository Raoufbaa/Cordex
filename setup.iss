; Cordex Installer Script

#define MyAppName "Cordex"
#define MyAppVersion "1.1"
#define MyAppPublisher "Raouf, Inc."
#define MyAppURL "https://raoufbakhti.is-a.dev/"
#define MyAppExeName "Cordex.exe"

[Setup]
AppId={{C759ACC1-AA55-4DD7-994A-231DEA2892D9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=Cordex_Setup
DisableDirPage=no
LicenseFile=eula.txt
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic";  MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; ── Main application ──
Source: "publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; ── All publish files ──
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ResultCode: Integer;

procedure CurStepChanged(CurStep: TSetupStep);
var
  wwwrootPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    // Hide wwwroot folder if it exists
    wwwrootPath := ExpandConstant('{app}\wwwroot');
    if DirExists(wwwrootPath) then
      Exec('cmd.exe', '/c attrib +h +s "' + wwwrootPath + '"', '',
        SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  wwwrootPath: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Unhide wwwroot before uninstalling
    wwwrootPath := ExpandConstant('{app}\wwwroot');
    if DirExists(wwwrootPath) then
      Exec('cmd.exe', '/c attrib -h -s "' + wwwrootPath + '"', '',
        SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
