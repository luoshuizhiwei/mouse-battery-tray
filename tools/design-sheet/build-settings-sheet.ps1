param([string]$OutputDirectory = 'E:\MouseBattery\design-sheet\bin')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$srcRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\src')
# Same source set as the product build, plus the sheet entry point; /main keeps
# Program.Main out of the way. Nothing here ships.
$sources = @('Hid.cs', 'Battery.cs', 'TrayIcon.cs', 'TaskbarPublisher.cs', 'NativeHost.cs', 'Program.cs', 'Ui.cs') | ForEach-Object { Join-Path $srcRoot $_ }
$sources += (Join-Path $PSScriptRoot 'SettingsSheet.cs')
$out = Join-Path $OutputDirectory 'SettingsSheet.exe'
& $compiler /nologo /optimize+ /platform:x64 /target:exe /main:SettingsSheet /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$out" $sources
if ($LASTEXITCODE -ne 0) { throw "SettingsSheet build failed: $LASTEXITCODE" }
Get-FileHash -Algorithm SHA256 -LiteralPath $out
