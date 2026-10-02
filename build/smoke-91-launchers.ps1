# Isolated startup check for the exact payloads installed by test-91-installer.ps1.
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
foreach ($variant in @('modern','win7')) {
    $directory = Join-Path $workspace ".tmp\installer-verification\$variant"
    $executable = Join-Path $directory 'BlockHelm_Launcher_x64.exe'
    if (-not (Test-Path -LiteralPath $executable)) { throw 'Run test-91-installer.ps1 first.' }
    $data = Join-Path $directory 'BHL'
    New-Item -ItemType Directory -Force -Path $data | Out-Null
    $settings = @{HasAcceptedUserAgreement=$true;Theme='Light';ThemeFollowSystem=$false;EnableDiagnosticLogging=$true}
    $settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'settings.json') -Encoding utf8
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $directory
    $start.Environment['AWP_DATA_DIRECTORY'] = Join-Path $directory 'test-roaming'
    $start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $directory 'runtime-cache'
    $process = [Diagnostics.Process]::Start($start)
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(40)
        do {
            Start-Sleep -Milliseconds 500
            $process.Refresh()
            if ($process.HasExited) { throw "Launcher exited early: $variant ($($process.ExitCode))" }
        } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
        if ($process.MainWindowHandle -eq [IntPtr]::Zero -or -not $process.Responding) { throw "No responsive launcher window: $variant" }
        Start-Sleep -Seconds 3
        & "$PSScriptRoot\capture-window.ps1" -ProcessId $process.Id -OutputPath (Join-Path $directory 'startup.png') -VisibleCapture
        if (-not (Test-Path -LiteralPath (Join-Path $data 'log'))) { throw 'Log directory is not beside the installed executable.' }
        $misplacedData = Get-ChildItem -LiteralPath (Join-Path $directory 'runtime-cache') -Directory -Recurse |
            Where-Object Name -In @('BHL','.minecraft')
        if ($misplacedData) { throw 'User data was incorrectly written inside the runtime cache.' }
        [pscustomobject]@{Variant=$variant;Version=(Get-Item -LiteralPath $executable).VersionInfo.ProductVersion;Window='PASS';DataPath='PASS'}
    }
    finally {
        if (-not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(10000)) { $process.Kill() }
        }
        $process.Dispose()
    }
}
