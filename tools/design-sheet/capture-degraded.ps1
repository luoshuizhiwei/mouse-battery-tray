param(
  [string]$Probe = 'E:\MouseBattery\robust-v3.2\degraded-probe-v420\DegradedProbe.exe',
  [string]$AppExe = 'E:\MouseBattery\app\MouseBattery.exe',
  [string]$OutDir = 'E:\MouseBattery\design-sheet\settings',
  [string]$StatusLog = 'E:\MouseBattery\app\data\status.txt'
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DegWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
Add-Type -AssemblyName System.Drawing
[void][DegWin]::SetProcessDPIAware()
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$shot = Join-Path $OutDir ('settings-degraded-' + (Get-Date).ToString('yyyyMMdd-HHmmss') + '.png')

function Stop-Any([string]$path) {
  if (Get-Process MouseBattery, DegradedProbe -ErrorAction SilentlyContinue) {
    Start-Process $path -ArgumentList '--exit' -WindowStyle Hidden
    for ($i = 0; $i -lt 40; $i++) { if (-not (Get-Process MouseBattery, DegradedProbe -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 250 }
  }
}

try {
  Stop-Any $AppExe
  Start-Process $Probe -ArgumentList '--manage' -WindowStyle Hidden
  $proc = $null
  for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 250
    $proc = Get-Process DegradedProbe -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($proc) { break }
  }
  if (-not $proc) { throw 'degraded settings window did not appear' }
  Start-Sleep -Milliseconds 1600
  $r = New-Object DegWin+RECT
  [void][DegWin]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc()
  [void][DegWin]::PrintWindow($proc.MainWindowHandle, $dc, 2)
  $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Write-Output ("saved $shot ${w}x${h} physical px")
  [void][DegWin]::SendMessage($proc.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
}
finally {
  Stop-Any $Probe
  Start-Process $AppExe -ArgumentList '--background' -WindowStyle Hidden
  Start-Sleep -Seconds 5
  if (Test-Path $StatusLog) { Write-Output ((Get-Content -LiteralPath $StatusLog -Tail 3) -join ' / ') }
}
