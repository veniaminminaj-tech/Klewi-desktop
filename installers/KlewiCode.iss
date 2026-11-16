#define AppName "Klewi Code"
#define AppVersion "0.2.0"
#define AppPublisher "Klewi"
#define AppExeName "KlewiDesktop.exe"

[Setup]
AppId={{A7B5C2D1-8F41-4C76-9D2E-4E71C0DE2026}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Klewi Code
DefaultGroupName=Klewi Code
OutputDir=..\artifacts\installer
OutputBaseFilename=Klewi-Code-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExeName}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "{#ModuleDir}\*.kcmodule"; DestDir: "{userdocs}\Klewi\ModuleMarket"; Flags: ignoreversion

[Dirs]
Name: "{userdocs}\Klewi\ModuleMarket"
Name: "{userdocs}\Klewi\Modules"

[Icons]
Name: "{group}\Klewi Code"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Klewi Code"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Klewi Code"; Flags: postinstall nowait skipifsilent
