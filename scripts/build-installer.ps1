param(
    [string]$Dotnet='dotnet',
    [string]$InnoCompiler='ISCC.exe',
    [string]$OutputDirectory,
    [string]$BuildDirectory,
    [string]$TestAppId,
    [string]$TestLocationPath,
    [string]$TestInstallDir,
    [switch]$NoRestore
)
$ErrorActionPreference='Stop'
if (-not $IsWindows) { throw 'The desktop installer must be built on Windows.' }
$projectRoot=Split-Path -Parent $PSScriptRoot
[xml]$properties=Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$version=[string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-preview\.\d+)?$') { throw 'Expected a release or preview version.' }
$compiler=(Get-Command $InnoCompiler -ErrorAction Stop).Source
$output=if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts/packages' }
$build=if ($BuildDirectory) { [IO.Path]::GetFullPath($BuildDirectory) } else { Join-Path $projectRoot ('artifacts/installer-build/' + [guid]::NewGuid()) }
if (Test-Path -LiteralPath $build) { throw "Build directory already exists: $build" }
$stage=Join-Path $build 'payload'
$isolated=Join-Path $build 'dotnet-artifacts'
New-Item -ItemType Directory -Path $output,$build -Force | Out-Null
$installerName = if ($TestAppId) { "AgentRelay-v$version-windows-x64-test-setup.exe" } else { "AgentRelay-v$version-windows-x64-setup.exe" }
$installer=Join-Path $output $installerName
if (Test-Path -LiteralPath $installer) { throw "Installer already exists: $installer" }
if (-not $NoRestore) {
    & $Dotnet restore (Join-Path $projectRoot 'AgentRelay.slnx') --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }
    foreach ($project in @('AgentRelay.Desktop','AgentRelay.Notify')) {
        & $Dotnet restore (Join-Path $projectRoot "src/$project/$project.csproj") --locked-mode
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
    }
}
& (Join-Path $PSScriptRoot 'publish.ps1') -Dotnet $Dotnet -IncludeDesktop -OutputDirectory $stage
# Runtime-specific assets are restored to an isolated artifacts directory so the repository's
# framework-dependent lock files and ordinary build outputs remain unchanged.
foreach ($entry in @(@('AgentRelay.Cli',$stage),@('AgentRelay.Mcp',(Join-Path $stage 'mcp')))) {
    $project=Join-Path $projectRoot ("src/{0}/{0}.csproj" -f $entry[0])
    & $Dotnet publish $project -c Release -r win-x64 --self-contained --artifacts-path $isolated -o $entry[1] `
        "-p:NuGetLockFilePath=$build/isolated-unused.lock.json" -p:RestorePackagesWithLockFile=false -p:RestoreLockedMode=false
    if ($LASTEXITCODE -ne 0) { throw "Self-contained publish failed: $($entry[0])" }
    $locked=Get-Content -Raw -LiteralPath (Join-Path $projectRoot "src/$($entry[0])/obj/project.assets.json") | ConvertFrom-Json
    $runtime=Get-Content -Raw -LiteralPath (Join-Path $isolated "obj/$($entry[0])/project.assets.json") | ConvertFrom-Json
    $expected=@($locked.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object Name | Sort-Object)
    $actual=@($runtime.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object Name | Sort-Object)
    if (Compare-Object $expected $actual) { throw "Self-contained dependency versions differ from the locked restore: $($entry[0])" }
}
foreach ($file in @('relay.exe','relay.dll','coreclr.dll','mcp/relay-mcp.exe','mcp/coreclr.dll',
    'gui/AgentRelay.Desktop.exe','gui/coreclr.dll','desktop/AgentRelay.Notify.exe','desktop/coreclr.dll',
    'LICENSE','THIRD_PARTY_NOTICES.md','DISTRIBUTION_TERMS.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $file) -PathType Leaf)) { throw "Installer payload is missing: $file" }
}
$private=@(Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -match '^(auth\.json|config\.toml|relay\.db(-wal|-shm)?|original\.bin|backup\.bin|proposed\.bin)$' })
if ($private.Count) { throw ('Private data in installer payload: ' + (($private.FullName) -join ', ')) }
$helpText = (& $compiler '/?' 2>&1 | Out-String)
$definePrefix = if ($helpText.Contains('--define=')) { '--define=' } else { '/D' }
$arguments=@("${definePrefix}SourcePath=$stage","${definePrefix}ReleaseVersion=$version","${definePrefix}OutputPath=$output",
    "${definePrefix}IconPath=$(Join-Path $projectRoot 'src/AgentRelay.Desktop/Assets/AgentRelay.ico')")
if ($TestAppId) {
    $arguments+="${definePrefix}TestAppId=$TestAppId"
    if (-not $TestLocationPath) { $TestLocationPath=Join-Path $build 'relay.location.json' }
    if (-not $TestInstallDir) { $TestInstallDir=Join-Path $build 'test-install' }
    $arguments+="${definePrefix}TestInstallDir=$([IO.Path]::GetFullPath($TestInstallDir))"
}
if ($TestLocationPath) {
    if (-not $TestAppId) { throw 'TestLocationPath requires TestAppId.' }
    $arguments+="${definePrefix}TestLocationPath=$([IO.Path]::GetFullPath($TestLocationPath))"
}
if ($TestInstallDir -and -not $TestAppId) { throw 'TestInstallDir requires TestAppId.' }
& $compiler @arguments (Join-Path $PSScriptRoot 'windows-installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Get-Item -LiteralPath $installer | Select-Object FullName,Length
