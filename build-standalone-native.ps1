param([string]$CompilerRoot='E:\MouseBattery\taskbar-v2\build-tools\Compiler')
$ErrorActionPreference='Stop'
$output='E:\MouseBattery\standalone\candidate\MouseBattery.Native.dll'
$source=Join-Path $PSScriptRoot 'native\NativeHost.cpp'
Push-Location $CompilerRoot
try {
    & "$CompilerRoot\bin\clang++.exe" -std=c++23 -O2 -shared -static -DUNICODE -D_UNICODE -D_WIN32_WINNT=0x0A00 -DWINVER=0x0A00 -D__USE_MINGW_ANSI_STDIO=0 -target x86_64-w64-mingw32 $source -o $output -lole32 -loleaut32 -lruntimeobject -lshell32 -luser32
    if($LASTEXITCODE -ne 0) {throw "Native component build failed: $LASTEXITCODE"}
} finally {Pop-Location}
Get-FileHash -LiteralPath $output
