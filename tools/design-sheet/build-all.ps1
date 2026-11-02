param([string]$Tag = 'candidate-v413')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$dll = 'E:\MouseBattery\standalone\candidate\MouseBattery.Native.dll'
$out = "E:\MouseBattery\robust-v3.2\$Tag"
& (Join-Path $root 'build-standalone-native.ps1') | Out-Null
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $dll).Hash
& (Join-Path $root 'build.ps1') -OutputDirectory $out -NativeDll $dll | Out-Null
& (Join-Path $root 'build-probe.ps1') -OutputDirectory $out -NativeDll $dll | Out-Null
& (Join-Path $root 'tools\design-sheet\build-sheet.ps1') -NativeDll $dll -ExpectedNativeSha256 $hash | Out-Null
Write-Output ('DLL=' + $hash)
Write-Output ('EXE=' + (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $out 'MouseBattery.exe')).Hash)
Write-Output ('PROBE=' + (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $out 'AppearanceProbe.exe')).Hash)
