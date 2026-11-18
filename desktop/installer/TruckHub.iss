; ============================================================
;  TruckHub - Computador de Bordo
;  Criado por Felipe Andrade
; ============================================================

#define MyAppName "TruckHub"
#define MyAppFullName "TruckHub - Computador de Bordo"
; A versao pode vir do workflow: ISCC /DMyAppVersion=1.2.3
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Felipe Andrade"
#define MyAppExeName "TruckHub.exe"
#define MyAppCopyright "Copyright (C) 2026 Felipe Andrade"

[Setup]
; AppId novo: identidade propria do TruckHub, sem heranca do instalador antigo.
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
DefaultDirName={localappdata}\TruckHub
UsePreviousAppDir=no
DefaultGroupName=TruckHub
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=TruckHub-Setup
SetupIconFile=..\..\assets\brand\icons\truckhub.ico
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
Source: "..\artifacts\TruckHub-package\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "install-ets2-plugin.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall

[InstallDelete]
; Limpeza da instalacao antiga (nome TransPoli): atalhos e binarios. Os dados (banco, login) ficam e
; sao copiados para a pasta TruckHub na primeira abertura do app.
Type: files; Name: "{autoprograms}\TransPoli.lnk"
Type: files; Name: "{autodesktop}\TransPoli.lnk"
Type: files; Name: "{localappdata}\TransPoli\*.exe"
Type: files; Name: "{localappdata}\TransPoli\*.dll"
Type: filesandordirs; Name: "{localappdata}\TransPoli\ETS2Plugin"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Area de Trabalho"; GroupDescription: "Atalhos:"

[Icons]
Name: "{autoprograms}\TruckHub"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "TruckHub - Computador de Bordo (por Felipe Andrade)"
Name: "{autodesktop}\TruckHub"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon; Comment: "TruckHub - Computador de Bordo (por Felipe Andrade)"

[Run]
; Instalacao e atualizacao usam o mesmo helper. Se o ETS2 ainda nao existir, o
; TruckHub continua instalado e a copia empacotada permanece em {app}\ETS2Plugin.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$p=Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File """"{tmp}\install-ets2-plugin.ps1"""" -PluginSource """"{app}\ETS2Plugin\TruckHub.VehicleControl.dll"""" -AutoStartPluginSource """"{app}\ETS2Plugin\TruckHub.AutoStart.dll""""' -Verb RunAs -Wait -PassThru; exit $p.ExitCode"""; Flags: runhidden waituntilterminated runasoriginaluser
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir o TruckHub"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\TruckHub"
