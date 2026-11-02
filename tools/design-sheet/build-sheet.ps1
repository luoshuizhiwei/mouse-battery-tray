param(
  [string]$OutputDirectory = 'E:\MouseBattery\design-sheet\bin',
  [string]$NativeDll = 'E:\MouseBattery\app\components\B84D3691A3EEDD9EB8111DB782845F08D313809A3ACE851E628D24EEF6BD99A8\MouseBattery.Native.dll',
  [string]$ExpectedNativeSha256 = 'B84D3691A3EEDD9EB8111DB782845F08D313809A3ACE851E628D24EEF6BD99A8'
)
$ErrorActionPreference = 'Stop'

# The sheet must render the exact component the delivered exe carries, or every
# pixel in the contact sheet belongs to some other build (v3.8.3 lesson).
if (-not (Test-Path -LiteralPath $NativeDll)) { throw 'Native DLL not found; pass -NativeDll at the delivered MouseBattery.Native.dll' }
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $NativeDll).Hash
if ($hash -ne $ExpectedNativeSha256) { throw "Native DLL hash mismatch: got $hash expected $ExpectedNativeSha256" }

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$srcRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\src')
$sources = @('Hid.cs', 'Battery.cs', 'TaskbarPublisher.cs', 'NativeHost.cs') | ForEach-Object { Join-Path $srcRoot $_ }
$sources += (Join-Path $PSScriptRoot 'SheetHost.cs')

$out = Join-Path $OutputDirectory 'SheetHost.exe'
& $compiler /nologo /optimize+ /platform:x64 /target:exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/resource:$NativeDll,MouseBattery.Native.dll" "/out:$out" $sources
if ($LASTEXITCODE -ne 0) { throw "SheetHost build failed: $LASTEXITCODE" }
Get-FileHash -Algorithm SHA256 -LiteralPath $out
