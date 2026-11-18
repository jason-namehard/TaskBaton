param([switch]$IncludeDesktop)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$seen = @{}
$allowed = @('MIT', 'Apache-2.0', 'BSD-2-Clause')
$projects = @('src/TaskBaton.Core', 'src/TaskBaton.Cli', 'src/TaskBaton.Mcp', 'tests/TaskBaton.Core.Tests')
if ($IncludeDesktop) { $projects += 'src/TaskBaton.Notify', 'src/TaskBaton.Desktop', 'tests/TaskBaton.Desktop.Smoke' }
foreach ($project in $projects) {
    $assetsPath = Join-Path $projectRoot "$project/obj/project.assets.json"
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package' -or $seen.ContainsKey($library.Name)) { continue }
        $packagePath = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder $library.Value.path
            if (Test-Path -LiteralPath $candidate) { $packagePath = $candidate; break }
        }
        if ($null -eq $packagePath) { throw "Restored package not found: $($library.Name)" }
        $specs = @(Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec')
        if ($specs.Count -ne 1) { throw "Expected one package manifest: $($library.Name)" }
        $spec = $specs[0]
        [xml]$metadata = Get-Content -LiteralPath $spec.FullName -Raw
        $license = $metadata.package.metadata.license
        if ($null -ne $license -and $license.type -eq 'expression' -and $license.InnerText -in $allowed) {
            $seen[$library.Name] = $license.InnerText
        } elseif ($null -ne $license -and $license.type -eq 'file') {
            $reviewed = Join-Path $projectRoot ('licenses/nuget/' + $library.Value.path + '/' + $license.InnerText)
            $original = Join-Path $packagePath $license.InnerText
            if (-not (Test-Path -LiteralPath $reviewed) -or
                [IO.File]::ReadAllText($reviewed).Replace("`r`n","`n") -ne [IO.File]::ReadAllText($original).Replace("`r`n","`n")) {
                throw "Review required for package license: $($library.Name)"
            }
            $seen[$library.Name] = 'Microsoft terms; preserved license file'
        } elseif ($library.Name -eq 'Microsoft.Windows.SDK.BuildTools/10.0.26100.4654' -and
            $metadata.package.metadata.licenseUrl -eq 'https://aka.ms/WinSDKLicenseURL' -and
            (Test-Path (Join-Path $projectRoot 'licenses/nuget/microsoft.windows.sdk.buildtools/10.0.26100.4654/sdk_license.rtf'))) {
            $seen[$library.Name] = 'Microsoft Windows SDK terms; build only'
        } else {
            throw "Review required for package license: $($library.Name)"
        }
    }
}
$seen.GetEnumerator() | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{ Package = $_.Name; License = $_.Value }
}
