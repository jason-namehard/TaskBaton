param([string]$Dotnet='dotnet', [string]$WorkDirectory=(Join-Path ([IO.Path]::GetTempPath()) ('TaskBaton.Desktop.Smoke/' + [guid]::NewGuid())), [switch]$NoRestore)
$ErrorActionPreference='Stop'
if (-not $IsWindows) { throw 'Desktop smoke tests require Windows' }
$projectRoot=Split-Path -Parent $PSScriptRoot
$cli=Join-Path $projectRoot 'src/TaskBaton.Cli/TaskBaton.Cli.csproj'
$smoke=Join-Path $projectRoot 'tests/TaskBaton.Desktop.Smoke/TaskBaton.Desktop.Smoke.csproj'
if (-not $NoRestore) {
    & $Dotnet restore $smoke --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Desktop smoke restore failed' }
    & $Dotnet restore $cli --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'CLI restore failed' }
}
& $Dotnet build $cli -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed' }
& $Dotnet build $smoke -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Desktop smoke build failed' }
$runner=Join-Path $projectRoot 'tests/TaskBaton.Desktop.Smoke/bin/Release/net10.0-windows10.0.19041.0/win-x64/TaskBaton.Desktop.Smoke.exe'
$relay=Join-Path $projectRoot 'src/TaskBaton.Cli/bin/Release/net10.0/taskbaton.dll'
$previousHost=$env:DOTNET_HOST_PATH
try {
    $env:DOTNET_HOST_PATH=(Get-Command $Dotnet -ErrorAction Stop).Source
    & $runner $WorkDirectory $relay
    if ($LASTEXITCODE -ne 0) {
        $report=Join-Path $WorkDirectory 'report.json'
        if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report }
        throw 'Desktop smoke test failed'
    }
    Get-Content -LiteralPath (Join-Path $WorkDirectory 'report.json')
} finally { $env:DOTNET_HOST_PATH=$previousHost }
