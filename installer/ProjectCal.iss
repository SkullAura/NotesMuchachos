#define AppName "ProjectCal"
#define AppVersion "1.0.0"
#ifndef SourceDir
#define SourceDir "..\artifacts\installer\input"
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts\installer\output"
#endif

[Setup]
AppId={{6B4FEF6F-835D-4B0F-A1F7-2F59F20D4117}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ProjectCal
DefaultDirName={autopf}\ProjectCal
DefaultGroupName=ProjectCal
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=ProjectCalSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#SourceDir}\ProjectCal.ico
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ProjectCal"; Filename: "{app}\ProjectCal.Launcher.exe"; WorkingDir: "{app}"; IconFilename: "{app}\ProjectCal.ico"
Name: "{autodesktop}\ProjectCal"; Filename: "{app}\ProjectCal.Launcher.exe"; WorkingDir: "{app}"; IconFilename: "{app}\ProjectCal.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\ProjectCal.Launcher.exe"; Description: "Launch ProjectCal"; Flags: nowait postinstall skipifsilent
