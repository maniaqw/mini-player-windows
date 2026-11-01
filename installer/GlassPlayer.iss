#define AppName "Glass Player"
#define AppVersion "2.2.0"

[Setup]
AppId={{16ACDA35-A3D4-4C22-BC6D-6F6800745180}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Glass Player
DefaultDirName={localappdata}\Programs\GlassPlayer
DefaultGroupName=Glass Player
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.19041
OutputDir=..\release
OutputBaseFilename=GlassPlayer-Setup-2.2.0
SetupIconFile=..\Assets\GlassPlayer.ico
UninstallDisplayIcon={app}\GlassPlayer.exe
WizardSmallImageFile=..\Assets\installer-logo.bmp
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\GlassPlayer.Desktop
VersionInfoVersion=2.2.0.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\GlassPlayer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\Glass Player"; Filename: "{app}\GlassPlayer.exe"
Name: "{autodesktop}\Glass Player"; Filename: "{app}\GlassPlayer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GlassPlayer.exe"; Parameters: "--language={code:GetPlayerLanguage}"; Description: "{cm:LaunchProgram,Glass Player}"; Flags: nowait postinstall skipifsilent

[Code]
function GetPlayerLanguage(Param: String): String;
begin
  if ActiveLanguage = 'russian' then Result := 'ru' else Result := 'en';
end;
