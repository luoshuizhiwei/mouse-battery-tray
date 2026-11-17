# 检查当前 Windows 任务栏是否仍被这个版本适配。
# 用法：右键“使用 PowerShell 运行”，或 pwsh -File 本文件
$ErrorActionPreference = 'Continue'
$expected = '49D9F43E7BDBF3B1EE27F09C03317E6057DDCD8787749F945FD2D63816988A27'
$dll = Join-Path $env:SystemRoot 'System32\taskbar.dll'
$app = 'E:\Agent任务\进行中\MouseBattery\app'

Write-Output '==== Windows 版本 ===='
$os = Get-CimInstance Win32_OperatingSystem
Write-Output ('系统      : ' + $os.Caption + ' ' + $os.Version)
Write-Output ('构建      : ' + (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').DisplayVersion)
Write-Output ''
Write-Output '==== 任务栏模块 ===='
$f = Get-Item $dll
$hash = (Get-FileHash $dll -Algorithm SHA256).Hash
Write-Output ('taskbar.dll 版本 : ' + $f.VersionInfo.FileVersion)
Write-Output ('taskbar.dll 修改 : ' + $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm'))
Write-Output ('当前指纹         : ' + $hash)
Write-Output ('适配指纹         : ' + $expected)
Write-Output ''
if ($hash -eq $expected) {
    Write-Output '结论：仍被当前版本适配，任务栏胶囊显示应正常。'
} else {
    Write-Output '结论：系统任务栏已变化，本工具会自动降级为托盘图标显示（不修改任务栏、不会破坏系统）。'
    Write-Output '     把上面这几行发给维护者即可重新适配。'
}
Write-Output ''
Write-Output '==== 当前运行状态 ===='
$status = Join-Path $app 'data\status.txt'
if (Test-Path $status) {
    $line = Select-String -LiteralPath $status -Pattern '^Native=' | Select-Object -First 1
    if ($line) { Write-Output $line.Line } else { Write-Output '（status.txt 里还没有 Native= 行）' }
} else {
    Write-Output '（还没有 data\status.txt，说明工具没在这台机器上跑过）'
}
$p = Get-Process MouseBattery -ErrorAction SilentlyContinue
if ($p) { Write-Output ('进程：运行中 pid=' + $p.Id + '，启动于 ' + $p.StartTime.ToString('HH:mm:ss')) } else { Write-Output '进程：未运行' }
