param([string]$SheetDir = 'E:\MouseBattery\design-sheet')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$dwellSource = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Dw {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string w);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] x, int cb);
  public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
  public static void MoveTo(int x, int y) {
    int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
    var a = new INPUT[1]; a[0].type = 0;
    a[0].mi.dx = (int)(((long)(x - vx) * 65535) / (vw - 1));
    a[0].mi.dy = (int)(((long)(y - vy) * 65535) / (vh - 1));
    a[0].mi.dwFlags = 0x0001 | 0x8000 | 0x4000;
    SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
  }
  public static void Save(int x, int y, int w, int h, string p) {
    var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(b)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
    b.Save(p, ImageFormat.Png); b.Dispose();
  }
  public static long Diff(string pa, string pb, int w, int h, int tol) {
    using (var a = new Bitmap(pa)) using (var b = new Bitmap(pb)) {
      var r = new Rectangle(0, 0, w, h);
      var da = a.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      var db = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      try {
        var ba = new byte[da.Stride * h]; var bb = new byte[db.Stride * h];
        Marshal.Copy(da.Scan0, ba, 0, ba.Length); Marshal.Copy(db.Scan0, bb, 0, bb.Length);
        long n = 0;
        for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) {
          int ka = j * da.Stride + i * 4, kb = j * db.Stride + i * 4;
          if (Math.Max(Math.Abs(ba[ka] - bb[kb]), Math.Max(Math.Abs(ba[ka+1] - bb[kb+1]), Math.Abs(ba[ka+2] - bb[kb+2]))) > tol) n++;
        }
        return n;
      } finally { a.UnlockBits(da); b.UnlockBits(db); }
    }
  }
}
'@
Add-Type -TypeDefinition $dwellSource -ReferencedAssemblies @([System.Drawing.Bitmap].Assembly.Location)
[void][Dw]::SetProcessDPIAware()

$runDir = Join-Path $SheetDir ('dwell-' + (Get-Date).ToString('yyyyMMdd-HHmmss'))
$hsDir = Join-Path $runDir 'hs'
New-Item -ItemType Directory -Force -Path $hsDir | Out-Null
$cells = Join-Path $runDir 'cells.csv'
Set-Content -LiteralPath $cells -Value "T-DWELL,85,0,1,1,2,PRO X 2 DEX,1,0" -Encoding ASCII

$app = 'E:\MouseBattery\app\MouseBattery.exe'
$hostExe = 'E:\MouseBattery\design-sheet\bin\SheetHost.exe'
$orig = [System.Windows.Forms.Cursor]::Position
$proc = $null
try {
  if (Get-Process MouseBattery -ErrorAction SilentlyContinue) {
    Start-Process $app -ArgumentList '--exit' -WindowStyle Hidden
    for ($i = 0; $i -lt 40; $i++) { if (-not (Get-Process MouseBattery -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 250 }
  }
  $proc = Start-Process $hostExe -ArgumentList ('"' + $cells + '" "' + $hsDir + '"') -WorkingDirectory (Split-Path $hostExe) -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $runDir 'host.log')
  $cellPath = Join-Path $hsDir 'cell.txt'
  for ($i = 0; $i -lt 300 -and -not (Test-Path $cellPath); $i++) { Start-Sleep -Milliseconds 100 }
  if (-not (Test-Path $cellPath)) { throw 'no cell published' }
  $content = [IO.File]::ReadAllText($cellPath); Remove-Item -LiteralPath $cellPath -Force
  $parts = $content -split "`t"
  $health = $parts[$parts.Count - 1]
  function N([string]$t, [string]$k) { $m = [regex]::Match($t, ('\b' + $k + '=([0-9.]+)')); if ($m.Success) { return [double]$m.Groups[1].Value } return -1 }
  $tray = [Dw]::FindWindowW('Shell_TrayWnd', $null)
  $tr = New-Object Dw+RECT; [void][Dw]::GetWindowRect($tray, [ref]$tr)
  $scale = 1.5
  $xDip = N $health 'x'; $hostW = N $health 'width'; $pillW = N $health 'pillWidth'; $pillH = N $health 'pillHeight'
  $pillX = [int]($tr.Left + ($xDip + ($hostW - $pillW) / 2) * $scale)
  $pillY = [int]($tr.Top + (($tr.Bottom - $tr.Top) - $pillH * $scale) / 2)
  $pw = [int]($pillW * $scale); $ph = [int]($pillH * $scale)
  $hoverW = 640; $above = 400
  $hx = [int]($pillX + $pw / 2 - $hoverW / 2); $hy = [int]($tr.Top - $above)
  $hh = [int]($above + ($tr.Bottom - $tr.Top))
  $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $park = New-Object System.Drawing.Point([int]($screen.Width * 0.35), [int]($screen.Height * 0.45))

  [Dw]::MoveTo($park.X, $park.Y); Start-Sleep -Milliseconds 300
  $base = Join-Path $runDir 'base.png'; [void][Dw]::Save($hx, $hy, $hoverW, $above, $base)
  [Dw]::MoveTo([int]($pillX + $pw / 2), [int]($tr.Top + 3)); Start-Sleep -Milliseconds 120
  [Dw]::MoveTo([int]($pillX + $pw / 2), [int]($pillY + $ph / 2))
  $series = @()
  for ($t = 0; $t -lt 24; $t++) {
    Start-Sleep -Milliseconds 150
    $now = Join-Path $runDir 'now.png'
    [void][Dw]::Save($hx, $hy, $hoverW, $above, $now)
    $series += [int][Dw]::Diff($base, $now, $hoverW, $above, 24)
  }
  Write-Output ('dwell series (150ms steps, card pixels above bar): ' + ($series -join ','))
  $visible = @($series | Where-Object { $_ -ge 30000 }).Count
  Write-Output ('frames with card: ' + $visible + '/' + $series.Count)
  Set-Content -LiteralPath (Join-Path $hsDir 'ack.txt') -Value 'ok' -Encoding ASCII
}
finally {
  [Dw]::MoveTo($orig.X, $orig.Y)
  if ($proc -and -not $proc.HasExited) { try { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null } catch { } }
  Start-Process $app -ArgumentList '--background' -WindowStyle Hidden
  Start-Sleep -Seconds 4
  Write-Output ('restored reader: ' + ((Get-Process MouseBattery -ErrorAction SilentlyContinue).Count))
}
