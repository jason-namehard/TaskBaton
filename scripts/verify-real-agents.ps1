param(
    [Parameter(Mandatory)][string]$Codex,
    [Parameter(Mandatory)][string]$Amq,
    [Parameter(Mandatory)][string]$ConfigPath,
    [string]$Dotnet = 'dotnet',
    [string]$WorkDirectory,
    [switch]$ResumeOrigin,
    [switch]$McpMigration,
    [ValidateRange(10, 1800)][int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($McpMigration) { $ResumeOrigin = $true }
if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $WorkDirectory = Join-Path $projectRoot ('artifacts/real-agents/' + [guid]::NewGuid())
}
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
$dataRoot = Join-Path $WorkDirectory 'relay'
$buildTarget = if ($McpMigration) { 'AgentRelay.slnx' } else { 'src/AgentRelay.Cli/AgentRelay.Cli.csproj' }
& $Dotnet build (Join-Path $projectRoot $buildTarget) -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$cli = Join-Path $projectRoot 'src/AgentRelay.Cli/bin/Release/net10.0/relay.dll'
function Invoke-Relay([string[]]$Arguments) {
    $response = & $Dotnet $cli --home $dataRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Relay command failed: $($response -join [Environment]::NewLine)" }
    return ($response -join [Environment]::NewLine | ConvertFrom-Json)
}
Invoke-Relay @('init') | Out-Null
foreach ($agent in @('agent-a', 'agent-b')) {
    Invoke-Relay @('agent','register','--id',$agent,'--host','codex','--config-path',$ConfigPath,'--cli-path',$Codex) | Out-Null
    Invoke-Relay @('agent','validate','--id',$agent) | Out-Null
    Invoke-Relay @('agent','verify-executor','--id',$agent) | Out-Null
}
if ($ResumeOrigin) {
    Invoke-Relay @('agent','verify-resumer','--id','agent-a','--kind','codex-exec-resume') | Out-Null
}
Invoke-Relay @('permission','set','--from','agent-a','--to','agent-b','--decision','allow') | Out-Null
Invoke-Relay @('adapter','verify','--id','amq-cli','--executable',$Amq) | Out-Null
$schema = Join-Path $WorkDirectory 'request.schema.json'
$answer = Join-Path $WorkDirectory 'agent-a.json'
Set-Content -LiteralPath $schema -Encoding utf8 -Value @'
{"type":"object","properties":{"request_markdown":{"type":"string"}},"required":["request_markdown"],"additionalProperties":false}
'@
$start = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Codex))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
$start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
$start.Environment['CODEX_HOME'] = Split-Path -Parent ([IO.Path]::GetFullPath($ConfigPath))
$initialArguments = @('exec','--ignore-user-config','--sandbox','read-only','--skip-git-repo-check',
    '--cd',$WorkDirectory,'--output-schema',$schema,'--output-last-message',$answer,'--json','-')
if (-not $ResumeOrigin) { $initialArguments += '--ephemeral' }
foreach ($argument in $initialArguments) {
    $start.ArgumentList.Add($argument)
}
$agentA = [Diagnostics.Process]::Start($start)
$events = $agentA.StandardOutput.ReadToEndAsync()
$diagnostics = $agentA.StandardError.ReadToEndAsync()
$memory = 'ORIGIN-' + [guid]::NewGuid().ToString('N')
$agentA.StandardInput.Write("记住原任务码 $memory ，此码不要写入交接指令，仅你记住。以后收到 TaskBaton 继续消息时，读取结果并报告此原任务码和计算结果，不要重复派发。当前轮不要调用工具。请给 Agent B 写一份简短中文任务：计算 4、9、16、25 的和，说明计算过程，返回标记 RELAY-PHASE2-54。你只写交接指令，不要自行解答。")
$agentA.StandardInput.Close()
if (-not $agentA.WaitForExit($TimeoutSeconds * 1000)) {
    $agentA.Kill($true)
    $agentA.WaitForExit()
    throw 'Agent A exceeded the execution timeout'
}
$agentAEvents = $events.GetAwaiter().GetResult()
Set-Content -LiteralPath (Join-Path $WorkDirectory 'agent-a-events.jsonl') -Encoding utf8 -Value $agentAEvents
Set-Content -LiteralPath (Join-Path $WorkDirectory 'agent-a-stderr.log') -Encoding utf8 -Value $diagnostics.GetAwaiter().GetResult()
if ($agentA.ExitCode -ne 0) { throw "Agent A exited with $($agentA.ExitCode)" }
$agentA.Dispose()
$instruction = Get-Content -LiteralPath $answer -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($instruction.request_markdown)) { throw 'Agent A did not provide a handoff' }
if ($ResumeOrigin -and $instruction.request_markdown.Contains($memory)) { throw 'Origin memory must not be included in the handoff' }
$request = Join-Path $WorkDirectory 'request-from-agent-a.md'
Set-Content -LiteralPath $request -Encoding utf8 -Value $instruction.request_markdown
$aSession = @($agentAEvents -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.type -eq 'thread.started' })[0].thread_id
$originArguments = @()
if ($ResumeOrigin) {
    $originPath = Join-Path $WorkDirectory 'origin.json'
    @{host='codex';session_id=$aSession;config_profile=[IO.Path]::GetFullPath($ConfigPath);cwd=$WorkDirectory} |
        ConvertTo-Json | Set-Content -LiteralPath $originPath -Encoding utf8
    $originArguments = @('--origin-session',$originPath)
}
$dispatchArguments = @('task','dispatch','--from','agent-a','--to','agent-b','--request',$request,
    '--mode','auto','--idempotency-key','real-flow-1','--execute','--execution-timeout-seconds',"$TimeoutSeconds") + $originArguments
if ($McpMigration) {
    $mcpDll = Join-Path $projectRoot 'src/AgentRelay.Mcp/bin/Release/net10.0/relay-mcp.dll'
    $runtime = (Get-Command $Dotnet).Source
    $hostConfig = Join-Path $WorkDirectory 'host-config.toml'
    $legacyArgs = @($mcpDll,'--home',$dataRoot,'--agent','agent-a','--route-id','legacy') | ConvertTo-Json -Compress
    $originalConfig = "# Existing local route`n[mcp_servers.legacy]`ncommand = $($runtime | ConvertTo-Json -Compress)`nargs = $legacyArgs`nenabled = true`n"
    Set-Content -LiteralPath $hostConfig -Value $originalConfig -Encoding utf8 -NoNewline
    $newArgs = @($mcpDll,'--home',$dataRoot,'--agent','agent-a','--route-id','{{migration_id}}') | ConvertTo-Json -Compress
    $revision = git -C $projectRoot rev-parse HEAD
    if (git -C $projectRoot status --porcelain) { $revision += '+working-tree' }
    $source = @{adapter_id='relay-mcp';version='0.4.0';repository='https://github.com/jason-namehard/agent-relay';
        revision=$revision;license='MIT';capabilities=@('stdio_mcp','completion_callback','session_resume');dependencies=@('.NET 10');limitations=@('Local stdio')}
    $manifest = Join-Path $WorkDirectory 'migration.json'
    $proposal = @{source=$source;target_path=$hostConfig;format='toml';before='';
        after="`n[mcp_servers.agent_relay]`ncommand = $($runtime | ConvertTo-Json -Compress)`nargs = $newArgs`n"}
    $proposal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifest -Encoding utf8
    $migrationPlan = (Invoke-Relay @('migration','plan','--manifest',$manifest)).data.plan
    if ([IO.File]::ReadAllText($hostConfig) -ne $originalConfig) { throw 'Planning changed the host fixture' }
    Invoke-Relay @('migration','apply','--id',$migrationPlan.migration_id,'--approve','--reason','Reviewed isolated integration fixture') | Out-Null
    $inventory = Invoke-Relay @('adapter','inspect-config','--path',$hostConfig,'--format','toml')
    function Test-CodexMcpConfig([bool]$LegacyEnabled) {
        $probeDirectory = Join-Path $WorkDirectory 'host-config-probe'
        New-Item -ItemType Directory -Path $probeDirectory -Force | Out-Null
        Copy-Item -LiteralPath $hostConfig -Destination (Join-Path $probeDirectory 'config.toml')
        $probe = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Codex))
        $probe.UseShellExecute=$false; $probe.CreateNoWindow=$true
        $probe.RedirectStandardOutput=$true; $probe.RedirectStandardError=$true
        $probe.StandardOutputEncoding=[Text.UTF8Encoding]::new($false)
        $probe.Environment['CODEX_HOME']=$probeDirectory
        foreach ($argument in @('mcp','list','--json')) { $probe.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($probe)
        $output = $process.StandardOutput.ReadToEndAsync(); $diagnostics = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(20000)) { $process.Kill($true); $process.WaitForExit(); throw 'Codex MCP config probe timed out' }
        if ($process.ExitCode -ne 0) { throw $diagnostics.GetAwaiter().GetResult() }
        $raw = $output.GetAwaiter().GetResult()
        $raw | Set-Content -LiteralPath (Join-Path $WorkDirectory "codex-mcp-legacy-$LegacyEnabled.json") -Encoding utf8
        $servers = $raw | ConvertFrom-Json
        if (@($servers | Where-Object {$_.name -eq 'agent_relay' -and $_.enabled}).Count -ne 1 -or
            @($servers | Where-Object {$_.name -eq 'legacy' -and $_.enabled -eq $LegacyEnabled}).Count -ne 1) {
            throw 'Codex did not recognize the expected new and legacy route settings'
        }
    }
    Test-CodexMcpConfig $true
    $entry = @($inventory.data.mcp_entries | Where-Object name -eq 'agent_relay')[0]
    $transport = [Diagnostics.ProcessStartInfo]::new($entry.command)
    $transport.UseShellExecute=$false; $transport.CreateNoWindow=$true
    $transport.RedirectStandardInput=$true; $transport.RedirectStandardOutput=$true; $transport.RedirectStandardError=$true
    $transport.StandardInputEncoding=[Text.UTF8Encoding]::new($false)
    $transport.StandardOutputEncoding=[Text.UTF8Encoding]::new($false)
    $transport.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
    foreach ($argument in $entry.arguments) { $transport.ArgumentList.Add($argument) }
    $mcp = [Diagnostics.Process]::Start($transport); $mcpErrors = $mcp.StandardError.ReadToEndAsync()
    $wire = [Collections.Generic.List[string]]::new()
    function Send-Mcp($Message) { $mcp.StandardInput.WriteLine(($Message | ConvertTo-Json -Depth 20 -Compress)); $mcp.StandardInput.Flush() }
    function Receive-Mcp($Id) {
        for ($i=0; $i -lt 100; $i++) {
            $line = $mcp.StandardOutput.ReadLineAsync()
            if (-not $line.Wait(20000)) { throw 'MCP response timed out' }
            $raw = $line.GetAwaiter().GetResult()
            if ($null -eq $raw) { throw 'MCP transport closed unexpectedly' }
            $wire.Add($raw); $message = $raw | ConvertFrom-Json
            if ($message.id -eq $Id) {
                if ($message.error) { throw ($message.error | ConvertTo-Json -Compress) }
                return $message.result
            }
        }
        throw 'MCP response ID was not found'
    }
    try {
        Send-Mcp @{jsonrpc='2.0';id=1;method='initialize';params=@{protocolVersion='2025-11-25';capabilities=@{};clientInfo=@{name='relay-phase4-verifier';version='1'}}}
        Receive-Mcp 1 | Out-Null
        Send-Mcp @{jsonrpc='2.0';method='notifications/initialized';params=@{}}
        Send-Mcp @{jsonrpc='2.0';id=2;method='tools/list';params=@{}}
        if (@((Receive-Mcp 2).tools).Count -ne 7) { throw 'Expected the seven Relay task tools' }
        $clock = [Diagnostics.Stopwatch]::StartNew()
        Send-Mcp @{jsonrpc='2.0';id=3;method='tools/call';params=@{name='relay_task_dispatch';arguments=@{
            to='agent-b';content=$instruction.request_markdown;idempotency_key='real-flow-1';mode='auto';execute=$true;
            timeout_seconds=$TimeoutSeconds;origin_session=(Get-Content $originPath -Raw | ConvertFrom-Json)}}}
        $reply = Receive-Mcp 3
        if ($reply.isError) { throw ($reply | ConvertTo-Json -Depth 10) }
        $dispatch = $reply.structuredContent
        $clock.Stop()
    } finally {
        $mcp.StandardInput.Close()
        if (-not $mcp.WaitForExit(10000)) { $mcp.Kill($true); $mcp.WaitForExit(); throw 'MCP server did not exit on stdin EOF' }
        $wire | Set-Content -LiteralPath (Join-Path $WorkDirectory 'mcp-wire.jsonl') -Encoding utf8
        $mcpErrors.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $WorkDirectory 'mcp-stderr.log') -Encoding utf8
    }
} else {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $dispatch = Invoke-Relay $dispatchArguments
}
$clock.Stop()
if ($clock.Elapsed.TotalSeconds -ge 10) { throw 'Dispatch did not return before task execution finished' }
$task = $dispatch.data.task
$workerPid = $dispatch.data.execution.worker_pid
# A bounded worker may finish before its process handle can be obtained; persisted task state remains authoritative.
$worker = Get-Process -Id $workerPid -ErrorAction SilentlyContinue
$workerBound = $TimeoutSeconds + 45
if ($ResumeOrigin) { $workerBound += 225 }
if ($null -ne $worker -and -not $worker.WaitForExit($workerBound * 1000)) { throw 'Worker did not exit within its execution bound' }
$final = Invoke-Relay @('task','status','--task',$task.task_id)
if ($final.state -ne 'succeeded') { throw "Task finished in state $($final.state): $($final.data.task.error_message)" }
$result = Get-Content -LiteralPath $final.data.task.result_path -Raw
if (-not $result.Contains('54') -or -not $result.Contains('RELAY-PHASE2-54')) { throw 'Agent B result failed the arithmetic acceptance case' }
$before = Invoke-Relay @('task','execution','--task',$task.task_id)
Invoke-Relay @('task','run','--task',$task.task_id,'--attempt',$task.attempt_id,'--sender','agent-b') | Out-Null
$after = Invoke-Relay @('task','execution','--task',$task.task_id)
if ($before.data.session_id -ne $after.data.session_id) { throw 'Duplicate run created another agent session' }
if ([string]::IsNullOrWhiteSpace($aSession) -or [string]::IsNullOrWhiteSpace($final.data.execution.session_id) -or $aSession -eq $final.data.execution.session_id) {
    throw 'Expected two distinct verified CLI sessions'
}
$summary = [ordered]@{
    status = 'passed'
    task_id = $task.task_id
    agent_a_session = $aSession
    agent_b_session = $final.data.execution.session_id
    dispatch_return_seconds = $clock.Elapsed.TotalSeconds
    result = '54'
    duplicate_run = 'deduplicated'
}
if ($ResumeOrigin) {
    $continuation = $final.data.continuation
    if ($continuation.state -ne 'completed') { throw "Origin continuation did not finish: $($continuation | ConvertTo-Json -Compress)" }
    $continued = Get-Content -LiteralPath $continuation.output_path -Raw
    if (-not $continued.Contains($memory) -or -not $continued.Contains('54')) { throw 'Origin did not recover its earlier context and recipient result' }
    Invoke-Relay @('task','continue','--task',$task.task_id) | Out-Null
    $audit = Invoke-Relay @('audit','list','--task',$task.task_id)
    if (@($audit.data | Where-Object {$_.event -eq 'session.resume_claimed'}).Count -ne 1) { throw 'Continuation was submitted more than once' }
    $summary.continuation_state = $continuation.state
    $summary.original_context_recovered = $true
    $summary.duplicate_continuation = 'deduplicated'
}
if ($McpMigration) {
    Invoke-Relay @('migration','validate','--id',$migrationPlan.migration_id,'--task',$task.task_id) | Out-Null
    $proposal.before='enabled = true'; $proposal.after='enabled = false'; $proposal.purpose='disable_old'; $proposal.validated_migration_id=$migrationPlan.migration_id
    $proposal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifest -Encoding utf8
    $disable = (Invoke-Relay @('migration','plan','--manifest',$manifest)).data.plan
    Invoke-Relay @('migration','apply','--id',$disable.migration_id,'--approve','--reason','New route verified; approve old fixture entry disable') | Out-Null
    if (-not [IO.File]::ReadAllText($hostConfig).Contains('enabled = false')) { throw 'Old entry was not disabled' }
    Test-CodexMcpConfig $false
    Invoke-Relay @('migration','rollback','--id',$disable.migration_id,'--approve','--reason','Restore fixture old entry') | Out-Null
    Invoke-Relay @('migration','rollback','--id',$migrationPlan.migration_id,'--approve','--reason','Restore fixture baseline') | Out-Null
    if ([IO.File]::ReadAllText($hostConfig) -ne $originalConfig) { throw 'Rollback did not preserve the original fixture' }
    if (-not (Test-Path $migrationPlan.backup_path)) { throw 'Migration backup missing' }
    $summary.mcp_dispatch=$true; $summary.migration_id=$migrationPlan.migration_id
    $summary.workflow_validated=$true; $summary.separate_old_entry_disable=$true; $summary.rollback_restored_original=$true
}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $WorkDirectory 'summary.json') -Encoding utf8
[pscustomobject]$summary
