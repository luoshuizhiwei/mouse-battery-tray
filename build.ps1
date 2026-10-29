param([string]$OutputDirectory = 'E:\MouseBattery\standalone\candidate',[string]$NativeDll = 'E:\MouseBattery\appearance-v3\MB-UI-02-v1\MouseBattery.Native.dll')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('Hid.cs','Battery.cs','TrayIcon.cs','TaskbarPublisher.cs','NativeHost.cs','Program.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
$native=$NativeDll
if(-not (Test-Path -LiteralPath $native)){throw 'Build the native component first with build-standalone-native.ps1'}
& $compiler /nologo /optimize+ /platform:x64 /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/resource:$native,MouseBattery.Native.dll" "/win32manifest:$PSScriptRoot\src\app.manifest" "/out:$OutputDirectory\MouseBattery.exe" $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
Get-FileHash -Algorithm SHA256 -LiteralPath "$OutputDirectory\MouseBattery.exe"
