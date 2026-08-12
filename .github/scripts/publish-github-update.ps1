[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("release", "beta")]
    [string]$Channel,

    [Parameter(Mandatory = $true)]
    [string]$VersionName,

    [Parameter(Mandatory = $true)]
    [int]$VersionCode,

    [Parameter(Mandatory = $true)]
    [string]$NotesPath,

    [Parameter(Mandatory = $true)]
    [string]$ManifestTemplate,

    [Parameter(Mandatory = $true)]
    [string]$AssetPath,

    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ManifestBranch,

    [switch]$Prerelease
)

$ErrorActionPreference = "Stop"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
    throw "GITHUB_REPOSITORY is not available."
}

if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    throw "GH_TOKEN is not available."
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME)) {
    throw "GITHUB_REF_NAME is not available."
}

$workspace = if ([string]::IsNullOrWhiteSpace($env:GITHUB_WORKSPACE)) {
    (Get-Location).Path
} else {
    $env:GITHUB_WORKSPACE
}

$notesPath = (Resolve-Path (Join-Path $workspace $NotesPath)).Path
$templatePath = (Resolve-Path (Join-Path $workspace $ManifestTemplate)).Path
$assetPath = (Resolve-Path (Join-Path $workspace $AssetPath)).Path
$publishDirectoryPath = Join-Path $workspace $PublishDirectory
$generatedManifestPath = Join-Path $publishDirectoryPath "latest.json"

if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
    throw "Release asset was not created: $AssetPath"
}

$asset = Get-Item -LiteralPath $assetPath
$assetName = $asset.Name
$assetHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $assetPath).Hash.ToLowerInvariant()
$downloadUrl = "https://github.com/$($env:GITHUB_REPOSITORY)/releases/download/$($env:GITHUB_REF_NAME)/$assetName"
$releaseTitle = if ($Prerelease) {
    "BlockHelm Launcher $VersionName Beta"
} else {
    "BlockHelm Launcher $VersionName"
}

$manifest = Get-Content -LiteralPath $templatePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.appId -ne "BlockHelm-Launcher") {
    throw "The update manifest template is invalid."
}

$assets = @($manifest.assets)
if ($assets.Count -ne 1) {
    throw "The update manifest template must contain exactly one asset."
}

$manifest.channel = $Channel
$manifest.versionName = $VersionName
$manifest.versionCode = $VersionCode
$manifest.publishedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$manifest.releaseNotes = Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8
$assets[0].fileName = $assetName
$assets[0].size = $asset.Length
$assets[0].sha256 = $assetHash
$assets[0].urls = @([pscustomobject]@{
        name = "github"
        url = $downloadUrl
        priority = 1
    })
$manifest.assets = $assets

New-Item -ItemType Directory -Force -Path $publishDirectoryPath | Out-Null
$manifestJson = ($manifest | ConvertTo-Json -Depth 20).Replace("`r`n", "`n")
[IO.File]::WriteAllText($generatedManifestPath, $manifestJson, [Text.UTF8Encoding]::new($false))

$releaseView = & gh release view $env:GITHUB_REF_NAME --json tagName 2>$null
$releaseViewExitCode = $LASTEXITCODE
if ($releaseViewExitCode -eq 0) {
    $releaseEditArguments = @(
        "release", "edit", $env:GITHUB_REF_NAME,
        "--title", $releaseTitle,
        "--notes-file", $notesPath,
        "--draft=false"
    )
    if ($Prerelease) {
        $releaseEditArguments += @("--prerelease", "--latest=false")
    } else {
        $releaseEditArguments += @("--prerelease=false", "--latest")
    }
    Invoke-Checked -Command "gh" -Arguments $releaseEditArguments
    Invoke-Checked -Command "gh" -Arguments @(
        "release", "upload", $env:GITHUB_REF_NAME,
        $assetPath, $generatedManifestPath,
        "--clobber"
    )
} elseif ($releaseViewExitCode -eq 1) {
    $ghArguments = @(
        "release", "create", $env:GITHUB_REF_NAME,
        $assetPath, $generatedManifestPath,
        "--title", $releaseTitle,
        "--notes-file", $notesPath
    )
    if ($Prerelease) {
        $ghArguments += @("--prerelease", "--latest=false")
    } else {
        $ghArguments += "--latest"
    }
    Invoke-Checked -Command "gh" -Arguments $ghArguments
} else {
    throw "The existing GitHub release could not be inspected. ExitCode=$releaseViewExitCode"
}

$manifestApiPath = "repos/$($env:GITHUB_REPOSITORY)/contents/update/$Channel/latest.json"
$existingManifest = $null
$existingManifestJson = & gh api "$manifestApiPath`?ref=$ManifestBranch" 2>$null
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($existingManifestJson -join "`n"))) {
    $existingManifest = ($existingManifestJson -join "`n") | ConvertFrom-Json
} elseif ($LASTEXITCODE -ne 1) {
    throw "The existing update manifest could not be inspected."
}

$manifestContent = [Convert]::ToBase64String([IO.File]::ReadAllBytes($generatedManifestPath))
$manifestRequest = [ordered]@{
    message = "Update $Channel manifest for $VersionName [skip ci]"
    content = $manifestContent
    branch = $ManifestBranch
}
if ($null -ne $existingManifest -and -not [string]::IsNullOrWhiteSpace($existingManifest.sha)) {
    $manifestRequest.sha = $existingManifest.sha
}

$manifestRequestPath = Join-Path $env:RUNNER_TEMP "blockhelm-manifest-request-$([Guid]::NewGuid().ToString('N')).json"
try {
    $manifestRequestJson = ($manifestRequest | ConvertTo-Json -Depth 10).Replace("`r`n", "`n")
    [IO.File]::WriteAllText($manifestRequestPath, $manifestRequestJson, [Text.UTF8Encoding]::new($false))
    $manifestResponseJson = & gh api `
        --method PUT `
        $manifestApiPath `
        --input $manifestRequestPath
    if ($LASTEXITCODE -ne 0) {
        throw "The update manifest could not be published through the GitHub Contents API."
    }

    $manifestResponse = ($manifestResponseJson -join "`n") | ConvertFrom-Json
    $manifestCommit = $manifestResponse.commit.sha
    if ([string]::IsNullOrWhiteSpace($manifestCommit)) {
        throw "The GitHub Contents API did not return the manifest commit SHA."
    }
} finally {
    if (Test-Path -LiteralPath $manifestRequestPath) {
        Remove-Item -LiteralPath $manifestRequestPath -Force -ErrorAction SilentlyContinue
    }
}

$verifyApiPath = Join-Path $env:RUNNER_TEMP "blockhelm-manifest-api-verify-$([Guid]::NewGuid().ToString('N')).json"
$verifyPath = Join-Path $env:RUNNER_TEMP "blockhelm-manifest-verify-$([Guid]::NewGuid().ToString('N')).json"
$verifyUrl = "https://raw.githubusercontent.com/$($env:GITHUB_REPOSITORY)/$manifestCommit/update/$Channel/latest.json"
try {
    $apiContent = & gh api "repos/$($env:GITHUB_REPOSITORY)/contents/update/$Channel/latest.json?ref=$manifestCommit" --jq .content
    if ($LASTEXITCODE -ne 0) {
        throw "The GitHub Contents API could not read the published manifest."
    }
    $apiBase64 = ($apiContent -join "") -replace '\s', ''
    [IO.File]::WriteAllBytes($verifyApiPath, [Convert]::FromBase64String($apiBase64))
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $verifyApiPath).Hash -ne
        (Get-FileHash -Algorithm SHA256 -LiteralPath $generatedManifestPath).Hash) {
        throw "The GitHub Contents API manifest did not match the generated manifest."
    }

    $verified = $false
    for ($attempt = 1; $attempt -le 12; $attempt++) {
        try {
            Invoke-WebRequest -Uri $verifyUrl -OutFile $verifyPath -UseBasicParsing -TimeoutSec 60
            if ((Get-FileHash -Algorithm SHA256 -LiteralPath $verifyPath).Hash -eq
                (Get-FileHash -Algorithm SHA256 -LiteralPath $generatedManifestPath).Hash) {
                $verified = $true
                break
            }
        } catch {
            if ($attempt -eq 12) {
                throw
            }
        }
        Start-Sleep -Seconds 5
    }

    if (-not $verified) {
        throw "The published GitHub manifest did not match the generated manifest."
    }
} finally {
    if (Test-Path -LiteralPath $verifyApiPath) {
        Remove-Item -LiteralPath $verifyApiPath -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $verifyPath) {
        Remove-Item -LiteralPath $verifyPath -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Published GitHub update. Repository=$($env:GITHUB_REPOSITORY) Version=$VersionName Channel=$Channel ManifestCommit=$manifestCommit"
