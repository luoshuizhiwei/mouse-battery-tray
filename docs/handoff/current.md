# 鼠标电量工具 — 当前交接

更新：2026-10-30，Asia/Hong_Kong。当前正式交付 v3.4.0，独立应用，“雾蓝·电量仪表”主题 + 指纹失效降级托盘 + 设置窗口图形化。

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
