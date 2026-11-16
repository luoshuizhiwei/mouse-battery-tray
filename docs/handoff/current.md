# 鼠标电量工具 — 当前交接

更新：2026-11-16。本轮按 unslop 走查修复用户截图指出的设置窗与悬停卡缺陷（Catppuccin 契约内的精修，非改品牌），已晋升正式版。历史轮次见各 dated 交接与 [track-flicker](2026-11-06-track-flicker.md)、[soft-colors](2026-11-06-soft-colors.md)、[settings-redesign](2026-11-06-settings-redesign.md)。

## 当前正式交付

- EXE：`E:\Agent任务\进行中\MouseBattery\app\MouseBattery.exe`。
- EXE SHA256：`D36F01B17F2C59309179A19CE9B685A12B8B7502A6C3B250C688FA933CB757D1`。
- 内嵌 DLL SHA256：`54E9CD7EF1CAA24DF803F5B1175A8905493DEA4F56654A2133A6661938257DBE`（运行时组件目录同名）。
- 回滚备份：`E:\Agent任务\进行中\MouseBattery\robust-v3.2\baseline\MouseBattery-B1705B08.exe`（旧 EXE 全哈希 `B1705B083D7AEA847BC8A07AEDFCCF3DBADA2A0CF1FF2B43DD939271BF611158`）；更早一轮备份 `MouseBattery-60E6D4FA.exe` 仍在。
- 18:15:13 启动 PID 49396；真实 Signature Plus M750 L 85%、Bolt 接收器、RawInput=on、RawInputError=（空）、Startup=True。Explorer 未重启。
- `Native=initialized | visible position=0 percent=85 … pillWidth=52 pillHeight=28 … tickHigh=255,255,255 tickCritical=255,255,255 … kind=office`：新 DLL 的轨内中性刻度（浅色=卡面白）已在正式版生效。
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
