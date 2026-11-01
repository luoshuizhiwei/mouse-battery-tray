# 鼠标电量 · 独立应用 v3.7

直接运行 `E:\MouseBattery\app\MouseBattery.exe`。无需安装或运行 Windhawk、G HUB、.NET SDK，也不需要打开设置窗口。

v3.1 使用统一的“雾蓝”主题：浅色冷白与灰蓝、深色石墨与浅蓝；悬停详情和设置窗口加入细电量刻度，并修复自启控件的无障碍名称。v3.4 完成“雾蓝·电量仪表”精修：任务栏胶囊玻璃渐变、悬停卡片与设置刻度条加入 20%/10% 阈值刻度线（装饰即数据）、大数字换 Segoe UI Variable Display、设置卡片圆角描边、自启开关化、托盘降级图标升级为电池壳设计。任务栏仍然只显示百分比，无动画。

应用自己在后台读取鼠标电量，并将百分比放入 Windows 11 原生任务栏布局。默认位于系统托盘与时钟区域左侧，独立占位。**多鼠标自动跟随**：支持 PRO X 2 DEX（Lightspeed 接收器/有线）与 Signature M750L（蓝牙 BLE），任务栏始终显示你**当前正在用的那只**（Raw Input 检测），悬停卡列出每只鼠标的电量；设备休眠时读数独立退避，互不拖累。**静息态与原生时钟完全同构**：纯文字、无底色，悬停时玻璃底才出现。左键无任何动作，右键打开菜单。≤20% 琥珀色、≤10% 红色。

- 再次运行 EXE 或右键电量打开设置，选择四个预设位置、登录自启或退出。**左键点击电量没有任何动作**（防误触）。右键菜单带系统图标，充电时悬停卡状态行有闪电符号。位置选择是迷你任务栏示意图：电量胶囊钉在四个落点中的当前位，点击落点或用方向键切换。悬停卡与设置的刻度条上，20%/10% 阈值刻度会在电量越过时点亮成琥珀/红。
- 设置窗口跟随系统深浅主题实时切换；任务栏组件未适配时，窗口顶部显示降级说明条并隐藏位置选择。
- 关闭设置窗口后继续后台运行；退出工具时电量和占位一并移除。
- 任务栏最左侧会避开已有的 TrafficMonitor。
- 查询失败保留并标记最近读数，距离上次成功达到 15 秒后隐藏。
- 当前登录自启只有本应用的 `MouseBatteryTray` 项；旧的 `MouseBatteryTaskbarHost` 项已移除。Windhawk 已退出，本工具的旧模块已禁用；安装文件保留。

## 独立应用结构

交付入口是一个 EXE，内嵌本项目的原生组件，首次启动自动解压到应用目录的 `components\<SHA256>`，验证内容后加载。该目录是应用内部文件，请与 EXE 一起保留。状态日志位于应用目录 `data\status.txt`，最近正常退出记录位于 `data\native-lifecycle.txt`。

后台 EXE 使用 Windows HID / HID++ 直接读取本机 Logitech PRO X 2 DEX（GPW4 鹰眼），支持有线 `046D:C0A0` 和 2.4G 接收器 `046D:C54D`。自己的原生 DLL 在 Explorer 中操作任务栏 XAML；HID I/O 留在 EXE。组件监视 EXE 进程，即使 EXE 被强制结束，也会移除界面并卸载自身。

没有捆绑或调用 Windhawk。编译阶段复用了先前下载的 Clang 工具链，运行阶段不使用该目录。源码参考的许可仍保留在 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 当前兼容范围

已在本机 Windows 11 build 26300、主任务栏、150% 缩放验证。由于使用了系统任务栏内部接口，程序会先验证 `System32\taskbar.dll` 的完整 SHA256；系统更新后指纹改变不再停止，而是自动降级为托盘图标：继续读取真实电量、自动提升图标到任务栏可见角并发气球提示，等待适配后自动回到任务栏显示。任务栏模式本身仍只使用已验证的内部地址。尚未验收多显示器、跨屏 DPI、真实重新登录和 Explorer 重启后的自动恢复。

位置保存在 `HKCU\Software\MouseBatteryTray\TaskbarPosition`。登录自启项是 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MouseBatteryTray`，指向当前 EXE。移动应用前先关闭自启，移动后再开启。

## 开发与验证

```powershell
.\build-standalone-native.ps1
.\build.ps1
.\test.ps1          # 36 项逻辑 + 10 项 IPC 回归
.\build-probe.ps1   # AppearanceProbe，8 项真实原生渲染验证（运行前需退出正式版）
```

使用本机 .NET Framework C# 编译器及已有 Clang/C++ WinRT 头文件，无 NuGet。源码自 v3.2.0 起有 Git 基线。`src/NativeHost.cs` 负责内嵌组件、版本校验和启动；`native/NativeHost.cpp` 负责独立加载及生命周期；`native/TaskbarView.h` 负责原生布局。

本轮（v3.2 健壮性与降级）已验证：36 项逻辑回归（含新增无读数托盘占位）、10 项 IPC、8 项原生渲染；假指纹探针实测降级链路（托盘图标出现在可见角、真实 78%、自启注册表不受扰、干净退出）；真机验收单实例 `--manage` 唤起设置窗口、关闭窗口后台续跑、正常退出 DetachResult=0。未修改的设备链路复用已有证据，未将这些离线证据扩大为新设备验收。

精确交付指纹、原始日志、已知边界见 [当前交接](docs/handoff/current.md)。旧 Windhawk 方案仅作为历史源码和恢复资产保留。

## 协议与来源

HID++ 协议参考 [Logitech specification](https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf) 与 [Solaar](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py)。任务栏 XamlRoot 发现及布局方式参考 [taskbar-folder-menus](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-folder-menus.wh.cpp)，许可见上述 notices。
