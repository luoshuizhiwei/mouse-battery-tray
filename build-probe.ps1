param([string]$OutputDirectory = 'E:\Agent任务\进行中\MouseBattery\robust-v3.2',[Parameter(Mandatory=$true)][string]$NativeDll)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('Hid.cs','Battery.cs','TaskbarPublisher.cs','NativeHost.cs','AppearanceProbe.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
if(-not (Test-Path -LiteralPath $NativeDll)){throw 'Native DLL not found; pass -NativeDll pointing at the delivered MouseBattery.Native.dll'}
& $compiler /nologo /optimize+ /platform:x64 /target:exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/resource:$NativeDll,MouseBattery.Native.dll" "/out:$OutputDirectory\AppearanceProbe.exe" $sources
if ($LASTEXITCODE -ne 0) { throw "Probe build failed: $LASTEXITCODE" }
Get-FileHash -Algorithm SHA256 -LiteralPath "$OutputDirectory\AppearanceProbe.exe"
