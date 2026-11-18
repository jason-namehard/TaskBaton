param(
    [string]$Dotnet = 'dotnet',
    [string]$Helper,
    [string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Desktop verification requires Windows and an interactive user session' }
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Helper)) { $Helper = Join-Path $projectRoot 'artifacts/taskbaton/desktop/TaskBaton.Notify.exe' }
if ([string]::IsNullOrWhiteSpace($WorkDirectory)) { $WorkDirectory = Join-Path $projectRoot ('artifacts/desktop-test/' + [guid]::NewGuid()) }
$Helper = [IO.Path]::GetFullPath($Helper)
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
$dataRoot = Join-Path $WorkDirectory 'relay'
$cli = Join-Path $projectRoot 'artifacts/taskbaton/taskbaton.dll'
function Invoke-Relay([string[]]$Arguments) {
    $raw = & $Dotnet $cli --home $dataRoot @Arguments
    if ($LASTEXITCODE -notin @(0,4)) { throw ($raw -join [Environment]::NewLine) }
    return ($raw -join [Environment]::NewLine | ConvertFrom-Json)
}
Invoke-Relay @('init') | Out-Null
foreach ($id in @('agent-a','agent-b')) {
    $config = Join-Path $WorkDirectory ($id + '.toml')
    Set-Content -LiteralPath $config 'fixture=true' -Encoding utf8
    Invoke-Relay @('agent','register','--id',$id,'--config-path',$config) | Out-Null
    Invoke-Relay @('agent','validate','--id',$id) | Out-Null
}
Invoke-Relay @('permission','set','--from','agent-a','--to','agent-b','--decision','allow') | Out-Null
Invoke-Relay @('desktop','configure','--executable',$Helper) | Out-Null
$request = Join-Path $WorkDirectory 'request.md'
Set-Content -LiteralPath $request 'TaskBaton 本地通知测试：结果为 54。' -Encoding utf8
$task = (Invoke-Relay @('task','dispatch','--from','agent-a','--to','agent-b','--request',$request,'--mode','manual','--idempotency-key','desktop-test')).data.task
Set-Content -LiteralPath $task.result_path '## 测试结果
54；本通知用于 TaskBaton 桌面能力验证。' -Encoding utf8
$done = Invoke-Relay @('task','complete','--task',$task.task_id,'--attempt',$task.attempt_id,'--sender','agent-b',
    '--status','succeeded','--result',$task.result_path,'--idempotency-key','done')
if ($done.data.continuation.desktop_state -ne 'submitted') { throw ($done.data.continuation | ConvertTo-Json) }
if (Get-Process TaskBaton.Notify -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $Helper}) {
    throw 'Close existing TaskBaton notification windows before verifying cold activation'
}
$registration = @(Get-ChildItem HKCU:\Software\Classes\CLSID | ForEach-Object {
    $key = Join-Path $_.PSPath 'LocalServer32'
    if (Test-Path $key) {
        $server = (Get-Item $key).GetValue('')
        if ($null -ne $server -and $server.StartsWith('"' + $Helper + '"', [StringComparison]::OrdinalIgnoreCase)) { $_.PSChildName }
    }
})
if ($registration.Count -ne 1) { throw 'Expected one COM registration for this helper path' }
$appId = $Helper.ToLowerInvariant().Replace('\','.').Replace('/','.')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRelayNotificationActivation {
    void Activate([MarshalAs(UnmanagedType.LPWStr)] string appId, [MarshalAs(UnmanagedType.LPWStr)] string args, IntPtr data, uint count);
}
public static class RelayNotificationActivation {
    [DllImport("ole32.dll", PreserveSig=false)]
    static extern void CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IRelayNotificationActivation instance);
    public static void Invoke(string clsid, string appId, string args) {
        var classId=Guid.Parse(clsid); var iid=typeof(IRelayNotificationActivation).GUID;
        IRelayNotificationActivation callback;
        CoCreateInstance(ref classId,IntPtr.Zero,4,ref iid,out callback);
        callback.Activate(appId,args,IntPtr.Zero,0);
        Marshal.ReleaseComObject(callback);
    }
}
'@
# Argument encoding follows Windows App SDK 1.8 AppNotificationActivatedEventArgs::Decode.
function Encode-Argument([string]$Value) { $Value.Replace('%','%25').Replace(';','%3B').Replace('=','%3D') }
$previous = [Windows.Forms.Clipboard]::GetDataObject()
$backup = [Windows.Forms.DataObject]::new()
if ($null -ne $previous) {
    foreach ($format in $previous.GetFormats($false)) { $backup.SetData($format,$false,$previous.GetData($format,$false)) }
}
try {
    $arguments = 'action=copy;home=' + (Encode-Argument $dataRoot) + ';task=' + $task.task_id + ';attempt=' + $task.attempt_id
    [RelayNotificationActivation]::Invoke($registration[0],$appId,$arguments)
    Start-Sleep -Seconds 2
    $copied = [Windows.Forms.Clipboard]::GetText()
    if ($copied -ne $done.data.prompt) { throw 'Cold notification activation did not copy the correct continuation prompt' }
    $window = @(Get-Process TaskBaton.Notify | Where-Object {$_.Path -eq $Helper})
    if ($window.Count -ne 1 -or $window[0].MainWindowTitle -ne 'TaskBaton') { throw 'Expected one continuation window from cold activation' }
    $window[0].CloseMainWindow() | Out-Null
    if (-not $window[0].WaitForExit(10000)) { throw 'Notification process did not exit after closing its window' }
    $summary = [pscustomobject]@{status='passed';task_id=$task.task_id;notification_state='submitted';cold_com_activation=$true;
        correct_prompt_copied=$true;window_closed_process_exited=$true}
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $WorkDirectory 'summary.json') -Encoding utf8
    $summary
} finally {
    [Windows.Forms.Clipboard]::SetDataObject($backup,$true)
}
