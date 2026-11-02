param(
  [string]$Candidate = 'E:\MouseBattery\robust-v3.2\candidate-v411\MouseBattery.exe',
  [string]$App = 'E:\MouseBattery\app\MouseBattery.exe',
  [string]$OutDir = 'E:\MouseBattery\design-sheet\settings'
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CandWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
Add-Type -AssemblyName System.Drawing
[void][CandWin]::SetProcessDPIAware()
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$shot = Join-Path $OutDir ('settings-candidate-' + (Get-Date).ToString('yyyyMMdd-HHmmss') + '.png')

function Stop-Resident([string]$path) {
  if (Get-Process MouseBattery -ErrorAction SilentlyContinue) {
    Start-Process $path -ArgumentList '--exit' -WindowStyle Hidden
    for ($i = 0; $i -lt 40; $i++) { if (-not (Get-Process MouseBattery -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 250 }
  }
}

try {
  Stop-Resident $App
  Start-Process $Candidate -ArgumentList '--manage' -WindowStyle Hidden
  $proc = $null
  for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 250
    $proc = Get-Process MouseBattery -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($proc) { break }
  }
  if (-not $proc) { throw 'candidate settings window did not appear (possible exception in the new paint code)' }
  Start-Sleep -Milliseconds 1600
  $r = New-Object CandWin+RECT
  [void][CandWin]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc()
  [void][CandWin]::PrintWindow($proc.MainWindowHandle, $dc, 2)
  $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Write-Output ("saved $shot ${w}x${h} physical px")
  [void][CandWin]::SendMessage($proc.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
}
finally {
  Stop-Resident $Candidate
  Start-Process $App -ArgumentList '--background' -WindowStyle Hidden
  Start-Sleep -Seconds 4
  $tail = (Get-Content 'E:\MouseBattery\app\data\status.txt' -Tail 2) -join ' / '
  Write-Output ('production restored: ' + $tail)
}
