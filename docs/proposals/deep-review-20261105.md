# 深度审查：2026-11-05

## 范围、基线与证据边界

用户要求继续深查、多轮寻找优化点。本轮仅审查、独立探针与文档整理，没有修复产品源码、部署候选、改变偏好、主动重启 Explorer 或退出后台工具。

- 固定点：HEAD `aa7d5e873898254ae38b988edc776242d441d5c6`。检查包含既有未提交修复的当前工作树，也追查其调用的既有实现；以下发现不是全部由该 diff 引入。
- 正式 EXE SHA256：`70AE1CAD10B301AEA7C524755A98D175B058C6DDCCE835768ACE6E29EFFFC7D0`；有效原生组件：`05B2DFD7AA161B5E61DC03DE22F6F929E4C7635D8A1A5AF722D0CF689FD272B1`。
- 需求依据：本会话的任务栏常驻、悬停详情、真实电量、自动跟随与唤醒恢复要求，以及当前交接。完整双模式仍是明确未交付功能，不当作新回归。
- 规范依据：本会话 AGENTS 的最小修改、禁止吞错、证据优先与部署纪律。没有额外发现适用的仓库编码规范。
- 原始证据统一在 `E:\MouseBattery\deep-review-20261105`。故障探针使用实际 `Battery.cs` / `TaskbarPublisher.cs`，替换 HID 边界或使用独立共享内存对象；没有写入正式快照。

## 四轮检查

1. 数据与恢复：检查 HID 发现、重试、缓存、Raw Input 身份关联；对部分发现失败、Bolt 休眠扫描做故障注入，并以新建健康数据源作正对照。
2. 生命周期与成本：追踪 UI 线程、注入引用、取消 I/O、健康重试；采样后台资源，测量 Raw Input 路径匹配分配，检查默认构建产物。
3. 界面：低成本 UI 检查者只读打开正式设置窗口、检查截图和可访问性树、关闭窗口并确认后台存活。记录见证据目录 `ui-review.md`。
4. 反证与边界：用本机真实 vendor/raw 路径核对身份映射；排除“短时波动等于泄漏”“接收器在线等于自动跟随正常”等推断；独立测试多设备时间戳及托盘提示文本。

P1 表示优先修复的确定故障；P2 表示有明确触发条件的功能或诊断缺口。此等级不表示已经发生新的崩溃。

## Standards

### ST-1 / P1：默认构建会重新嵌入旧原生 DLL

位置：`build.ps1:1`；对照 `build-standalone-native.ps1:3`。

默认 NativeDll 指向旧的 `E:\MouseBattery\appearance-v3\MB-UI-02-v1\MouseBattery.Native.dll`，与当前原生构建输出不一致。只运行正常默认构建，便会把已验证修复与旧原生资产混装；构建成功不能保证候选包含本次修复。

本轮安全复现：仅运行 `build.ps1 -OutputDirectory E:\MouseBattery\deep-review-20261105\default-build`，抽取资源校验，未执行生成的 EXE。嵌入 DLL 实际 SHA256 为 `4F157C4598657842A8FD98D3E3D99E269760385AE716FE73C637723C0A3D5A08`，不等于当前验证的 `05B2…`。见 `default-build.txt`、`default-build-native.json`。

最小改法：统一原生构建与托管打包的产物路径，或要求显式传入 NativeDll；发布记录必须包含被嵌入 DLL 的哈希。这里没有执行旧 DLL，不能据此声称它又引发了某个具体异常。

### ST-2 / P2：Raw Input 全量吞错，失效时没有可诊断证据

位置：`src/Program.cs:360`，`HandleRawInput` 的 `catch { }`。

路径缓存、Marshal 与逻辑绑定的任意托管异常均被完全吞掉。托盘仍显示电量时，自动跟随异常可能长期没有错误记录；这直接违反本轮 AGENTS 的禁止吞错约定。当前真实路径不匹配主要是正常返回 null，不应把它错误归因为该 catch。

最小改法：明确预期边界错误，对 Win32 调用失败保留错误码和限频诊断；非预期托管异常必须可见。不能在每个鼠标报告上写日志，也不应为“快速失败”把设备拔插变成后台崩溃。

Standards：2 项；轴内最严重为 ST-1，默认构建无法可靠复现当前交付。

## Spec

### SP-1 / P1：发现过程中一次超时会留下永久半初始化状态

位置：`src/Battery.cs:106`、`:111`、`:147`。

`e.Unit` 先赋值，之后才查询 0x1004 电量特性和 capabilities，最后赋值 `e.Feature`。后半段失败后，下一轮用 `e.Unit == null` 判断是否需要发现，结果跳过发现并不断访问默认 feature 0。退避只改变时间，不能纠正该状态。设备唤醒、无线重连的短暂超时会放大触发机会，但本轮没有物理睡眠复现。

故障注入结果：`first=0 second=0 third=0 batteryDiscoveryAttempts=1 invalidFeatureQueries=2`；相同健康设备的新数据源正对照得到 1 条读数。见 `DeviceFaultProbe.cs`、`device-fault-results.txt`。

最小改法：所有发现步骤成功后一次提交元数据，或用真正完成发现的状态判断初始化；不要把 Unit 已读取等同于 Feature 已就绪。回归需覆盖在 battery feature/capabilities 阶段失败后自动恢复。

### SP-2 / P1：Bolt 首次扫描空结果被永久缓存

位置：`src/Battery.cs:58`、`:72`、`:139`。

睡眠或短暂超时使首次 1/2/3 地址扫描无读数时，空数组也写入 boltSweep；以后相同接收器路径直接返回空结果。没有 Entry 可参与已有重试，也没有清理该负缓存。鼠标醒来后甚至相同路径接收器重连后，都可能一直被忽略，直到重新创建数据源。

探针：`asleepReadings=0 awakeReadings=0 discoveryAttemptsBefore=3 after=3`；新建数据源、同一已唤醒设备正对照为 1 条。见 `device-fault-results.txt`。

最小改法：负结果有期限/退避，相同接收器重新出现时失效；避免每一帧重新扫全地址。不要泛化为当前 GPW 接收器必然受 Bolt 专用分支影响。

### SP-3 / P1：真实接口路径匹配失败，RawInput=on 不代表自动跟随生效

位置：`src/Battery.cs:183`、`:198-214`；调用 `src/Program.cs:358`。

vendor HID 接口和鼠标 Raw Input 接口的 collection/instance 不同。当前代码把 `mi_02&colNN` 当作设备区分标识，还对非 Bolt 使用该规则；Bolt fallback 的 `EndsWith(":c548")` 与实际 `c548:…` token 也不匹配。

本机只读枚举得到：

- C54D：vendor `mi_02&col02`，raw `mi_00`，匹配 NULL。
- C548：vendor `mi_02&col02`，raw `mi_01&col01`，匹配 NULL。

完整真实路径与匹配结果见 `path-match-results.txt`、`PathMatchProbe.cs`。正式状态也为 `RawInput=on`、`ActiveKey=(none)`。探针确认这些路径在实际匹配函数返回 null；没有通过实体双鼠标切换验收。单鼠标仍可通过 freshest fallback 正常显示，不能用此证明跟随功能正常。

最小改法：先分离物理接口与逻辑鼠标身份；唯一候选可做有边界的匹配，多同 PID/多配对设备必须查 parent/container 等关联，不能随意挑第一只。新增测试必须使用真实路径形态，覆盖同 PID 歧义。

### SP-4 / P2：当前显示读数的有效期被另一只鼠标的成功刷新

位置：`src/Program.cs:388`、`src/TaskbarPublisher.cs:17,24`、`native/TaskbarView.h:507`。

共享快照 tick 来自“任意设备最后成功时间”，原生却用它判断当前显示电量是否超过 15 秒。平时托管定时发布会很快更正状态；当发布线程被阻塞/暂停时，两端对同一电量的新鲜度判定分歧。

独立协议探针：A 的 20% 已有 14900ms，B 此时成功；发布仍选活动 A，快照 timestamp age 却为 0。模拟发布暂停 200ms 后，托管模型已经不再显示 A，原生年龄规则却仅看到 200ms。没有实际阻塞正式 UI，也没有把这称为原生渲染实机验收。见 `FreshnessProbe.cs`、`freshness-results.txt`。

最小改法：tick 表达被显示读数的真实成功时间，统一 monotonic 时间语义；可以保持现有 ABI，不需要扩大快照。回归覆盖 A 老/B 新以及发布暂停。

### SP-5 / P2：降级托盘悬停缺少当前鼠标的详情

位置：`src/Battery.cs:243-252`、`src/Program.cs:451-454`。

Tooltip 第 0 行仅含当前设备名，其他行才含另一设备的百分比和连接。原生卡另读字段，所以正常任务栏详情不因此缺失；降级托盘直接复用该字符串，当前鼠标百分比、充电和连接没有进入提示文本。单设备时仅有名称。

探针实际文本：`Mouse A | Mouse B · 90% · 蓝牙`，A 的 20% 未出现。见 `freshness-results.txt`。这是降级路径文本缺口，本轮没有切换正式版到托盘做悬停截图。

最小改法：托盘单独格式化 displayed 的完整详情，保留原生 tooltip 首行协议；不要直接修改首行导致原生名称解析回归。遵守 NotifyIcon.Text 长度限制。

Spec：5 项；轴内最高为 P1 的 SP-1/SP-2/SP-3，分别影响恢复或实际跟随。

## 已量化但不宜先做大改的优化

| 项目 | 实际证据 | 建议和边界 |
|---|---|---|
| 日志每秒落盘 | 12.67 秒观察到 status.txt 13 个修改时间；Describe 包含秒级年龄，导致每次文本不同 | 用有意义状态变化加低频心跳；P3，无证据支持“磁盘损坏”类推断 |
| 高频路径分配 | 真实匹配函数独立执行 20000 次，23.8059ms、28640040 分配字节，即约1432B/次，4次Gen0 | 修正映射后缓存 hDevice→逻辑设备并处理失效；没有实测实体上报率，不能据此声称鼠标造成高CPU |
| 后台资源 | 12.67 秒 CPU 增量78.12ms、PrivateBytes约63MiB、句柄475–480 | 短样本无持续泄漏证据；不能替代隔夜观测 |
| UI线程阻塞 | Maintain 在 UI tick 上调用；远程加载/启动各可等10秒；工作派发用无超时 SendMessageW | 改异步调度前先明确 payload/取消所有权；不能直接加超时，因为当前 Work 在栈上，回调晚到会产生悬空引用 |
| 健康恢复 | ReadHealth 对过期心跳仅改 Status；Maintain 对同owner、state=2的过期心跳未建立恢复/降级动作 | 定义健康失效策略后做冻结/恢复测试；本轮没有制造正式 Explorer 卡死 |
| 串行轮询 | 一个设备的超时会延后整批 Poll 交付；取消后等待完成还可能超过名义超时 | 后续考虑按物理接收器隔离调度，不并发争用同一HID通道；必须等完成后释放OVERLAPPED和缓冲 |
| 原生模块驻留 | 远程 LoadLibrary 的初始引用刻意保留；重attach可能继续累积引用 | 记录为所有权债务。先设计跨线程/远程加载的完整释放协议；禁止直接恢复 FreeLibrary(g_instance) 重引竞态 |
| 适配元数据 | taskbar指纹、RVA、IPC版本和打包路径分散 | 先堵默认构建旧资产，再考虑少量统一元数据；不需要新插件框架 |

关于取消 I/O：CancelIoEx 不等待完成，完成前不能释放/重用对应 OVERLAPPED；现有等待不能简单删除以“提速”。依据 [Microsoft CancelIoEx 文档](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-cancelioex)。模块引用释放也必须区分每个引用的所有者，见 [FreeLibraryAndExitThread 文档](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-freelibraryandexitthread)。这些风险不等于本轮发现新的堆损坏根因。

## UI 与验证边界

设置窗口截图为563×550，底部按钮完整，设备/百分比/连接字段一致。位置提示较小且接近示意图，但没有充分证据判定覆盖或裁切。位置选项的独立可访问性节点缺失值得后续键盘实测，当前没有测试选择切换，不能直接判定键盘操作损坏。未取得本轮任务栏和悬停卡截图；不据设置窗口推断它们全部美观合格。

本轮不建议立即重做主题。可先在现有外观上验证文字可读性、焦点、系统缩放与悬停详情；原生视觉变动仍需真机布局检查。

- 正式程序 PID32592、Explorer PID12264 在本轮检查与结束核对均存活；设置窗口已关闭，后台保持运行。
- 最后核对 23:40:26，正式 EXE 与 Program.cs 哈希未变。状态为 PRO X 2 DEX 80%、2.4G，Native initialized/visible，实际布局56×48 DIP；这次状态来源不是模拟探针。
- 18:38:27 至本轮检查时间，Application/1000/1001/1026 中匹配 MouseBattery.exe/explorer.exe 的记录为0。更早的失败证据保留；PID较早交付变化的原因未查明，不据此宣称从未重启。
- 既有48逻辑+15 IPC与堆修复 canary 输入未变，复用之前记录；本轮新增探针揭示它们未覆盖的路径，不将旧测试通过扩大为新问题已修复。
- 未进行物理睡眠/唤醒、长时稳定性、双鼠标实际切换、降级托盘悬停和健康卡死恢复验收。

## 后续顺序与退出条件

1. 构建资产必须确定，显式识别新DLL；防止任何后续修复打包回旧组件。
2. 修复发现提交时机与Bolt负缓存，每项保留失败→恢复正对照；C#5、ABI和视觉保持不变。
3. 修正真实Raw Input身份关联，以同PID歧义及实体双设备切换验收。
4. 统一显示读数时间戳，补齐托盘详情；再处理日志/分配和异步维护。
5. 只有涉及的逻辑/IPC回归通过、候选哈希明确，才能按优雅退出→文件锁等待→复制→启动部署，并进行真实唤醒观察。

这些是建议，尚未实施。当前堆损坏修复、准确 taskbar profile 和共享内存名称必须保留。报告没有授权提交、push、清理证据或恢复整个历史双模式框架。
