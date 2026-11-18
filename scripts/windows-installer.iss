#ifndef SourcePath
  #error SourcePath is required
#endif
#ifndef ReleaseVersion
  #error ReleaseVersion is required
#endif
#ifndef OutputPath
  #error OutputPath is required
#endif
#ifndef IconPath
  #error IconPath is required
#endif

[Setup]
#ifdef TestAppId
AppId={#TestAppId}
#else
AppId={{3C9E5B7A-6D24-4F81-9B3A-2E8F1D6C4A70}
#endif
AppName=TaskBaton
AppVersion={#ReleaseVersion}
AppPublisher=TaskBaton contributors
AppPublisherURL=https://github.com/jason-namehard/TaskBaton
#ifdef TestInstallDir
DefaultDirName={#TestInstallDir}
#else
DefaultDirName={localappdata}\Programs\TaskBaton
#endif
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
SetupIconFile={#IconPath}
UninstallDisplayIcon={app}\app\gui\TaskBaton.Desktop.exe
LicenseFile={#SourcePath}\LICENSE
OutputDir={#OutputPath}
#ifdef TestAppId
OutputBaseFilename=TaskBaton-v{#ReleaseVersion}-windows-x64-test-setup
#else
OutputBaseFilename=TaskBaton-v{#ReleaseVersion}-windows-x64-setup
#endif
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
#ifdef TestAppId
; A test installer must never close or replace the formal installation's processes.
CloseApplications=no
#else
CloseApplications=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "installer-languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "installer-languages\ChineseTraditional.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourcePath}\*"; DestDir: "{app}\app"; Excludes: "LICENSE,THIRD_PARTY_NOTICES.md,DISTRIBUTION_TERMS.md,README.md,GETTING_STARTED.md,CONFIGURATION_ASSISTANT.md,SESSIONS.md,licenses\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourcePath}\LICENSE"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourcePath}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourcePath}\DISTRIBUTION_TERMS.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourcePath}\README.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourcePath}\GETTING_STARTED.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourcePath}\CONFIGURATION_ASSISTANT.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourcePath}\SESSIONS.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourcePath}\licenses\*"; DestDir: "{app}\docs\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
#ifdef TestAppId
Name: "{autoprograms}\{#TestAppId}"; Filename: "{app}\app\gui\TaskBaton.Desktop.exe"; Parameters: "--home ""{app}\data"""; WorkingDir: "{app}\app\gui"
Name: "{autodesktop}\{#TestAppId}"; Filename: "{app}\app\gui\TaskBaton.Desktop.exe"; Parameters: "--home ""{app}\data"""; WorkingDir: "{app}\app\gui"; Tasks: desktopicon
#else
Name: "{autoprograms}\TaskBaton"; Filename: "{app}\app\gui\TaskBaton.Desktop.exe"; WorkingDir: "{app}\app\gui"
Name: "{autodesktop}\TaskBaton"; Filename: "{app}\app\gui\TaskBaton.Desktop.exe"; WorkingDir: "{app}\app\gui"; Tasks: desktopicon
#endif

[Run]
#ifdef TestAppId
Filename: "{app}\app\gui\TaskBaton.Desktop.exe"; Parameters: "--home ""{app}\data"""; Description: "{cm:LaunchProgram,TaskBaton}"; Flags: nowait postinstall skipifsilent
#else
Filename: "{app}\app\gui\TaskBaton.Desktop.exe"; Description: "{cm:LaunchProgram,TaskBaton}"; Flags: nowait postinstall skipifsilent
#endif

[CustomMessages]
english.UninstallData=Delete TaskBaton configuration and task records?%n%nCurrent TaskBaton station: %1%n%nThis removes the TaskBaton database (including Agent configuration and task history), desktop preferences, and TaskBaton's registered handoff rules. Files in the workspace, other files in the station, and your own instructions are kept. This cannot be undone.%n%nChoose No to keep configuration and task records.
chinesesimplified.UninstallData=是否清除 TaskBaton 配置和任务记录？%n%n当前中转站：%1%n%n这会删除 TaskBaton 账本（包括 Agent 配置和任务记录）、桌面偏好，以及已登记的交接规则。工作区文件、中转站中的其他文件和你的其他指令都会保留。此操作无法撤销。%n%n选择“否”可保留配置和任务记录。
chinesetraditional.UninstallData=是否清除 TaskBaton 設定和任務紀錄？%n%n目前中轉站：%1%n%n這會刪除 TaskBaton 帳本（包括 Agent 設定和任務紀錄）、桌面偏好，以及已登記的交接規則。工作區檔案、中轉站中的其他檔案和你的其他指令都會保留。此操作無法復原。%n%n選擇「否」可保留設定和任務紀錄。
english.UninstallRules=Remove TaskBaton's registered handoff rules from Agent instruction files? Other instructions and task data will be kept.
chinesesimplified.UninstallRules=是否从 Agent 指令文件中移除 TaskBaton 已登记的交接规则？其他指令和任务数据会保留。
chinesetraditional.UninstallRules=是否從 Agent 指令檔案中移除 TaskBaton 已登記的交接規則？其他指令和任務資料會保留。
english.UninstallFailed=TaskBaton could not complete the selected cleanup. The software will still be uninstalled, but some rules or data may remain. Reinstall TaskBaton to review them. Details: %1
chinesesimplified.UninstallFailed=TaskBaton 未能完成所选清理。软件仍会卸载，但部分规则或数据可能保留。请重新安装 TaskBaton 后检查。详情：%1
chinesetraditional.UninstallFailed=TaskBaton 無法完成所選清理。軟體仍會解除安裝，但部分規則或資料可能保留。請重新安裝 TaskBaton 後檢查。詳情：%1
english.UninstallLocationFailed=Could not read the current TaskBaton station. Configuration and rules will be kept. Reinstall TaskBaton to review them.
chinesesimplified.UninstallLocationFailed=无法读取当前 TaskBaton 中转站。配置与规则会保留，请重新安装 TaskBaton 后检查。
chinesetraditional.UninstallLocationFailed=無法讀取目前 TaskBaton 中轉站。設定與規則會保留，請重新安裝 TaskBaton 後檢查。

[Code]
#ifdef TestLocationPath
procedure CurStepChanged(CurStep: TSetupStep);
var
  LocationFile, DataDirectory: String;
begin
  // A test installation must never point at the formal location file or ledger.
  if CurStep = ssPostInstall then
  begin
    LocationFile := '{#TestLocationPath}';
    ForceDirectories(ExtractFileDir(LocationFile));
    DataDirectory := ExpandConstant('{app}\data');
    StringChangeEx(DataDirectory, '\', '\\', True);
    SaveStringToFile(LocationFile, '{"home":"' + DataDirectory + '"}', False);
  end;
end;
#endif

#ifndef TestAppId
const
  LegacyAppId = '{6476C39F-39A5-4BF2-AFC7-1E9371D4B556}';

function LegacyUninstaller(out InstallDirectory: String): String;
var
  Uninstaller: String;
begin
  Result := '';
  InstallDirectory := ExpandConstant('{localappdata}\Programs\Agent Relay');
  if RegQueryStringValue(HKCU,
       'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + LegacyAppId + '_is1',
       'UninstallString', Uninstaller) then
  begin
    Uninstaller := RemoveQuotes(Uninstaller);
    if FileExists(Uninstaller) then Result := Uninstaller;
  end;
  if (Result = '') and FileExists(InstallDirectory + '\unins000.exe') then
    Result := InstallDirectory + '\unins000.exe';
end;

function ImageIsRunning(const ImageName: String): Boolean;
var
  Command, OutputFile: String;
  Lines: TArrayOfString;
  Code, I: Integer;
begin
  Result := False;
  OutputFile := ExpandConstant('{tmp}\taskbaton-process-check.txt');
  Command := '/c tasklist /FI "IMAGENAME eq ' + ImageName + '" /NH /FO CSV > "' + OutputFile + '"';
  if not Exec(ExpandConstant('{cmd}'), Command, '', SW_HIDE, ewWaitUntilTerminated, Code) then Exit;
  if LoadStringsFromFile(OutputFile, Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do
      if Pos('"' + ImageName + '"', Lines[I]) > 0 then Result := True;
  DeleteFile(OutputFile);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Uninstaller, InstallDirectory, Arguments: String;
  Code: Integer;
begin
  Result := '';
  if ImageIsRunning('AgentRelay.Desktop.exe') or ImageIsRunning('AgentRelay.Notify.exe') then
  begin
    Result := '检测到旧版 Agent Relay 仍在运行。请先从托盘退出 Agent Relay 桌面程序和通知窗口，再重新运行 TaskBaton 安装程序。旧版程序退出后，安装程序会自动静默卸载旧版并保留数据。';
    Exit;
  end;
  Uninstaller := LegacyUninstaller(InstallDirectory);
  if Uninstaller = '' then Exit;
  Arguments := '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL';
  // The old silent uninstaller explicitly preserves configuration and task data.
  if not Exec(Uninstaller, Arguments, InstallDirectory, SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Result := '无法静默卸载旧版 Agent Relay（退出码 ' + IntToStr(Code) + '）。未继续安装；请手动卸载旧版后重试。';
end;
#endif

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Cli, LocationFile, PlanFile, ReportFile, ErrorFile, Home, Args, Detail: String;
  Lines: TArrayOfString;
  Code, I: Integer;
  RemoveData, RemoveRules: Boolean;
begin
  if (CurUninstallStep <> usUninstall) or UninstallSilent then Exit;
  Cli := ExpandConstant('{app}\app\taskbaton.exe');
  LocationFile := ExpandConstant('{localappdata}\TaskBaton\taskbaton.location.json');
#ifdef TestLocationPath
  LocationFile := '{#TestLocationPath}';
#endif
  PlanFile := ExpandConstant('{tmp}\taskbaton-uninstall-plan.txt');
  ReportFile := ExpandConstant('{tmp}\taskbaton-uninstall-report.json');
  ErrorFile := ExpandConstant('{tmp}\taskbaton-uninstall-error.txt');
  Args := 'uninstall plan --location-file "' + LocationFile + '" --output "' + PlanFile + '"';
  if not Exec(Cli, Args, '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) or
     not LoadStringsFromFile(PlanFile, Lines) or (GetArrayLength(Lines) <> 1) then begin
    MsgBox(CustomMessage('UninstallLocationFailed'), mbError, MB_OK);
    Exit;
  end;
  Home := Lines[0];
  RemoveData := MsgBox(FmtMessage(CustomMessage('UninstallData'), [Home]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  RemoveRules := RemoveData;
  if not RemoveData then
    RemoveRules := MsgBox(CustomMessage('UninstallRules'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  if not RemoveRules then Exit;
  Args := 'uninstall cleanup --location-file "' + LocationFile + '" --expected-home "' + Home + '" --report-path "' + ReportFile + '" --error-message-path "' + ErrorFile + '" --remove-rules';
  if RemoveData then Args := Args + ' --remove-data';
  if not Exec(Cli, Args, '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
    Detail := IntToStr(Code);
    if LoadStringsFromFile(ErrorFile, Lines) then begin
      Detail := '';
      for I := 0 to GetArrayLength(Lines) - 1 do Detail := Detail + Lines[I] + #13#10;
    end;
    MsgBox(FmtMessage(CustomMessage('UninstallFailed'), [Detail]), mbError, MB_OK);
  end;
end;
