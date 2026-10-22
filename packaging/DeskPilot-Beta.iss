#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef BuildId
  #define BuildId "development"
#endif
#ifndef SourceArchive
  #error SourceArchive is required
#endif
[Setup]
AppId={{BC8B171E-A8F5-47E0-9092-6105638B5192}
AppName=DeskPilot Beta
AppVersion=0.1.0-beta.3
VersionInfoVersion=0.1.0.3
#ifdef PublicBeta
AppVerName=DeskPilot Beta 0.1.0-beta.3 ({#BuildId})
#else
AppVerName=DeskPilot Beta 0.1.0-beta.3 ({#BuildId}) — private test
#endif
AppPublisher=DeskPilot contributors
DefaultDirName={localappdata}\Programs\DeskPilot Beta
DefaultGroupName=DeskPilot Beta
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=yes
UsePreviousAppDir=no
OutputDir=..\installer-output
#ifdef PublicBeta
OutputBaseFilename=DeskPilot-Beta-0.1.0-beta.3-{#BuildId}-Setup
#else
OutputBaseFilename=DeskPilot-Beta-Private-Test-{#BuildId}-Setup
#endif
SetupIconFile=..\upstream\Terminals\Source\Terminals\DeskPilot.ico
UninstallDisplayIcon={app}\DeskPilot.Preview.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
#ifdef PublicBeta
InfoBeforeFile=PUBLIC-BETA.txt
#else
InfoBeforeFile=PRIVATE-TEST.txt
#endif
LicenseFile=..\upstream\Terminals\LICENSE.md
MinVersion=10.0

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "ui-*,*.pdb,*.vshost.*,*.log"
#ifdef PublicBeta
Source: "PUBLIC-BETA.txt"; DestDir: "{app}"
#else
Source: "PRIVATE-TEST.txt"; DestDir: "{app}"
#endif
Source: "{#SourceArchive}"; DestDir: "{app}\Source"; Flags: ignoreversion

[InstallDelete]
; Retired application binary only; never delete profile folders or use wildcards.
Type: files; Name: "{app}\TabControl.dll"

[Icons]
Name: "{group}\DeskPilot Beta"; Filename: "{app}\DeskPilot.Preview.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\DeskPilot Beta"; Filename: "{app}\DeskPilot.Preview.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\Uninstall DeskPilot Beta"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\DeskPilot.Preview.exe"; Description: "Open DeskPilot Beta"; Flags: postinstall nowait skipifsilent unchecked

[Code]
function AppIsClosed(): Boolean;
var
  Stream: TFileStream;
  Exe: String;
begin
  Result := True;
  Exe := ExpandConstant('{app}\DeskPilot.Preview.exe');
  if FileExists(Exe) then
  begin
    try
      Stream := TFileStream.Create(Exe, fmOpenRead or fmShareExclusive);
      Stream.Free;
    except
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not AppIsClosed() then
    Result := 'Close DeskPilot Beta and its sessions before installing. Setup will not close connections for you.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := AppIsClosed();
  if not Result then
    MsgBox('Close DeskPilot Beta and its sessions before uninstalling. Your saved connections and VPN settings will be kept.', mbInformation, MB_OK);
end;

function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    MsgBox('DeskPilot requires Microsoft .NET Framework 4.8 or later. Install it from https://dotnet.microsoft.com/download/dotnet-framework/net48 and run Setup again.', mbInformation, MB_OK);
end;
