# Runs isolated installer builds without registration, shortcuts, prerequisite
# installation or system registry writes. The distributed EXE has no test switch.
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$verificationRoot = Join-Path $workspace '.tmp\installer-verification'
New-Item -ItemType Directory -Force -Path $verificationRoot | Out-Null
$compiler = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe') |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup compiler not found.' }
foreach ($variant in @('modern','win7')) {
    $arguments = @('/Q','/DBHL_INSTALLER_VERIFY',"/O$verificationRoot","/Fverify-$variant")
    if ($variant -eq 'win7') { $arguments += '/DBHL_VERIFY_LEGACY' }
    & $compiler @arguments "$PSScriptRoot\installer\BlockHelmLauncher91.iss"
    if ($LASTEXITCODE -ne 0) { throw 'Verification installer compilation failed.' }
    $target = Join-Path $verificationRoot $variant
    $log = Join-Path $verificationRoot "$variant.log"
    $process = Start-Process -FilePath (Join-Path $verificationRoot "verify-$variant.exe") -WindowStyle Hidden -PassThru -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=`"$target`"", "/LOG=`"$log`"")
    if (-not $process.WaitForExit(60000)) { throw 'Verification installer did not finish within 60 seconds.' }
    if ($process.ExitCode -ne 0) { throw ('Verification installation failed: ' + $process.ExitCode) }
    $installed = Join-Path $target 'BlockHelm_Launcher_x64.exe'
    $expected = Join-Path $workspace "publish\26A17091-$variant\BlockHelm_Launcher_x64.exe"
    if ((Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $expected).Hash) {
        throw 'Installer selected or copied the wrong launcher payload.'
    }
    [pscustomobject]@{Variant=$variant;Version=(Get-Item -LiteralPath $installed).VersionInfo.ProductVersion;PayloadHash='PASS';Log=$log}
}
