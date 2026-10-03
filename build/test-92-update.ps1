param(
    [string]$PackageDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) '交付文件\26A17092更新补丁'),
    [switch]$CheckLive,
    [ValidatePattern('^[0-9A-F]{8}$')][string]$VersionName = '26A17092'
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
Push-Location $workspace
try {
    $legacyDirectory = Join-Path $workspace '.tmp\legacy91-update-contract'
    New-Item -ItemType Directory -Force -Path $legacyDirectory | Out-Null
    foreach ($name in @('RemoteManifestLauncherUpdateService', 'LauncherSelfUpdateService', 'UpdateSecurity')) {
        $source = (& git show "v26A17091:Launcher.Infrastructure/Updates/$name.cs") -join "`n"
        if ($LASTEXITCODE -ne 0) { throw 'Original update protocol source is unavailable.' }
        # Only namespace/imports change, so the old 91 protocol is exercised as shipped.
        $source = "global using Launcher.Infrastructure.Updates;`n" + $source.Replace('namespace Launcher.Infrastructure.Updates;', 'namespace Legacy91;')
        [IO.File]::WriteAllText((Join-Path $legacyDirectory ($name + '.cs')), $source)
    }
    if ($CheckLive) {
        dotnet run --project build/UpdateContract/UpdateContract.csproj -c Release -- --check-live
    }
    else {
        $baselineDirectory = Join-Path $workspace 'publish\update-baseline-91'
        $baseline = Join-Path $baselineDirectory 'BlockHelm_Launcher_x64.exe'
        if (-not (Test-Path -LiteralPath $baseline)) {
            New-Item -ItemType Directory -Force -Path $baselineDirectory | Out-Null
            gh release download v26A17091 --repo 11451446545/BlockHelm-Launcher --pattern BlockHelm_Launcher_x64.exe --dir $baselineDirectory
            if ($LASTEXITCODE -ne 0) { throw 'Original 91 download failed.' }
        }
        if ((Get-FileHash -LiteralPath $baseline).Hash -ne '892bdb92eb025321904cb611aed897eb0b8b35acd40f1fbdc0bbf6807cdbd13f') { throw 'Original 91 checksum mismatch.' }
        $testRoot = Join-Path $workspace ('.tmp\update-' + $VersionName + '-e2e-' + [Guid]::NewGuid().ToString('N'))
        dotnet run --project build/UpdateContract/UpdateContract.csproj -c Release -- `
            (Join-Path $PackageDirectory 'latest.json') `
            (Join-Path $PackageDirectory "BlockHelm-Launcher-$VersionName-Update-x64.exe") `
            $baseline (Join-Path $workspace "publish\$VersionName-patch\modern\BlockHelm_Launcher_x64.exe") $testRoot
    }
    if ($LASTEXITCODE -ne 0) { throw 'Update contract verification failed.' }
}
finally { Pop-Location }
