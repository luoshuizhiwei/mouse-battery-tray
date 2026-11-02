param([string]$RunDir = '', [string[]]$Ids = @('T-01', 'T-07', 'T-02', 'T-08', 'T-05', 'T-11', 'T-06', 'T-12'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not $RunDir) {
  $RunDir = (Get-ChildItem 'E:\MouseBattery\design-sheet' -Directory -Filter 'run-*' | Sort-Object Name -Descending | Select-Object -First 1).FullName
}
$tsv = Join-Path $RunDir 'measurements.tsv'
$lines = Get-Content -LiteralPath $tsv | Select-Object -Skip 1
foreach ($id in $Ids) {
  $row = $null
  foreach ($l in $lines) { $p = $l -split "`t"; if ($p[0] -eq $id) { $row = $p; break } }
  if (-not $row) { Write-Output ($id + ' not in run'); continue }
  $pillX = [int]$row[4]; $pillY = [int]$row[5]; $pillW = [int]$row[6]; $pillH = [int]$row[7]
  $hovX = [int]$row[10]; $hovY = [int]$row[11]
  $restX = [int]$row[8]; $restY = [int]$row[9]
  $hx = $pillX - $hovX; $hy = $pillY - $hovY
  $rx = $pillX - $restX; $ry = $pillY - $restY
  $hoverPath = Join-Path $RunDir ('png\hover-' + $id + '.png')
  $restPath = Join-Path $RunDir ('png\rest-' + $id + '.png')
  $out = $id
  if (Test-Path $hoverPath) {
    $b = New-Object System.Drawing.Bitmap($hoverPath)
    # three depths through the glass: top of the pill, middle, just under the digits
    foreach ($frac in @(0.08, 0.45, 0.85)) {
      $c = $b.GetPixel($hx + [int]($pillW / 2), $hy + [int]($pillH * $frac))
      $out += ' | top' + [int]($frac * 100) + '=A' + $c.A + ' ' + $c.R + ',' + $c.G + ',' + $c.B
    }
    # card body colour, sampled well inside the card area above the bar
    $cb = $b.GetPixel($hx + [int]($pillW / 2), [int](($hy - 120)))
    $out += ' | card=' + $cb.R + ',' + $cb.G + ',' + $cb.B
    $b.Dispose()
  }
  if (Test-Path $restPath) {
    $r = New-Object System.Drawing.Bitmap($restPath)
    $bg = $r.GetPixel($rx + [int]($pillW / 2), $ry + [int]($pillH * 0.15))
    $out += ' | restBg=' + $bg.R + ',' + $bg.G + ',' + $bg.B
    $r.Dispose()
  }
  Write-Output $out
}
