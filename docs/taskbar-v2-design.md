# 任务栏文字版：已确认设计与兼容性调查

日期：2026-10-29，Asia/Hong_Kong。

## 已确认的目标

用户明确纠正旧版托盘方案，并通过逐题 grilling 确认：

- 电量文字直接位于任务栏，默认在系统托盘/时钟区域左侧。
- 独立占位，不覆盖应用按钮或系统图标，不被收进托盘折叠菜单。
- 设置里提供任务栏内的预设位置切换。
- 悬停基础详情、20% 黄/10% 红、登录自启、15 秒旧值期限与断连隐藏沿用旧版。
- 已确认“准确，按此验证并修改”。旧版的托盘形态及旧记忆里的托盘偏好不再作为这次实现的依据。

示意：`应用按钮区域 …… | 95% | 系统托盘与时钟`。

## 当前机器的只读证据

Windows 11 Home build 26300。任务栏包含 `Windows.UI.Composition.DesktopWindowContentBridge`、`TrayNotifyWnd` 和兼容窗口。原始窗口树保存于 `E:\MouseBattery\evidence\taskbar-20261029\window-tree.txt`。

本轮未发现运行中的 MouseBattery、Windhawk / StartAllBack 进程；常见安装目录与卸载注册表也未发现 Windhawk / StartAllBack / ExplorerPatcher。这个结论仅覆盖已检查的路径，不证明磁盘上没有其他便携副本。

现有 HID 实现可以复用，本轮尚未把旧版 EXE 替换为另一种浮窗或托盘模拟。

## 实现路线与限制

### 原生 XAML 任务栏扩展

Windhawk 的任务栏扩展已有在系统托盘不同位置插入原生控件，以及独立预留空间的实现先例。最有希望满足已确认的显示约束；但需要常驻扩展宿主，并把显示模块加载到 Explorer。它改变了原先单个便携 EXE 的交付假设，必须让用户选择是否接受依赖，再进行本机原型验证，不能将上游能力当作本机兼容通过。

电量读取仍保留在独立进程，显示模块只接收电量状态；避免在 Explorer UI 线程做 HID I/O。

主要参考：[Windhawk taskbar-folder-menus source](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-folder-menus.wh.cpp)、[Taskbar System Info placement](https://github.com/starychenko/windhawk-taskbar-system-info)。

### 普通任务栏子窗或覆盖空白区域

TrafficMonitor 上游文档明确记录，Windows 11 任务栏变满时会与图标重叠。因此不能把普通子窗放到当前空白处就宣称满足“独立占位、不遮挡”。参考：[TrafficMonitor help](https://github.com/zhongyang219/TrafficMonitor/blob/master/Help_en-us.md)。

### 透明托盘图标占位

TrafficMonitor PR #2387 使用透明托盘图标、托盘可见性注册表和定位窗口预留空间。它仍依赖托盘位置及可见性管理，且任意预设位置不能直接由该机制实现；不直接作为本轮确认目标的替代品。该 PR 当前尚未合并，不能按正式上游功能宣称可用。参考：[PR #2387](https://github.com/zhongyang219/TrafficMonitor/pull/2387)。

## 下一项必要决策

已决策：用户选择“接受 Windhawk 扩展（推荐）”，允许配置常驻宿主并加载任务栏显示模块。安装命令曾被自动审批拒绝，用户随后手动完成便携安装；实际路径为 `E:\MouseBattery\taskbar-v2\downloads\Windhawk`。

实现已通过本机原型：原生 XAML StackPanel 项目 / Grid 自动列独立占位，四个预设位置均得到 56 DIP 宽度。任务栏左端会额外为当前已有的 TrafficMonitor 保留前置空间；实测电量控件 x=262 DIP，避开其占用的 x=1..254 DIP 区域。

模拟 42% 只用于布局验证，测试结束已经移除。本轮真实设备恢复后读到 87%，原生模块收到同一个真实值并显示在默认位置。精确交付、最新证据和未测范围统一见 `docs/handoff/current.md`。
