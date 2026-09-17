#define MyAppName "TransPoli • Computador de Bordo"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Felipe Andrade"
#define MyAppExeName "TransPoli.exe"

[Setup]
AppId={{7B1A4D9E-3E1D-4A76-9B50-123456789026}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (C) 2026 Felipe Andrade
VersionInfoCompany=Felipe Andrade
VersionInfoCopyright=Copyright (C) 2026 Felipe Andrade
DefaultDirName={autopf}\TransPoli
DefaultGroupName=TransPoli
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=TransPoli-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName={#MyAppName}

[Files]
Source: "..\artifacts\TransPoli-package\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\TransPoli • Computador de Bordo"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\TransPoli • Computador de Bordo"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir TransPoli • Computador de Bordo"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\TransPoli"
