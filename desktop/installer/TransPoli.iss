; ============================================================
;  TransPoli - Computador de Bordo
;  Criado por Felipe Andrade
; ============================================================

#define MyAppName "TransPoli"
#define MyAppFullName "TransPoli - Computador de Bordo"
; A versao pode vir do workflow: ISCC /DMyAppVersion=1.2.3
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Felipe Andrade"
#define MyAppExeName "TransPoli.exe"
#define MyAppCopyright "Copyright (C) 2026 Felipe Andrade"

[Setup]
; AppId novo: identidade propria do TransPoli, sem heranca do instalador antigo.
AppId={{5F2B9C41-8A16-4D7E-B3C9-27E4A6D15B80}
AppName={#MyAppName}
AppVerName={#MyAppFullName} {#MyAppVersion}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright={#MyAppCopyright}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppFullName}
VersionInfoDescription={#MyAppFullName}
VersionInfoCopyright={#MyAppCopyright}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={localappdata}\TransPoli
DefaultGroupName=TransPoli
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=TransPoli-Setup
SetupIconFile=..\..\assets\brand\icons\transpoli.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppFullName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
brazilianportuguese.WelcomeLabel2=Este assistente vai instalar o {#MyAppFullName} no seu computador.%n%nDesenvolvido por Felipe Andrade.
brazilianportuguese.FinishedLabel=A instalacao do {#MyAppFullName} foi concluida.%n%nCriado por Felipe Andrade. Bom trabalho e boa viagem!

[Files]
Source: "..\artifacts\TransPoli-package\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Area de Trabalho"; GroupDescription: "Atalhos:"

[Icons]
Name: "{autoprograms}\TransPoli"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "TransPoli - Computador de Bordo (por Felipe Andrade)"
Name: "{autodesktop}\TransPoli"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon; Comment: "TransPoli - Computador de Bordo (por Felipe Andrade)"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir o TransPoli"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\TransPoli"
