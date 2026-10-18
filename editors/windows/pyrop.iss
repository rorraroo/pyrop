#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Executable
  #define Executable "..\..\publish\Pyrop.exe"
#endif
#ifndef Vsix
  #define Vsix "..\vscode\pyrop-" + AppVersion + ".vsix"
#endif
#ifndef ExtensionId
  #define ExtensionId "radardev.pyrop"
#endif
#ifndef OutputFolder
  #define OutputFolder "..\..\publish"
#endif

[Setup]
AppId={{9DA2DB72-0F06-4A53-86F7-B38A3A0A36EB}
AppName=Pyrop
AppVersion={#AppVersion}
AppVerName=Pyrop {#AppVersion}
AppPublisher=radardev
DefaultDirName={autopf}\Pyrop
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
ChangesAssociations=yes
SetupIconFile=pyrop.ico
UninstallDisplayIcon={app}\pyrop.ico
UninstallDisplayName=Pyrop {#AppVersion}
WizardStyle=modern
OutputDir={#OutputFolder}
OutputBaseFilename=pyrop-setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes

[Tasks]
Name: vscode; Description: "Install the Pyrop extension for Visual Studio Code"; Check: VSCodeFound

[Files]
Source: "{#Executable}"; DestDir: "{app}"; DestName: "pyrop.exe"; Flags: ignoreversion
Source: "pyrop.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Vsix}"; DestDir: "{tmp}"; DestName: "pyrop.vsix"; Tasks: vscode

[Registry]
Root: HKCU; Subkey: "Software\Classes\.prp"; ValueType: string; ValueData: "Pyrop.Script"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\.prp"; ValueType: string; ValueName: "PerceivedType"; ValueData: "text"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.prp"; ValueType: string; ValueName: "Content Type"; ValueData: "text/plain"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\Pyrop.Script"; ValueType: string; ValueData: "Pyrop Script"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Pyrop.Script\DefaultIcon"; ValueType: string; ValueData: """{app}\pyrop.ico"""
Root: HKCU; Subkey: "Software\Classes\Pyrop.Script\shell"; ValueType: string; ValueData: "open"
Root: HKCU; Subkey: "Software\Classes\Pyrop.Script\shell\open\command"; ValueType: string; ValueData: """{code:ScriptEditor}"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\Pyrop.Script\shell\run"; ValueType: string; ValueData: "Run with Pyrop"
Root: HKCU; Subkey: "Software\Classes\Pyrop.Script\shell\run"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\pyrop.ico"""
Root: HKCU; Subkey: "Software\Classes\Pyrop.Script\shell\run\command"; ValueType: string; ValueData: """{cmd}"" /c """"{app}\pyrop.exe"" ""%1"" & echo. & pause"""

[Run]
Filename: "{cmd}"; Parameters: "/c """"{code:VSCodeCli}"" --install-extension ""{tmp}\pyrop.vsix"" --force"""; StatusMsg: "Installing the Visual Studio Code extension..."; Flags: runhidden waituntilterminated; Tasks: vscode

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c """"{code:VSCodeCli}"" --uninstall-extension {#ExtensionId}"""; Flags: runhidden waituntilterminated; Tasks: vscode; RunOnceId: "UninstallVSCodeExtension"

[Messages]
FinishedLabel=Setup has installed Pyrop on your computer. Open a new command prompt and type pyrop to start it, or pyrop script.prp to run a script.

[Code]
const
  EnvironmentKey = 'Environment';

function VSCodeFolder: string;
begin
  Result := ExpandConstant('{localappdata}\Programs\Microsoft VS Code');
  if FileExists(Result + '\Code.exe') then Exit;
  Result := ExpandConstant('{commonpf64}\Microsoft VS Code');
  if FileExists(Result + '\Code.exe') then Exit;
  Result := '';
end;

function VSCodeFound: Boolean;
begin
  Result := VSCodeFolder <> '';
end;

function VSCodeCli(Param: string): string;
begin
  Result := VSCodeFolder + '\bin\code.cmd';
end;

function ScriptEditor(Param: string): string;
begin
  if VSCodeFound then
    Result := VSCodeFolder + '\Code.exe'
  else
    Result := ExpandConstant('{sys}\notepad.exe');
end;

function PathPosition(Paths, Dir: string): Integer;
begin
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Paths) + ';');
end;

procedure AddToPath(Dir: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
    Paths := '';
  if PathPosition(Paths, Dir) > 0 then
    Exit;
  if (Paths <> '') and (Paths[Length(Paths)] <> ';') then
    Paths := Paths + ';';
  RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths + Dir);
end;

procedure RemoveFromPath(Dir: string);
var
  Paths: string;
  Position: Integer;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
    Exit;
  Position := PathPosition(Paths, Dir);
  if Position = 0 then
    Exit;
  Paths := ';' + Paths + ';';
  Delete(Paths, Position, Length(Dir) + 1);
  RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Copy(Paths, 2, Length(Paths) - 2));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    AddToPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromPath(ExpandConstant('{app}'));
end;
