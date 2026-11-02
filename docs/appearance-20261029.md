# v2.1 外观调整

日期：2026-10-29。用户请求“美化一下这个”，范围为当前任务栏工具的显示、悬停详情与设置窗口。

## 设计与实现

- 任务栏占位保持 56×48 DIP，内部采用 52×28 DIP 的薄圆角底，圆角 8 DIP。
- 电量数字使用 Segoe UI Variable Text 15.5 DIP Semibold，百分号使用 10.5 DIP、65% 不透明度，仍只显示电量。
- 正常色为深色主题的柔白 `#EEF2F8` / 浅色主题的墨灰 `#202833`；低电量为琥珀色 `#EAC36A` / `#8B650A`，临界电量为柔红 `#FA8A8A` / `#B32F3C`。20% 和 10% 阈值保持。
- 悬停详情拆成设备名称、大字号电量、充电状态与连接方式，旧值重连和未知充电状态仍明确显示。
- 设置窗口使用分组信息区、统一的配色与按钮层次；跟随应用明暗主题，保持原来设置与退出操作。
- 没有增加常驻图标、动画、通知或设备查询。

## 核对与实际问题

本机 CUA 截图发现旧布局在 150% 缩放下信息裁切。单独的 DPI 探针表明：字体与 Graphics DPI 是 144，但程序化创建控件经过 AutoScale 后，控件边界仍为 96-DPI 设计尺寸；三行文本的 PreferredHeight=78 px，而 Label.Height=64 px。

最终保留 AutoScaleMode.None，按当前窗口 Graphics DPI 将设计边界缩放一次。探针结果：Label.Height=96 px，足以容纳 78 px 的文字。最终设置窗口已实际截图复核，设备名称、电量、连接状态三行完整；退出与关闭按钮可见。关闭窗口后后台进程存活。

原生模块实测：外层仍为 56×48 DIP、圆角底 52×28 DIP，真实 87% 的数字宽 16.915 DIP、百分号宽 8.593 DIP，均完整放入内部空间。当前数据距最近成功采样约 1.2 秒。

证据：`<部署目录>\evidence\appearance-20261029`。本轮完成构建、真实模块加载、数据新鲜度和设置界面视觉核对；读取逻辑、共享内存协议和位置布局未改，没有重复全量设备测试。

未通过 CUA 直接截取整个系统任务栏或实际悬停卡片；任务栏使用实际 XAML 布局尺寸核对。浅色设置窗口已经截图，深色分支尚未单独截图。多显示器及跨屏动态 DPI 不属于本轮验收。

本轮实际使用 frontend-design、computer-use、verification-before-completion 的相关流程。参考 [Microsoft DWM 主题接口](https://learn.microsoft.com/windows/apps/desktop/modernize/apply-windows-themes) 与 [WinForms 缩放实现](https://github.com/dotnet/winforms/blob/main/src/System.Windows.Forms/System/Windows/Forms/Layout/Containers/ContainerControl.cs)。
