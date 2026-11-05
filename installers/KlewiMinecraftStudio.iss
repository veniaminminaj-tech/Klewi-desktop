#ifndef SourceDir
  #define SourceDir "..\artifacts\KlewiDesktop"
#endif

[Setup]
AppId={{7E2D3F4A-9B81-4D5E-8C62-1F6A8A4C1201}
AppName=Klewi Minecraft Studio
AppVersion=1.0.0
AppPublisher=Klewi
DefaultDirName={autopf}\Klewi Minecraft Studio
DefaultGroupName=Klewi Minecraft Studio
OutputDir=..\artifacts\installers
OutputBaseFilename=Klewi-Minecraft-Studio-Setup
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Klewi Minecraft Studio

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Klewi Minecraft Studio"; Filename: "{app}\KlewiDesktop.exe"; Parameters: "--module=minecraft"
Name: "{autodesktop}\Klewi Minecraft Studio"; Filename: "{app}\KlewiDesktop.exe"; Parameters: "--module=minecraft"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\KlewiDesktop.exe"; Parameters: "--module=minecraft"; Description: "Launch Klewi Minecraft Studio"; Flags: postinstall nowait skipifsilent
