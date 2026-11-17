# 鼠标电量工具 — 当前交接

更新：2026-11-16。本轮按 unslop 走查修复用户截图指出的设置窗与悬停卡缺陷（Catppuccin 契约内的精修，非改品牌），已晋升正式版；17:25 又修复造成 0xC0000374 崩溃的 Raw Input 分配回滚，并补齐高 DPI 下头部标记/开关尺寸与单鼠标卡片留白；21:5x 修复「任务栏最左侧」把左端刷成黑块的布局缺陷。历史轮次见各 dated 交接与 [track-flicker](2026-11-06-track-flicker.md)、[soft-colors](2026-11-06-soft-colors.md)、[settings-redesign](2026-11-06-settings-redesign.md)。

## 当前正式交付

- EXE：`E:\Agent任务\进行中\MouseBattery\app\MouseBattery.exe`。
- EXE SHA256：`968DE28023DA62979411CD67A5AC9BD90B73377971EA8184AC312F0AB8551F6D`。
- 内嵌 DLL SHA256：`4EA5227983BC1E66D04C3A36EF2A814BA81780971AA31E8E9810B494424164B2`（运行时组件目录同名）。
- 回滚备份：`E:\Agent任务\进行中\MouseBattery\robust-v3.2\baseline\MouseBattery-B1705B08.exe`（旧 EXE 全哈希 `B1705B083D7AEA847BC8A07AEDFCCF3DBADA2A0CF1FF2B43DD939271BF611158`）；更早一轮备份 `MouseBattery-60E6D4FA.exe` 仍在。
- 21:51:38 启动 PID 5428；活动设备 PRO X 2 DEX 53%、2.4G 无线、position=3（任务栏最左侧）、RawInput=on、RawInputError=（空）、Startup=True。Explorer 未重启。18:15 的 `D36F01B1…` 已被本轮覆盖（源码在 758d2f3，可重建）。
- `Native=initialized | visible position=3 percent=53 x=275.333344 width=56 height=48 … pillWidth=52 pillHeight=28 … kind=gaming`：position 3 走 Canvas 悬浮（无暗块），读数距左端 TrafficMonitor 面板 14 DIP。
- 本轮证据：`E:\Agent任务\进行中\MouseBattery\design-sheet\settings\settings-candidate-20261116-180958.png`（150% 真机设置窗）、`settings\sheet-S-light/dark.png`（控件状态矩阵）、`dwell-20261116-181346\now.png`（真机悬停卡）、`standalone\candidate\build-manifest.json`。

## 2026-11-16 圈出缺陷二次精修（任务栏示意图与轨内刻度）

- 用户红圈两处：设置窗「显示位置」整段示意图、悬停卡电量轨的两处刻痕。判定为轻度 slop 精修（Catppuccin 品牌不变）。
- 示意图：原「一排白色占位块」改画成可识别的 Win11 任务栏——开始四格（accent）、搜索/文件夹/浏览器/聊天线图标、托盘 chevron/wifi/音量、右对齐实时时钟（HH:mm + MM/dd，15s Timer 跨分钟才 Invalidate）、show-desktop 细条；全部 GDI+ 自绘且避开四个落点区。未选落点改「虚线胶囊槽」（镜像胶囊自身形状，3-3 dash、muted α150；hover/pressed 填身份色 α34/62），点击目标首次可见。Zones/VisualOrder/键盘与 AX 语义未改。
- 电量轨刻痕：原「上下出挑 12/14 band」在臂距读作刮痕（用户红圈）。改轨内分段标记 2×6（`Ui.NotchWidth=2/NotchHeight=6`，y=4 落在 6 高槽内），中性色=卡材质（浅=Surface 白、暗=Base 落在 Surface→Scrim 渐变轨行），读作「在 20%/10% 处切开」；越过阈值独立点亮 Yellow/Red 不变。托管 `NotchHalf=1`、原生 tick Margin 39/19、探针中性期望改 255,255,255 / 30,30,46。
- 回归：48 逻辑 + 18 IPC + 8 真机原生渲染全过；Raw Input canary 4/4、越界 0；控件矩阵明暗已查看；150% 真机设置窗与真机悬停卡 dwell 已查看（dwell series 第 5 帧起稳定 ~3 万像素差、18/24 过阈，其余为淡入期）。
- 部署：promote-candidate.ps1 优雅退出→备份 B1705B08→复制→双哈希校验→--background（PID 49396）。最新 component 目录=54E9CD7E… 确认新 DLL 加载。
- 未验证：暗色主题真机设置窗（系统浅色，暗色由控件矩阵覆盖）、隔夜/睡眠唤醒、Explorer 外部重启后自动恢复、≥30 分钟无崩溃观察。
- 遗留建议（未改）：companion 行双列+身份点（§2.2）、悬停胶囊数字改身份色（§4.2）。
- 改动文件：src/Program.cs（仅示意图/NotchHalf/轨刻 hunk）、src/Ui.cs、native/TaskbarView.h、src/AppearanceProbe.cs、docs/proposals/catppuccin-design.md。并行轮的 Raw Input/高 DPI/aPad hunk 与 test.ps1 保留未提交，未混入本提交。512 字节 ABI、堆修复表达式、Battery.cs/Hid.cs/TaskbarPublisher.cs/NativeHost.* 未动。

## 2026-11-17 双鼠标"跟随正在使用"修复（匹配规则）

- **更正上一节的错误结论**：WM_INPUT 一直都有投递，之前几次测到 `RawSeen=0` 是因为那几十秒用户没有移动物理鼠标（注入的 SendInput 不算真实设备输入，且其 `hDevice=0`）。加了 `GetRegisteredRawInputDevices` 回读后确认注册有效（`got=1 flags=256 usage=1,2`），随后 `RawSeen` 随真实移动正常增长。
- 真正的 bug 在匹配：原始输入报的是鼠标接口 `MI_00`，而电量读数来自厂商接口 `MI_02`。`KeyForRawPath` 用 `token:接口序号` 精确比较，于是永远对不上——`status.txt` 里 `ActiveKey` 从 11-16 起一直是 `(none)`，这就是"在用游戏鼠标却显示办公鼠标"的原因。
- 修复：先做精确匹配，失败时退化为按产品 token 唯一命中（`v.Token.StartsWith(token+":")`，且只有一个设备时采用）。设备内部键实测为 `PRO X 2 DEX|c0a0:02`、`Signature Plus M750 L|c548:02`。
- 离线验证（临时 csc 编译 Battery.cs + 测试 Main）：raw PRO X（MI_00）→ `unit-prox`；raw M750 → `unit-m750`，两者都命中。
- 真机确认（用户实测通过）：`ActiveKey=02-02-5C-94`、`RawLast=…MI_00… => 02-02-5C-94`、`Devices=PRO X 2 DEX 96% 正在充电 有线 [活动]`、胶囊显示 96%；`RawSeen=4730`。晃动游戏鼠标时读数跟随它，晃动办公鼠标时跟随另一只。
- status.txt 现保留 `RawSeen` / `RawLast` / `RawReg` / `Tokens` 四个诊断字段。

## 2026-11-17 双鼠标"跟随正在使用"排查（未解决）

- 现象：两只鼠标同时连接（PRO X 2 DEX 有线充电中 + M750 Bolt），用户在用游戏鼠标，胶囊却显示办公鼠标。
- 关键证据：`status.txt` 里 `ActiveKey=(none)` —— 原始输入的活跃设备标记**从未成功设置过**，不是这轮改坏的。
- 加了三个诊断计数：`DispatcherMsgs`（消息窗口收到的消息数）、`DispatcherRaw`（其中 WM_INPUT 数）、`RawSeen`（HandleRawInput 调用数）。实测 `DispatcherMsgs=43`、`DispatcherRaw=0`、`RawSeen=0` —— 窗口活着、`RegisterRawInputDevices` 返回成功，但**一条 WM_INPUT 都没有**。
- 已排除：隐藏窗口（原先）、屏外 1×1 实窗口、屏内 1×1 实窗口、以及设置窗当前台时；四种情况全部 0。判断为该系统上 `RIDEV_INPUTSINK` 的 WM_INPUT 不投递到这个进程（与本项目代码路径无关）。
- 结论：靠 raw input 自动判断"正在用哪只"这条路暂时走不通。可选替代：① 某只鼠标插拔/充电状态变化时把它切为当前（半自动）；② 设置窗里手动指定"优先显示哪只"（兜底）。
- 诊断字段 `DispatcherMsgs`/`DispatcherRaw`/`RawSeen`/`RawLast` 已留在 status.txt 里，方便以后复测。

## 2026-11-17 边框流光尝试（未落地）

- 需求：充电时外框有一道光沿边框转一圈。
- 方案一（旋转渐变）：`LinearGradientBrush` + `RelativeTransform` 旋转，动画 `RotateTransform.Angle`。光带不出现；把旋转中心从像素 (26,14) 改成相对 (0.5,0.5) 后仍不动。
- 方案二（虚线偏移）：`Path` + `EllipseGeometry` 画满一圈，`StrokeDashArray` 留一段亮弧，动画 `StrokeDashOffset 0→-131`（并置 `EnableDependentAnimation(true)`）。同样不动。
- 判据：连拍 8 帧（300 ms 间隔）对比边框左/上/右/下四点亮度——四点完全同步起伏（172→190→184→167→151→128→152→169），即只有呼吸环在呼吸；流光若在走应出现相位差，实测左右差始终 ≈0。
- 结论：任务栏 XAML 宿主只放行合成属性动画（`UIElement.Opacity` 能动），对 Brush/几何类 dependent animation 直接忽略。这是宿主限制，不是写法问题。
- 现状：流光代码已移除，呼吸保留（`charging` 1..3 触发）；未充电时该区域青色像素实测 0。
- 若继续：唯一有希望的方向是把光段做成独立 UIElement，用 `CompositeTransform`（Rotation + ScaleY≈0.52）拼出椭圆轨道，全部走合成属性；需再一轮实现与验证，且不保证宿主放行。

## 2026-11-17 菜单收窄与首帧闪烁修复

- 宽度：卡片 176 → 144 DIP（实测白色区间 215 px @150%，即 143 DIP），内容仍是 32 行高、16 图标、12 号字，右侧留白不再空旷。
- 首帧闪烁三处原因，都改了：① 原来先 `SWP_SHOWWINDOW` 显示、再 `SetWindowRgn` 裁圆角，中间会露出一帧方形白框——改为先定位、设圆角，最后 `ShowWindow(SW_SHOWNOACTIVATE)`；② `WM_ERASEBKGND` 返回 1 且 `WM_PAINT` 走内存 DC 双缓冲，消除"先擦白、再画字"的中间态；③ 绘制改到兼容位图再 `BitBlt`。
- 同时修掉一个自己引入的 bug：`SetWindowPos` 里带了 `SWP_NOZORDER`，它会吞掉 `HWND_TOPMOST`，窗口因此不置顶、会被前台窗口盖住——去掉该标志。
- 部署教训：命令链里 `Stop-Process` 在目标不存在时返回非 0，会中断后面的 `&& cp`，导致"部署看起来成功、exe 其实没换"。此后 cp 与 exe 哈希必须单独校验。
- 验证：白色区间 1929..2143（215 px）；鼠标扫过第二行 (255,255,255)→(242,243,244)；点「设置」设置窗实际打开。

## 2026-11-17 自绘菜单最终方案（普通窗口 + 圆角 region）

- 三次失败的原因（记下来，别再重走）：
  1. GDI+ 画到 HDC 不写 alpha 通道，`UpdateLayeredWindow` 收到全 0 alpha → 窗口"显示成功"但整张卡片透明。
  2. **Explorer 里 native DLL 会累积多个实例**：不同哈希名的 DLL 各自驻留、旧 worker 不随 reader 退出而停止，实测并存 4 个、各有一个菜单窗口。新注入的代码根本没有接管任务栏——这是这几天"改了没效果"的主因，不是改动本身无效。
  3. 改为普通窗口（去掉 `WS_EX_LAYERED`）+ `CreateRoundRectRgn` 裁剪圆角 + `WM_PAINT` 直绘，像素一次到位。
- 运维结论：**每次改 native 组件，必须重启一次资源管理器（explorer.exe）**，否则旧实例继续跑在新代码之前。部署流程要带这一步。
- 验证：右键 → 日志 `menu-owned-shown`、卡片采样 (255,255,255)；鼠标扫过第二行 → (255,255,255)→(242,243,244)；点「设置」→ 设置窗实际打开。
- 已知取舍：无软阴影（region 裁剪做不了）；Esc 不能关（无焦点窗口），靠 80ms 定时器 + `GetAsyncKeyState` 检测外部点击关闭。
- 改动：`native/TaskbarView.h`（OwnedMenu 模块）；`build-standalone-native.ps1` 增 `-lgdiplus -lgdi32`。

## 2026-11-17 右键菜单改为自绘窗口

- 背景：系统 MenuFlyout 改不动（presenter 只要设一个 setter 就完全不弹），XAML Flyout 在任务栏上方收不到指针事件。用户在「独立窗口」与「删掉菜单」之间选了前者。
- 实现：native 新增 `OwnedMenu` 模块。独立线程建 `WS_POPUP|WS_EX_LAYERED|WS_EX_TOPMOST|WS_EX_NOACTIVATE` 窗口，GDI+ 画 32bpp DIB（外 12 圆角、1px Border α115、6 层软阴影、32 DIP 行高、16 DIP MDL2 图标、Microsoft YaHei UI 12 号、分组用 4 DIP 留白），`UpdateLayeredWindow` 上屏，绘制后手工预乘 alpha；行 hover 立即重绘；`WM_LBUTTONUP` 判定行号后 `Launch` 对应参数。关闭用 80ms 定时器 + `GetAsyncKeyState` 检测外部点击——不用 `SetCapture`，因为 no-activate 窗口握不住指针，且第一次点击不该穿到下层。
- 定位：XAML 侧右键时用 `host.TransformToVisual(XamlRoot.Content())` + 任务栏窗口 rect + `GetDpiForWindow` 算出胶囊顶边中点（实测 dpi=144、anchor 2036,1528），菜单底边对齐、水平居中。
- 验证：窗口 rect 实测 `1886,1348 300x198`（144 DPI 换算正确）；卡片内五个采样点实测 (255,255,255)，说明确实画到屏幕上；日志 `menu-owned-shown` / `menu-paint dst=1886,1348 size=300,198 dpi=144`。
- 未验证：hover 高亮与点击动作的注入复验不稳定（多次注入测试中胶囊有时处于隐藏态，右键落空），需要用户实点确认。
- 构建依赖：`build-standalone-native.ps1` 追加 `-lgdiplus -lgdi32`。

## 2026-11-17 充电呼吸描边

- 需求：鼠标充电时给胶囊一点动效，克制、不干扰读数。方案由用户在三个选项里选定「描边呼吸」。
- 实现：pill 外包一层 52×28 Grid，底下叠一个 52×28、圆角 10、1px 的环；只在 `charging` 为 1..3（充电中）时出现，`Opacity` 在 0.30↔0.95 之间 1.2s 单程 + AutoReverse（2.4s 一个呼吸周期）、Forever 循环；颜色取身份色（office=Sapphire / gaming=Mauve），与 hover 玻璃同源。环 `IsHitTestVisible(false)`，不抢 hover、右键和 ToolTip。
- 验证（动画真的在 Explorer 的 XAML 里跑）：临时把门控改成常开，在真实任务栏连拍 8 帧（320ms 间隔），描边蓝色余量 78→105→121→94→68→44→59→84，呈约 2.4s 周期；恢复门控后未充电状态该区域蓝色像素为 0、环 `Visibility=Collapsed`。
- 未验证：真实硬件 `charging=1..3` 的端到端。测试宿主注入的 charging 没能进入 snapshot（日志里 `charging=0`），所以"充电才出现"这一步只验证了门控代码路径，等价效果靠临时常开模式证明。
- 改动：`native/TaskbarView.h`（View 增 `glowRing`/`glowStory`/`glowOn`，pill 外层 Grid，Update 门控，Remove 停止动画）。

## 2026-11-16 右键菜单改为应用卡片（unslop）

- 现象：右键胶囊弹出的是系统 MenuFlyout 默认外观——纯白不透明卡、一条贯穿分隔线、20 DIP 图标、字号与行高都不在应用的卡片体系里；和任务栏自己的 #F3F3F3 菜单并排时读作网页弹层。
- 方向（判定为轻度 slop，走 polish）：角色 = 安静、克制、精确；世界 = Windows 11 任务栏系统表面 + Catppuccin 纸白与发丝线；签名 = 1px 发丝线 + 外 12/内 8 嵌套圆角，分组用留白不用横线。
- 实现：保留 MenuFlyout（系统输入路径），用 `MenuFlyoutPresenterStyle` 把表面换成 `CatSurface` 底 + `CatBorder α115` 1px 描边；三项统一 MinHeight 32、Padding 8/12、字号 12、Microsoft YaHei UI 字体、FontIcon 16；去掉 `MenuFlyoutSeparator`，退出项用 `Margin 4` 分组；`Update` 里按明暗主题刷新 brush（此前菜单完全不跟主题）。
- 踩到的坑：presenter 上加 `CornerRadius`/`Padding`/`MinWidth` setter 后菜单**完全不弹**（无异常、无日志），只保留 Background/BorderBrush/BorderThickness 才恢复；圆角沿用系统 presenter 默认。
- 未采用的路（实测不可行）：完全自绘 Flyout。任务栏的 XamlRoot 只有 48 DIP 高，`ShowAt` 到栏外的卡片能画出来但收不到指针事件——右键、左键、悬停全部无响应（native-worker 日志确认 `ShowAt` 成功、`RightTapped`/`ContextRequested` 均触发）；要做彻底自绘只能改用 Win32 分层窗口，成本与输入路由风险另计。
- 未验证：菜单项点击。桌面鼠标在验证时进入 HID 超时休眠（`--diagnose` 返回 `C54D: HID response timeout`），无法用注入输入复验；交互路径与改造前一致，仍走 MenuFlyout 的 Click。

## 2026-11-16 任务栏最左侧（position 3）布局修订

- 现象：位置改成「任务栏最左侧」后，任务栏左端出现一块纯黑矩形（约 325 DIP 宽），本机 TrafficMonitor 的透明面板被衬成黑底，「53 %」深灰读数几乎不可读；读数与监控数据只隔 8 DIP，读起来像那块面板里的第六项。
- 根因：position 3 往任务栏 `RootGrid` 插入一个 `ExternalLeftWidth()+56` 像素宽的列，RootGrid 里铺满的背景层被 reindex 到列 1，新列没有背景 → 显示 XAML island 黑底；TrafficMonitor 是透明叠加，随之一起变黑。
- 修复：不再插列。host 放进一个 `Canvas` overlay（Canvas 不占布局槽），`Canvas::SetLeft = ExternalLeftWidth()+kExternalGap`（14 DIP）、host 56×48；每 500ms 仅当左端外部窗口宽度变化时更新偏移。`ExternalLeftWidth` 去掉原先隐含的 `+8`，间距统一由 `kExternalGap` 控制。
- 中途试过「host 作为跨列的 Grid 子元素」：暗块同样消失，但 ToolTipService 拿不到 56×48 的锚点，悬停卡不再出现（dwell 0/24）；改 Canvas 后卡片恢复（全屏截图可见 PRO X 2 DEX / 53% / 未充电 · 2.4G 无线）。
- 验证：真实任务栏剖面 x=0..600 在 y=8/64 全为浅色（无暗块），TrafficMonitor 数据直接显示在浅灰任务栏上；UIA 边界 `L=413 T=1528 W=84 H=72`（56×48 DIP）；位置 0（未改动路径）dwell 16/24 帧有卡片。
- 未验证：双鼠标伴随行、暗色任务栏、其它 DPI 档位。
- 改动文件：`native/TaskbarView.h`（View 增 `overlay`/`leftPad`、`kExternalGap`、Insert/Remove/Update 的 position 3 分支）；EXE `968DE280…`、内嵌 DLL `4EA52279…`，与 758d2f3 的源码基线叠加。

## 2026-11-16 崩溃回归修复（Raw Input 分配）

- 现象：任务栏百分比与托盘图标在运行数分钟后整体消失，进程已不在，不会自动重启。
- 证据：Application/1000 共 20 条 MouseBattery 崩溃，异常码 `0xC0000374`、故障偏移 `ntdll+0x112fc5`（11-13 起 18 条 + 11-16 16:59:38 / 17:00:20 两条，版本均为 4.5.1.0）。
- 根因：`RIDI_DEVICENAME` 的 `pcbSize` 是 UTF-16 字符数，`src/Program.cs` 却按字节 `AllocHGlobal((int)size+16)` 分配；真实鼠标上报时越界写 63–79 字节，踩坏相邻堆块。9993e95 已修，033c229（11-14 16:30 任务栏全链修复）把它连同 `rawInputError` 一起回滚。
- 修复：恢复 `AllocHGlobal(checked(((int)size+16)*sizeof(char)))`；恢复 `rawInputError` 并写入 `data/status.txt` 的 `RawInputError=`；设备名查询失败不再永久缓存 null，下次 WM_INPUT 重试。
- 探针：`tools/Test-RawInputNameBuffer.ps1` 从生产源码提取到的表达式即 `checked(((int)size+16)*sizeof(char))`，4 台真实鼠标设备 PASS 4/4、`overwrittenBytesBeyondAllocation=0`；旧表达式对照 FAIL 4/4、越界 63–79 字节。
- 门禁：`test.ps1` 末尾新增 Raw Input canary stage，同类回滚会当场失败（此前 `test.ps1` 不含该探针，11-14 的回滚静默通过）。
- 高 DPI 视觉（150%，客户区 560×502 DIP）：头部电池标记原按设备像素绘制（实测 24×21 px），改画在 26 DIP 设计空间后实测 37×31 px（含 nub 宽 25 DIP、高 20 DIP），与标题间距回到规格 12 DIP；「登录后自启」开关原轨道实测 40×20 px，加 `k=Height/26f` 设计变换后实测 61×30 px = 40×20 DIP。
- 单鼠标卡片：`managerCompanion` 收起时卡 A 底部空 48 DIP，现按当前 tooltip 是否有第二行决定 `aPad`（有伴随设备 0、单设备 14），内容与伴随行同步下移，上下留白 34/34 DIP。
- 部署：`app\MouseBattery.exe` 由 `--exit` 优雅退出后复制、SHA256 校验 `B1705B083D7AEA847BC8A07AEDFCCF3DBADA2A0CF1FF2B43DD939271BF611158`、以 `--background` 启动（PID 41448，17:24:57）；旧版备份在 `patch-20261116\MouseBattery-app-before-20261116.exe` 与 `standalone\candidate\MouseBattery-prev-991728D5.exe`。桌面/开始菜单图标缓存已用 `ie4uinit.exe -show` 刷新。
- 观察结果（21:56 补记）：17:25 首次部署修复版后至今，Application/1000 无新增 MouseBattery 崩溃（16:00 之后仅有 16:59:38 / 17:00:20 两条旧版记录）；其中 18:15 版（含同一修复）连续运行 3 小时 25 分，覆盖了旧版「3–5 分钟必崩」的窗口。仍未验证暗色主题真机设置窗、隔夜/睡眠唤醒。崩溃因果是「同一故障偏移 + 版本窗口对齐 + canary 越界 + 仓库既有 RCA」四重证据，不是现场可控复现。

## 2026-11-16 UI 走查修复（unslop）

- 设置窗：大读数改 DIP 空间自绘（数字与 % 共基线，根治小 % 丢失/错位的 "o/"）；迷你任务栏示意图全 GDI+ 自绘（ScaleTransform 下 TextRenderer 不缩放导致的落点错位根治）；刻度改 10%/20% 正确位置并上下出挑；右轨统一 500 DIP 对齐（电量/连接/芯片自适应宽度贴右）；命令区收敛为单行「退出工具 + 完成」RoundedButton（r10、hover/pressed/disabled/focus 全状态），窗高 524→502；头部渐变紫瓷砖改扁平电池标记（与托盘图标同族）。
- 悬停卡：nameRow 改 Grid（14px 点列 + Star 名列），身份点 8×8 + 16×16 α36 光晕；statusLine 改 Grid Auto+Star 复活右轨；去掉构造期 Opacity 双重 fade（墨色单 α 在 Update 设置）；刻度 margin 改 Grid 相对 39.25/19.25（契约 59.25/39.25 重复计了 padding）；卡大数字改身份色、状态墨色矩阵（重连=WarnInk、充电故障=CritInk、旧读数=α210 WarnInk）；track 下加 α115 发丝线；胶囊 % 改 11.5 DIP、单 α。
- 图标：`scripts/build-icon.ps1` Draw() 改扁平电池标记（accent #1E66F5、70% 底锚电量楔），16–128 DIB + 256 PNG 结构不变；预览 `E:\Agent任务\进行中\MouseBattery\icon-preview2.png`。
- 深色示意图占位图标块原 Surface α180 与条底仅差 1 RGB 步而不可见，改 Text α26 表面步（Program.cs PositionPicker）。
- 回归：48 逻辑 + 18 IPC + 8 真机原生渲染全过；控件状态矩阵明暗双主题已查看；150% 真机设置窗（含真实 85% 数据）与真机悬停卡 dwell 已查看（卡 ~600ms 出现、20 帧像素稳定）。dwell 脚本 30000 像素阈值按深色壁纸调过，浅色壁纸下以 series 非零稳定判定。
- 测试 harness 漂移修复（仅测试代码）：PublisherTests/AppearanceProbe/SheetHost 补 `MarkSuccess()`（offset 8 现为发布端系统时钟 tick）、PublisherTests 的 `Apply` 改两参。生产语义未改。
- 存量失败不属本轮：RecoveryTests 5 失败（该 stage 只编译 Battery.cs+RecoveryTests.cs，两者本轮未改；9993e95 登记并行轮时即红，待整合决策）。本轮回归门禁不含该 stage。
- 未验证：暗色主题真机设置窗（系统为浅色，暗色由控件矩阵覆盖）、隔夜/睡眠唤醒、Explorer 外部重启后自动恢复。
- 遗留建议（未改）：companion 行双列+身份点（§2.2）、悬停胶囊数字改身份色（§4.2；现为 Text 以与探针断言一致）。
- 脚本编码：build-icon.ps1 补 UTF-16LE BOM、probe-card-dwell.ps1 补 UTF-8 BOM（PS 5.1 读中文路径）；无 BOM UTF-8 的构建链脚本一律用 pwsh 7 执行。verify-candidate-settings.ps1 增加 -WaitMs 参数（首poll前截图会拍到"尚未读取"）。
- 改动文件：src/Program.cs、src/Ui.cs、src/app.ico、native/TaskbarView.h、scripts/build-icon.ps1、src/PublisherTests.cs、src/AppearanceProbe.cs、tools/design-sheet/{SettingsSheet.cs,SheetHost.cs,probe-card-dwell.ps1,verify-candidate-settings.ps1}。512 字节 ABI、堆修复表达式、Battery.cs/Hid.cs/TaskbarPublisher.cs/NativeHost.* 未动。

## 恢复显示所需修复

1. 托管端仍校验旧 taskbar.dll SHA256 `CA1E…`，而本机已为 `A52F99F7BE8CC1EE360025D1C06C1480A489B1D309BEF230C05EB21A106CCF34`。更新为已验证的当前完整指纹，继续保留严格版本守卫。
2. 跨端对象名不一致：托管发布 v2，但原生读取 v4；托管健康读取 v3，但原生写 v4。将原生恢复为发布端的 `Local\MouseBatteryTaskbar.v2` 和 `Local\MouseBatteryTaskbar.NativeHealth.v3`，未改 512 字节快照 ABI。`tools/SnapshotDump.cs` 同步名称，避免诊断误报。
3. 旧 S5 将 `CTaskListWnd::GetHost`（0xF9C40）误认为所需的 `CTaskBand::GetTaskbarHost`。原 Windhawk 源码需求是后者，PDB 完整符号解析给出正确入口 **0x1237B0**，已修正。其余为 FrameHeight=0xB10A4、Decref=0x290B0、CTaskBand/ITaskListWndSite vtable=0x1CFA30。
4. 修正 3 个符号脚本的 SYMBOL_INFO ABI 使用：Name 在偏移 84，不是带尾部 padding 的 sizeof=88；SizeOfStruct 必须是结构尺寸，不是含字符串缓冲的总尺寸。vtable 查询还修复了 RVA 未加基址、读取未回填的 NameLen=0 两个问题。新解析结果保存在 `taskbar-symbols-corrected.txt`、`profile-symbols.txt`、`vtable-check-corrected.txt`。

## 必须保留的失败记录

最初只对齐版本和通道、沿用旧 S5 getter 的候选，在 18:29:40 触发 Explorer 的 **0xC0000005，Taskbar.dll+0xF9C63**，是错误 GetHost 内部。Explorer 随后自动恢复到 PID 21068（18:29:53 启动）。本轮没有主动强杀或重启 Explorer，但不能写“Explorer 从未重启”。后台当即恢复已修复的托盘版，停止部署错误候选。

随后按完整 PDB 符号和函数体证据修正 getter，8 项实际原生渲染检查全部通过，再部署正式版。错误事件和候选分别保留在 `probe-errors.json`、`failed-candidate`，不能删除失败痕迹或继续引用旧“G1 4/4”的通过声明。

## 恢复任务栏时的验证（历史）

- `native-build-fixed-getter.txt` / `build-fixed-getter.txt`：Clang C++23 与 Framework C# 构建成功，显式内嵌本次 DLL。
- `appearance-probe-fixed.txt`：8/8 真实 XAML 渲染检查通过（输入为明确的模拟 0/9/10/20/21/86/100/100旧值）；各自电量、颜色、刻痕、56×48 占位正确；结束 DetachResult=0。不是只检查 EXE 侧 initialized。
- `real-status.txt`：正式版真实 85%，原生实际布局 visible，x=1336、width=56、height=48 DIP。
- `snapshot-final.txt`：共享快照 state=1、percent=85、readerPID=48676、pos=0、startup=1，与实际原生布局一致。
- `modules-final.txt`：本项目正式 DLL 确实在 Explorer 中加载；探针 DLL 因现有引用策略也保持映像驻留，但探针 worker 已正常停止、布局已移除。未为卸载该映像重启 Explorer。
- `deployment.json`：优雅退出→等待文件可独占打开→复制→校验→启动，无强杀覆盖。
- `post-deploy-errors.json`：修正后正式部署窗口内无新的 MouseBattery/Explorer Application/1000 或 1026 错误；此前失败单独保留。
- 恢复任务栏当时复用48逻辑+15 IPC及4设备canary，Program.cs为A7CD…；本轮Program.cs已变化并重新验证，当前以以下深查修复证据为准。

## 范围与剩余风险

- 堆损坏根因、红绿实验及 3 轮设置验收见 [RCA](../proposals/heap-corruption-rca.md) 和 [上一交接](2026-11-05-heap-fix-delivery.md)。修复表达式保持 `AllocHGlobal(checked(((int)size+16)*sizeof(char)))`，不得回滚。
- 仅适配当前 taskbar.dll 完整指纹，系统再次更新需按准确符号和原生检查重新适配。
- 实际任务栏截图、真实隔夜/睡眠唤醒、Explorer 外部重启后的自动恢复、完整双模式偏好切换未在本轮验收；不可扩大为全链路稳定性关闭。
- 不恢复/重写整个 5492525/fa76eb6，未改原生胶囊、系统主题或 NotifyIconSettings，未启动 Windhawk、未提交或 push。

## 深查修复与当前验证

- 原始审查 [报告](../proposals/deep-review-20261105.md)作为历史保留；本轮修复7项发现，详见 [交付与逐项边界](2026-11-05-deep-fixes-delivery.md)。
- 最终56逻辑+18 IPC+9恢复检查通过；原实现恢复回归5项失败→修复后0失败。当前源码提取的Raw Input名称分配表达式，5个真实节点canary全部无越界。
- 本机C54D/C548真实接口路径均可匹配唯一逻辑鼠标；正式运行已产生活动鼠标标识。相同PID多只设备不猜测归属，parent/container精确关联仍未实现。
- 显式NativeDll构建成功，实际嵌入05B2…；缺参构建会拒绝。正式EXE复制前等待优雅退出和独占打开，无强杀覆盖，旧版70AE…保存在本轮before目录。
- 深查交付时Program.cs为5187…；电量条修复后当前SHA256为`0EE2C67923ABD345B4EBDB53D76D21B7E09348CE0AAF09C2296002D95FF62BB8`，Raw Input方法未改，UTF-16字节修复未回退；Battery.cs与TaskbarPublisher.cs未变。
- 深查交付当时真实快照state1、percent80、owner11476、age5688ms且无未来时间戳；11-06 00:00:50前该次部署后匹配崩溃事件为0。当前UI版进程以顶部PID为准。长时/真实睡眠唤醒尚未验收。
- 首个候选设置三轮开关通过；最终版UI源码未变，复用该证据并追加一轮冒烟，通过后关闭窗口且后台保持。详见`ui-validation.md`、`ui-validation-final.md`；截图仅CUA内联，任务栏/悬停卡视觉未验收。线程等待、模块引用所有权、日志频率等建议仍属后续事项，不与本次7项混为已完成。

## 当前设置页优化

- 仅Program.cs/Ui.cs：现为低饱和雾蓝灰强调与浅灰蓝按钮；数字与百分号合并绘制，去掉重复型号徽标，四个文字位置选项，分段电量刻度，辅助文字与底部留白保持。高度不足可滚动；不提供新的双模式偏好。
- 56逻辑+18 IPC+9恢复回归通过；明暗×80/100/未连接6种离屏模拟渲染已查看关键状态。独立控件12个缩放点击目标与左右键检查通过，不等同实体系统DPI切换验收。
- 首次新版真机CUA确认80%紧邻、蓝色统一、选项与底部按钮完整；发现读数AX名称缺数值后已补成实时80%/暂无电量并重新构建、优雅部署。最终AX与截图均为80%，关闭设置后PID47384存活，详见`ui-validation-final.md`。验收结束后又为用户打开新版设置页，当前窗口有意保留供查看，记录`final-user-view.json`。
- 两次UI部署均有优雅退出与文件独占打开记录，Explorer保持PID12264。00:42:29状态真实80%，原生visible；没有变更系统主题、任务栏位置或启动偏好。
- 后续软色调部署于00:48:26，实际CUA确认AX/视觉80%、雾蓝灰条与开关、浅底深字按钮和完整底部；关闭后47444存活。验收后重新打开软色调设置页供用户查看，详见当前证据目录`final-user-view.json`。布局轮与配色轮均未改变持久偏好。

## 电量条闪烁修复

- 旧普通Panel未双缓冲，250ms无条件Invalidate。独立实际WinForms更新探针稳定80%时6tick有6次WM_PAINT；修复后0次，值变更/断开/恢复仍正常绘制。只有level改变才显式Invalidate，条控件启用双缓冲。
- 明暗×80/100/未连接6种静态预览前后PNG逐组完全相同，配色/布局不变。实际屏幕连续无闪烁不能由静态截图预先认定；真实窗口检查见当前重绘目录`ui-validation.md`。
- 部署前已没有旧驻留进程，00:55:54旧生命周期记录DetachResult=0；原因未确定。发出退出信号并通过文件独占打开后复制/启动，未强杀。00:48:26至00:58:59无相关崩溃事件，Explorer未重启。
- 实机两次间隔截图均80%、软色条与底部完整；关闭后47640存活。检查后再次为用户打开设置页供观察，见`final-user-view.json`。静态截图未扩大为连续无闪烁验收。
