param(
  [string]$SheetDir = 'E:\MouseBattery\design-sheet',
  [string]$AppExe = 'E:\MouseBattery\app\MouseBattery.exe',
  [string]$HostExe = 'E:\MouseBattery\design-sheet\bin\SheetHost.exe',
  [string]$Cells = '',
  [string]$StatusLog = 'E:\MouseBattery\app\data\status.txt',
  [switch]$KeepAppRunning
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
if (-not $Cells) { $Cells = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'cells.csv' }

$imgSource = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Img {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string win);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int cbSize);
  public struct RECT { public int Left, Top, Right, Bottom; }

  // SetCursorPos only relocates the cursor; it posts no mouse message, so the
  // taskbar XAML island never sees a pointer-enter and the card stays hidden.
  [StructLayout(LayoutKind.Sequential)]
  public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)]
  public struct INPUT { public uint type; public MOUSEINPUT mi; }
  public static void MoveTo(int x, int y) {
    int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
    var inputs = new INPUT[1];
    inputs[0].type = 0;
    inputs[0].mi.dx = (int)(((long)(x - vx) * 65535) / (vw - 1));
    inputs[0].mi.dy = (int)(((long)(y - vy) * 65535) / (vh - 1));
    inputs[0].mi.dwFlags = 0x0001 | 0x8000 | 0x4000; // MOVE | ABSOLUTE | VIRTUALDESK
    SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
  }

  public static void Save(int x, int y, int w, int h, string path) {
    var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(b)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
    b.Save(path, ImageFormat.Png);
    b.Dispose();
  }

  // [0]median [1]mean [2]inkLeft [3]inkRight [4]inkTop [5]inkBottom [6]inkCount
  // [7..9] ink mean BGR  [10..12] region mean BGR
  public static double[] Stats(string path, int x, int y, int w, int h, double threshold) {
    using (var bmp = new Bitmap(path)) {
      var rect = new Rectangle(x, y, w, h);
      var d = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      try {
        int st = d.Stride;
        var buf = new byte[st * h];
        Marshal.Copy(d.Scan0, buf, 0, buf.Length);
        var lum = new double[w * h];
        var samples = new List<double>();
        for (int j = 0; j < h; j++) {
          for (int i = 0; i < w; i++) {
            int k = j * st + i * 4;
            double l = 0.114 * buf[k] + 0.587 * buf[k + 1] + 0.299 * buf[k + 2];
            lum[j * w + i] = l;
            if (((i + j) & 3) == 0) samples.Add(l);
          }
        }
        samples.Sort();
        double median = samples[samples.Count / 2];
        double mean = 0, bSum = 0, gSum = 0, rSum = 0, inkB = 0, inkG = 0, inkR = 0, inkSum = 0;
        int left = -1, right = -1, top = -1, bot = -1, count = 0;
        for (int j = 0; j < h; j++) {
          for (int i = 0; i < w; i++) {
            int k = j * st + i * 4;
            double l = lum[j * w + i];
            mean += l; bSum += buf[k]; gSum += buf[k + 1]; rSum += buf[k + 2];
            if (Math.Abs(l - median) > threshold) {
              count++; inkSum += l; inkB += buf[k]; inkG += buf[k + 1]; inkR += buf[k + 2];
              if (left < 0) left = i;
              right = i;
              if (top < 0) top = j;
              bot = j;
            }
          }
        }
        int n = w * h;
        double inkMean = count > 0 ? inkSum / count : 0;
        return new double[] {
          median, mean / n, left, right, top, bot, count,
          count > 0 ? inkB / count : 0, count > 0 ? inkG / count : 0, count > 0 ? inkR / count : 0,
          bSum / n, gSum / n, rSum / n
        };
      } finally { bmp.UnlockBits(d); }
    }
  }

  public static double Diff(string pa, string pb, int x, int y, int w, int h, double threshold) {
    using (var a = new Bitmap(pa)) using (var b = new Bitmap(pb)) {
      var rect = new Rectangle(x, y, w, h);
      var da = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      var db = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      try {
        var bufA = new byte[da.Stride * h];
        var bufB = new byte[db.Stride * h];
        Marshal.Copy(da.Scan0, bufA, 0, bufA.Length);
        Marshal.Copy(db.Scan0, bufB, 0, bufB.Length);
        long changed = 0;
        for (int j = 0; j < h; j++) {
          for (int i = 0; i < w; i++) {
            int ka = j * da.Stride + i * 4, kb = j * db.Stride + i * 4;
            double d = Math.Max(Math.Abs(bufA[ka] - bufB[kb]),
                        Math.Max(Math.Abs(bufA[ka + 1] - bufB[kb + 1]), Math.Abs(bufA[ka + 2] - bufB[kb + 2])));
            if (d > threshold) changed++;
          }
        }
        return changed;
      } finally { a.UnlockBits(da); b.UnlockBits(db); }
    }
  }

  static double Chan(double c) { c /= 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
  public static double Contrast(double r1, double g1, double b1, double r2, double g2, double b2) {
    double l1 = 0.2126 * Chan(r1) + 0.7152 * Chan(g1) + 0.0722 * Chan(b1);
    double l2 = 0.2126 * Chan(r2) + 0.7152 * Chan(g2) + 0.0722 * Chan(b2);
    double hi = Math.Max(l1, l2), lo = Math.Min(l1, l2);
    return (hi + 0.05) / (lo + 0.05);
  }

  // Bounding box of the pixels that changed between two grabs of the same
  // region: that box is the card itself, so its size can be checked against
  // the 240 x 208 DIP spec instead of trusted.
  public static double[] DiffBox(string pa, string pb, int x, int y, int w, int h, double threshold) {
    using (var a = new Bitmap(pa)) using (var b = new Bitmap(pb)) {
      var rect = new Rectangle(x, y, w, h);
      var da = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      var db = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      try {
        var bufA = new byte[da.Stride * h];
        var bufB = new byte[db.Stride * h];
        Marshal.Copy(da.Scan0, bufA, 0, bufA.Length);
        Marshal.Copy(db.Scan0, bufB, 0, bufB.Length);
        long count = 0;
        int left = -1, right = -1, top = -1, bot = -1;
        for (int j = 0; j < h; j++) {
          for (int i = 0; i < w; i++) {
            int ka = j * da.Stride + i * 4, kb = j * db.Stride + i * 4;
            double d = Math.Max(Math.Abs(bufA[ka] - bufB[kb]),
                        Math.Max(Math.Abs(bufA[ka + 1] - bufB[kb + 1]), Math.Abs(bufA[ka + 2] - bufB[kb + 2])));
            if (d > threshold) {
              count++;
              if (left < 0) left = i;
              right = i;
              if (top < 0) top = j;
              bot = j;
            }
          }
        }
        return new double[] { left, right, top, bot, count };
      } finally { a.UnlockBits(da); b.UnlockBits(db); }
    }
  }
}
'@
$refs = @([System.Drawing.Bitmap].Assembly.Location, [System.Windows.Forms.Screen].Assembly.Location)
Add-Type -TypeDefinition $imgSource -ReferencedAssemblies $refs

[void][Img]::SetProcessDPIAware()
$runDir = Join-Path $SheetDir ('run-' + (Get-Date).ToString('yyyyMMdd-HHmmss'))
$pngDir = Join-Path $runDir 'png'
$hsDir = Join-Path $runDir 'hs'
New-Item -ItemType Directory -Force -Path $pngDir | Out-Null
New-Item -ItemType Directory -Force -Path $hsDir | Out-Null

$dpi = [Img]::GetDpiForSystem()
$scale = $dpi / 96.0
$tray = [Img]::FindWindowW('Shell_TrayWnd', $null)
$trayRect = New-Object Img+RECT
[void][Img]::GetWindowRect($tray, [ref]$trayRect)
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$barTop = $trayRect.Top
$barH = $trayRect.Bottom - $trayRect.Top
$originalCursor = [System.Windows.Forms.Cursor]::Position
$parkPoint = New-Object System.Drawing.Point(([int]($screen.Width * 0.35)), [int]($screen.Height * 0.45))
Write-Output ("env scale=" + $scale + " tray=" + $trayRect.Left + "," + $trayRect.Top + "," + $trayRect.Right + "," + $trayRect.Bottom + " screen=" + $screen.Width + "x" + $screen.Height)

$cellPath = Join-Path $hsDir 'cell.txt'
$ackPath = Join-Path $hsDir 'ack.txt'
$donePath = Join-Path $hsDir 'done.txt'
$rows = New-Object System.Collections.Generic.List[string]
$rows.Add("id`tline`tconfirm`txDip`tpillPxX`tpillPxY`tpillPxW`tpillPxH`textPxX`textPxY`thoverX`thoverY`thoverW`thoverH`trst_med`trst_mean`trst_inkL`trst_inkR`trst_inkCount`trst_contrast`thov_med`thov_mean`thov_inkL`thov_inkR`tdeltaAbs`tdeltaPct`tcardPixels`tcardWDip`tcardHDip`thealth")
$hostProcess = $null
$verdict = 'clean'

function Grab([int]$x, [int]$y, [int]$w, [int]$h, [string]$path) {
  $cx = [Math]::Max(0, $x); $cy = [Math]::Max(0, $y)
  $cw = [Math]::Min($w, $screen.Width - $cx); $ch = [Math]::Min($h, $screen.Height - $cy)
  if ($cw -le 0 -or $ch -le 0) { throw "crop off screen: $x $y $w $h" }
  [Img]::Save($cx, $cy, $cw, $ch, $path)
  return @{ X = $cx; Y = $cy; W = $cw; H = $ch }
}
function Num([string]$text, [string]$name) {
  $m = [regex]::Match($text, ('\b' + $name + '=([0-9.]+)'))
  if ($m.Success) { return [double]$m.Groups[1].Value }
  return -1
}
function Field([string]$text, [string]$name) {
  $m = [regex]::Match($text, ('\b' + $name + '=([^\s]+)'))
  if ($m.Success) { return $m.Groups[1].Value }
  return ''
}

try {
  if (-not $KeepAppRunning) {
    if (Get-Process MouseBattery -ErrorAction SilentlyContinue) {
      Write-Output 'stopping the resident reader (its 500ms republish would overwrite the sheet)'
      Start-Process -FilePath $AppExe -ArgumentList '--exit' -WindowStyle Hidden
      for ($i = 0; $i -lt 40; $i++) {
        if (-not (Get-Process MouseBattery -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 250
      }
      if (Get-Process MouseBattery -ErrorAction SilentlyContinue) { throw 'resident reader did not exit within 10s' }
    }
  }

  $hostProcess = Start-Process -FilePath $HostExe -ArgumentList ('"' + $Cells + '" "' + $hsDir + '"') `
    -WorkingDirectory (Split-Path -Parent $HostExe) -NoNewWindow -PassThru `
    -RedirectStandardOutput (Join-Path $runDir 'sheet-host.log') -RedirectStandardError (Join-Path $runDir 'sheet-host.err')

  $guard = 0
  while ($true) {
    if (Test-Path $donePath) { break }
    if ($hostProcess.HasExited) { Start-Sleep -Milliseconds 400; if (-not (Test-Path $donePath)) { $verdict = 'host exited early'; break } else { break } }
    $guard++
    if ($guard -gt 600) { $verdict = 'watchdog'; break }

    $content = $null
    for ($i = 0; $i -lt 300; $i++) {
      if ((Test-Path $donePath) -or $hostProcess.HasExited) { break }
      if (Test-Path $cellPath) {
        try { $content = [IO.File]::ReadAllText($cellPath); Remove-Item -LiteralPath $cellPath -Force; break }
        catch [IO.FileNotFoundException] { Start-Sleep -Milliseconds 60; continue }
        catch { Start-Sleep -Milliseconds 60; continue }
      }
      Start-Sleep -Milliseconds 100
    }
    if (-not $content) {
      if ((Test-Path $donePath) -or $hostProcess.HasExited) { break }
      $verdict = 'no cell within 30s'; break
    }

    $parts = $content -split "`t"
    $id = $parts[0]; $line = $parts[1]; $confirm = $parts[2]; $health = $parts[3]

    $xDip = Num $health 'x'
    $hostWDip = Num $health 'width'
    $pillWDip = Num $health 'pillWidth'
    $pillHDip = Num $health 'pillHeight'
    $insetDip = [Math]::Max(0, ($hostWDip - $pillWDip) / 2)
    $pillW = [int]([Math]::Round(($pillWDip - 1) * $scale))   # drop the 1 DIP border each side for the fill measure
    $pillH = [int]([Math]::Round($pillHDip * $scale))
    $pillX = [int]([Math]::Round($trayRect.Left + ($xDip + $insetDip) * $scale))
    $pillY = [int]([Math]::Round($barTop + ($barH - ($pillHDip * $scale)) / 2))

    $restW = [Math]::Min(760, $screen.Width - ($pillX - 140))
    $hoverW = 640; $hoverAbove = 400
    $hover = Grab ([int]($pillX + $pillW / 2 - $hoverW / 2)) ([int]($barTop - $hoverAbove)) $hoverW ([int]($hoverAbove + $barH)) (Join-Path $runDir 'probe-hover-rect.png')
    $cardH = [Math]::Max(1, $barTop - $hover.Y)

    # Park first: the previous cell left the pointer on the pill, which would
    # have lit the glass into what is supposed to be the resting frame.
    [Img]::MoveTo($parkPoint.X, $parkPoint.Y)
    Start-Sleep -Milliseconds 260

    $restFile = Join-Path $pngDir ('rest-' + $id + '.png')
    $rest = Grab ($pillX - 140) $barTop $restW $barH $restFile
    [void][Img]::Save($hover.X, $hover.Y, $hover.W, $hover.H, (Join-Path $runDir 'probe-hover-base.png'))

    # rest measurements (the numeral's own ink over the real taskbar backdrop)
    $relPillX = [int]($pillX - $rest.X); $relPillY = [int]($pillY - $rest.Y)
    $restStats = [Img]::Stats($restFile, $relPillX, $relPillY, $pillW, $pillH, 40)
    $restContrast = if ($restStats[6] -gt 40) { [Img]::Contrast($restStats[9], $restStats[8], $restStats[7], $restStats[12], $restStats[11], $restStats[10]) } else { 0 }

    $centreX = $pillX + [int]($pillW / 2); $centreY = $pillY + [int]($pillH / 2)
    $hoverFile = Join-Path $pngDir ('hover-' + $id + '.png')
    $nowPath = Join-Path $runDir 'probe-hover-now.png'
    $basePath = Join-Path $runDir 'probe-hover-base.png'
    # The card lives inside a show delay while the native side repaints the
    # tooltip's own brush every 500ms, which can dismiss it again. A single
    # sampled moment therefore misses it: burst-sample and keep the frame with
    # the largest changed area above the bar. Wallpaper Engine also animates
    # that region, so only a card-sized block counts as a real appearance.
    $best = 0.0; $cardBox = @(0, -1, 0, -1)
    for ($round = 0; $round -lt 3; $round++) {
      [Img]::MoveTo($centreX, ($barTop + 3)); Start-Sleep -Milliseconds 90
      [Img]::MoveTo($centreX, $centreY)
      for ($t = 0; $t -lt 24; $t++) {
        Start-Sleep -Milliseconds 80
        [void][Img]::Save($hover.X, $hover.Y, $hover.W, $hover.H, $nowPath)
        $box = [Img]::DiffBox($basePath, $nowPath, 0, 0, $hover.W, $cardH, 24)
        if ($box[4] -gt $best) {
          $best = $box[4]
          $cardBox = @($box[0], $box[1], $box[2], $box[3])
          Copy-Item -LiteralPath $nowPath -Destination $hoverFile -Force
        }
        if ($best -ge 30000) { break }
      }
      if ($best -ge 30000) { break }
    }
    $cardPixels = $best
    if (-not (Test-Path $hoverFile)) { [void][Img]::Save($hover.X, $hover.Y, $hover.W, $hover.H, $hoverFile) }

    $relHoverX = [int]($pillX - $hover.X); $relHoverY = [int]($pillY - $hover.Y)
    $hovStats = [Img]::Stats($hoverFile, $relHoverX, $relHoverY, $pillW, $pillH, 40)
    $deltaAbs = $hovStats[0] - $restStats[0]
    $deltaPct = if ($restStats[0] -gt 1) { $deltaAbs / $restStats[0] } else { 0 }

    $rows.Add(($id, $line, $confirm, $xDip, $pillX, $pillY, $pillW, $pillH, $rest.X, $rest.Y, $hover.X, $hover.Y, $hover.W, $hover.H,
        [Math]::Round($restStats[0], 1), [Math]::Round($restStats[1], 1), $restStats[2], $restStats[3], $restStats[6], [Math]::Round($restContrast, 2),
        [Math]::Round($hovStats[0], 1), [Math]::Round($hovStats[1], 1), $hovStats[2], $hovStats[3],
        [Math]::Round($deltaAbs, 1), [Math]::Round($deltaPct, 4), $cardPixels,
        [Math]::Round(($cardBox[1] - $cardBox[0] + 1) / $scale, 1), [Math]::Round(($cardBox[3] - $cardBox[2] + 1) / $scale, 1),
        ($health -replace '[\r\n\t]', ' ')) -join "`t")
    Write-Output ($id + ' card=' + [int]$cardPixels + ' cardDIP=' + [Math]::Round(($cardBox[1] - $cardBox[0] + 1) / $scale, 1) + 'x' + [Math]::Round(($cardBox[3] - $cardBox[2] + 1) / $scale, 1) +
        ' restMed=' + [Math]::Round($restStats[0], 1) + ' hovMed=' + [Math]::Round($hovStats[0], 1) +
        ' ink=' + $restStats[2] + '..' + $restStats[3] + ' contrast=' + [Math]::Round($restContrast, 2) + ' ' + $confirm)

    Set-Content -LiteralPath $ackPath -Value 'ok' -Encoding ASCII -NoNewline
    for ($i = 0; $i -lt 100; $i++) { if (-not (Test-Path $ackPath)) { break }; Start-Sleep -Milliseconds 80 }
  }
  [Img]::MoveTo($parkPoint.X, $parkPoint.Y)
}
finally {
  if ($hostProcess -and -not $hostProcess.HasExited) { try { $hostProcess.Kill(); $hostProcess.WaitForExit(5000) | Out-Null } catch { } }
  if (-not $KeepAppRunning) {
    Start-Process -FilePath $AppExe -ArgumentList '--background' -WindowStyle Hidden
    Start-Sleep -Seconds 5
    if (Test-Path $StatusLog) {
      $tail = (Get-Content -LiteralPath $StatusLog -Tail 3) -join ' / '
      Write-Output ('resident reader restored: ' + $tail)
    }
  }
  [Img]::MoveTo($originalCursor.X, $originalCursor.Y)
  Write-Output ('cursor returned to ' + $originalCursor.X + ',' + $originalCursor.Y)
}

$log = Join-Path $runDir 'measurements.tsv'
Set-Content -LiteralPath $log -Value $rows -Encoding ASCII
Write-Output ('verdict=' + $verdict + ' cells=' + ($rows.Count - 1) + ' log=' + $log + ' png=' + $pngDir)
