# 鼠标电量工具 — 当前交接

更新：2026-11-02, Asia/Hong_Kong。当前正式交付 **v4.0.0**，Catppuccin 主题重构完成。

## 当前交付锚点

- 正式 EXE：`E:\MouseBattery\app\MouseBattery.exe` (待晋升)，SHA256 `8F311D56422C3C30E1C1DADBAD3652DB5BE7796A5F55F172DEBFFCF217ACA065`
- 内嵌原生 DLL SHA256: `2A07E8FA573C9430F7224D353F193FA6B3D19415BA43CB92F98726F0E1073517`
- Git `main` 最新：（即将提交）
- 备份：`E:\MouseBattery\robust-v3.2\baseline\` (保留 v3.8.3)

## 设计轮完成（v4.1.0 方案，待实现）

Qoder 设计师轮产出 `docs/proposals/catppuccin-design.md`（590 行，主会话审查通过，Git e758414）。核心：11 条可复核缺陷审计（F1"85% %"双百分号、F2 刻度线画在 80%/90% 却按 20%/10% 点亮——历史遗留真 bug、F3 玻璃底未走 token、F4 蓝牙误标 2.4G）+ 五带信息架构 + 三档材质（Edge-lit/Frosted/Warning）+ 身份色玻璃底推导 + token 总表与断言迁移清单。实施顺序 S1-S8 在文档 §7，R2（刻痕容器裁切）为 S3 首查项。崩溃观察：RawInput=on 已稳定 140+ 分钟（历史崩溃窗口 ~30min），接近关闭。

## v3.7 → v3.8.3 演进摘要（详见 git log）

- **v3.7.0 双鼠标支持**：PRO X 2 DEX + M750L，任务栏自动跟随当前在用的（Raw Input），悬停卡列全两只。
- **v3.7.1 Bolt 索引扫描**：M750L 经 Bolt 接收器时配对在设备索引 2（非 1），发现阶段扫索引 1-3。
- **v3.8.0 差异化显示**：Kind 由身份字节判定，悬停卡按设备人格渲染。
- **v3.8.1 chip 改产品线名**：用户不要"电竞/办公"分类词，chip 改为 PRO X 系列/Signature 系列。
- **v3.8.3 崩溃修复（重要）**：v3.8.0 原生源码变量顺序错误导致该轮原生构建实际失败，部署管线放行旧 DLL 继续走完；叠加 Raw Input `GetRawInputData` 缓冲只有 header 大小（需 header+RAWMOUSE），开设置时 UI 线程繁忙、WM_INPUT 爆发期踩堆（0xC0000374）。**教训：原生构建失败必须中止整条部署链；DLL 哈希要核对是本次产物。**

## v4.0.0 Catppuccin 主题重构（已完成）

- **测试验证**：`test.ps1` 48 逻辑 + 12 IPC 全过；AppearanceProbe 8 项全过（含刻度线点亮断言）
- **构建部署**：`build-standalone-native.ps1` 成功编译原生 DLL；`build.ps1` 生成候选 EXE `candidate-v400/MouseBattery.exe`
- **EXE SHA256**: `8F311D56422C3C30E1C1DADBAD3652DB5BE7796A5F55F172DEBFFCF217ACA065`
- **DLL SHA256**: `2A07E8FA573C9430F7224D353F193FA6B3D19415BA43CB92F98726F0E1073517`
- **主要改动**：
  - A. 设置窗口 `src/Program.cs`: 新增 `Ui.cs` token 类，所有颜色、几何、排版从 Catppuccin token 取
  - B. 任务栏胶囊 + 悬停卡 `native/TaskbarView.h`: 增加 token 函数，卡片宽度 224→232，padding {14,12}→{16,14}，大数字 30→32pt
  - C. 降级托盘 `src/TrayIcon.cs`: 调用 Ui.GetColor(token)
  - D. 铁律遵守：静息纯文字无底、左键零动作、无位移动画、托管 C# 用 C# 5 语法（无 ?.、$""、表达式体、nameof）
- **未完成/边界**: 真机验收（双鼠差异化实截、深浅主题截图）记为待观察，代码已就绪

## v3.7 → v3.8.3 演进摘要（详见 git log）

## v3.7 增量（11-01）

用户要求添加罗技 Signature Plus M750 L（AA 电池，Logi Options+ 配套）。实测侦察确定其接入形态后实现多设备支持，展示方案经用户确认：**任务栏自动跟随当前在用的鼠标，悬停卡列全两只**。

### 侦察结论（全部实测，M750Probe/ListAll 证据在 robust-v3.2）

- M750L 走**蓝牙 BLE**（BTHLEDEVICE，PID B02C），**不**在 C548 Bolt 接收器上（C548 在位但无配对应答）；设备实例 ID 为 GATT 前缀格式 `HID\{00001812-...}_dev_vid&02046d_pid&b02c...`——旧过滤器 `vid_046d` 因此漏掉它。
- BLE 厂商集合 usage page **FF43**（非 FF00），in/out 20/20，长 0x11 报文可用，地址 255。
- 电池特性与 PRO X 相同：0x1004 Unified Battery（idx 8），百分比位支持，实测 85%。
- **BLE 休眠即断链**：HID 节点整体消失（GATT 节点保留 IsConnected=False），重连才回来；Win32_Battery/MSBatteryClass 不聚合它的电量，必须走 HID++。
- 工具链新增：`M750Probe.cs`（Bolt 45s 守候抓取）、`ListAll.cs`（全集合枚举+BLE 握手，含 120s 循环重枚举）、`Native.Enumerate` 增加 allCollections 诊断参数。

### 实现

- `src/Hid.cs`：路径过滤接受 `vid&02046d`；厂商页放行 FF00+FF43。
- `src/Battery.cs` 重写为多设备模型：`BatteryReading` 增加 Path；`BatterySource` 按接口路径维护每设备缓存/特性索引/失败退避（10s→20s→40s→60s，BLE 重现即复位）；候选白名单 C54D(addr 1)/C0A0(255)/B02C(255)/C548(addr 1，60s 退避——当前无配对，将来把 M750L 配到 Bolt 免改码)；身份白名单 PRO X（[9][10]=C0,A0）+ M750（[7][8]=B0,2C）；传输标签 有线/蓝牙/2.4G。
- `DisplayState` 多视图：按 unit 序列号（identity[1..4]）跨重连稳定；`SetActive` 来自 Raw Input；显示规则=活动设备新鲜则显示之，否则最近成功的新鲜设备，全不新鲜隐藏（各自 15s 期限）；`Tooltip` 行0=显示设备名、行1=另一只摘要（≤15 分钟内，含"旧读数"标记）；`KeyForRawPath` 按 pid 令牌匹配 Raw Input 路径（两种实例格式同令牌）。
- `src/Program.cs`：`MessageWindow`（WM_INPUT 透传）+ `RegisterRawInputDevices`（RIDEV_INPUTSINK 挂 dispatcher）+ `GetRawInputData/GetRawInputDeviceInfoW`（hDevice→路径缓存字典）；`--diagnose` 输出改为逐设备；诊断串新增 `ActiveKey=`/`RawInput=`/`Devices=`。
- `src/TaskbarPublisher.cs`：快照字段取 `Displayed` 设备，transport 新增 3=蓝牙；**ABI 结构/共享内存名不变**，双鼠信息经 tooltip 行传递。
- `native/TaskbarView.h`：悬停卡新增 `tooltipOther`（tooltip 行1，单设备 Collapsed）；transport==3 显示“已连接 · 蓝牙”。
- 测试契约更新：Tests.cs 38→48 项（新增双设备用例：活动跟随/失效回落/全静默隐藏/两种 Raw Input 路径格式映射）；PublisherTests 10→12 项（蓝牙 transport=3、伴随设备 tooltip 行、平局取最新应用者）。
- 版本 3.7.0.0；新内嵌 DLL SHA256 `4DD7F948AE347B7A25E7F1125FF81B647DE0C9367151078A90A6D0C6BD044C8F`；正式 EXE SHA256 `49EC09879B28419DC330F52AC20AC318CA472DCFC5B0F9F0797481F899CBB174`（v3.6.3 备份 `baseline\MouseBattery-v3.6.3.exe`）。

### v3.7 验证

- `test.ps1` 48 逻辑 + 12 IPC 全过；AppearanceProbe 8 项（新 DLL 重建，MarkFailed 模拟旧读数）全过，单设备 `other=0` 正确。
- 真机上线即验证：M750L 使用中 → `ActiveKey=CD-4C-7D-86`（Raw Input 命中）、任务栏显示 M750L 85% 蓝牙、`RawInput=on`、`Native=initialized`；PRO X 休眠中未出现在列表（触碰后应出现并跟随，待用户日常使用观察）。
- 构建期抓出并修复：`--diagnose` 旧 API、Raw Input P/Invoke 前缀笔误、降级托盘 `state.Last` 残留、Probe 旧读数模拟方式（MarkFailed 替代 Apply(null)）、测试断言的设备名空格与平局顺序问题。
- 待观察：双鼠切换跟随的用户实际体验（status.txt 的 ActiveKey/Devices 行持续记录）；M750L 休眠断链/重连的日常节奏。

## v3.6 增量（10-31 晚）

## v3.6 增量（10-31 晚）

用户纠正 v3.5 的两个方向性错误：① **左键点击开设置必须移除**——“左键不能有任何动作，万一误触”，只保留右键菜单与悬停；② 任务栏读数要“完全契合任务栏”而非贴片感（用户使用 TranslucentTB 透明任务栏，玻璃底在透明背景上像贴片）。

- 左键 `PointerPressed` → `Launch(--manage)` 已整体移除（handler+token+Remove 解绑），左键零动作是硬约束，写入 README。
- 静息态与原生时钟同构：pill 背景与描边透明，数字 Segoe UI Variable **Text 12.5 Regular**（原 Display 15.5 SemiBold），% 9.5pt；悬停时玻璃底+描边出现、数字字重转 SemiBold（`PaintPill` 按 hover 分支，无动画）。阈值变色、悬停卡、菜单图标、刻度点亮全部保留。
- 编译踩坑记录：WinRT `FontWeights` 是 `Normal` 不是 WPF 的 `Regular`。
- 版本 3.6.0.0；新内嵌 DLL SHA256 `BA02518CD0706DC122DAD2DF6713340A357CE79161A5D714F0B00C099479D252`；正式 EXE SHA256 `2EA2D00C8C1FB8ECD06FF9B7AC3EF85521A8A8097F47AB9F681B7A926E6C6C7E`（v3.5 备份 `baseline\MouseBattery-v3.5.0.exe`）。

### v3.6 验证

- `test.ps1` 38+10；AppearanceProbe 8 项（新 DLL 重建）全过，100% 内容宽降到 27.5（同构字体更窄，占位约束余量更大）。
- 真机：晋升后 `Native=initialized`、真实 52% 实时读数、`Startup=True`。
- 静息同构/悬停显底为视觉状态，待用户任务栏目视（全屏游戏与光标不扰原则下不做自动化截屏）。

## v3.5 增量（10-31）

## v3.5 增量（10-31）

用户确认三项质感方向：胶囊交互可感知、刻度线点亮、状态符号语言。原生 `TaskbarView.h` 重建（新 DLL），托管同步。

- **胶囊交互**：PointerEntered/Exited 悬停提亮（alpha +18，`PaintPill` 助手即时重涂，不等 500ms 数据节拍）；host 左键 PointerPressed → `Launch(--manage)`（右键保留 ContextFlyout）。注：UWP XAML 无 per-element Cursor API，手型光标不可用（编译期确认，记为平台限制）。
- **刻度线点亮**（签名元素第二层）：20% 刻度在填充越过时变琥珀、10% 变红，未越过保持中性；健康串新增 `tickHigh=`/`tickCritical=` 颜色输出，AppearanceProbe 按值断言（0/9/10 双亮、20 单亮、21+ 双中性）。设置窗口 `managerTrack` 同款逻辑。
- **符号语言**：右键菜单 FontIcon 图标（设置 E713/切换启动 E72C/退出 E7E8，离线渲染表核验）+ 退出前 MenuFlyoutSeparator；悬停卡充电时状态行前 E945 闪电 FontIcon（accent 色，非充电态 Collapsed）。
- 版本 3.5.0.0；新内嵌 DLL SHA256 `E7EE9B7BF6B46D3E1D8AA6170690F8182AA7F1D994F7AD4AF895432AD5A3F7A5`；正式 EXE SHA256 `FFFE1ED3E1FBF42076C791FBD3A72508DD2C56604EF9B8B9C83FCD9C02DA3CA8`（v3.4.1 备份 `baseline\MouseBattery-v3.4.1.exe`）。

### v3.5 验证

- 原生初次编译抓出三类错误并修复：UWP XAML 无 Cursor API（功能放弃而非绕过）、`v.`/`v->` 笔误、缺 `Windows.UI.Input.h`。
- `test.ps1` 38 逻辑 + 10 IPC；AppearanceProbe 8 项含新刻度断言全过。
- 真机：候选真实 52%、正常退出 `DetachResult=0`（验证新事件解绑不破坏 Detach/Remove 路径）、晋升后 `Native=initialized`、`Startup=True`。
- **待目视确认**（验收时全屏游戏在前台，桌面自动化停止；图标码位已离线渲染核验，属性路径由 Probe 覆盖）：悬停提亮的实截、左键点击开设置、菜单图标观感、充电闪电（需充电时目视）。
- `app\components\` 现存 4 个历史版本目录（4F15/5832/BF57/E7EE），按需清理非本轮范围。

## v3.4 增量（10-30 深夜）

## v3.4 增量（同日深夜）

用户要求全方位美化，确认方向“精修雾蓝·仪表化”（保留雾蓝身份，工艺升级；任务栏只显示百分比、无动画两个已确认约束不动）。**本轮结束原生侧自 v3.1 未动的状态**：`native/TaskbarView.h` 重建为新原生 DLL，托管侧同步升级。

- 任务栏胶囊：单色填充改垂直 LinearGradientBrush 玻璃渐变（顶浓底淡，三态同构），描边 alpha light 26→45 / dark 36→55，圆角 8→9，数字 Segoe UI Variable **Display 15.5pt**，% 后缀 0.75 不透明度。外层 56×48 与内容宽约束不变。
- 悬停卡片（签名元素落地）：卡宽 208→224、内边距 {14,12,14,12}；大百分比后加 13pt 弱色 "%" 单位；**刻度条 3→4px 并在 20%/10% 位置加两道阈值刻度线**（Grid 结构：背景+填充+两枚 Rectangle，刻度绘制在填充之上）；ToolTip 加 CornerRadius 8；名称行不透明度 0.78→0.85。
- 设置窗口（`src/Program.cs`）：新增 `RoundedPanel`（圆角填充+1px 描边，token #D8E3F0/#3A4A5E）用于状态卡与降级横幅；状态卡大数字 22→30pt Display + 独立小 "%" 标签（离线态大数字显示 "--" 不再重复百分号）；刻度条 4px + 同款阈值刻度；标题左侧 24px 自绘电量标记；自启复选框 → `ToggleSwitch`（CheckBox 子类 UserPaint 全自绘 36×18 轨道开关，Checked/UIA ToggleState 语义保留）；按钮补 MouseOverBackColor；位置选择器描边 token 统一；选择器胶囊离线显示 "--"。
- 托盘降级图标（`src/TrayIcon.cs`）：纯文字 → 电池壳设计（圆角壳+右侧 nub+内部数字，阈值色作用于整体）；`Tests.cs` levels 加 -1，逻辑检查 36→38。
- 版本 3.4.0.0。

### v3.4 验证

- `build-standalone-native.ps1` 重建成功；新内嵌 DLL SHA256 `BF5777F11A3E2C8F60948EA226AAE15C56FA2BB13B0460DB8839A8DD1A4AA37F`（旧 4F157C45 组件目录保留，应用按哈希各自解压）。
- `test.ps1`：38 项逻辑 + 10 项 IPC 全过；`icons.png` 证据表含电池壳 -1/-- 占位行。
- AppearanceProbe（新 DLL 重建）8 项全过：56×48 外层、100% 内容宽 33.100≤42、阈值色、旧值 state=2。
- 正式 EXE SHA256：`C7FBFEC58C66DAC0337693386E5BF7FBD5E7626D8BC09A423E4BAAB63EE2A147`（v3.3 为 `A338EB37…DCF9`，已备份 `robust-v3.2\baseline\MouseBattery-v3.3.exe`）。
- 截图证据（`robust-v3.2\`）：`settings-v340-offline.png`（深色正常态：电量标记/圆角卡/Display 大数字/开关/选择器）、`settings-v340-degraded.png`（假指纹降级横幅态）。
- 真机链路：候选启动时真实读到 78%（01:47:51）；随后设备空闲休眠（C54D timeout），15 秒旧值期限按设计自动隐藏（`TaskbarDataVisible=False`）——顺带完成一次期限路径的现场验证。设备唤醒后恢复显示。
- 未完成/边界：**悬停卡片实截未完成**（设备休眠期间胶囊隐藏，两次捕获尝试均未出现 tooltip；悬停卡属性设置路径由 Probe 覆盖）；浅色主题截图继续记边界；ToggleSwitch 方向键/读屏实测未做（Checked 语义未变，风险低）。

### 单实例测试注意

本轮实测确认：候选运行中启动假指纹探针 `--manage`，信号被转给候选（探针按单实例设计退出）——探针类验证前必须先 `--exit` 候选。

## v3.3 增量（同日晚间）

## v3.3 增量（同日晚间）

用户确认做三项设置窗口改动：迷你任务栏位置选择器（签名改动）、降级状态说明条、深浅主题实时跟随。改动全部在 `src/Program.cs`（版本 3.3.0.0），原生侧与 HID/IPC 链路未动。

- 迷你任务栏选择器 `PositionPicker`：自绘 Control 画出任务栏示意图（应用按钮区/托盘区/时钟块），电量胶囊（含实时百分比）钉在四个落点中的当前位，其余落点画锚点圆圈；点击落点或左右方向键切换（`IsInputKey` 接管方向键，焦点圈可见），落点命中区宽约 10% 控件宽。无障碍：AccessibleName/Description + 键盘路径，替换原 owner-draw ComboBox。
- 降级横幅：降级模式下窗口顶部显示琥珀说明条“任务栏版本未适配，当前以托盘图标显示电量（指纹 <SHA256 缩写>），适配后自动恢复”（`ShortHash` 取 64 位指纹前 8 后 4），隐藏位置选择器，底部提示改为“关闭窗口后继续在托盘显示”，窗口高度相应收敛。
- 主题实时跟随：`SystemEvents.UserPreferenceChanged` → UI 线程检查 `AppsUseLightTheme` 变化 → 关闭并重建设置窗口（调色板统一重读）；降级时托盘图标同步换色重建。Dispose 解除订阅。

### v3.3 验证

- `test.ps1` 36 项逻辑 + 10 项 IPC 全过（不触及 ShowManager，守护未被改逻辑）。
- 设置窗口三态截图（`E:\MouseBattery\robust-v3.2\settings-v330-*.png`，PrintWindow 实截）：深色正常态（胶囊在位置 0、三锚点、说明文字正确）、点击落点 1 后（胶囊移动、说明“紧邻时钟左侧”、注册表 TaskbarPosition=1）、假指纹降级态（琥珀横幅 + 指纹缩写 + 选择器隐藏 + “托盘显示”提示）。点击回位置 0 已还原注册表。
- 注意：本次窗口开在 125% 缩放的屏幕（此前交接按 150% 验证），布局按 DPI 正确缩放，坐标点击按 1.25 系数命中，实际额外验证了非 150% DPI 下的布局自适应。
- 未验收：浅色调色板未单独截图（改系统主题验证过于侵入；调色板代码与深色对称）；主题实时切换未实测（同因）；方向键路径未实测（需抢焦点，代码路径与点击共用 Apply）。降级横幅下的窗口交互只做了截图核对。

## v3.2 主改动（当日早些时候）

用户授权按健壮性、资源与性能、功能增强（指纹失效降级托盘）、轻整理四个方向优化。全部改动仅在托管 C# 侧；原生 `native/NativeHost.cpp`、`native/TaskbarView.h` 未改动，内嵌原生 DLL 与 v3.1 完全相同。

- 正式 EXE：`E:\MouseBattery\app\MouseBattery.exe`。
- EXE SHA256：`A338EB37552A84CEE50B3AA79A3BE3EC2A744694D44EA4B8B624F66AB072DCF9`（v3.3.0；v3.2.0 为 `9A6E35DE…B04C4`，v3.1 为 `1B0DBEA6…2A56A`）。
- 内嵌原生 DLL SHA256：`4F157C4598657842A8FD98D3E3D99E269760385AE716FE73C637723C0A3D5A08`（未变）。
- 本轮目录：`E:\MouseBattery\robust-v3.2`（`candidate-v330`、`degraded-probe*`、`baseline\MouseBattery-v3.1.exe` 与 `baseline\MouseBattery-v3.2.exe`、`appearance-probe.txt`、设置窗口截图）。
- 真机实测 78%、无线、未充电、位置 0、登录自启开启；Explorer 未重启。
- taskbar.dll 指纹 `CA1E335A…0741` 不变；v3.1 交接见 [v3.1 历史](2026-10-29-taskbar-v3.1.0.md)。
- 源码 Git 基线 `main` 分支（2026-10-30 建立，v3.2.0 首次提交），v3.3 以 feat 提交跟进。

## 本轮改动

- `src/Program.cs`：① `Poll()` 由裸 async void 改为整体 try/catch，意外异常写入既有 `failure` 错误面，不再可能击穿常驻进程；② `Startup.Enabled` 结果缓存于 TrayContext，250ms Tick 不再每次开注册表键（所有改动路径经 `SetStartup()` 刷新缓存，托盘/原生菜单 toggle 仍读实时注册表决定目标值）；③ 降级托盘：`NativeHost` 构造抛 `NotSupportedException`（指纹/profile 不匹配）或运行中 `Maintain()` 抛同异常（Explorer 侧热更新场景）时转入降级——创建 `NotifyIcon`（`TrayIcon` 渲染，仅在电量变化时重建图标）、右键菜单（打开设置/退出工具）、双击开设置、气球提示一次、`status.txt` 记录 `Native=降级托盘（…实际哈希…）`；降级模式跳过 Publish 与 Maintain，并经 Tick 节流重试把 shell 已建的 `HKCU\Control Panel\NotifyIconSettings\<…>` 条目写为 `IsPromoted=1`（按 ExecutablePath 匹配，最多尝试 10 次每次 2 秒），用户已确认选择自动提升。
- `src/Hid.cs`：`Native.Enumerate(List<string> rejected)` 单台设备 `CreateFile`/`HidD_GetAttributes`/`HidP_GetCaps` 失败改为跳过并记录，不再让一台被占用设备拖垮整轮枚举；setupapi 级失败仍抛出。
- `src/Battery.cs`：`Poll()` 把 rejected 并入最终 `LastError`。
- `src/NativeHost.cs`：`Dispose()` 生命周期日志写入包 try/catch，data 目录被占用不阻断退出清理。
- `src/TrayIcon.cs`：新增无读数占位（`--`，正常色不入阈值红/黄）。
- `src/Tests.cs`：新增 3 项占位渲染检查；`src/Probe.cs` 调用点同步签名。
- `build.ps1`：sources 增加 `TrayIcon.cs`；新增 `-NativeDll` 参数（默认指向 v3.1 原生 DLL `E:\MouseBattery\appearance-v3\MB-UI-02-v1\MouseBattery.Native.dll`），不再隐式依赖候选目录里的旧 DLL。
- `build-probe.ps1`（新增）：AppearanceProbe 编译脚本化，内置 `/resource:<原生DLL>`；手动编译漏掉该资源会因 `GetManifestResourceStream` 返回 null 空引用（本轮实测确认）。
- 版本号 3.2.0.0；README 同步降级行为。

## 评估后明确不做

- 共享内存发布去重：MMF 写入是内存操作，原生侧本就按自身 500ms 节奏重渲染，去重无实际收益且要动已验证 IPC 文件。
- HID 句柄复用：当前每轮开关句柄对设备休眠/重插更鲁棒，收益约 1-2ms/5 秒。
- 250ms Tick 降频：稳态开销可忽略，降频只拖慢菜单响应。

## 验证及准确边界

- `test.ps1`：36 项逻辑回归（33 项既有 + 3 项占位新增）+ 10 项 IPC 全部 PASS。
- `appearance-probe.txt`：8 项真实原生渲染（模拟值 0/9/10/20/21/86/100/旧值），位置 0、56×48 占位、雾蓝配色、阈值色、旧值 state=2 全过。探针需以 `/resource:<原生DLL>,MouseBattery.Native.dll` 编译，否则 `GetManifestResourceStream` 返回 null（本轮实测确认该失败形态，产品构建不受影响）。
- 降级链路（假指纹探针候选，全零 SHA256 常量）：真实读数 78% 正常、`status.txt` 出现 `Native=降级托盘（…CA1E335A…0741）`；UIA 实测托盘按钮出现在任务栏可见角且名称带真实电量；`NotifyIconSettings` 条目 `IsPromoted=1` 自动写入；注册表 Run 项全程未被动过；`--exit` 干净退出。降级模式无 `native-lifecycle.txt` 属预期（从未 attach）。
- 真机验收正式候选：默认位置显示真实 78%、第二实例 `--manage` 经单实例信号唤起设置窗口（UIA 控件树完整：设备/电量/位置/自启/按钮）、关闭窗口后台续跑、`--exit` DetachResult=0，随后已重新启动正式版并确认 `Startup=True`、真实电量恢复显示。
- 指纹常量已还原并以最终源重建，`9A6E35DE…` 为含全部改动的最终产物（早期候选 `CE88AB3A…` 不含自动提升，作废）。

已知边界：Win11 shell 对托盘按钮的 UIA 名称会把首次注册提示与当前提示拼接（形如“鼠标电量不可用 PRO X 2 DEX · 电量：78%…”），悬停 tooltip 为实时值，属 shell 命名习惯非功能缺陷；自动提升依赖 shell 先创建 NotifyIconSettings 条目（实测即时创建，仍保留 10 次重试）。`Startup` 状态语义不变：表示“自启项是否指向当前 EXE”，路径不同即 False。多显示器、真实重登录、物理拔插、Explorer 重启恢复仍未验收，底层未改动的旧证据保持原范围。

## 恢复与后续

v3.1 EXE 备份于本轮目录 `baseline\MouseBattery-v3.1.exe`。源码已于 2026-10-30 建立 Git 基线（v3.2.0 首次提交），此后每轮改动有 diff 可审、可回退。下一步：Windows 更新导致指纹失效时，工具会自动降级并显示真实哈希，届时按该哈希适配 `NativeHost.cs` 常量与 `NativeHost.cpp` 的 PE profile（TimeDateStamp/SizeOfImage/CheckSum/偏移）；降级不是长期形态。托盘 UIA 名称拼接如需修正需另查 shell 命名来源，非必需。
