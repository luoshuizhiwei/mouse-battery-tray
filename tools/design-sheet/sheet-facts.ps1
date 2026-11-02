param([string]$RunDir = '')
$ErrorActionPreference = 'Stop'
if (-not $RunDir) {
  $RunDir = (Get-ChildItem 'E:\MouseBattery\design-sheet' -Directory -Filter 'run-*' | Sort-Object Name -Descending | Select-Object -First 1).FullName
}
$tsv = Join-Path $RunDir 'measurements.tsv'
if (-not (Test-Path $tsv)) { throw "missing $tsv" }

function Pick([string]$text, [string]$name) {
  $m = [regex]::Match($text, ('\b' + $name + '=([^\s]+)'))
  if ($m.Success) { return $m.Groups[1].Value }
  return '-'
}

foreach ($l in (Get-Content -LiteralPath $tsv | Select-Object -Skip 1)) {
  $p = $l -split "`t"
  if ($p.Count -lt 28) { continue }
  $h = $p[($p.Count - 1)]
  $card = $p[($p.Count - 3)] + 'x' + $p[($p.Count - 2)]
  Write-Output ($p[0] + ' ' + $p[2] +
    ' cmd[' + $p[1] + ']' +
    ' kind=' + (Pick $h 'kind') + ' other=' + (Pick $h 'other') + ' state=' + (Pick $h 'state') +
    ' pct=' + (Pick $h 'percent') + ' text=' + (Pick $h 'textRGB') +
    ' tickHi=' + (Pick $h 'tickHigh') + ' tickCr=' + (Pick $h 'tickCritical') +
    ' numW=' + (Pick $h 'numberWidth') + ' sufW=' + (Pick $h 'suffixWidth') +
    ' dL=' + $p[24] + '(' + $p[25] + ') card=' + $p[26] + ' cardDIP=' + $card + ' contrast=' + $p[19])
}

$pers = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
Write-Output ('THEME AppsUseLightTheme=' + $pers.AppsUseLightTheme + ' SystemsUseLightTheme=' + $pers.SystemsUseLightTheme)
Write-Output ('RUNDIR ' + $RunDir)
Write-Output ('PNGCOUNT rest=' + @(Get-ChildItem (Join-Path $RunDir 'png') -Filter 'rest-*.png').Count + ' hover=' + @(Get-ChildItem (Join-Path $RunDir 'png') -Filter 'hover-*.png').Count)
