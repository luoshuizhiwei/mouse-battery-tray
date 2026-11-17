$ErrorActionPreference = 'Stop'
$root = 'E:\Agent任务\进行中\MouseBattery'
New-Item -ItemType Directory -Force "$root\build","$root\evidence" | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('Hid.cs','Battery.cs','TrayIcon.cs','Tests.cs','DisplayMode.cs','Ui.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
& $compiler /nologo /optimize+ /platform:x64 /target:exe /reference:System.Drawing.dll "/out:$root\build\Tests.exe" $sources
if ($LASTEXITCODE -ne 0) { throw "Test build failed: $LASTEXITCODE" }
& "$root\build\Tests.exe" "$root\evidence\icons.png" | Tee-Object -FilePath "$root\evidence\tests.txt"
if ($LASTEXITCODE -ne 0) { throw "Tests failed: $LASTEXITCODE" }
$publisherSources = @('Hid.cs','Battery.cs','TaskbarPublisher.cs','PublisherTests.cs','DisplayMode.cs','Ui.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
& $compiler /nologo /optimize+ /platform:x64 /target:exe "/out:$root\build\PublisherTests.exe" $publisherSources
if ($LASTEXITCODE -ne 0) { throw "Publisher test build failed: $LASTEXITCODE" }
& "$root\build\PublisherTests.exe" | Tee-Object -FilePath "$root\evidence\publisher-tests.txt"
if ($LASTEXITCODE -ne 0) { throw "Publisher tests failed: $LASTEXITCODE" }
$recoverySources = @('Battery.cs','RecoveryTests.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
& $compiler /nologo /optimize+ /platform:x64 /target:exe "/out:$root\build\RecoveryTests.exe" $recoverySources
if ($LASTEXITCODE -ne 0) { throw "Recovery test build failed: $LASTEXITCODE" }
& "$root\build\RecoveryTests.exe" | Tee-Object -FilePath "$root\evidence\recovery-tests.txt"
if ($LASTEXITCODE -ne 0) { throw "Recovery tests failed: $LASTEXITCODE" }

# Regression gate: RIDI_DEVICENAME is measured in UTF-16 characters while
# AllocHGlobal counts bytes. 9993e95 fixed the mismatch, 033c229 rolled it
# back and the process died with 0xC0000374 - keep the probe in the gate.
$probe = Join-Path $PSScriptRoot 'tools\Test-RawInputNameBuffer.ps1'
if (Test-Path $probe) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $probe -OutputDirectory "$root\evidence\raw-name-probe" | Tee-Object -FilePath "$root\evidence\raw-name-buffer.txt"
    if ($LASTEXITCODE -ne 0) { throw "Raw Input device-name buffer probe failed (heap overflow)" }
} else {
    Write-Warning 'Raw Input device-name probe missing; gate skipped'
}
