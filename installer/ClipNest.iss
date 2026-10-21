; Inno Setup script for the ClipNest .exe installer (per-user, no admin rights).
; Built by CI:  iscc /DAppVersion=0.0.1 /DArch=x64 /DSourceDir=..\publish\win-x64 /DOutputDir=..\dist installer\ClipNest.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-" + Arch
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#if Arch == "arm64"
  #define ArchId "arm64"
#else
  #define ArchId "x64compatible"
#endif

[Setup]
AppId={{D1E53A75-D973-492F-AED1-187265E8BC9C}
AppName=ClipNest
AppVersion={#AppVersion}
AppVerName=ClipNest {#AppVersion}
AppPublisher=ClipNest contributors
AppPublisherURL=https://github.com/tsix2019/ClipNest
AppSupportURL=https://github.com/tsix2019/ClipNest/issues
DefaultDirName={localappdata}\Programs\ClipNest
DefaultGroupName=ClipNest
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed={#ArchId}
ArchitecturesInstallIn64BitMode={#ArchId}
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=ClipNest-Setup-win-{#Arch}
SetupIconFile=..\src\ClipNest.App\Assets\ClipNest.ico
UninstallDisplayIcon={app}\ClipNest.exe
UninstallDisplayName=ClipNest
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
#ifexist "ChineseSimplified.isl"
Name: "zh"; MessagesFile: "ChineseSimplified.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\ClipNest"; Filename: "{app}\ClipNest.exe"
Name: "{autodesktop}\ClipNest"; Filename: "{app}\ClipNest.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ClipNest.exe"; Description: "{cm:LaunchProgram,ClipNest}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im ClipNest.exe"; Flags: runhidden; RunOnceId: "StopClipNest"

[Registry]
; "Launch at startup" is written by the app itself; remove it together with the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "ClipNest"; Flags: uninsdeletevalue dontcreatekey
