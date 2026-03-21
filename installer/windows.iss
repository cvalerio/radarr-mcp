#define AppName "RadarrMcp"
#define AppExe "RadarrMcp.exe"
#define AppURL "https://github.com/cvalerio/radarr-mcp"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}/releases
DefaultDirName={localappdata}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
OutputBaseFilename={#AppName}-win-x64-setup
OutputDir=installer-out
Compression=lzma
SolidCompression=yes
; User-level install — no admin rights required
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

[Files]
; Main binary
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

; Config file — only placed on first install, never overwritten on upgrade
Source: "{#SourceDir}\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Registry]
; Add install dir to user PATH
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
  ValueData: "{olddata};{app}"; Check: PathEntryMissing('{app}')

[Messages]
FinishedLabel=Setup has installed [name] and added it to your PATH.%n%nOpen a new terminal and configure your Radarr connection in:%n%n    {app}\appsettings.json%n%nor via environment variables RADARR__URL and RADARR__API_KEY.

[Code]

{ Returns true if Dir is not already in the current user PATH }
function PathEntryMissing(Dir: string): Boolean;
var
  Path: string;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Path) then
    Path := '';
  Result := Pos(';' + Uppercase(Dir) + ';',
                ';' + Uppercase(Path) + ';') = 0;
end;

{ Removes all occurrences of Dir from the user PATH using Pos/Delete/Copy }
function RemoveFromPath(Dir: string): string;
var
  Path, NormPath, UpperSep: string;
  P, Len: Integer;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Path) then
  begin
    Result := '';
    Exit;
  end;

  { Wrap in semicolons so every entry has the pattern ;entry; }
  NormPath := ';' + Path + ';';
  UpperSep := ';' + Uppercase(Dir) + ';';
  Len := Length(UpperSep) - 1; { characters to delete per hit (keep one semicolon) }

  P := Pos(UpperSep, Uppercase(NormPath));
  while P > 0 do
  begin
    Delete(NormPath, P, Len);
    P := Pos(UpperSep, Uppercase(NormPath));
  end;

  { Strip the leading semicolon we added; trailing one was consumed }
  Result := Copy(NormPath, 2, Length(NormPath) - 1);
  { Remove any trailing semicolons }
  while (Length(Result) > 0) and (Result[Length(Result)] = ';') do
    Delete(Result, Length(Result), 1);
end;

{ Remove install dir from user PATH when uninstalling }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Path: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Path := RemoveFromPath(ExpandConstant('{app}'));
    RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Path);
  end;
end;
