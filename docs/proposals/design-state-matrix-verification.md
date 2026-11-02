# 状态矩阵对照表（Design State Sheet）验证方案 —— 交实现轮

日期：2026-11-02。作者：设计师轮。决定：**方案 A（快照加 `themeOverride`）／T 表现在排／本表定为之后每轮 UI 改动的必跑基线**。
本文件只描述取证方法与判据，不改任何生产行为；实现轮按 §6 的能力清单施工。

---

## 0. 三条实测更正（覆盖上一轮的旧交接说法）

本轮读盘与跑环境探针，发现**磁盘状态已与我会话开始时的快照不同**（另一条会话线已把 v4.1.0 设计稿实现并提交，HEAD=`6a25d9c`）。以下更正覆盖此前所有相关表述：

| # | 我先前说的 | 实测结果 | 影响 |
|---|---|---|---|
| C1 | "512 字节里 offset 96 之后是未触碰的空位，加字段不动已有偏移" | **错**。`Snapshot` 恰好填满 512：`tooltip[232]` = 464B，48+464=512。未用的只是字符串末尾的**空字节**，它们被数组占有，**不能在中间塞 int32** | A 方案必须**同时缩短 `tooltip` 数组**才能腾出字段位，见 §5 |
| C2 | "Program.cs:275 的 `rawInputOk=false // BISECT` 仍留着，自动跟随是关的" | **已不成立**。[src/Program.cs:276](../../src/Program.cs#L276) 现为 `rawInputOk=RegisterRawInputDevices(...)`，实时 status.txt 为 `RawInput=on` | 撤回该条；README 的承诺与实现一致 |
| C3 | "设计稿里 F1/F4/F11 待修" | 磁盘 HEAD 已实现（`kindRow` grep 计数 0、大数字 40、轨道带 14） | 本表验的是**已实现的 v4.1.0**，不是 v4.0.0 |

**另有一条实时事实值得注意**：`Devices=Signature Plus M750 L 85% 未充电 **Bolt 接收器**`——M750L 现在挂在 Bolt 上而非蓝牙。所以"蓝牙"这条连接分支**当前硬件不会自然出现**，只能靠表驱动模拟（又一个必须做对照表的理由）。

---

## 1. 这张表要回答的四个问题（非目标也写清）

| 编号 | 问题 | 判法 |
|---|---|---|
| Q1 | 静息胶囊与原生时钟是否同族 | 裁切必须**同框含系统时钟**，一眼对照 |
| Q2 | 悬停玻璃底在真实底上是否可见、是否像贴片 | 同格 rest/hover 帧差 + 人判 |
| Q3 | 悬停卡 5 带能否 0.5 秒分层；小字号中文在 150% 下是否吃力 | 人判（记"人判"） |
| Q4 | 双产品线**去掉颜色与文字**后能否区分 | 两格转灰度并排，人判 |

Q4 的结论直接决定 **D5（身份形状化）要不要做**，Q2 决定 **D1（形状化胶囊）值不值得动铁律 1**。
非目标：性能、崩溃面、HID 链路正确性（`test.ps1` 48+12 已覆盖）。本表**不做**"好不好看"的判断。

---

## 2. 本轮抓到的头号问题：PRO X 分支从未被渲染过

[src/AppearanceProbe.cs](../../src/AppearanceProbe.cs) 的状态构造是：

```
BatteryReading.Parse(new byte[]{value,4,0,0}, "布局验证（模拟电量）", "mock", "2.4G")
publisher.Publish(state, 1, 0, false)
```

grep 全文：**探针从未设置 `Gaming`**，因此 `reserved` 恒为 0（[src/TaskbarPublisher.cs:36](../../src/TaskbarPublisher.cs#L36) 由 `shown.Gaming` 写入）。实时 health 串也印证：`kind=office`。

后果：交接里反复登记的"AppearanceProbe 8 项全过"，**从未驱动过 PRO X 那条渲染分支**——身份色（Mauve）、Edge-lit 描边档、`reserved==1` 的充电态文案分支（`未充电/正在充电/…`，办公线走的是 `AA 电池供电`）**一次都没有被渲染或被断言过**。这正是"用例看着过、其实没跑生产分支"的教科书案例。

另外两条覆盖漏洞同源：
- 探针写死的设备名是中文 `布局验证（模拟电量）`，真机名是拉丁 `Signature Plus M750 L`；**名字行的排版判断此前是在错的字符串上做的**（CJK 走 YaHei fallback，宽度与基线都和真名不同）。
- 探针只跑 `transport=2`（2.4G），`1=有线`、`3=蓝牙` 两条文案分支同样未被渲染。

**→ 本表的第一个产出物，就是把这三条补齐。**

---

## 3. 矩阵定义

### 3.1 三张表（宿主不同，取证手段不同，不得混在一张图里）

| 表 | 内容 | 渲染宿主 | 驱动 | 截图方式 |
|---|---|---|---|---|
| **T** | 任务栏胶囊（静息）+ 胶囊（悬停玻璃）+ 悬停卡 | **Explorer 内真实 XAML** | SheetHost 写共享内存 + 真实指针悬停 | `CopyFromScreen` 区域裁切 |
| **S** | 设置窗组件（卡 A / 开关 / 按钮 / picker / 横幅 / 标题标记） | 托管 WinForms，可多实例离屏 | 直接实例化 + 伪状态 | PrintWindow |
| **S-full** | 设置窗整窗版式 | 真窗体 | `--manage` | PrintWindow |
| **R** | 降级托盘图标 | `TrayIcon.Render`（纯函数，最高保真） | 直接调用 | 位图平铺 |

### 3.2 T 表格子集合（主表 24 + 叠加 7 = 31 帧，每帧 rest/hover 两次 → 62 张）

维度：`percent × productLine × theme`

- `percent ∈ {100, 85, 21, 20, 10, 0}` —— 阈值两侧成对取（21/20、11/10 中的 11 见叠加格），**不取中间值凑数**。
- `productLine ∈ {PRO X(reserved=1), M750L(reserved=0)}`
- `theme ∈ {Latte(浅色，当前系统), Mocha(深色，需 §5 覆盖通道)}`
- 设备名用**真实名**：`PRO X 2 DEX` / `Signature Plus M750 L`（与 [src/Tests.cs:38](../../src/Tests.cs#L38) 的用例名一致）。

叠加格（各 1 帧）：

| 格 | 命令值 | 要看的点 |
|---|---|---|
| T-A1 | `charging=1`（正在充电）+ PRO X | 图标托 + 闪电 + 状态文本 |
| T-A2 | `charging=3`（已充满） | 应无闪电（语义：终点） |
| T-A3 | `charging=4`（充电异常） | 应转 Red + 告警档描边（此前从未渲染） |
| T-A4 | `state=2`（旧读数） | 状态行转黄 + 玻璃 α×0.72 衰减 |
| T-A5 | 单设备（无第 2 行） | ⑤带 Collapsed 后卡高是否自然收成 183，不留空洞 |
| T-A6 | 伴随行含 `旧读数` | 尾串转黄的判读强度 |
| T-A7 | `percent=11` + `transport=3(蓝牙)` | 阈值上界外一格 + 蓝牙文案分支（现硬件不会自然出现） |

### 3.3 S / R 表

- S：开关 2 态 × 5 状态（rest/hover/pressed/disabled/focus）× 2 主题 = 20；按钮 2 类 × 4 态 × 2 主题 = 16；卡 A 6 值 + 离线 + 双 chip = 8；picker 4 落点 × {ghost, hover, selected} 取 8 + focus 1；横幅 ×2 主题；标题标记 4 电量。合计 ≈ 62 格 → 深/浅各一张长图。
- S-full：2 主题 × {正常, 降级} = 4 张整窗。
- R：`{100,85,21,20,11,10,0,-1}` × 2 主题 × `{16,24,32}px` = 48 格 → 每主题一张拼图（1:1，不缩放）。

---

## 4. 实测环境（本表必须在真实环境上跑，不接受 96-DPI 近似）

来自 [E:\MouseBattery\design-sheet\env-probe.ps1](file:///E:/MouseBattery/design-sheet/env-probe.ps1) 的读数：

| 项 | 值 | 对取证的约束 |
|---|---|---|
| `AppsUseLightTheme` | **1（浅色）** | 免费拿到的主题是 **Latte**；Mocha 那 12 格必须靠 §5 的覆盖通道，或临时切系统主题（不推荐） |
| SystemDpi | **144 → 150%** | 1 DIP = 1.5 px。**1 DIP 的发丝线在 150% 下是被 AA 淡化的 1.5px** —— "描边看不看得见"必须在真机判，纸面 α 表不算数 |
| 屏幕 | 2560×1600，单显示器 | 裁切窗口全部安全 |
| `Shell_TrayWnd` | 0,1528 → 2560,1600（高 72px=48DIP） | rest 帧裁切：x 从 health 的 pill x 左移 180px 到屏右，y 1520..1600（**含时钟**） |
| pill 实测 | health `x=1336.0 y=0 width=56 height=48`（DIP）→ 物理 x≈2004，宽 84px | 悬停点 = pill 中心；卡向上展开，hover 帧裁切 y 需上探 ~340px |
| **TranslucentTB** | **在运行** | 任务栏半透明 → 玻璃底背后就是壁纸 |
| **壁纸** | `WallpaperEngineOverride_randomNLVIVB.jpg` | **Wallpaper Engine = 动态壁纸，底亮度随时间变化** → 同格的 rest 与 hover 必须**背靠背连拍**，且记录帧时间戳；跨格比较亮度无意义 |
| 正式版 | PID 34584 在跑，`TaskbarPosition=0` | 它每 500ms 重发快照，会覆盖 harness 的写入 → 跑表前必须 `--exit`（可由我随后 `--background` 拉起，可逆） |

> 动态壁纸这条是本轮最有价值的环境发现：它意味着"玻璃底在当前系统上到底可不可见"不是一个稳定答案，而是随壁纸帧变化。**这直接给 D5（按背景亮度键控材质）提供了现实依据**，也让 Q2 必须用背靠背帧差来判，而不是用绝对色值。

---

## 5. A 方案的准确字段规格（实现轮照此施工，勿自行发挥）

`tooltip` 末尾的空字节被数组占有，因此需要缩短数组。取 **216**（432B），尾部让出 32B = 8 个 int32。

```
magic 0 · sequence 4 · tick 8 · state 16 · percent 20 · charging 24 · transport 28
pid 32 · position 36 · startup 40 · reserved 44 · tooltip[216] @48..479   ← 长度变，起点不变
themeOverride @480 · lastActiveTick @484 · lastSuccessTickAny @488 · pad[5] @492..511
static_assert(sizeof(Snapshot)==512)   ← 保持
```

规则（逐条都是可验收项）：

1. **所有既有字段偏移不变**；只有 `tooltip` 数组长度和尾部新增变化。
2. `themeOverride`：`0=跟随 ActualTheme`（生产恒定写 0）、`1=强制 Latte`、`2=强制 Mocha`。原生取 `light = override==1 ? true : override==2 ? false : (ActualTheme==Light)`。
3. **0 必须是"未提供"而非合法值**：所有新增字段一律用 0 作哨兵；时间戳类字段 0 = 无数据，界面不得渲染成"0 秒前"。
4. 混合版本行为（必须实测一次，别只推理）：
   - 旧 EXE + 新 DLL：新字段读到 0 → override=0 跟随、时间戳=无数据 ✓ 安全。
   - 新 EXE + 旧 DLL：旧结构把 480..511 当 `tooltip` 尾部读；写入侧 `byte[] payload=new byte[512]` 是零初始化，且旧读法会强制 `tooltip[231]=0` ✓ 安全。
   - 但 `TaskbarPublisher` 的截断上限 `Capacity-50` 必须同步改为 `Capacity-48-2`，否则长名会写进新字段区。
5. `tooltip[216]` 的截断影响：HID++ 名称允许最长 100 字符（[src/Battery.cs:98](../../src/Battery.cs#L98) 的守卫），两行最坏 ≈ 210 → 极端设备名会被截掉几字。真实两台设备名分别为 11 / 21 字符，余量 >4×。**记录为已知边界，不做额外处理。**
6. `lastActiveTick`（显示中那只的最后成功时刻）是 D3"新鲜度"的地基；本表先把它**渲染到 tile 标注里**（不参与界面），验证数据链路，再决定是否上屏。
7. 这次改动落在 `TaskbarPublisher.cs` + `TaskbarView.h`（原生重建）——**正是另一条会话线正在提交的区域**。要求：在**独立 worktree** 做，产出后先跑 `test.ps1` + `build-standalone-native.ps1`（失败即中止整链），并核对新 DLL 哈希属于本次产物。

**覆盖不到的部分要说清**：`themeOverride` 只改**我们自己的绘制色**，改不了任务栏真实底色（那是系统主题 + 壁纸）。所以强制出来的 Mocha 帧是"Mocha 绘制 × 真实浅色半透明底"的组合。判读 Q2 时：**Latte 那 12 格是真实组合，可直接下结论；Mocha 那 12 格只用于版式与色阶判读，不得据它断言"深色主题在实际桌面上可见"。** 要真深色环境只有两条路：临时改系统主题（侵入，会广播给全部应用，且本工具会真重建窗口），或在另一台/另一会话上跑。本表默认不做。

---

## 6. 需要新增的能力（全部是加法，不改生产行为）

| # | 能力 | 落点 | 说明 |
|---|---|---|---|
| N1 | `SheetHost.exe` | **新文件** `tools/design-sheet/SheetHost.cs` + 新 `build-sheet.ps1`（照 `build-probe.ps1` 的编译方式，内嵌原生 DLL 资源） | `build.ps1` 的 sources 是显式列表，新文件不会被带进产品构建 ✓ |
| N2 | 表驱动 | 由 `cells.csv` 每行读入 `percent,charging,state,reserved,transport,gaming,name,companion,themeOverride,keepMs` | 探针侧直接构造 `BatteryReading` 并**设 `Gaming` 公有字段**（§2 的洞就在这） |
| N3 | 逐格握手 | SheetHost 写 `cell.txt`（格号+命令值）→ 等 PS 的 `ack.txt` → 下一格 | 不要靠固定 sleep 对齐截图，抖动一定会踩 |
| N4 | health 补 `screenPillRect=` | 原生打印物理 px 矩形（`GetWindowRect(Shell_TrayWnd)` + `TransformToVisual` + DPI 换算） | 纯日志字段，零 ABI 影响；PS 据此算裁切框 |
| N5 | 悬停驱动 + 出现判定 | PS：`SetCursorPos` 到 pill 中心 → 轮询到卡区域与 rest 基线**像素有差异**才拍；两轮无差异 → 该格标 `CAPTURE_FAILED` 而非静默出一张错图 | UWP ToolTip 出现延时不稳定，这是本表最容易造假的地方 |
| N6 | 拼图与标注 | PS：1:1 平铺，**禁止缩放**；每格下方烧入 `格号/命令值/实测 health 摘要/EXE+DLL 前 8 位/HEAD sha/时间戳` | 标注即证据；否则三个月后不知道是哪一版 |
| N7 | 客观判据脚本 | PS：按每格声明的采样区取**中位数**（壁纸有渐变，不能采单点），算 WCAG 对比度 + rest/hover 帧的 ΔL | 见 §7 |
| N8 | 伪状态（S 表） | 通过给控件**已有的内部字段**赋值 + `Invalidate()` 呈现 hover/pressed/focus | **禁止新增渲染分支**，否则验的不是生产在画的那张图 |

---

## 7. 判据（能机器化的绝不交给人眼）

| 判据 | 算法 | 通过线 |
|---|---|---|
| 文字对比度 | 字形墨 vs 底（中位数采样） | 小字 ≥4.5:1；`40` 大数字（≥18 DIP）≥3:1 |
| 悬停确有反馈 | 同格 rest/hover 帧的平均亮度差 | **ΔL ≥ 8%**，否则玻璃等于没画（Q2 硬判据） |
| 数字不跳位 | 连发 85→100→85，对齐后叠帧取墨包围盒左缘 | 位移 = 0 DIP |
| 产品线无需颜色文字可辨 | 两格转灰度并排 | 人判，记"能/不能"；不能 → D5 立即立项 |
| 阈值 0.3 秒可辨 | 只看数字+槽，回答"现在该不该充电" | 人判，不许先想色值 |
| 版式对齐 | 同轨元素墨顶 y 差（S 表） | ≤1 DIP（自动雷达，专抓 F6 那类） |
| 中文可读性 | 11/10.5 DIP 中文行在 150% 下的实拍判读 | 人判；眯眼即不过 |

人判结论一律标 `人判`，与机器判分开记入 manifest，**不得合并成"已验证"**。

---

## 8. 执行手册

**阶段 0 — 准备（无需授权，不碰输入设备）**
1. 建目录 `E:\MouseBattery\design-sheet\<HEAD-sha7>\<EXE哈希前8>\`。
2. 记基线：`git rev-parse HEAD`、正式 EXE/DLL SHA256、`TaskbarPosition`（现=0）、Wallpaper Engine 是否暂停。
3. 编译 N1–N8（先跑 `-DryRun`：不写映射、不动鼠标，只验证拼图与采样管线）。

**阶段 1 — 当前主题（Latte）全表：24 主格里的 12 格 + 全部叠加格**
4. **需要授权**：`MouseBattery.exe --exit`（任务栏电量消失，直至步骤 8）。
5. 启 SheetHost → 逐格：写快照 → rest 帧（含时钟同框）→ 悬停 → 卡帧 → ack → 下一格。
6. 结束后**立刻**拉起正式版并核对：`Native=initialized`、真实读数回来、`TaskbarPosition=0` 未变。
7. 同阶段并行出 S / S-full / R（这几张不碰 Explorer、不需要悬停，可全程静默跑）。

**阶段 2 — Mocha 12 格**：待 §5 的 `themeOverride` 落地后跑，同样两拍（rest/hover）。

**中止协议**：任一格失败即停止并执行步骤 6 恢复；不带病继续。中止用信号（删 `cell.txt` / 停 SheetHost），**不要靠连按 Esc**；原生组件监视宿主进程，宿主被结束会自行移除界面并卸载，不会留幽灵胶囊。

---

## 9. 基线留档制度（本轮已拍板）

- 目录即版本：`design-sheet/<sha7>/<exeHash8>/`，内含 `sheet-T.png`、`sheet-S-dark.png`、`sheet-S-light.png`、`sheet-R.png`、`manifest.tsv`（每格命令值 + health 摘要 + 机器判结果 + 人判结论）、`findings.md`。
- **规则 1**：表必须由**已提交的 HEAD 构建**生成，不接受未提交工作区的图（否则无法归因）。
- **规则 2**：之后每轮 UI 改动，交付前必须重跑同一格序的表并与上一版**逐格 diff**；有意的变化要在 manifest 写明"改了哪格、为什么"。
- **规则 3**：格序与格号冻结（`T-01..T-31`、`S-01..S-62`、`R-01..R-48`），新增状态往后加号，**不重排**——否则历史图全部失效。
- **规则 4**：`manifest.tsv` 里机器判/人判分列，禁止出现无来源的"已验证"。

---

## 10. 风险清单

| ID | 风险 | 处置 |
|---|---|---|
| S1 | **动态壁纸（Wallpaper Engine）使底亮度非平稳**，跨格比较无意义 | 同格 rest/hover 背靠背连拍；跑表期间请求暂停 Wallpaper Engine（需你同意，属改动桌面环境） |
| S2 | ToolTip 出现延时抖动 → 拍到没有卡的"卡帧" | N5 的像素差判定 + `CAPTURE_FAILED` 标记，不静默出图 |
| S3 | 单实例互斥：正式版在跑时 harness 写入被 500ms 覆盖 | 必须先 `--exit`；跑完立刻恢复并核对哈希 |
| S4 | **夺鼠标**：悬停驱动会移动你的真实指针 | 只在阶段 1 的窗口期发生，跑前单独取得你一句确认；期间请勿动鼠标，要中止按协议而非 Esc |
| S5 | §5 动到原生 → 踩 v3.8.3 那类"旧 DLL 继续走完"的坑 | 独立 worktree；构建失败中止整链；核对 DLL 哈希是本次产物 |
| S6 | 与另一条会话线同时改 `TaskbarPublisher.cs`/`TaskbarView.h` | 分工隔离（worktree），或等它的 v4.1.0 收尾提交后再动 |
| S7 | N8 伪状态与真实输入路径分歧 | 只允许设已有内部字段；不接受为截图新开的渲染分支 |
| S8 | 150% 下发丝线/1.5px 刻痕被 AA 淡化到看不清，纸面 α 表与实图不符 | 本表的核心价值之一：判完可能要回改设计稿的 α 与线宽（记为"表可能推翻文档"的合法产出） |
| S9 | 拼图若被任何环节缩放，1px/字号判断全部失真 | 硬规定禁止缩放；拼图后校验 tile 像素尺寸与裁切框一致 |
| S10 | `tooltip[216]` 截断极端设备名 | 真实名余量 >4×，记已知边界 |

---

## 11. 交付物与结论格式

一张总图（T 表拼版）+ 两张 S 长图 + 一张 R 拼图 + `manifest.tsv` + `findings.md`。`findings.md` 必须逐条回答 Q1–Q4，并给出**下一轮要不要动铁律 1/3/5 的明确建议**（含"不动"的选项），不许写"效果良好"这类无法反驳的话。
