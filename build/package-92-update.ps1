param(
    [string]$OutputDirectory = (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) '26A17092更新补丁'),
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
Push-Location $workspace
try {
    & "$PSScriptRoot\verify-91-design.ps1" -ExpectedVersion '26A17092'
    $payloadRoot = Join-Path $workspace 'publish\26A17092-patch'
    $payloads = foreach ($variant in @(
        @{Name='modern'; Framework='net8.0-windows'; Profile='WindowsModern91'; Version='26A17092'},
        @{Name='win7'; Framework='net6.0-windows'; Profile='Windows7Compatibility'; Version='26A17092-Compatible'}
    )) {
        $directory = Join-Path $payloadRoot $variant.Name
        if (-not $SkipPublish) {
            dotnet publish Launcher.App/Launcher.App.csproj -c Release -f $variant.Framework "-p:PublishProfile=$($variant.Profile)" "-p:PublishDir=$directory\" -v quiet
            if ($LASTEXITCODE -ne 0) { throw ('Launcher publish failed: ' + $variant.Name) }
        }
        $executable = Join-Path $directory 'BlockHelm_Launcher_x64.exe'
        $file = Get-Item -LiteralPath $executable
        if ($file.VersionInfo.ProductVersion -ne $variant.Version) { throw ('Wrong payload version: ' + $variant.Name) }
        [ordered]@{
            ResourceName='BlockHelm.Update.' + $variant.Name + '.exe'
            Size=$file.Length
            Sha256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $payloads | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $payloadRoot 'payloads.json') -Encoding utf8NoBOM
    $bootstrapDirectory = Join-Path $payloadRoot 'bootstrap'
    dotnet publish Launcher.UpdateBootstrap/Launcher.UpdateBootstrap.csproj -c Release "-p:PublishDir=$bootstrapDirectory\" -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Update bootstrap publish failed.' }
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $fileName = 'BlockHelm-Launcher-26A17092-Update-x64.exe'
    $destination = Join-Path $OutputDirectory $fileName
    Copy-Item -LiteralPath (Join-Path $bootstrapDirectory 'BlockHelm_Update_x64.exe') -Destination $destination -Force
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $destination).Length
    if ($size -gt 512MB) { throw 'Update exceeds the legacy client size limit.' }
    [IO.File]::WriteAllText($destination + '.sha256', $hash + '  ' + $fileName + [Environment]::NewLine)
    $manifest = [ordered]@{
        schemaVersion=1; appId='BlockHelm-Launcher'; channel='release'
        versionName='26A17092'; versionCode=648114322
        publishedAt=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        mandatory=$false; minSupportedVersionCode=0
        releaseNotes=(Get-Content -LiteralPath 'update\release\notes\26A17092.md' -Raw)
        assets=@([ordered]@{
            platform='windows'; arch='x64'; packageType='exe'; fileName=$fileName
            size=$size; sha256=$hash
            urls=@([ordered]@{name='github'; url="https://github.com/11451446545/BlockHelm-Launcher/releases/download/v26A17092-patch/$fileName"; priority=1})
        })
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'latest.json') -Encoding utf8NoBOM
    [pscustomobject]@{Update=$destination;Bytes=$size;Sha256=$hash}
}
finally { Pop-Location }
