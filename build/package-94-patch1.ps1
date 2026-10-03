param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$version = '26A17094-Patch1'
$releaseId = '26A17094-patch.1'
Push-Location $workspace
try {
    & "$PSScriptRoot/verify-91-design.ps1" -ExpectedVersion $version -AllowResourceChineseChanges
    $payloadRoot = Join-Path $workspace "publish/$releaseId"
    $payloads = foreach ($variant in @(
        @{Name='modern';Framework='net8.0-windows';Profile='WindowsModern91';Version=$version},
        @{Name='win7';Framework='net6.0-windows';Profile='Windows7Compatibility';Version="$version-Compatible"}
    )) {
        $destination = Join-Path $payloadRoot $variant.Name
        if (-not $SkipPublish) {
            dotnet publish Launcher.App/Launcher.App.csproj -c Release -f $variant.Framework "-p:PublishProfile=$($variant.Profile)" "-p:PublishDir=$destination\" -v quiet | Out-Host
            if ($LASTEXITCODE -ne 0) { throw ('Launcher publish failed: ' + $variant.Name) }
        }
        $exe = Join-Path $destination 'BlockHelm_Launcher_x64.exe'
        if ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -ne $variant.Version) { throw 'Wrong patch payload version.' }
        [ordered]@{ResourceName="BlockHelm.Update.$($variant.Name).exe";Size=(Get-Item -LiteralPath $exe).Length;Sha256=(Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant()}
    }
    $payloads | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $payloadRoot 'payloads.json') -Encoding utf8NoBOM
    $bootstrap = Join-Path $payloadRoot 'bootstrap'
    dotnet publish Launcher.UpdateBootstrap/Launcher.UpdateBootstrap.csproj -c Release "-p:PublishDir=$bootstrap\" "-p:UpdatePayloadDirectory=$payloadRoot" '-p:Version=0.9.19.1' "-p:InformationalVersion=$version" -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Patch bootstrap compilation failed.' }
    $output = Join-Path $workspace '交付文件/26A17094补丁1'
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $fileName = "BlockHelm-Launcher-$version-Update-x64.exe"
    $package = Join-Path $output $fileName
    Copy-Item -LiteralPath (Join-Path $bootstrap 'BlockHelm_Update_x64.exe') -Destination $package -Force
    $hash = (Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $package).Length
    if ($size -gt 512MB) { throw 'Update size limit exceeded.' }
    [IO.File]::WriteAllText($package + '.sha256', "$hash  $fileName" + [Environment]::NewLine)
    $manifest = [ordered]@{
        schemaVersion=2;appId='BlockHelm-Launcher';channel='release';versionName=$version
        releaseId=$releaseId;releaseSequence=648114325L;releaseType='patch';baseReleaseIds=@('26A17094')
        publishedAt=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ');mandatory=$false;minSupportedVersionCode=0
        summary='移除资源列表顶部加载更多与来源提示行，恢复原有模糊过渡，保留滚动自动加载。'
        releaseNotes=(Get-Content -LiteralPath "update/release/notes/$version.md" -Raw)
        assets=@([ordered]@{platform='windows';arch='x64';packageType='exe';delivery='self-update';fileName=$fileName;size=$size;sha256=$hash
            urls=@([ordered]@{name='github';url="https://github.com/11451446545/BlockHelm-Launcher/releases/download/v$releaseId/$fileName";priority=1})})
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'latest-v2.json') -Encoding utf8NoBOM
    [pscustomobject]@{Patch=$package;Sha256=$hash;Bytes=$size;ReleaseId=$releaseId;Publication='Local artifacts only'}
}
finally { Pop-Location }
