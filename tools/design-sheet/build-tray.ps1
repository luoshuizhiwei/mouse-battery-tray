param([string]$OutputDirectory = 'E:\MouseBattery\design-sheet\bin')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$srcRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\src')
$sources = @('TrayIcon.cs', 'Ui.cs') | ForEach-Object { Join-Path $srcRoot $_ }
$sources += (Join-Path $PSScriptRoot 'TraySheet.cs')
$out = Join-Path $OutputDirectory 'TraySheet.exe'
& $compiler /nologo /optimize+ /platform:x64 /target:exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$out" $sources
if ($LASTEXITCODE -ne 0) { throw "TraySheet build failed: $LASTEXITCODE" }
Get-FileHash -Algorithm SHA256 -LiteralPath $out
