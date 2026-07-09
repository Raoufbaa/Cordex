; Cordex Installer Script

#define MyAppName "Cordex"
#ifndef MyAppVersion
  #define MyAppVersion "1.2.1"
#endif
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

; ── Microsoft Edge WebView2 bootstrapper ──
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: ignoreversion; Check: InstallWebViewChecked

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ResultCode: Integer;
  WebViewPage: TWizardPage;
  InstallWebViewCheckBox: TNewCheckBox;
  WebViewReqLabel: TNewStaticText;
  WebViewNeeded: Boolean;

function IsWebView2Installed(): Boolean;
var
  Version: String;
begin
  Result := False;
  // Check HKLM 64-bit and 32-bit registry paths
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8ABB-2135AEEB4466}', 'pv', Version) then
  begin
    if Version <> '' then Result := True;
  end
  else if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8ABB-2135AEEB4466}', 'pv', Version) then
  begin
    if Version <> '' then Result := True;
  end
  else if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8ABB-2135AEEB4466}', 'pv', Version) then
  begin
    if Version <> '' then Result := True;
  end;
end;

function InstallWebViewChecked(): Boolean;
begin
  Result := WebViewNeeded and (InstallWebViewCheckBox <> nil) and InstallWebViewCheckBox.Checked;
end;

procedure InitializeWizard();
begin
  WebViewNeeded := not IsWebView2Installed();

  if WebViewNeeded then
  begin
    // Create the custom page after SelectTasks page
    WebViewPage := CreateCustomPage(wpSelectTasks, 'Microsoft Edge WebView2 Runtime Requirement', 'WebView2 Runtime is required to run Cordex.');
    
    WebViewReqLabel := TNewStaticText.Create(WebViewPage);
    WebViewReqLabel.Parent := WebViewPage.Surface;
    WebViewReqLabel.Caption := 'Cordex uses Microsoft Edge WebView2 for rendering the Discord interface.' + #13#10 +
                               'The installer detected that Microsoft Edge WebView2 Runtime is NOT installed on this computer.' + #13#10#13#10 +
                               'It is highly recommended to let the installer install it for you.';
    WebViewReqLabel.Width := WebViewPage.SurfaceWidth;
    WebViewReqLabel.Height := ScaleY(60);
    WebViewReqLabel.WordWrap := True;

    InstallWebViewCheckBox := TNewCheckBox.Create(WebViewPage);
    InstallWebViewCheckBox.Parent := WebViewPage.Surface;
    InstallWebViewCheckBox.Top := WebViewReqLabel.Top + WebViewReqLabel.Height + ScaleY(10);
    InstallWebViewCheckBox.Width := WebViewPage.SurfaceWidth;
    InstallWebViewCheckBox.Caption := 'Install Microsoft Edge WebView2 Runtime (Recommended)';
    InstallWebViewCheckBox.Checked := True;
    InstallWebViewCheckBox.Font.Style := [fsBold];
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  wwwrootPath: String;
  InstallerPath: String;
begin
  if CurStep = ssInstall then
  begin
    if InstallWebViewChecked() then
    begin
      // Update wizard progress status
      WizardForm.StatusLabel.Caption := 'Installing Microsoft Edge WebView2 Runtime...';
      WizardForm.ProgressGauge.Style := npbstMarquee;
      
      InstallerPath := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
      if FileExists(InstallerPath) then
      begin
        // Run WebView2 installer silently and wait for it to finish
        if Exec(InstallerPath, '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
        begin
          if ResultCode <> 0 then
          begin
            MsgBox('WebView2 Runtime installation returned exit code ' + IntToStr(ResultCode) + '. The application might not run properly.', mbWarning, MB_OK);
          end;
        end
        else
        begin
          MsgBox('Failed to start WebView2 Runtime installer. The application might not run properly.', mbError, MB_OK);
        end;
      end
      else
      begin
        MsgBox('WebView2 installer was not found in temporary files. Skipping installation.', mbInformation, MB_OK);
      end;
      
      WizardForm.ProgressGauge.Style := npbstNormal; // restore progress gauge
    end;
  end
  else if CurStep = ssPostInstall then
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
