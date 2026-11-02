param(
  [string]$Candidate = 'E:\MouseBattery\robust-v3.2\candidate-v412\MouseBattery.exe',
  [string]$AppExe = 'E:\MouseBattery\app\MouseBattery.exe',
  [string]$BaselineDir = 'E:\MouseBattery\robust-v3.2\baseline',
  [string]$ExpectedHash = 'E589774FEF79CF547EC5F47286A628924D95F87BF3FB035449CD22CA7AD3AE2F',
  [string]$StatusLog = 'E:\MouseBattery\app\data\status.txt'
)
$ErrorActionPreference = 'Stop'
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Candidate).Hash
if ($hash -ne $ExpectedHash) { throw "candidate hash mismatch: $hash" }
Write-Output ('candidate verified: ' + $hash)

if (Get-Process MouseBattery -ErrorAction SilentlyContinue) {
  Start-Process -FilePath $AppExe -ArgumentList '--exit' -WindowStyle Hidden
  for ($i = 0; $i -lt 40; $i++) { if (-not (Get-Process MouseBattery -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 250 }
  if (Get-Process MouseBattery -ErrorAction SilentlyContinue) { throw 'resident reader did not exit; aborting promotion' }
}
New-Item -ItemType Directory -Force -Path $BaselineDir | Out-Null
if (Test-Path $AppExe) {
  $old = (Get-FileHash -Algorithm SHA256 -LiteralPath $AppExe).Hash
  $backupName = 'MouseBattery-' + $old.Substring(0,8) + '.exe'
  Copy-Item -LiteralPath $AppExe -Destination (Join-Path $BaselineDir $backupName) -Force
  Write-Output ('backed up previous exe ' + $old + ' -> baseline\' + $backupName)
}
Copy-Item -LiteralPath $Candidate -Destination $AppExe -Force
$installed = (Get-FileHash -Algorithm SHA256 -LiteralPath $AppExe).Hash
if ($installed -ne $ExpectedHash) { throw "installed hash mismatch: $installed" }
Write-Output ('installed: ' + $installed)

Start-Process -FilePath $AppExe -ArgumentList '--background' -WindowStyle Hidden
Start-Sleep -Seconds 7
$running = @(Get-Process MouseBattery -ErrorAction SilentlyContinue).Count
Write-Output ('resident processes: ' + $running)
if (Test-Path $StatusLog) { Write-Output ((Get-Content -LiteralPath $StatusLog -Tail 8) -join "`n") }
$components = Get-ChildItem 'E:\MouseBattery\app\components' -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($components) { Write-Output ('newest component dir: ' + $components.Name) }
