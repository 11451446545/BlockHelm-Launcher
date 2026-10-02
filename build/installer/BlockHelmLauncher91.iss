#define LauncherVersion "26A17091"
#define Windows7Source "..\..\publish\26A17091-win7"
#define ModernSource "..\..\publish\26A17091-modern"
#define PrerequisiteSource "..\..\publish\prerequisites"

[Setup]
#ifdef BHL_INSTALLER_VERIFY
AppId=BlockHelm91InstallerVerification
Uninstallable=no
CreateUninstallRegKey=no
UsePreviousAppDir=no
#else
AppId={{BC7AA7C3-FA57-4D69-902E-9D71E4B31D72}
#endif
AppName=BlockHelm Launcher
AppVersion={code:GetLauncherVersion}
AppVerName=BlockHelm Launcher {code:GetLauncherVersion}
AppPublisher=BlockHelm Launcher
; The original launcher stores settings and games beside its executable.
; A new installation must therefore default to a user-writable directory.
DefaultDirName={localappdata}\Programs\BlockHelm Launcher
DefaultGroupName=BlockHelm Launcher
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1
#ifdef BHL_INSTALLER_VERIFY
PrivilegesRequired=lowest
Compression=none
SolidCompression=no
#else
PrivilegesRequired=admin
Compression=lzma2/ultra64
SolidCompression=yes
#endif
WizardStyle=classic
SetupIconFile=..\..\Launcher.App\Assets\Icons\app_icon_concept.ico
UninstallDisplayIcon={app}\BlockHelm_Launcher_x64.exe
OutputDir=..\..\publish\installer
OutputBaseFilename=BlockHelm-Launcher-{#LauncherVersion}-Universal-Setup-x64
VersionInfoVersion=0.9.16.0
VersionInfoProductName=BlockHelm Launcher
VersionInfoDescription=BlockHelm Launcher universal Windows installer
#ifdef BHL_INSTALLER_VERIFY
CloseApplications=no
#else
CloseApplications=yes
#endif
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#Windows7Source}\BlockHelm_Launcher_x64.exe"; DestDir: "{app}"; Flags: ignoreversion; Check: UseCompatibilityBuild
Source: "{#Windows7Source}\licenses\*.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion; Check: UseCompatibilityBuild
Source: "{#ModernSource}\BlockHelm_Launcher_x64.exe"; DestDir: "{app}"; Flags: ignoreversion; Check: not UseCompatibilityBuild
Source: "{#ModernSource}\licenses\*.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion; Check: not UseCompatibilityBuild
Source: "{#PrerequisiteSource}\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: UseCompatibilityBuild
Source: "{#PrerequisiteSource}\Windows6.1-KB3063858-x64.msu"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: IsWindows7

[Icons]
#ifndef BHL_INSTALLER_VERIFY
Name: "{autoprograms}\BlockHelm Launcher"; Filename: "{app}\BlockHelm_Launcher_x64.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\BlockHelm Launcher"; Filename: "{app}\BlockHelm_Launcher_x64.exe"; WorkingDir: "{app}"; Tasks: desktopicon
#endif

[Registry]
#ifndef BHL_INSTALLER_VERIFY
; Windows 7 disables TLS 1.2 by default. Enable the client protocol when no
; explicit setting exists; preserve administrator policy and all other protocols.
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client"; ValueType: dword; ValueName: "Enabled"; ValueData: "1"; Flags: createvalueifdoesntexist; Check: IsWindows7
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client"; ValueType: dword; ValueName: "DisabledByDefault"; ValueData: "0"; Flags: createvalueifdoesntexist; Check: IsWindows7
#endif

[Run]
#ifndef BHL_INSTALLER_VERIFY
Filename: "{app}\BlockHelm_Launcher_x64.exe"; Description: "Launch BlockHelm Launcher"; Flags: nowait postinstall skipifsilent; Check: CanLaunchAfterInstall
#endif

[Code]
var
  PrerequisiteRestartRequired: Boolean;

function RequiresCompatibility(Major, Minor: Cardinal): Boolean;
begin
  Result := (Major = 6) and (Minor >= 1) and (Minor <= 3);
end;

function UseCompatibilityBuild: Boolean;
var
  Version: TWindowsVersion;
begin
#ifdef BHL_INSTALLER_VERIFY
#ifdef BHL_VERIFY_LEGACY
  Result := RequiresCompatibility(6, 1);
  exit;
#endif
#endif
  GetWindowsVersionEx(Version);
  Result := RequiresCompatibility(Version.Major, Version.Minor);
end;

function GetLauncherVersion(Param: String): String;
begin
  Result := '{#LauncherVersion}';
  if UseCompatibilityBuild then
    Result := Result + '-Compatible';
end;

function IsWindows7: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major = 6) and (Version.Minor = 1);
end;

function CanLaunchAfterInstall: Boolean;
begin
  Result := not PrerequisiteRestartRequired;
end;

function NeedsRestart: Boolean;
begin
  Result := PrerequisiteRestartRequired;
end;

procedure InstallWindows7PlatformUpdate;
var
  ResultCode: Integer;
  UpdatePath: String;
begin
  if not IsWindows7 then
    exit;

  UpdatePath := ExpandConstant('{tmp}\Windows6.1-KB3063858-x64.msu');
  if not Exec(
      ExpandConstant('{sys}\wusa.exe'),
      '"' + UpdatePath + '" /quiet /norestart',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) then
    RaiseException('Unable to start the Windows 7 platform update installer.');

  Log('Windows 7 platform update returned exit code ' + IntToStr(ResultCode) + '.');
  if ResultCode = 3010 then
    PrerequisiteRestartRequired := True
  else if (ResultCode <> 0)
      and (ResultCode <> 2359302)
      and (ResultCode <> 2359303)
      and (ResultCode <> -2145124329) then
    RaiseException('The required Windows 7 platform update could not be installed (exit code ' +
      IntToStr(ResultCode) + ').');
end;

procedure InstallVisualCppRuntime;
var
  ResultCode: Integer;
begin
  if not UseCompatibilityBuild then
    exit;

  if not Exec(
      ExpandConstant('{tmp}\vc_redist.x64.exe'),
      '/install /quiet /norestart',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) then
    RaiseException('Unable to start the Microsoft Visual C++ runtime installer.');

  Log('Microsoft Visual C++ runtime returned exit code ' + IntToStr(ResultCode) + '.');
  if ResultCode = 3010 then
    PrerequisiteRestartRequired := True
  else if (ResultCode <> 0) and (ResultCode <> 1638) then
    RaiseException('The Microsoft Visual C++ runtime could not be installed (exit code ' +
      IntToStr(ResultCode) + ').');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
#ifdef BHL_INSTALLER_VERIFY
  exit;
#endif
  if CurStep = ssPostInstall then
  begin
    InstallWindows7PlatformUpdate;
    InstallVisualCppRuntime;
  end;
end;

function InitializeSetup: Boolean;
var
  Version: TWindowsVersion;
begin
#ifdef BHL_INSTALLER_VERIFY
  if not RequiresCompatibility(6, 1) or not RequiresCompatibility(6, 2) or
     not RequiresCompatibility(6, 3) or RequiresCompatibility(10, 0) or
     RequiresCompatibility(6, 0) then
    RaiseException('Installer OS selection regression.');
#endif
  GetWindowsVersionEx(Version);
  Log('Detected OS ' + IntToStr(Version.Major) + '.' + IntToStr(Version.Minor) +
      '; selected launcher ' + GetLauncherVersion('') + '.');
  Result := True;
  if (Version.Major < 6) or ((Version.Major = 6) and (Version.Minor < 1)) then
  begin
    MsgBox('BlockHelm Launcher requires Windows 7 SP1 x64 or later.', mbError, MB_OK);
    Result := False;
  end;
end;
