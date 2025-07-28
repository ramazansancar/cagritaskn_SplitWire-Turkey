[Setup]
AppName=SplitWire-Turkey
AppVersion=1.0
DefaultDirName={pf}\SplitWire-Turkey
DefaultGroupName=SplitWire-Turkey
OutputDir=.
OutputBaseFilename=SplitWire-Turkey-Setup
Compression=lzma
SolidCompression=yes
DisableProgramGroupPage=yes
SetupIconFile=res\splitwire.ico

[Files]
; Ana program
Source: "SplitWire-Turkey.exe"; DestDir: "{app}"; Flags: ignoreversion

; Resource dosyaları
Source: "res\loading.gif"; DestDir: "{app}\res"; Flags: ignoreversion
Source: "res\splitwire.ico"; DestDir: "{app}\res"; Flags: ignoreversion
Source: "res\splitwire-logo-128.png"; DestDir: "{app}\res"; Flags: ignoreversion
Source: "res\splitwire-logo-1024.png"; DestDir: "{app}\res"; Flags: ignoreversion
Source: "res\splitwireturkeytext.png"; DestDir: "{app}\res"; Flags: ignoreversion
Source: "res\wgcf.exe"; DestDir: "{app}\res"; Flags: ignoreversion

[Icons]
; Masaüstü kısayolu
Name: "{commondesktop}\SplitWire-Turkey"; Filename: "{app}\SplitWire-Turkey.exe"; WorkingDir: "{app}"

; Başlat Menüsü kısayolu
Name: "{group}\SplitWire-Turkey"; Filename: "{app}\SplitWire-Turkey.exe"; WorkingDir: "{app}"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\res"
