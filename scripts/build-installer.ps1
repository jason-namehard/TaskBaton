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
$installerName = if ($TestAppId) { "TaskBaton-v$version-windows-x64-test-setup.exe" } else { "TaskBaton-v$version-windows-x64-setup.exe" }
$installer=Join-Path $output $installerName
if (Test-Path -LiteralPath $installer) { throw "Installer already exists: $installer" }
if (-not $NoRestore) {
    & $Dotnet restore (Join-Path $projectRoot 'TaskBaton.slnx') --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }
    foreach ($project in @('TaskBaton.Desktop','TaskBaton.Notify')) {
        & $Dotnet restore (Join-Path $projectRoot "src/$project/$project.csproj") --locked-mode
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
    }
}
& (Join-Path $PSScriptRoot 'publish.ps1') -Dotnet $Dotnet -IncludeDesktop -OutputDirectory $stage
# Runtime-specific assets are restored to an isolated artifacts directory so the repository's
# framework-dependent lock files and ordinary build outputs remain unchanged.
foreach ($entry in @(@('TaskBaton.Cli',$stage),@('TaskBaton.Mcp',(Join-Path $stage 'mcp')))) {
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
foreach ($file in @('taskbaton.exe','taskbaton.dll','relay.exe','relay.dll','tb.exe','coreclr.dll',
    'mcp/taskbaton-mcp.exe','mcp/relay-mcp.exe','mcp/coreclr.dll',
    'gui/TaskBaton.Desktop.exe','gui/coreclr.dll','desktop/TaskBaton.Notify.exe','desktop/coreclr.dll',
    'LICENSE','THIRD_PARTY_NOTICES.md','DISTRIBUTION_TERMS.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $file) -PathType Leaf)) { throw "Installer payload is missing: $file" }
}
# The self-contained publish above replaces taskbaton.*; refresh the old-name shims from it.
$companionExtensions = @('.exe', '.dll', '.runtimeconfig.json', '.deps.json')
foreach ($alias in @('relay', 'tb')) {
    foreach ($extension in $companionExtensions) {
        $source = Join-Path $stage ('taskbaton' + $extension)
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $stage ($alias + $extension)) -Force }
    }
}
foreach ($extension in $companionExtensions) {
    $mcpSource = Join-Path (Join-Path $stage 'mcp') ('taskbaton-mcp' + $extension)
    if (Test-Path -LiteralPath $mcpSource) { Copy-Item -LiteralPath $mcpSource -Destination (Join-Path (Join-Path $stage 'mcp') ('relay-mcp' + $extension)) -Force }
}
$private=@(Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -match '^(auth\.json|config\.toml|(taskbaton|relay)\.db(-wal|-shm)?|original\.bin|backup\.bin|proposed\.bin)$' })
if ($private.Count) { throw ('Private data in installer payload: ' + (($private.FullName) -join ', ')) }
$helpText = (& $compiler '/?' 2>&1 | Out-String)
$definePrefix = if ($helpText.Contains('--define=')) { '--define=' } else { '/D' }
$arguments=@("${definePrefix}SourcePath=$stage","${definePrefix}ReleaseVersion=$version","${definePrefix}OutputPath=$output",
    "${definePrefix}IconPath=$(Join-Path $projectRoot 'src/TaskBaton.Desktop/Assets/TaskBaton.ico')")
if ($TestAppId) {
    $arguments+="${definePrefix}TestAppId=$TestAppId"
    if (-not $TestLocationPath) { $TestLocationPath=Join-Path $build 'taskbaton.location.json' }
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
