; SplitWire-Turkey Setup Script
; Inno Setup 6

#define MyAppName "SplitWire-Turkey"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SplitWire-Turkey"
#define MyAppURL "https://splitwire-turkey.com"
#define MyAppExeName "SplitWire-Turkey.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
AppId={{06b842bd-739c-4958-841e-b398791dfaf6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
UsePreviousAppDir=no
LicenseFile=
InfoBeforeFile=
InfoAfterFile=
OutputDir=Output
OutputBaseFilename=SplitWire-Turkey-Setup
SetupIconFile=res\splitwire.ico
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64 x86
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequiredOverridesAllowed=commandline

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
Source: "SplitWire-Turkey.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SplitWire-Turkey.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "SplitWire-Turkey.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "MaterialDesignThemes.Wpf.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "MaterialDesignColors.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "Microsoft.Xaml.Behaviors.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "res\*"; DestDir: "{app}\res"; Flags: ignoreversion recursesubdirs
Source: "Prerequisites\VC_redist.x64.exe"; DestDir: "{app}\Prerequisites"; Flags: ignoreversion
Source: "Prerequisites\Windows.Packet.Filter.3.6.1.1.x64.msi"; DestDir: "{app}\Prerequisites"; Flags: ignoreversion
; .NET Framework Runtime Installers
Source: "Prerequisites\.NET 6.0\windowsdesktop-runtime-6.0.35-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: Is64BitInstallMode
Source: "Prerequisites\.NET 6.0\windowsdesktop-runtime-6.0.35-win-x86.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not Is64BitInstallMode

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Registry]
Root: HKCU; Subkey: "SOFTWARE\SplitWire-Turkey"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "SOFTWARE\SplitWire-Turkey"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

[Run]
; Install .NET 6.0 Runtime if not present
Filename: "{tmp}\windowsdesktop-runtime-6.0.35-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing .NET 6.0 Runtime..."; Flags: runhidden; Check: Is64BitInstallMode and not IsDotNetDetected
Filename: "{tmp}\windowsdesktop-runtime-6.0.35-win-x86.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing .NET 6.0 Runtime..."; Flags: runhidden; Check: not Is64BitInstallMode and not IsDotNetDetected
; Launch application after installation (with admin privileges)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
function IsDotNetDetected: Boolean;
var
  Success: Boolean;
  InstallPath: String;
  ReleaseInstallPath: String;
  Release: Cardinal;
  Key: String;
begin
  Result := False;
  
  // Check for .NET 6.0 Desktop Runtime
  Key := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{D5E5A659-C2DD-42D5-A1A3-6B2C8DD7DDB0}';
  if RegQueryStringValue(HKLM, Key, 'InstallLocation', InstallPath) then
  begin
    if DirExists(InstallPath) then
    begin
      Result := True;
      Exit;
    end;
  end;
  
  // Check for .NET 6.0 Runtime
  Key := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{D5E5A659-C2DD-42D5-A1A3-6B2C8DD7DDB0}';
  if RegQueryStringValue(HKLM, Key, 'InstallLocation', InstallPath) then
  begin
    if DirExists(InstallPath) then
    begin
      Result := True;
      Exit;
    end;
  end;
  
  // Check for .NET 6.0 Desktop Runtime (x64)
  if Is64BitInstallMode then
  begin
    Key := 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{D5E5A659-C2DD-42D5-A1A3-6B2C8DD7DDB0}';
    if RegQueryStringValue(HKLM, Key, 'InstallLocation', InstallPath) then
    begin
      if DirExists(InstallPath) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
  
  // Check for .NET 6.0 Runtime (x64)
  if Is64BitInstallMode then
  begin
    Key := 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{D5E5A659-C2DD-42D5-A1A3-6B2C8DD7DDB0}';
    if RegQueryStringValue(HKLM, Key, 'InstallLocation', InstallPath) then
    begin
      if DirExists(InstallPath) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  
  // Check if .NET 6.0 is installed
  if not IsDotNetDetected then
  begin
    MsgBox('.NET 6.0 Desktop Runtime is required for SplitWire-Turkey.' + #13#10 + 
           'The installer will now install .NET 6.0 Desktop Runtime automatically.', 
           mbInformation, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Additional installation steps if needed
  end;
end; 