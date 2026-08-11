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

$manifestRepoPath = Join-Path $env:RUNNER_TEMP "blockhelm-update-manifest-$([Guid]::NewGuid().ToString('N'))"
$repositoryUrl = "https://github.com/$($env:GITHUB_REPOSITORY).git"
$previousGitConfigCount = $env:GIT_CONFIG_COUNT
$previousGitConfigKey0 = $env:GIT_CONFIG_KEY_0
$previousGitConfigValue0 = $env:GIT_CONFIG_VALUE_0
$gitBasicCredential = [Convert]::ToBase64String(
    [Text.Encoding]::ASCII.GetBytes("x-access-token:$($env:GH_TOKEN)"))
$env:GIT_CONFIG_COUNT = "1"
$env:GIT_CONFIG_KEY_0 = "http.extraheader"
$env:GIT_CONFIG_VALUE_0 = "AUTHORIZATION: basic $gitBasicCredential"

try {
    Invoke-Checked -Command "git" -Arguments @("clone", "--filter=blob:none", "--no-checkout", $repositoryUrl, $manifestRepoPath)
    Push-Location $manifestRepoPath
    try {
        Invoke-Checked -Command "git" -Arguments @("config", "user.name", "github-actions[bot]")
        Invoke-Checked -Command "git" -Arguments @("config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com")

        & git ls-remote --exit-code --heads origin $ManifestBranch *> $null
        $branchExists = $LASTEXITCODE -eq 0
        if ($branchExists) {
            Invoke-Checked -Command "git" -Arguments @("fetch", "origin", $ManifestBranch)
            Invoke-Checked -Command "git" -Arguments @("checkout", "-B", $ManifestBranch, "FETCH_HEAD")
        } else {
            Invoke-Checked -Command "git" -Arguments @("checkout", "--orphan", $ManifestBranch)
            Get-ChildItem -Force | Where-Object { $_.Name -ne ".git" } | Remove-Item -Recurse -Force
        }

        $targetDirectory = Join-Path (Join-Path (Get-Location).Path "update") $Channel
        New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
        Copy-Item -LiteralPath $generatedManifestPath -Destination (Join-Path $targetDirectory "latest.json") -Force
        Invoke-Checked -Command "git" -Arguments @("add", "update/$Channel/latest.json")

        & git diff --cached --quiet
        if ($LASTEXITCODE -ne 0) {
            Invoke-Checked -Command "git" -Arguments @("commit", "-m", "Update $Channel manifest for $VersionName [skip ci]")
        }

        Invoke-Checked -Command "git" -Arguments @("push", "origin", "HEAD:$ManifestBranch")
        $manifestCommit = (git rev-parse HEAD).Trim()
    } finally {
        Pop-Location
    }
} finally {
    if (Test-Path -LiteralPath $manifestRepoPath) {
        Remove-Item -LiteralPath $manifestRepoPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $previousGitConfigCount) {
        Remove-Item Env:GIT_CONFIG_COUNT -ErrorAction SilentlyContinue
    } else {
        $env:GIT_CONFIG_COUNT = $previousGitConfigCount
    }
    if ($null -eq $previousGitConfigKey0) {
        Remove-Item Env:GIT_CONFIG_KEY_0 -ErrorAction SilentlyContinue
    } else {
        $env:GIT_CONFIG_KEY_0 = $previousGitConfigKey0
    }
    if ($null -eq $previousGitConfigValue0) {
        Remove-Item Env:GIT_CONFIG_VALUE_0 -ErrorAction SilentlyContinue
    } else {
        $env:GIT_CONFIG_VALUE_0 = $previousGitConfigValue0
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
