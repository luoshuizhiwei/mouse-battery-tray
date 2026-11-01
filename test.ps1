$ErrorActionPreference = 'Stop'
$root = 'E:\MouseBattery'
New-Item -ItemType Directory -Force "$root\build","$root\evidence" | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('Hid.cs','Battery.cs','TrayIcon.cs','Tests.cs','Ui.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
& $compiler /nologo /optimize+ /platform:x64 /target:exe /reference:System.Drawing.dll "/out:$root\build\Tests.exe" $sources
if ($LASTEXITCODE -ne 0) { throw "Test build failed: $LASTEXITCODE" }
& "$root\build\Tests.exe" "$root\evidence\icons.png" | Tee-Object -FilePath "$root\evidence\tests.txt"
if ($LASTEXITCODE -ne 0) { throw "Tests failed: $LASTEXITCODE" }
$publisherSources = @('Hid.cs','Battery.cs','TaskbarPublisher.cs','PublisherTests.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
& $compiler /nologo /optimize+ /platform:x64 /target:exe "/out:$root\build\PublisherTests.exe" $publisherSources
if ($LASTEXITCODE -ne 0) { throw "Publisher test build failed: $LASTEXITCODE" }
& "$root\build\PublisherTests.exe" | Tee-Object -FilePath "$root\evidence\publisher-tests.txt"
if ($LASTEXITCODE -ne 0) { throw "Publisher tests failed: $LASTEXITCODE" }
