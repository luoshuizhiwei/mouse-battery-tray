param(
  [string]$AppExe = 'E:\MouseBattery\app\MouseBattery.exe',
  [string]$Probe = 'E:\MouseBattery\robust-v3.2\candidate-v411\AppearanceProbe.exe',
  [string]$StatusLog = 'E:\MouseBattery\app\data\status.txt'
)
$ErrorActionPreference = 'Stop'
$running = Get-Process MouseBattery -ErrorAction SilentlyContinue
if ($running) {
  Start-Process -FilePath $AppExe -ArgumentList '--exit' -WindowStyle Hidden
  for ($i = 0; $i -lt 40; $i++) { if (-not (Get-Process MouseBattery -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 250 }
  if (Get-Process MouseBattery -ErrorAction SilentlyContinue) { throw 'resident reader did not exit' }
}
$code = 1
try {
  & $Probe
  $code = $LASTEXITCODE
} finally {
  Start-Process -FilePath $AppExe -ArgumentList '--background' -WindowStyle Hidden
  Start-Sleep -Seconds 4
  if (Test-Path $StatusLog) { Write-Output ('restored: ' + ((Get-Content -LiteralPath $StatusLog -Tail 2) -join ' / ')) }
}
Write-Output ('probeExit=' + $code)
exit $code
