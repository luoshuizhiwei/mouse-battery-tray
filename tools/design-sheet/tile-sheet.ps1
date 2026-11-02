param([Parameter(Mandatory = $true)][string]$RunDir, [string]$PngDir = '', [string]$Label = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not $PngDir) { $PngDir = Join-Path $RunDir 'png' }
$logPath = Join-Path $RunDir 'measurements.tsv'
if (-not (Test-Path $logPath)) { throw "missing $logPath" }
if (-not $Label) { $Label = (Split-Path -Leaf $RunDir) }

$lines = Get-Content -LiteralPath $logPath
$cells = @()
foreach ($l in ($lines | Select-Object -Skip 1)) {
  $f = $l -split "`t"
  if ($f.Count -lt 27) { continue }
  $cells += [pscustomobject]@{
    id = $f[0]; line = $f[1]; confirm = $f[2]; restX = $f[8]; restY = $f[9]
    restMed = [double]$f[14]; restInkL = [double]$f[16]; restInkR = [double]$f[17]; restInkCount = [double]$f[18]; restContrast = [double]$f[19]
    hoverX = $f[10]; hoverY = $f[11]; hoverW = [int]$f[12]; hoverH = [int]$f[13]
    hovMed = [double]$f[20]; deltaAbs = [double]$f[24]; deltaPct = [double]$f[25]; cardPixels = [double]$f[26]
    health = if ($f.Count -gt 27) { $f[($f.Count - 1)] } else { '' }
    cardDIP = $f[27] + 'x' + $f[28]
  }
}
Write-Output ('cells=' + $cells.Count)

function New-Sheet([int]$w, [int]$h) {
  $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::FromArgb(255, 24, 24, 32))
  $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
  return @{ Bmp = $bmp; G = $g }
}
$font = New-Object System.Drawing.Font('Consolas', [single]9)
$ink = [System.Drawing.Brushes]::White
$warn = [System.Drawing.Brushes]::Orange

function Caption([string]$prefix, $cell) {
  $tail = 'dL=' + ('{0:N1}' -f $cell.deltaAbs) + ' (' + ('{0:N3}' -f $cell.deltaPct) + ')' +
    ' card=' + [int]$cell.cardPixels +
    ' restMed=' + ('{0:N1}' -f $cell.restMed) + ' hovMed=' + ('{0:N1}' -f $cell.hovMed) +
    ' ink=' + [int]$cell.restInkL + '..' + [int]$cell.restInkR + ' c=' + ('{0:N2}' -f $cell.restContrast) +
    ' ' + $cell.confirm
  return ($prefix + ' ' + $cell.line + ' | ' + $tail)
}

# Sheet 1: resting strips (each includes the system clock for the Q1 side-by-side)
$stripH = 0; $labelH = 16
foreach ($c in $cells) {
  $p = Join-Path $PngDir ('rest-' + $c.id + '.png')
  if (Test-Path $p) { $stripH = [Math]::Max($stripH, (New-Object System.Drawing.Bitmap($p)).Height); (New-Object System.Drawing.Bitmap($p)).Dispose() }
}
$restW = 780
$sheet = New-Sheet ($restW + 26) (($labelH + $stripH + 6) * $cells.Count + 26)
$g = $sheet.G
$y = 4
foreach ($c in $cells) {
  $p = Join-Path $PngDir ('rest-' + $c.id + '.png')
  $g.DrawString((Caption 'REST' $c), $font, $(if ($c.confirm -eq 'CONFIRMED') { $ink } else { $warn }), 4, $y)
  $y += $labelH
  if (Test-Path $p) {
    $b = New-Object System.Drawing.Bitmap($p)
    $g.DrawImage($b, 4, $y, [Math]::Min($restW - 8, $b.Width), $b.Height)
    $b.Dispose()
  }
  $y += $stripH + 6
}
$out1 = Join-Path $RunDir 'sheet-T-rest.png'
$sheet.Bmp.Save($out1, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.G.Dispose(); $sheet.Bmp.Dispose()

# Sheet 2: hover frames (glass pill + card in one crop)
$cols = 4
$tileW = ($cells | Measure-Object -Property hoverW -Maximum).Maximum
$tileH = ($cells | Measure-Object -Property hoverH -Maximum).Maximum
$rowsN = [int][Math]::Ceiling($cells.Count / $cols)
$sheet2 = New-Sheet ($cols * ($tileW + 10) + 12) ($rowsN * ($tileH + $labelH + 10) + 26)
$g = $sheet2.G
for ($i = 0; $i -lt $cells.Count; $i++) {
  $c = $cells[$i]
  $col = $i % $cols; $row = [int][Math]::Floor($i / $cols)
  $x = 6 + $col * ($tileW + 10); $y = 6 + $row * ($tileH + $labelH + 10)
  $p = Join-Path $PngDir ('hover-' + $c.id + '.png')
  $g.DrawString((Caption 'HOVER' $c), $font, $(if ($c.cardPixels -ge 30000) { $ink } else { $warn }), $x, $y)
  $y += $labelH
  if (Test-Path $p) {
    $b = New-Object System.Drawing.Bitmap($p)
    $g.DrawImage($b, $x, $y, $b.Width, $b.Height)
    $b.Dispose()
  }
}
$out2 = Join-Path $RunDir 'sheet-T-hover.png'
$sheet2.Bmp.Save($out2, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet2.G.Dispose(); $sheet2.Bmp.Dispose()

# Sheet 3: Q4 evidence - PRO X vs Signature pairs, same percent, colour removed
$cm = New-Object System.Drawing.Imaging.ColorMatrix
$cm.Matrix00 = 0.299; $cm.Matrix10 = 0.587; $cm.Matrix20 = 0.114
$cm.Matrix01 = 0.299; $cm.Matrix11 = 0.587; $cm.Matrix21 = 0.114
$cm.Matrix02 = 0.299; $cm.Matrix12 = 0.587; $cm.Matrix22 = 0.114
$cm.Matrix33 = 1; $cm.Matrix44 = 1
$attrs = New-Object System.Drawing.Imaging.ImageAttributes
$attrs.SetColorMatrix($cm, [System.Drawing.Imaging.ColorMatrixFlag]::Default, [System.Drawing.Imaging.ColorAdjustType]::Bitmap)
$pairs = @(
  @{ a = 'T-01'; b = 'T-07'; tag = 'p=100 PRO X | M750L' },
  @{ a = 'T-02'; b = 'T-08'; tag = 'p=85  PRO X | M750L' },
  @{ a = 'T-03'; b = 'T-09'; tag = 'p=21  PRO X | M750L' },
  @{ a = 'T-04'; b = 'T-10'; tag = 'p=20  PRO X | M750L' },
  @{ a = 'T-05'; b = 'T-11'; tag = 'p=10  PRO X | M750L' },
  @{ a = 'T-06'; b = 'T-12'; tag = 'p=0   PRO X | M750L' }
)
$sheet3 = New-Sheet (2 * ($tileW + 10) + 12) ($pairs.Count * ($tileH + $labelH + 10) + 26)
$g = $sheet3.G
$g.DrawString(('Q4 EVIDENCE - BOTH COLUMNS IN GREYSCALE (colour and wording removed). ' + $Label), $font, [System.Drawing.Brushes]::Yellow, 6, 4)
for ($i = 0; $i -lt $pairs.Count; $i++) {
  $pr = $pairs[$i]
  $y = 24 + $i * ($tileH + $labelH + 10)
  $g.DrawString($pr.tag + ' (grayscale)', $font, $ink, 6, $y)
  $y += $labelH
  $slot = 0
  foreach ($cid in @($pr.a, $pr.b)) {
    $x = 6 + $slot * ($tileW + 10)
    $p = Join-Path $PngDir ('hover-' + $cid + '.png')
    if (Test-Path $p) {
      $b = New-Object System.Drawing.Bitmap($p)
      $dest = New-Object System.Drawing.Rectangle($x, $y, $b.Width, $b.Height)
      $g.DrawImage($b, $dest, 0, 0, $b.Width, $b.Height, [System.Drawing.GraphicsUnit]::Pixel, $attrs)
      $b.Dispose()
    }
    $slot++
  }
}
$out3 = Join-Path $RunDir 'sheet-T-gray.png'
$sheet3.Bmp.Save($out3, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet3.G.Dispose(); $sheet3.Bmp.Dispose()

# Console summary: the machine-judged numbers only, no interpretation.
$ok = @($cells | Where-Object { $_.cardPixels -ge 30000 })
$lit = @($cells | Where-Object { $_.deltaPct -ge 0.08 })
$conf = @($cells | Where-Object { $_.confirm -eq 'CONFIRMED' })
$centers = @($cells | ForEach-Object { if ($_.restInkR -ge 0) { ($_.restInkL + $_.restInkR) / 2.0 } })
Write-Output ('cardAppeared=' + $ok.Count + '/' + $cells.Count + ' glassLit(>=8%)=' + $lit.Count + '/' + $cells.Count + ' confirmed=' + $conf.Count + '/' + $cells.Count)
Write-Output ('minContrast=' + (($cells | Measure-Object -Property restContrast -Minimum).Minimum) + ' medianLumaDelta=' + ((($cells | Sort-Object deltaAbs)[[Math]::Floor($cells.Count / 2)]).deltaAbs))
Write-Output ('numeralCentre spread=' + ((($centers | Measure-Object -Maximum).Maximum - ($centers | Measure-Object -Minimum).Minimum)) + 'px (0 = no jitter)')
Write-Output ('sheets: ' + $out1 + ' | ' + $out2 + ' | ' + $out3)
