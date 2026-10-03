$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
Push-Location $workspace
try {
    $sourceDirectory = Join-Path $workspace '.tmp/shipped94-update-contract'
    New-Item -ItemType Directory -Force -Path $sourceDirectory | Out-Null
    foreach ($name in @('RemoteManifestLauncherUpdateService','LauncherSelfUpdateService','UpdateSecurity')) {
        $source = (git show "v26A17094-patch:Launcher.Infrastructure/Updates/$name.cs") -join [Environment]::NewLine
        if ($LASTEXITCODE -ne 0) { throw 'Shipped 94 protocol source unavailable.' }
        $source = 'global using Launcher.Infrastructure.Updates;' + [Environment]::NewLine + $source.Replace('namespace Launcher.Infrastructure.Updates;', 'namespace Shipped94;')
        [IO.File]::WriteAllText((Join-Path $sourceDirectory ($name + '.cs')), $source)
    }
    $output = Join-Path $workspace '交付文件/26A17094补丁1'
    $baselineHashes = @{
        modern='6fe5fb1cb377bd08c046bf7c58eb2f864813ac4016945a8d89cf8e3f3eb776c0'
        win7='1d20d678caa479e70a424ece8efc29ab8dfb4444f0f7022aba73391524d5744e'
    }
    foreach ($variant in @('modern','win7')) {
        $baseline = Join-Path $workspace "publish/26A17094-$variant/BlockHelm_Launcher_x64.exe"
        if ((Get-FileHash -LiteralPath $baseline).Hash -ne $baselineHashes[$variant]) { throw 'Baseline differs from the published 94 payload.' }
        $root = Join-Path $workspace ('.tmp/patch94-e2e-' + $variant + '-' + [Guid]::NewGuid().ToString('N'))
        # Both baselines run on modern Windows; the bridge selects the modern payload on this host.
        $arguments = @('run','--project','build/Patch94Contract/Patch94Contract.csproj','-c','Release','--',
            (Join-Path $output 'latest-v2.json'),
            (Join-Path $output 'BlockHelm-Launcher-26A17094-Patch1-Update-x64.exe'),
            $baseline, (Join-Path $workspace 'publish/26A17094-patch.1/modern/BlockHelm_Launcher_x64.exe'), $root)
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw ('Shipped 94 patch upgrade failed: ' + $variant) }
    }
}
finally { Pop-Location }
