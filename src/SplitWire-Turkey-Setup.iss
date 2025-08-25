; SplitWire-Turkey Setup Script
; Inno Setup 6

#define MyAppName "SplitWire-Turkey"
#define MyAppVersion "1.5.0"
#define MyAppPublisher "SplitWire-Turkey"
#define MyAppURL "https://github.com/cagritaskn/SplitWire-Turkey"
#define MyAppExeName "SplitWire-Turkey.exe"

; Türkçe dil dosyası
[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

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
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible x86
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=commandline
; Türkçe varsayılan dil
LanguageDetectionMethod=locale
ShowLanguageDialog=no

[Tasks]
Name: "desktopicon"; Description: "Masaüstünde kısayol oluştur"; GroupDescription: "Ek simgeler:"; Flags: unchecked
Name: "quicklaunchicon"; Description: "Hızlı başlat çubuğunda kısayol oluştur"; GroupDescription: "Ek simgeler:"; Flags: unchecked; Check: not IsAdminInstallMode

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
Name: "{group}\SplitWire-Turkey'i Kaldır"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Registry]
Root: HKCU; Subkey: "SOFTWARE\SplitWire-Turkey"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "SOFTWARE\SplitWire-Turkey"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

[Run]
; Install .NET 6.0 Runtime if not present
Filename: "{tmp}\windowsdesktop-runtime-6.0.35-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: ".NET 6.0 Runtime kuruluyor..."; Flags: runhidden; Check: Is64BitInstallMode and not IsDotNetDetected
Filename: "{tmp}\windowsdesktop-runtime-6.0.35-win-x86.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: ".NET 6.0 Runtime kuruluyor..."; Flags: runhidden; Check: not Is64BitInstallMode and not IsDotNetDetected
; Launch application after installation (with admin privileges)
Filename: "{app}\{#MyAppExeName}"; Description: "Kurulum tamamlandıktan sonra SplitWire-Turkey'i çalıştır"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
// Forward declarations
function IsServiceInstalled(ServiceName: String): Boolean; forward;
function StopAndRemoveService(ServiceName: String): Boolean; forward;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep); forward;

function IsDotNetDetected: Boolean;
var
  InstallPath: String;
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
    // MsgBox('.NET 6.0 Desktop Runtime, SplitWire-Turkey için gereklidir.' + #13#10 + 
          // 'Kurulum programı şimdi .NET 6.0 Desktop Runtime''ı otomatik olarak kuracaktır.', 
          // mbInformation, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Additional installation steps if needed
  end;
end;



function IsServiceInstalled(ServiceName: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  // 5 saniye timeout ile hizmet sorgula
  if Exec('sc', 'query ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := (ResultCode = 0);
  end;
end;

function StopAndRemoveService(ServiceName: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  
  try
    // Hizmeti durdur (5 saniye timeout)
    if Exec('sc', 'stop ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      // Hizmeti kaldır (5 saniye timeout)
      if Exec('sc', 'delete ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      begin
        Result := True;
      end;
    end;
  except
    // Hata durumunda false döndür
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  LocalAppDataPath: String;
  SplitWireTurkeyPath: String;
  ResetDNSScriptPath: String;
  WireSockUninstallPath: String;
  WireSockMSIPath: String;
  UninstallLogPath: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Kaldırma log dosyası yolu
    UninstallLogPath := ExpandConstant('{app}') + '\unins000.log';
    
    // İlerleme çubuğu güncelleme - Kaldırıcıda GUI elementleri yok
    // WizardForm.ProgressGauge.Position := 0;
    
    // 1. Aşama: Hizmetleri kaldır (İlerleme: %20)
    Log('=== 1. AŞAMA: Hizmetler kaldırılıyor ===');
    
    // WireSock hizmetini kaldır
    if IsServiceInstalled('wiresock-client-service') then
    begin
      if not StopAndRemoveService('wiresock-client-service') then
      begin
        Log('Uyarı: WireSock hizmeti kaldırılamadı');
      end
      else
      begin
        Log('WireSock hizmeti başarıyla kaldırıldı');
      end;
    end
    else
    begin
      Log('WireSock hizmeti zaten yüklü değil');
    end;

    // ByeDPI hizmetini kaldır
    if IsServiceInstalled('byedpi') then
    begin
      if not StopAndRemoveService('byedpi') then
      begin
        Log('Uyarı: ByeDPI hizmeti kaldırılamadı');
      end
      else
      begin
        Log('ByeDPI hizmeti başarıyla kaldırıldı');
      end;
    end
    else
    begin
      Log('ByeDPI hizmeti zaten yüklü değil');
    end;

    // ProxiFyre hizmetini kaldır
    if IsServiceInstalled('ProxiFyreService') then
    begin
      if not StopAndRemoveService('ProxiFyreService') then
      begin
        Log('Uyarı: ProxiFyre hizmeti kaldırılamadı');
      end
      else
      begin
        Log('ProxiFyre hizmeti başarıyla kaldırıldı');
      end;
    end
    else
    begin
      Log('ProxiFyre hizmeti zaten yüklü değil');
    end;

    // WinWS1 hizmetini kaldır
    if IsServiceInstalled('winws1') then
    begin
      if not StopAndRemoveService('winws1') then
      begin
        Log('Uyarı: WinWS1 hizmeti kaldırılamadı');
      end
      else
      begin
        Log('WinWS1 hizmeti başarıyla kaldırıldı');
      end;
    end
    else
    begin
      Log('WinWS1 hizmeti zaten yüklü değil');
    end;

    // WinWS2 hizmetini kaldır
    if IsServiceInstalled('winws2') then
    begin
      if not StopAndRemoveService('winws2') then
      begin
        Log('Uyarı: WinWS2 hizmeti kaldırılamadı');
      end
      else
      begin
        Log('WinWS2 hizmeti başarıyla kaldırıldı');
      end;
    end
    else
    begin
      Log('WinWS2 hizmeti zaten yüklü değil');
    end;

    // Zapret hizmetini kaldır
    if IsServiceInstalled('zapret') then
    begin
      if not StopAndRemoveService('zapret') then
      begin
        Log('Uyarı: Zapret hizmeti kaldırılamadı');
      end
      else
      begin
        Log('Zapret hizmeti başarıyla kaldırıldı');
      end;
    end
    else
    begin
      Log('Zapret hizmeti zaten yüklü değil');
    end;

    // GoodbyeDPI hizmetini kaldır
    if IsServiceInstalled('GoodbyeDPI') then
    begin
      if not StopAndRemoveService('GoodbyeDPI') then
      begin
        Log('Uyarı: GoodbyeDPI hizmeti kaldırılamadı');
      end
      else
      begin
        Log('GoodbyeDPI hizmeti başarıyla kaldırıldı');
      end;
    end
    else
    begin
      Log('GoodbyeDPI hizmeti zaten yüklü değil');
    end;

    // WinDivert hizmetini kaldır (en son)
    if IsServiceInstalled('WinDivert') then
    begin
      if not StopAndRemoveService('WinDivert') then
      begin
        Log('Uyarı: WinDivert hizmeti kaldırılamadı');
      end
      else
      begin
        Log('WinDivert hizmeti başarıyla kaldırıldı');
      end;
    end
    else
    begin
      Log('WinDivert hizmeti zaten yüklü değil');
    end;

    Log('1. Aşama tamamlandı - İlerleme: %20');
    
    // 2. Aşama: %localappdata%/SplitWire-Turkey klasörünü sil (İlerleme: %40)
    Log('=== 2. AŞAMA: SplitWire-Turkey klasörü siliniyor ===');
    
    LocalAppDataPath := ExpandConstant('{localappdata}');
    SplitWireTurkeyPath := LocalAppDataPath + '\SplitWire-Turkey';
    
    if DirExists(SplitWireTurkeyPath) then
    begin
      if DelTree(SplitWireTurkeyPath, True, True, True) then
      begin
        Log('SplitWire-Turkey klasörü başarıyla silindi');
      end
      else
      begin
        Log('Uyarı: SplitWire-Turkey klasörü silinemedi');
      end;
    end
    else
    begin
      Log('SplitWire-Turkey klasörü bulunamadı');
    end;

    Log('2. Aşama tamamlandı - İlerleme: %40');
    
    // 3. Aşama: reset_dns_settings.bat çalıştır (İlerleme: %60)
    Log('=== 3. AŞAMA: DNS ayarları sıfırlanıyor ===');
    
    ResetDNSScriptPath := ExpandConstant('{app}') + '\res\reset_dns_settings.bat';
    if FileExists(ResetDNSScriptPath) then
    begin
      try
        if Exec(ResetDNSScriptPath, '', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        begin
          Log('DNS ayarları başarıyla sıfırlandı');
        end
        else
        begin
          Log('Uyarı: DNS sıfırlama scripti çalıştırılamadı');
        end;
      except
        Log('HATA: DNS sıfırlama scripti çalıştırma sırasında exception oluştu');
      end;
    end
    else
    begin
      Log('Uyarı: DNS sıfırlama scripti bulunamadı');
    end;

    Log('3. Aşama tamamlandı - İlerleme: %60');
    
    // 4. Aşama: WireSock 2.4.16.1'i sessiz kaldır (İlerleme: %80)
    Log('=== 4. AŞAMA: WireSock 2.4.16.1 kaldırılıyor ===');
    
    WireSockUninstallPath := ExpandConstant('{app}') + '\res\wiresock-secure-connect-x64-2.4.16.1.exe';
    
    if FileExists(WireSockUninstallPath) then
    begin
      Log('WireSock 2.4.16.1 kaldırılıyor...');
      try
        if Exec(WireSockUninstallPath, '/uninstall /S', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        begin
          Log('WireSock 2.4.16.1 başarıyla kaldırıldı');
        end
        else
        begin
          Log('Uyarı: WireSock 2.4.16.1 kaldırılamadı');
        end;
      except
        Log('HATA: WireSock 2.4.16.1 kaldırma işlemi sırasında exception oluştu');
      end;
    end
    else
    begin
      Log('WireSock 2.4.16.1 kaldırma dosyası bulunamadı');
    end;

    Log('4. Aşama tamamlandı - İlerleme: %80');
    
    // 5. Aşama: WireSock 1.4.7.1'i sessiz kaldır (İlerleme: %100)
    Log('=== 5. AŞAMA: WireSock 1.4.7.1 kaldırılıyor ===');
    
    WireSockMSIPath := ExpandConstant('{app}') + '\res\wiresock-vpn-client-x64-1.4.7.1.msi';
    
    if FileExists(WireSockMSIPath) then
    begin
      Log('WireSock 1.4.7.1 kaldırılıyor...');
      try
        if Exec('msiexec', '/x "' + WireSockMSIPath + '" /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        begin
          Log('WireSock 1.4.7.1 başarıyla kaldırıldı');
        end
        else
        begin
          Log('Uyarı: WireSock 1.4.7.1 kaldırılamadı');
        end;
      except
        Log('HATA: WireSock 1.4.7.1 kaldırma işlemi sırasında exception oluştu');
      end;
    end
    else
    begin
      Log('WireSock 1.4.7.1 MSI dosyası bulunamadı');
    end;

    Log('5. Aşama tamamlandı - İlerleme: %100');
    Log('Tüm kaldırma aşamaları tamamlandı. SplitWire-Turkey kaldırılıyor...');
  end;
end;

 