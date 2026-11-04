# 方案评审意见：《双显示模式（任务栏内嵌 / 托盘图标）用户可选 + 新 taskbar.dll 适配》

> **评审角色声明**：外部评审意见文档，供方案制定者与实现者参考。不涉及源码与构建操作。
> **评审对象**：`docs/proposals/dual-mode-display.md`
> **评审日期**：2026-11-04

---

## 一、总体判断：方案是否成立与方向性评估

- **总体结论**：**方案总体成立，双模式解耦与托盘原生化设计方向正确，但方案 B 的“跨进程热切换”与“长期 Profile 维护”存在高危工程隐患。**
- **方向性分析与替代建议**：
  1. **方案 A（双模式与托盘原生化）完全成立**：
     将 `degraded` 拆解为 `preferredMode` / `taskbarUnavailable` / `ActiveMode` 的三量模型，彻底解耦“用户主观意图”与“客观环境兼容性”，方向非常明确且合理。
  2. **方案 B 热切换（Taskbar ↔ Tray 无需重启）存在高危隐患**：
     方案设计在运行时直接 `native.Dispose()`（跨进程 Detach）并在切回时重新构造。但源码中 `NativeHost` 是通过 `CreateRemoteThread` 向 `explorer.exe` 注入并由后台 worker 线程维持（`NativeHost.cpp:212-221`）。在 XAML 渲染树生命周期内动态摘除/插入 `ColumnDefinition` 并重入 `LoadLibraryW`，极易诱发 Explorer 内存死锁、堆损坏或桌面闪烁。
     - **替代方向**：**取消跨进程热切换，改为“配置即时保存 + 进程平滑重启”**。用户在设置窗口中切换单选框后，更新注册表，通过现有的互斥体与退出/拉起机制平滑自我重启（Restart），彻底规避跨进程热卸载带来的状态竞态。
  3. **Profile 表维护在频繁更新分支上的局限性**：
     用户所处系统分支（26300）更新频繁（10月至今已有5个KB）。方案依赖人工逆向提取 4 个 RVA，即使设有 G1 闸门，每次系统推送补丁仍会导致内嵌功能中断。方案必须将**托盘模式作为核心一等公民**，将任务栏内嵌明确定义为“在受支持版本上的增强特性”。

---

## 二、方案末尾 7 个问题的逐条回答

### Q1: `degraded` 单向 bool 拆成三量的重构，是否有遗漏的检查点或生命周期陷阱？
- **明确结论**：三量概念成立，但**存在 2 处生命周期陷阱和 1 处检查点遗漏**。
- **依据与代码定位**：
  1. **构造期与运行期 `EnterDegraded` 的语义不对称**：
     - 构造期（`src/Program.cs:300-301`）：若改为惰性创建，在 `preferredMode == Tray` 时根本不会调用 `new NativeHost()`，此时 `taskbarUnavailable` 状态未知。若用户后续切到 Taskbar，才首次触发创建并抛出 `NotSupportedException`。
     - 运行期（`src/Program.cs:411`）：`native.Maintain()` 抛出 `NotSupportedException` 时，不仅需要设置 `taskbarUnavailable = true`，还必须将 `ActiveMode` 强制切到 Tray，且**绝对不能覆盖用户已保存的 `preferredMode`**。
  2. **`native.Dispose()` 的主线程阻塞陷阱**：
     `src/NativeHost.cs:106-120` 结合 `native/NativeHost.cpp:219`：`NativeDetach()` 跨进程等待 worker 退出时包含 `while(... GetTickCount64()-start<6000) Sleep(10);`。如果在设置窗口的单选事件（UI 线程）直接调用 `native.Dispose()`，一旦 Explorer 响应缓慢，UI 线程将**硬卡顿高达 6 秒**。
  3. **主题切换检查点遗漏**：
     `src/Program.cs:327`：`OnPreferenceChanged` 中目前仅在 `trayIcon != null` 时刷新托盘。双模式下若 Taskbar 模式没有实例化 `trayIcon`，或 Tray 模式未监听，需要确保主题切换对两种模式的分支均有效。

---

### Q2: 单一 NotifyIcon 实例 + 只改 Visible 的方案，是否真能保证"设置页最多 1 条"？有没有其它触发路径会新增条目？
- **明确结论**：**真能保证**。更换图标句柄、修改 Tooltip 文本、甚至进程重启均**不会**在设置页增加重复条目。
- **依据与代码定位**：
  1. **Windows 11 托盘记账索引机制**：
     Windows 11 `HKCU\Control Panel\NotifyIconSettings` 下的子项唯一标识，是 Shell 在首次接收到 `Shell_NotifyIcon(NIM_ADD)` 时，依据进程的**完整可执行文件绝对路径**（`src/Program.cs:475` 校验的 `ExecutablePath`）生成的哈希。
  2. **修改内容不产生新项**：
     修改 `trayIcon.Icon`（`src/Program.cs:459`）或 `trayIcon.Text`（`src/Program.cs:453`）在底层发送的是 `NIM_MODIFY` 消息，Shell 仅在内存及对应注册表键下更新数据，绝不会分配新的 GUID。
  3. **历史 4 个条目的真正根因**：
     此前出现多个条目，是因为在构建、排错过程中分别从不同路径（例如 `...\standalone\candidate\...`、`...\baseline\...`、`...\app\...`）运行过 EXE。只要固定在唯一的发布路径，且进程内保持单实例只切换 `Visible`（底层对应 `NIM_ADD`/`NIM_DELETE`），在 Windows 设置页永远**有且仅有 1 条**记录。

---

### Q3: `TryPromoteTrayIcon` 改为"仅当 IsPromoted 缺失时写 1"是否足够原生？用户主动选 Tray 时会不会导致"选了却看不见"？
- **明确结论**：**方案中的改法存在逻辑失效风险，且直接不提升确实会导致“选了却看不见”的严重体验问题。**
- **依据与代码定位**：
  1. **注册表语义陷阱**：
     Windows 11 在首次为新应用生成 `NotifyIconSettings` 子键时，通常会直接写入 `IsPromoted = 0`，而不是“缺失”。如果代码写成 `if (sub.GetValue("IsPromoted") == null)`，该条件大概率永远无法命中，导致提升逻辑彻底变成空操作。
  2. **交互割裂**：
     Windows 11 默认将所有新托盘图标折叠入 `^` 溢出面板。用户在设置里主动把显示方式切为“托盘图标”，心理预期是任务栏右下角出现电量；如果被系统默认折叠且无任何提示，用户会误以为程序崩溃或切换失败。
  3. **修正方案**：
     不要基于系统注册表键是否缺失来判定，而应在软件自身的配置键 `HKCU\Software\MouseBatteryTray` 中记录 `TrayPromotedOnce = 1`。仅在用户**首次**切换到托盘模式时执行一次提升写入；之后无论是 0 还是 1，本工具均不再覆写，完全交由用户在 Windows 设置中自由定制。

---

### Q4: 方案 B 的 RVA 逆向顺序与 G1 闸门是否合理？有没有更快更可靠的定位手段？
- **明确结论**：**逆向顺序与 G1 闸门设置严谨合理，但存在更快、更确定性的定位手段——微软公共符号服务器（PDB）。**
- **依据与替代手段**：
  1. **方案顺序合理性**：
     - `decref`（特征明确，全版本稳定）→ `getHost`（虚表偏移 0x3B0 不变量）→ `frameHeight`（prologue 结构特征）→ `siteVtable`（指针数组匹配与 RIP 交叉引用）。这套顺序逻辑严密，G1 硬性闸门（缺一即止）是杜绝蓝屏/崩溃的关键，必须保留。
  2. **更快更可靠的手段：微软官方 PDB 符号解析**：
     `taskbar.dll` 是 Windows 核心系统组件，微软在公共符号服务器（`https://msdl.microsoft.com/download/symbols`）上为所有正式发布的补丁版本（包含 KB5127936 / KB5132402）**公开提供 PDB 符号文件**。
     - **离线工具链验证**：在离线适配阶段，通过对应版本的 PDB，`siteVtable`（相关接口虚表）、`GetHost` 等符号名称都是明文可见的，可直接导出精确 RVA。
     - **价值**：避免仅凭汇编代码片段在二进制层盲猜（可能因编译器指令重排或寄存器分配变化导致假阳性）。利用 PDB 验证后再固化进 Profile 表，可靠性提高一个数量级。

---

### Q5: Profile 表放在托管与原生两处是否违反"单一事实源"？有没有更稳妥的共享方式？
- **明确结论**：**确实严重违反单一事实源（SSOT）原则**，存在双向同步脱节隐患。
- **依据与改进方案**：
  - 源码中 `src/NativeHost.cs:14` 硬编码 SHA-256，而 `native/NativeHost.cpp:62` 校验 `TimeDateStamp` / `SizeOfImage` / `CheckSum`。两处维护一旦漏改一处，就会导致 C# 认为可用、C++ 却报 `unsupported` 的脱节 bug。
  - **更稳妥的方案**：**将 Profile 完全收敛于原生层（C++），托管层只做透传。**
    托管侧无需维护任何哈希或 RVA 列表。`NativeHost.dll` 暴露一个探测函数（或由 `NativeAttach` 直接返回详细状态码及不支持的指纹）。C# 侧仅通过 P/Invoke 询问原生组件当前系统是否被支持，支持则接入，不支持则提取原生组件返回的指纹字符串弹窗兜底。

---

### Q6: 动态窗口高度 + 原生 RadioButton 在 125%/150% DPI 下有没有已知坑？
- **明确结论**：**有严重已知坑。WinForms 的 DPI 缩放模型极易引发尺寸重复缩放或文字/控件裁剪。**
- **依据与避坑指南**：
  1. **`manager.Scale` 引起的计算混乱**：
     查看 `src/Program.cs:641-644`：窗体现有逻辑是在 96-DPI 下构建，最后执行 `manager.Scale(new SizeF(uiScale, uiScale))`。如果使用动态高度，在运行时通过读取已缩放子控件的 `Bottom` 或 `Height` 来累加窗体高度，会导致 `uiScale` 被重复计算（乘以 $uiScale^2$）。
  2. **原生 RadioButton 的系统绘制偏移**：
     WinForms 原生 `RadioButton` 在 125%/150% DPI 下，系统主题绘制的圆形单选框与文本的 Baseline 垂直对齐常出现 1~2px 的错位，且自动计算的 `PreferredSize` 在高 DPI 下偶尔会将末尾汉字换行或裁切。
  3. **规避原则**：
     - 动态高度计算必须基于**逻辑 96-DPI 标量**进行累加（例如：`TotalDpi96Height = Header + Card1 + Card2 + Buttons + Margins`），赋值给 `ClientSize`，然后统一交付 `manager.Scale` 缩放。
     - 原生 RadioButton 必须显式留足宽度余量（Padding），关闭 AutoSize 或固定高度为 24~28px。

---

### Q7: 有没有更简单、更贴合 Windows 的第三条路值得优先于方案 B？
- **明确结论**：**有。深度原生化的“系统托盘 Fluent 常驻形态”在工程和用户体验上全面优于方案 B。**
- **建议**：见后文第六节。

---

## 三、发现的具体缺陷与遗漏（按严重度由高到低排序）

### 【严重】1. 跨进程注入的热卸载竞态与资源泄露
- **位置**：方案第 2 节模式热切换机制；源码 `src/NativeHost.cs:106`、`native/NativeHost.cpp:212-221`
- **分析**：
  在主进程未退出的情况下执行 `native.Dispose()`，C# 卸载本地句柄，而 `explorer.exe` 端的 `NativeWorker` 线程需要异步调用 `Remove()` 还原 XAML 树节点。若用户在设置窗口中快速连续点击切换单选框，上一次 Detach 的 `FreeLibraryAndExitThread` 尚未在 Explorer 内部完全析构，下一次 `CreateRemoteThread` 注入的 `NativeStart` 已强行进入，会造成 `g_lifecycle` 互斥量错乱或 XAML 节点重复插入崩溃。

### 【高】2. `taskbarUnavailable` 下设置保存覆写用户偏好
- **位置**：方案第 1 节与第 5 节交互逻辑
- **分析**：
  当用户原本保存了 `preferredMode = Taskbar`，系统升级后触发降级（`taskbarUnavailable = true`），此时设置窗口中“任务栏”选项被禁用，UI 处于“托盘图标”选中状态。若用户在设置窗口修改了其他选项（例如开机启动）并点击“完成”，如果代码直接将 UI 当前选中态写入注册表，就会**悄悄将用户的持久化偏好覆写为 `Tray`**，破坏了需求清单 R5。必须对 UI 保存逻辑做严格鉴别：置灰禁用项决不能反写配置。

### 【中】3. 托盘模式下的硬件轮询停顿与重连闪烁
- **位置**：`src/Program.cs:402-412` `RefreshDisplay`
- **分析**：
  在 Taskbar 模式下，有 `src/TaskbarPublisher.cs` 通过共享内存持续写入快照；而在 Tray 模式下完全依赖 `UpdateTrayIcon`。目前 `trayPercent` 仅在百分比数字变化时重绘（`src/Program.cs:455`）。但在设备休眠、断连或重连状态切换时，托盘图标目前无法展示“充电中”或“重连中”的细分视觉指示，状态反馈维度弱于任务栏悬停卡。

### 【低】4. 诊断日志中的状态混淆
- **位置**：`src/Program.cs:415`
- **分析**：
  `diagnostic` 字符串输出 `Native=...`。在主动选择 Tray 模式时，`native` 为 null，目前逻辑会输出 `"降级托盘（...）"`，导致日常诊断日志把“用户主动选托盘”误判为“系统故障降级”，干扰后续运维诊断。

---

## 四、实施顺序调整建议

建议将原 S1-S6 调整为如下依赖链路：

```
S1 (数据模型与单测)
 └─► S2-A (托管三量模型与纯分流，解耦热切换)
      └─► S3 (托盘单实例保障与首次引导提升，解决4个图标问题)
           └─► S4 (设置窗口模式卡片 + 动态高度与DPI对齐)
                └─► 阶段交付：发布方案 A 完整稳定版 (零崩溃风险)
                     └─► S5-A (PDB符号校对 + Profile表收敛至Native)
                          └─► S5-B (G1闸门：4项RVA反汇编语义断言)
                               └─► S6 (全链集成与长周期稳定性观察)
```

- **关键建议**：
  1. **拆分 S2**：剥离复杂的跨进程热切换，先做到冷启动时按配置分流。
  2. **提前 S4**：在新 DLL 尚未适配的强制降级状态下，验证 S4 设置界面置灰与 DPI 缩放。此时方案 A 已经能独立形成高质量交付包。
  3. **S5 独立分闸**：方案 B 作为独立的高风险模块后置，严格执行 G1，不过闸门则随时安全回退。

---

## 五、明确"绝对不该做"的部分及理由

1. **绝对不要在 WinForms UI 线程内同步执行 `NativeDetach()` 试图实现“无感瞬间热切换”**：
   - 跨进程等待 Explorer 线程退出包含高达 6 秒的超时循环，UI 必然出现假死；强行异步卸载又会导致 XAML 树并发操作产生崩溃。
2. **绝对不要在 C# 代码中主动调用注册表 API 批量删除 `HKCU\Control Panel\NotifyIconSettings` 下的子项**：
   - 该注册表树是 Windows Shell 的未公开私有结构。外力篡改极易损坏托盘配置库，导致系统其他软件图标常态消失。只需保证本程序自身路径唯一即可。
3. **绝对不要在运行时使用 AOB 内存模式扫描（Pattern Scan）暴力强刷未知版本的 taskbar.dll**：
   - 内存模式扫描若因微小指令漂移发生误判，调用的就是非法指令地址，直接导致 Windows 资源管理器 Crash 循环。必须坚持“指纹白名单 Profile + 未知一律安全降级”的铁律。
4. **绝对不要为模式选择卡片再次手写自绘单选控件**：
   - 方案中决定“使用 WinForms 原生 RadioButton”，这是极其正确的选择。切勿重走自绘逻辑，避免引入额外的焦点框、高对比度主题及 DPI 计算陷阱。

---

## 六、更贴合 Windows 的第三条路：Fluent 托盘实时仪表（纯用户态稳健方案）

方案 B（向 Explorer 注入并逆向 `taskbar.dll` 内部 XAML）本质上是与微软 Windows 快速迭代对抗，每一次累积更新（KB）都会导致 RVA 失效，长期维护成本极高。

**更贴合 Windows 原生哲学的替代路径：**
1. **彻底放弃 XAML 逆向注入**，消除破坏 Explorer 稳定性的系统级隐患。
2. **升级现有托盘渲染引擎（`src/TrayIcon.cs`）**：
   - 利用 GDI+ 绘制高质量 Windows 11 Fluent 风格的电池胶囊（支持充电动态指示、低电量高对比告警色）。
   - 根据当前显示器 DPI 自动选择 16x16 / 20x20 / 24x24 / 32x32 最优位图尺寸生成 `Icon`，保证百分比文字在 100%、125%、150%、200% 缩放比例下绝对清晰。
3. **系统常驻引导**：
   - 在首次启动或设置页中，引导用户完成一次 `IsPromoted = 1` 提升，使其常驻于系统托盘（紧邻时钟与通知中心）。
4. **优势**：
   - **100% 免疫系统更新**：无论 Windows 安装多少个 KB，该方案永远稳定生效。
   - **零注入、零报毒**：无需敏感 API，稳定性达到最高等级。
