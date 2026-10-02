param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$VisibleCapture
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CompatibilityWindowCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
}
'@
[CompatibilityWindowCapture]::SetProcessDPIAware() | Out-Null
$process = Get-Process -Id $ProcessId
$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw 'The test launcher has no main window.' }
if ($VisibleCapture) {
    [CompatibilityWindowCapture]::ShowWindow($handle, 9) | Out-Null
    [CompatibilityWindowCapture]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 500
}
$rect = [CompatibilityWindowCapture+Rect]::new()
if (-not [CompatibilityWindowCapture]::GetWindowRect($handle, [ref]$rect)) { throw 'Cannot read the window bounds.' }
$bitmap = [Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    if ($VisibleCapture) {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
    } else {
        $dc = $graphics.GetHdc()
        try { $captured = [CompatibilityWindowCapture]::PrintWindow($handle, $dc, 2) }
        finally { $graphics.ReleaseHdc($dc) }
        if (-not $captured) { throw 'Window capture failed.' }
    }
    $bitmap.Save([IO.Path]::GetFullPath($OutputPath), [Drawing.Imaging.ImageFormat]::Png)
}
finally { $graphics.Dispose(); $bitmap.Dispose() }
Get-Item -LiteralPath $OutputPath | Select-Object FullName, Length
