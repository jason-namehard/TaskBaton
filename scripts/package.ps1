param(
    [string]$Dotnet='dotnet',
    [ValidateSet('Windows','Portable')][string]$Flavor='Portable',
    [string]$OutputDirectory,
    [switch]$NoRestore
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
[xml]$properties=Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$version=[string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(-preview\.\d+)?$') { throw 'Expected an explicit release or preview version in Directory.Build.props' }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") { throw 'Release tag does not match the source version' }
$windows=$Flavor -eq 'Windows'
if ($windows -and -not $IsWindows) { throw 'Windows packages must be built on Windows' }
$output=if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts/packages' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$suffix=if ($windows) { 'windows-x64' } else { 'cli-mcp' }
$archivePath=Join-Path $output "AgentRelay-v$version-$suffix.zip"
if (Test-Path -LiteralPath $archivePath) { throw "Package already exists: $archivePath" }
$stage=Join-Path $projectRoot ('artifacts/staging/' + [guid]::NewGuid())
if (-not $NoRestore) {
    & $Dotnet restore (Join-Path $projectRoot 'AgentRelay.slnx') --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed' }
    if ($windows) {
        foreach ($project in @('AgentRelay.Notify','AgentRelay.Desktop')) {
            & $Dotnet restore (Join-Path $projectRoot "src/$project/$project.csproj") --locked-mode
            if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
        }
    }
}
& (Join-Path $PSScriptRoot 'publish.ps1') -Dotnet $Dotnet -IncludeDesktop:$windows -OutputDirectory $stage
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$archivePath,[IO.Compression.CompressionLevel]::Optimal,$false)
$archive=[IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $names=@($archive.Entries.FullName | ForEach-Object { $_.Replace('\','/') })
    $required=@('relay.dll','relay.runtimeconfig.json','mcp/relay-mcp.dll','mcp/relay-mcp.runtimeconfig.json',
        'README.md','GETTING_STARTED.md','CONFIGURATION_ASSISTANT.md','SESSIONS.md','LICENSE','THIRD_PARTY_NOTICES.md','DISTRIBUTION_TERMS.md',
        'licenses/MCP-SDK-Apache-2.0.txt','licenses/Tomlyn-BSD-2-Clause.txt','licenses/ZstdSharp-MIT.txt')
    if ($windows) { $required+=@('TryDemo.cmd','gui/AgentRelay.Desktop.exe','gui/coreclr.dll',
        'gui/PresentationFramework.dll','desktop/AgentRelay.Notify.exe') }
    foreach ($entry in $required) { if ($entry -notin $names) { throw "Missing package entry: $entry" } }
    if (-not $windows -and @($names | Where-Object { $_ -match '^(gui|desktop)/' }).Count) { throw 'Portable package contains Windows desktop files' }
    # Published code must not carry the operator's ledger, original config, authentication or migration backups.
    $private=@($names | Where-Object { $_ -match '(^|/)(auth\.json|config\.toml|relay\.db(-wal|-shm)?|original\.bin|backup\.bin|proposed\.bin)$' })
    if ($private.Count) { throw ('Private data in package: ' + ($private -join ', ')) }
} finally { $archive.Dispose() }
Get-Item -LiteralPath $archivePath | Select-Object FullName,Length
