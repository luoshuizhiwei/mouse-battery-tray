param([string]$WindhawkRoot='E:\MouseBattery\taskbar-v2\downloads\Windhawk')
$ErrorActionPreference='Stop'
$compiler=Join-Path $WindhawkRoot 'Compiler\bin\clang++.exe'
$engine=Get-ChildItem -LiteralPath (Join-Path $WindhawkRoot 'Engine') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName '64\windhawk.lib') } | Select-Object -First 1
if(-not $engine) { throw 'Windhawk engine import library not found' }
$output='E:\MouseBattery\taskbar-v2\candidate\mouse-battery-taskbar.dll'
$source=Join-Path $PSScriptRoot 'windhawk\mouse-battery-taskbar.wh.cpp'
$arguments=@('-std=c++23','-O2','-shared','-DUNICODE','-D_UNICODE','-DWINVER=0x0A00','-D_WIN32_WINNT=0x0A00','-D_WIN32_IE=0x0A00','-DNTDDI_VERSION=0x0A000008','-D__USE_MINGW_ANSI_STDIO=0','-DWH_MOD','-DWH_MOD_ID=L"mouse-battery-taskbar"','-DWH_MOD_VERSION=L"2.1.0"',(Join-Path $engine.FullName '64\windhawk.lib'),$source,'-include','windhawk_api.h','-target','x86_64-w64-mingw32','-Wl,--export-all-symbols','-o',$output,'-lole32','-loleaut32','-lruntimeobject','-lshell32','-luser32')
Push-Location (Join-Path $WindhawkRoot 'Compiler')
try { & $compiler @arguments; if($LASTEXITCODE -ne 0) { throw "Taskbar module build failed: $LASTEXITCODE" } } finally { Pop-Location }
Get-FileHash -LiteralPath $output
