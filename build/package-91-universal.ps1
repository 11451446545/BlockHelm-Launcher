param(
    [string]$OutputDirectory,
    [switch]$SkipPublish,
    [ValidatePattern('^[0-9A-F]{8}$')][string]$VersionName = '26A17091',
    [switch]$AllowResourceChineseChanges
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $workspace ('交付文件\' + $VersionName + '统一安装包') }
Push-Location $workspace
try {
    & "$PSScriptRoot\verify-91-design.ps1" -ExpectedVersion $VersionName -AllowResourceChineseChanges:$AllowResourceChineseChanges
    $variants = @(
        @{Name='modern'; Framework='net8.0-windows'; Profile='WindowsModern91'; Version=$VersionName; RuntimeMajor=8},
        @{Name='win7'; Framework='net6.0-windows'; Profile='Windows7Compatibility'; Version=($VersionName + '-Compatible'); RuntimeMajor=6}
    )
    foreach ($variant in $variants) {
        $payload = Join-Path $workspace ("publish\$VersionName-" + $variant.Name)
        if (-not $SkipPublish) {
            dotnet publish Launcher.App/Launcher.App.csproj -c Release -f $variant.Framework "-p:PublishProfile=$($variant.Profile)" "-p:PublishDir=$payload\" -v quiet
            if ($LASTEXITCODE -ne 0) { throw ('Publish failed: ' + $variant.Name) }
        }
        $executable = Join-Path $payload 'BlockHelm_Launcher_x64.exe'
        if ((Get-Item -LiteralPath $executable).VersionInfo.ProductVersion -ne $variant.Version) {
            throw ('Wrong launcher version: ' + $variant.Name)
        }
        $metadata = Join-Path $workspace ('publish\bundle-metadata\' + $variant.Name)
        $entries = & "$PSScriptRoot\inspect-bundle.ps1" -Executable $executable -ExtractTo $metadata -Files @('BlockHelm_Launcher_x64.runtimeconfig.json')
        if ($entries | Where-Object Path -Match '(?i)(^|/)(qt[^/]*|qml)(/|\.)') { throw 'Unexpected Qt payload.' }
        $config = Get-Content (Join-Path $metadata 'BlockHelm_Launcher_x64.runtimeconfig.json') -Raw | ConvertFrom-Json
        $runtime = $config.runtimeOptions.includedFrameworks | Where-Object name -EQ 'Microsoft.NETCore.App'
        if (-not $runtime -or ([version]$runtime.version).Major -ne $variant.RuntimeMajor) { throw 'Wrong or missing self-contained runtime.' }
        $packageFolders = (Get-Content 'Launcher.App\obj\project.assets.json' -Raw | ConvertFrom-Json).packageFolders.PSObject.Properties.Name
        $licenses = Join-Path $payload 'licenses'
        New-Item -ItemType Directory -Force -Path $licenses | Out-Null
        Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $licenses 'BlockHelm-GPL-3.0.txt') -Force
        Copy-Item -LiteralPath 'build\COMPATIBILITY-NOTICES.txt' -Destination $licenses -Force
        $searchNotices = 'Launcher.Infrastructure\Resources\SearchData\SOURCE.md'
        if (Test-Path -LiteralPath $searchNotices) {
            Copy-Item -LiteralPath $searchNotices -Destination (Join-Path $licenses 'RESOURCE-SEARCH-SOURCES.md') -Force
        }
        $noticePath = Join-Path $licenses 'COMPATIBILITY-NOTICES.txt'
        $notice = [IO.File]::ReadAllText($noticePath).Replace('BlockHelm Launcher 26A17091 universal', "BlockHelm Launcher $VersionName universal").Replace('selects 26A17091 with', "selects $VersionName with").Replace('or 26A17091-Compatible', "or $VersionName-Compatible")
        [IO.File]::WriteAllText($noticePath, $notice)
        foreach ($package in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
            foreach ($notice in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
                $source = $packageFolders | ForEach-Object { Join-Path $_ "$package/$($runtime.version)/$notice" } |
                    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
                if ($source) { Copy-Item -LiteralPath $source -Destination (Join-Path $licenses "$package-$notice.txt") -Force }
            }
        }
    }
    $prerequisites = @(
        @{Name='vc_redist.x64.exe'; Sha256='6AFAE68A783F11292149175844AED0E2CE3F247BC0250F6CB18C931295B3F399'; Url='https://aka.ms/vs/16/release/vc_redist.x64.exe'},
        @{Name='Windows6.1-KB3063858-x64.msu'; Sha256='6FEC4E38CDCBDAA334937A2EF38BAD6800E9C80CB513183451B4049E84479A85'; Url='https://download.microsoft.com/download/0/8/E/08E0386B-F6AF-4651-8D1B-C0A95D2731F0/Windows6.1-KB3063858-x64.msu'}
    )
    New-Item -ItemType Directory -Force -Path 'publish\prerequisites' | Out-Null
    foreach ($prerequisite in $prerequisites) {
        $path = Join-Path $workspace ('publish\prerequisites\' + $prerequisite.Name)
        if (-not (Test-Path -LiteralPath $path)) { Invoke-WebRequest -Uri $prerequisite.Url -OutFile $path }
        if ((Get-FileHash -LiteralPath $path).Hash -ne $prerequisite.Sha256) { throw 'Prerequisite checksum mismatch.' }
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
            throw 'Prerequisite signature validation failed.'
        }
    }
    $compiler = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe') |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $compiler) { throw 'Inno Setup 6 compiler not found.' }
    [xml]$appProject = Get-Content 'Launcher.App\Launcher.App.csproj'
    $fileVersion = @($appProject.Project.PropertyGroup.FileVersion | Where-Object { $_ })[0]
    & $compiler /Q "/DLauncherVersion=$VersionName" "/DInstallerFileVersion=$fileVersion" "/DWindows7Source=$workspace\publish\$VersionName-win7" "/DModernSource=$workspace\publish\$VersionName-modern" "$PSScriptRoot\installer\BlockHelmLauncher91.iss"
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $workspace ("publish\installer\BlockHelm-Launcher-$VersionName-Universal-Setup-x64.exe")
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $destination = Join-Path $OutputDirectory (Split-Path -Leaf $installer)
    Copy-Item -LiteralPath $installer -Destination $destination -Force
    $hash = (Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($destination + '.sha256', $hash + '  ' + (Split-Path -Leaf $destination) + [Environment]::NewLine)
    [pscustomobject]@{Installer=$destination;Sha256=$hash}
}
finally { Pop-Location }
