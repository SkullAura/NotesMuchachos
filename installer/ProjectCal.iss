#define AppName "DayScribe"
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
AppPublisher=DayScribe
DefaultDirName={autopf}\DayScribe
DefaultGroupName=DayScribe
DisableDirPage=no
DisableProgramGroupPage=yes
AlwaysShowDirOnReadyPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=DayScribeSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#SourceDir}\DayScribe.ico
UninstallDisplayIcon={app}\DayScribe.ico
UninstallFilesDir={app}
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DayScribe"; Filename: "{app}\ProjectCal.Launcher.exe"; WorkingDir: "{app}"; IconFilename: "{app}\DayScribe.ico"
Name: "{group}\Uninstall DayScribe"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DayScribe"; Filename: "{app}\ProjectCal.Launcher.exe"; WorkingDir: "{app}"; IconFilename: "{app}\DayScribe.ico"; Tasks: desktopicon
Name: "{app}\Uninstall DayScribe"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\ProjectCal.Launcher.exe"; Description: "Launch DayScribe"; Flags: nowait postinstall skipifsilent
