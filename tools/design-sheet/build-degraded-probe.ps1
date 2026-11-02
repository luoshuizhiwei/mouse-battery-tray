param(
  [string]$NativeDll = 'E:\MouseBattery\standalone\candidate\MouseBattery.Native.dll',
  [string]$OutDir = 'E:\MouseBattery\robust-v3.2\degraded-probe-v420'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$src = Join-Path $root 'src'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stage = Join-Path $env:TEMP ('degraded-src-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
  # Product source set only: copying *.cs would drag in AppearanceProbe/Tests
  # and their Main methods.
  $productSources = @('Hid.cs', 'Battery.cs', 'TrayIcon.cs', 'TaskbarPublisher.cs', 'NativeHost.cs', 'Program.cs', 'Ui.cs')
  foreach ($name in $productSources) { Copy-Item (Join-Path $src $name) $stage }
  $manifest = Join-Path $src 'app.manifest'
  $nativeHost = Join-Path $stage 'NativeHost.cs'
  $text = [IO.File]::ReadAllText($nativeHost, [Text.Encoding]::UTF8)
  $zero = ('0' * 64)
  $patched = [regex]::Replace($text, 'const string TaskbarSha256="[0-9A-Fa-f]{64}"', 'const string TaskbarSha256="' + $zero + '"')
  if ($patched -eq $text) { throw 'fingerprint constant not found; refusing to build a probe that would not degrade' }
  [IO.File]::WriteAllText($nativeHost, $patched, (New-Object System.Text.UTF8Encoding($false)))
  $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
  $sources = $productSources | ForEach-Object { Join-Path $stage $_ }
  $out = Join-Path $OutDir 'DegradedProbe.exe'
  & $compiler /nologo /optimize+ /platform:x64 /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/resource:$NativeDll,MouseBattery.Native.dll" "/win32manifest:$manifest" "/out:$out" $sources
  if ($LASTEXITCODE -ne 0) { throw "degraded probe build failed: $LASTEXITCODE" }
  Get-FileHash -Algorithm SHA256 -LiteralPath $out
}
finally {
  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
