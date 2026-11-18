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
AppId={{6476C39F-39A5-4BF2-AFC7-1E9371D4B556}
#endif
AppName=TaskBaton
AppVersion={#ReleaseVersion}
AppPublisher=TaskBaton contributors
AppPublisherURL=https://github.com/jason-namehard/TaskBaton
#ifdef TestInstallDir
DefaultDirName={#TestInstallDir}
#else
DefaultDirName={localappdata}\Programs\Agent Relay
#endif
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
SetupIconFile={#IconPath}
UninstallDisplayIcon={app}\app\gui\AgentRelay.Desktop.exe
LicenseFile={#SourcePath}\LICENSE
OutputDir={#OutputPath}
#ifdef TestAppId
OutputBaseFilename=AgentRelay-v{#ReleaseVersion}-windows-x64-test-setup
#else
OutputBaseFilename=AgentRelay-v{#ReleaseVersion}-windows-x64-setup
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
Name: "{autoprograms}\{#TestAppId}"; Filename: "{app}\app\gui\AgentRelay.Desktop.exe"; Parameters: "--home ""{app}\data"""; WorkingDir: "{app}\app\gui"
Name: "{autodesktop}\{#TestAppId}"; Filename: "{app}\app\gui\AgentRelay.Desktop.exe"; Parameters: "--home ""{app}\data"""; WorkingDir: "{app}\app\gui"; Tasks: desktopicon
#else
Name: "{autoprograms}\TaskBaton"; Filename: "{app}\app\gui\AgentRelay.Desktop.exe"; WorkingDir: "{app}\app\gui"
Name: "{autodesktop}\TaskBaton"; Filename: "{app}\app\gui\AgentRelay.Desktop.exe"; WorkingDir: "{app}\app\gui"; Tasks: desktopicon
#endif

[Run]
#ifdef TestAppId
Filename: "{app}\app\gui\AgentRelay.Desktop.exe"; Parameters: "--home ""{app}\data"""; Description: "{cm:LaunchProgram,TaskBaton}"; Flags: nowait postinstall skipifsilent
#else
Filename: "{app}\app\gui\AgentRelay.Desktop.exe"; Description: "{cm:LaunchProgram,TaskBaton}"; Flags: nowait postinstall skipifsilent
#endif

[CustomMessages]
english.UninstallData=Delete Relay configuration and task records?%n%nCurrent relay station: %1%n%nThis removes the Relay database (including Agent configuration and task history), desktop preferences, and Relay's registered handoff rules. Files in the workspace, other files in the station, and your own instructions are kept. This cannot be undone.%n%nChoose No to keep configuration and task records.
chinesesimplified.UninstallData=是否清除 Relay 配置和任务记录？%n%n当前中转站：%1%n%n这会删除 Relay 账本（包括 Agent 配置和任务记录）、桌面偏好，以及已登记的交接规则。工作区文件、中转站中的其他文件和你的其他指令都会保留。此操作无法撤销。%n%n选择“否”可保留配置和任务记录。
chinesetraditional.UninstallData=是否清除 Relay 設定和任務紀錄？%n%n目前中轉站：%1%n%n這會刪除 Relay 帳本（包括 Agent 設定和任務紀錄）、桌面偏好，以及已登記的交接規則。工作區檔案、中轉站中的其他檔案和你的其他指令都會保留。此操作無法復原。%n%n選擇「否」可保留設定和任務紀錄。
english.UninstallRules=Remove Relay's registered handoff rules from Agent instruction files? Other instructions and task data will be kept.
chinesesimplified.UninstallRules=是否从 Agent 指令文件中移除 Relay 已登记的交接规则？其他指令和任务数据会保留。
chinesetraditional.UninstallRules=是否從 Agent 指令檔案中移除 Relay 已登記的交接規則？其他指令和任務資料會保留。
english.UninstallFailed=Relay could not complete the selected cleanup. The software will still be uninstalled, but some rules or data may remain. Reinstall Relay to review them. Details: %1
chinesesimplified.UninstallFailed=Relay 未能完成所选清理。软件仍会卸载，但部分规则或数据可能保留。请重新安装 Relay 后检查。详情：%1
chinesetraditional.UninstallFailed=Relay 無法完成所選清理。軟體仍會解除安裝，但部分規則或資料可能保留。請重新安裝 Relay 後檢查。詳情：%1
english.UninstallLocationFailed=Could not read the current Relay station. Configuration and rules will be kept. Reinstall Relay to review them.
chinesesimplified.UninstallLocationFailed=无法读取当前 Relay 中转站。配置与规则会保留，请重新安装 Relay 后检查。
chinesetraditional.UninstallLocationFailed=無法讀取目前 Relay 中轉站。設定與規則會保留，請重新安裝 Relay 後檢查。

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

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Cli, LocationFile, PlanFile, ReportFile, ErrorFile, Home, Args, Detail: String;
  Lines: TArrayOfString;
  Code, I: Integer;
  RemoveData, RemoveRules: Boolean;
begin
  if (CurUninstallStep <> usUninstall) or UninstallSilent then Exit;
  Cli := ExpandConstant('{app}\app\relay.exe');
  LocationFile := ExpandConstant('{localappdata}\AgentRelay\relay.location.json');
#ifdef TestLocationPath
  LocationFile := '{#TestLocationPath}';
#endif
  PlanFile := ExpandConstant('{tmp}\agent-relay-uninstall-plan.txt');
  ReportFile := ExpandConstant('{tmp}\agent-relay-uninstall-report.json');
  ErrorFile := ExpandConstant('{tmp}\agent-relay-uninstall-error.txt');
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
