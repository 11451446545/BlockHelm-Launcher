param([switch]$SkipLauncherPublish)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$version = '26A17094'
Push-Location $workspace
try {
    & "$PSScriptRoot\package-91-universal.ps1" -VersionName $version -AllowResourceChineseChanges -SkipPublish:$SkipLauncherPublish
    $payloadRoot = Join-Path $workspace "publish\$version-patch"
    $payloads = foreach ($variant in @('modern', 'win7')) {
        $source = Join-Path $workspace "publish\$version-$variant\BlockHelm_Launcher_x64.exe"
        $destination = Join-Path $payloadRoot "$variant\BlockHelm_Launcher_x64.exe"
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
        [ordered]@{ResourceName="BlockHelm.Update.$variant.exe";Size=(Get-Item -LiteralPath $source).Length;Sha256=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()}
    }
    $payloads | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $payloadRoot 'payloads.json') -Encoding utf8NoBOM
    $bootstrap = Join-Path $payloadRoot 'bootstrap'
    dotnet publish Launcher.UpdateBootstrap/Launcher.UpdateBootstrap.csproj -c Release "-p:PublishDir=$bootstrap\" "-p:UpdatePayloadDirectory=$payloadRoot" "-p:Version=0.9.19" "-p:InformationalVersion=$version" -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Update bridge compilation failed.' }
    $output = Join-Path $workspace ('交付文件\' + $version + '更新补丁')
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $fileName = "BlockHelm-Launcher-$version-Update-x64.exe"
    $package = Join-Path $output $fileName
    Copy-Item -LiteralPath (Join-Path $bootstrap 'BlockHelm_Update_x64.exe') -Destination $package -Force
    $hash = (Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $package).Length
    if ($size -gt 512MB) { throw 'Legacy update size limit exceeded.' }
    [IO.File]::WriteAllText($package + '.sha256', "$hash  $fileName" + [Environment]::NewLine)
    $notes = Get-Content -LiteralPath "update\release\notes\$version.md" -Raw
    $manifest = [ordered]@{
        schemaVersion=1;appId='BlockHelm-Launcher';channel='release';versionName=$version;versionCode=648114324
        publishedAt=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ');mandatory=$false;minSupportedVersionCode=0
        summary='资源中心双源中文搜索与离线名称词典、更新内容展示，以及独立发布身份的更新检测。'
        releaseNotes=$notes
        assets=@([ordered]@{platform='windows';arch='x64';packageType='exe';fileName=$fileName;size=$size;sha256=$hash
            urls=@([ordered]@{name='github';url="https://github.com/11451446545/BlockHelm-Launcher/releases/download/v$version-patch/$fileName";priority=1})})
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'latest.json') -Encoding utf8NoBOM
    # Keep schema 1 for the original 91. Only upgraded clients request latest-v2.json.
    $manifest.schemaVersion = 2
    $manifest.releaseId = $version
    $manifest.releaseSequence = 648114324L
    $manifest.releaseType = 'full'
    $manifest.assets[0].delivery = 'self-update'
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'latest-v2.json') -Encoding utf8NoBOM
    [pscustomobject]@{Patch=$package;Sha256=$hash;Bytes=$size;Publication='Local artifacts only'}
}
finally { Pop-Location }
