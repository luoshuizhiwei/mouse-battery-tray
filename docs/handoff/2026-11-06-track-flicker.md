# 分段电量条重复重绘修复

用户圈出设置页电量条，反馈“感觉在闪”。2026-11-06 00:58:14部署最小修复，配色/布局保持。

## 定位与修复

250ms UI tick经RefreshDisplay调用UpdateManagerDisplay，后者不论数值是否变化都会Invalidate电量条。该条是普通Panel，OptimizedDoubleBuffer=false。无变化时仍反复执行背景和刻度绘制，容易暴露中间帧。

仅改`src/Program.cs`：使用启用UserPaint/AllPaintingInWmPaint/OptimizedDoubleBuffer/ResizeRedraw的BatteryTrack；UpdateManagerDisplay保存previousLevel，仅数值变化时显式Invalidate。初次显示、遮挡后恢复和尺寸变化仍由WinForms正常重绘，没有停止定时器或修改读取频率。

## 原始证据

统一目录：`E:\MouseBattery\track-flicker-20261106`。

- `paint-red.txt`：实际设置页源码的独立WinForms运行探针，80%不变时6tick→6次Invalidate/6次WM_PAINT；普通Panel双缓冲false。
- `paint-green.txt`：BatteryTrack双缓冲true；80%不变6tick→0次Invalidate/0次WM_PAINT；80→79只重画1次；断开、未连接保持、恢复分别按需绘制，断言通过。
- 探针通过隔离的RefreshDisplay和未初始化上下文避免创建HID源/注入worker；其余构造、更新方法、Panel Paint handler使用实际源码，窗口在屏外，模拟读数不写正式共享内存或偏好。原/新两侧WM_ERASEBKGND计数均为0，不能把闪烁归因写成“抓到了背景擦除消息”；问题证据是无谓WM_PAINT及未双缓冲，而非视频抓帧。
- `appearance-comparison.json`：明/暗×80/100/未连接6组前后完整PNG SHA256逐组相同，外观未变。
- `preview-render.txt`：6状态生成与原有缩放点击、键盘、AX值检查通过。输入为模拟。
- `build.txt`、`candidate/build-manifest.json`：构建成功；部署时实际抽取内嵌原生DLL校验为05B2…，原生代码未改。
- `deployment-final.json`：部署前未找到旧驻留进程；旧的native-lifecycle记录00:55:54、DetachResult=0，不能自行推断用户操作或退出原因。仍发出--exit信号并确认EXE可独占打开后复制/校验/启动47640；无强杀覆盖。记录GracefulExit为null而非伪称本次等待了旧PID退出。
- `events.json`：00:48:26至00:58:59无MouseBattery/Explorer相关Application1000/1001/1026。Explorer保持PID12264，未重启。
- `ui-validation.md`：实机窗口数值、外观和后台存活另核对；静态离散截图不等同连续视频无闪烁验收。
- 实机两次间隔截图均80%、雾蓝灰条外观保持，底部完整；关闭后47640存活。验收后重新为用户打开设置页供连续观察，记录`final-user-view.json`。

未重跑输入和行为未变的HID/IPC全量回归；本次风险由重绘红绿实验、变化/断开/恢复状态、6状态外观对照和新构建覆盖。

## 正式版本

EXE：`E:\MouseBattery\app\MouseBattery.exe`，SHA256 `8A9E8009FE0EED988F19D66BC1017A74BB2227199BA862DAEC69129ACF0627EB`。

内嵌原生DLL仍为`05B2DFD7AA161B5E61DC03DE22F6F929E4C7635D8A1A5AF722D0CF689FD272B1`；进程47640，真实PRO X 2 DEX 80%、活动Unit02-02-5C-94、RawInputError为空、Native visible。

原设置页软色调、堆修复和数据链路均保留。没有系统主题/偏好变更，没有提交、push、Explorer重启或证据清理。
