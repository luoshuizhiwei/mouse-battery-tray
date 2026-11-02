param(
  [string]$OutPath = '',
  [string]$AppExe = 'E:\MouseBattery\app\MouseBattery.exe'
)
$ErrorActionPreference = 'Stop'
if (-not $OutPath) {
  $dir = 'E:\MouseBattery\design-sheet\settings'
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  $OutPath = Join-Path $dir ('settings-' + (Get-Date).ToString('yyyyMMdd-HHmmss') + '.png')
}
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class SheetWin {
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
Add-Type -AssemblyName System.Drawing
# PrintWindow must run DPI-aware, otherwise the bitmap is the 96-DPI logical
# size and every small-text judgement is made on the wrong raster.
[void][SheetWin]::SetProcessDPIAware()

# --manage is routed to the resident instance by the single-instance event; no
# second reader is started and the taskbar display is never interrupted.
Start-Process -FilePath $AppExe -ArgumentList '--manage' -WindowStyle Hidden
$proc = $null
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 250
  $proc = Get-Process MouseBattery -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  if ($proc) { break }
}
if (-not $proc) { throw 'settings window did not appear within 10s' }
Start-Sleep -Milliseconds 1600
$handle = $proc.MainWindowHandle
$rect = New-Object SheetWin+RECT
[void][SheetWin]::GetWindowRect($handle, [ref]$rect)
$w = $rect.Right - $rect.Left
$ht = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$graphics = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $graphics.GetHdc()
[void][SheetWin]::PrintWindow($handle, $dc, 2)
$graphics.ReleaseHdc($dc)
$graphics.Dispose()
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ('saved ' + $OutPath + ' ' + $w + 'x' + $ht + ' physical px')
[void][SheetWin]::SendMessage($proc.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE, reader keeps running
Write-Output ('settings window closed; reader still running: ' + [bool](Get-Process MouseBattery -ErrorAction SilentlyContinue))
