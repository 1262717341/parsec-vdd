; Parsec 虚拟显示器（中文版）安装脚本
; 版本号由 build.ps1 通过 /DMyAppVersion 传入

#define MyAppName "Parsec 虚拟显示器"
#define MyAppNameEn "ParsecVDisplay"
#define MyAppPublisher "nomi-san（原项目） / 中文汉化版"
#define MyAppURL "https://github.com/nomi-san/parsec-vdd"
#define MyAppExeName "ParsecVDisplay.exe"
#define MyAppCopyright "© 2024 Nguyen Duy. 中文汉化与功能修复版。"

#ifndef MyAppVersion
  #define MyAppVersion "0.46.0"
#endif

#ifndef OutDir
  #define OutDir "."
#endif

#define _Major
#define _Minor
#define _Rev
#define _Build
#define VddVersion GetVersionComponents(".\parsec-vdd-setup.exe", _Major, _Minor, _Rev, _Build), Str(_Major) + "." + Str(_Minor)

[Setup]
AppId={{D2005B5A-A8C4-4B77-807F-155132973D5D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoCopyright={#MyAppCopyright}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={commonpf64}\{#MyAppNameEn}
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
LicenseFile=.\license-zh.txt
PrivilegesRequired=admin
OutputDir={#OutDir}
OutputBaseFilename={#MyAppNameEn}-v{#MyAppVersion}-setup
SetupIconFile=..\app\Resources\icon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=classic
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: ".\ChineseSimplified.isl"

[Tasks]
Name: "add_startup"; Description: "开机自动启动（推荐）"; GroupDescription: "启动选项："; Flags: checkedonce
Name: "install_vdd"; Description: "安装 Parsec 虚拟显示驱动 v{#VddVersion}"; GroupDescription: "驱动选项："; Flags: checkedonce

[Dirs]
Name: "{app}"; Permissions: everyone-full
Name: "{app}\driver-setup"; Permissions: everyone-full

[Files]
Source: "..\bin\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\{#MyAppNameEn}Svc.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\parsec-vdd-setup.exe"; DestDir: "{app}\driver"; Flags: ignoreversion
Source: ".\sunshine-dependency.ps1"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{sys}\sc.exe"; Parameters: "stop ParsecVDisplaySvc"; Flags: runhidden; StatusMsg: "正在停止旧版后台服务 …"
Filename: "{sys}\sc.exe"; Parameters: "create ParsecVDisplaySvc binPath= ""{app}\{#MyAppNameEn}Svc.exe"" start= auto DisplayName= ""{#MyAppName}后台服务"""; Flags: runhidden waituntilterminated; StatusMsg: "正在注册虚拟显示器后台服务 …"
Filename: "{sys}\sc.exe"; Parameters: "description ParsecVDisplaySvc ""开机即以系统服务启动虚拟显示器，让串流软件在登录前就能看到显示器"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start ParsecVDisplaySvc"; Flags: runhidden; StatusMsg: "正在启动后台服务 …"
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser
Filename: "{app}\driver\parsec-vdd-setup.exe"; Parameters: "/S"; Flags: runascurrentuser; Tasks: install_vdd

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ParsecVDisplaySvc"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete ParsecVDisplaySvc"; Flags: runhidden

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppNameEn}"; ValueData: """{app}\{#MyAppExeName}"" -silent"; Flags: uninsdeletevalue; Tasks: add_startup
Root: HKCU; Subkey: "SOFTWARE\{#MyAppNameEn}"; ValueType: dword; ValueName: "RunOnStartup"; ValueData: 1; Flags: uninsdeletevalue; Tasks: add_startup
Root: HKLM; Subkey: "SOFTWARE\{#MyAppNameEn}"; ValueType: dword; ValueName: "DisplayCount"; ValueData: 1; Permissions: users-full; Flags: createvalueifdoesntexist uninsdeletekey

[Code]
// 安装前先结束正在运行的旧版本与后台服务，避免文件占用
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop ParsecVDisplaySvc', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Sleep(1500);

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppNameEn}Svc.exe /F', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Result := True;
end;

// 让 Sunshine 等串流服务“依赖”本服务：开机时 Windows 会先启动本服务，
// 而本服务的 OnStart 会一直等到虚拟显示器在系统里出现才返回，
// 于是串流软件启动时一定能看到虚拟显示器。
procedure RunSunshineDependencyScript(Mode: String);
var
  ResultCode: Integer;
  ScriptPath: String;
  Params: String;
begin
  ScriptPath := ExpandConstant('{tmp}\sunshine-dependency.ps1');

  if not FileExists(ScriptPath) then
  begin
    Log('依赖脚本不在临时目录，跳过');
    Exit;
  end;

  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" -Mode ' + Mode;

  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '',
     SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('Sunshine 依赖脚本执行完成，返回码 ' + IntToStr(ResultCode))
  else
    Log('Sunshine 依赖脚本无法启动');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    ExtractTemporaryFile('sunshine-dependency.ps1');
    RunSunshineDependencyScript('Add');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    ExtractTemporaryFile('sunshine-dependency.ps1');
    RunSunshineDependencyScript('Remove');
  end;
end;
