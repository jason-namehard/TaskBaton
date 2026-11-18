param([string]$Dotnet = 'dotnet', [switch]$IncludeDesktop, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$destination = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts/taskbaton' }
& $Dotnet publish (Join-Path $projectRoot 'src/TaskBaton.Cli/TaskBaton.Cli.csproj') -c Release --no-restore -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
& $Dotnet publish (Join-Path $projectRoot 'src/TaskBaton.Mcp/TaskBaton.Mcp.csproj') -c Release --no-restore -o (Join-Path $destination 'mcp')
if ($LASTEXITCODE -ne 0) { throw 'MCP publish failed' }
# Keep the old entry-point names working for existing scripts and host configs.
$companionExtensions = @('.exe', '.dll', '.runtimeconfig.json', '.deps.json')
foreach ($alias in @('relay', 'tb')) {
    foreach ($extension in $companionExtensions) {
        $source = Join-Path $destination ('taskbaton' + $extension)
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $destination ($alias + $extension)) }
    }
}
foreach ($extension in $companionExtensions) {
    $mcpSource = Join-Path (Join-Path $destination 'mcp') ('taskbaton-mcp' + $extension)
    if (Test-Path -LiteralPath $mcpSource) {
        Copy-Item -LiteralPath $mcpSource -Destination (Join-Path (Join-Path $destination 'mcp') ('relay-mcp' + $extension))
    }
}
if ($IncludeDesktop) {
    if (-not $IsWindows) { throw 'The desktop helper must be built on Windows' }
    & $Dotnet publish (Join-Path $projectRoot 'src/TaskBaton.Desktop/TaskBaton.Desktop.csproj') -c Release --no-restore -o (Join-Path $destination 'gui')
    if ($LASTEXITCODE -ne 0) { throw 'Desktop GUI publish failed' }
    & $Dotnet publish (Join-Path $projectRoot 'src/TaskBaton.Notify/TaskBaton.Notify.csproj') -c Release --no-restore -o (Join-Path $destination 'desktop')
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
    $assets = Get-Content (Join-Path $projectRoot 'src/TaskBaton.Notify/obj/project.assets.json') -Raw | ConvertFrom-Json
    $runtimeRoot = Join-Path $destination 'licenses/dotnet-runtime'
    New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
    foreach ($pack in $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies) {
        if ($pack.name -notmatch '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64$') { continue }
        $version = $pack.version.Trim('[',']').Split(',')[0].Trim()
        $package = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder ($pack.name.ToLowerInvariant() + '/' + $version)
            if (Test-Path $candidate) { $package = $candidate; break }
        }
        if ($null -eq $package) { throw "Runtime package missing: $($pack.name)" }
        $target = Join-Path $runtimeRoot ($pack.name + '/' + $version)
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        foreach ($file in Get-ChildItem $package -File | Where-Object {$_.Name -match 'LICENSE|THIRD.PARTY.NOTICES'}) {
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE'), (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md'), (Join-Path $projectRoot 'DISTRIBUTION_TERMS.md') -Destination $destination
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $destination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/PACKAGE_USAGE.md') -Destination (Join-Path $destination 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/GETTING_STARTED.md') -Destination (Join-Path $destination 'GETTING_STARTED.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/CONFIGURATION_ASSISTANT.md') -Destination (Join-Path $destination 'CONFIGURATION_ASSISTANT.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/SESSIONS.md') -Destination (Join-Path $destination 'SESSIONS.md')
if ($IncludeDesktop) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TryDemo.cmd') -Destination $destination }
Write-Output $destination
