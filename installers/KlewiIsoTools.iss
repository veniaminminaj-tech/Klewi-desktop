#ifndef SourceDir
  #define SourceDir "..\artifacts\KlewiDesktop"
#endif

[Setup]
AppId={{9B6E5A31-2F47-4B80-A8D1-5C3E7F920402}
AppName=Klewi ISO Tools
AppVersion=1.0.0
AppPublisher=Klewi
DefaultDirName={autopf}\Klewi ISO Tools
DefaultGroupName=Klewi ISO Tools
OutputDir=..\artifacts\installers
OutputBaseFilename=Klewi-ISO-Tools-Setup
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Klewi ISO Tools

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Klewi ISO Tools"; Filename: "{app}\KlewiDesktop.exe"; Parameters: "--module=iso"
Name: "{autodesktop}\Klewi ISO Tools"; Filename: "{app}\KlewiDesktop.exe"; Parameters: "--module=iso"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\KlewiDesktop.exe"; Parameters: "--module=iso"; Description: "Launch Klewi ISO Tools"; Flags: postinstall nowait skipifsilent
