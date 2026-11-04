# 方案（待外部评审）：双显示模式（任务栏内嵌 / 托盘图标）用户可选 + 新 taskbar.dll 适配

> 状态：**方案轮产物，未实施、未批准**。用途：交其他 agent 评审。
> 日期：2026-11-04。性质：只描述方案，不含代码改动。

## Context

用户系统更新（KB5127936 / KB5132402，11-04）后 taskbar.dll 指纹变化，工具按既有安全锁降级为托盘图标。用户希望：① 托盘形态**保留并升级为正式可选模式**；② **恢复之前更偏爱的任务栏内嵌形态**；③ 由用户在设置里**自己选**；④ 更贴合 Windows 原生习惯；⑤ 不再出现"设置页 4 个图标"的重复注册。**本轮只给方案，不实现。**

侦察基线（已实测）：
- 新 taskbar.dll：SHA256 `A52F99F7BE8CC1EE360025D1C06C1480A489B1D309BEF230C05EB21A106CCF34`，大小 3294288，TimeDateStamp `1635664427`，SizeOfImage `3252224`，CheckSum `3342558`
- 旧版（我们适配过的 `CA1E335A…`，3217192 字节）在 WinSxS 保留 → 可做二进制差分
- 首轮差分：`decref` 有 21B 前缀匹配；`getHost` 27B 前缀（落在函数尾，入口待定）；`frameHeight` 仅 15B 且候选在函数中部；`siteVtable` 8 个 `sub rcx,imm;jmp` thunk 目标全变 → **vtable 需按"指针数组结构 + RIP 交叉引用"重新定位**

## 需求清单（用户的 + 按情况补充的）

| # | 需求 | 来源 |
|---|---|---|
| R1 | 两种形态都可用，设置里用户可选，选择**持久化** | 用户 |
| R2 | 恢复任务栏内嵌（完成新 taskbar.dll 适配） | 用户 |
| R3 | 更原生：模式选择用系统控件、尊重系统托盘图标开关、不强制注册表提升 | 用户 |
| R4 | 设置页里"鼠标电量"**恰好 0 或 1 条**记录，永不出现多路径重复 | 用户 |
| R5 | 已保存的偏好**不被指纹失效悄悄改写**；适配完成后自动回到用户选的模式 | 补充（用户 Windows 更新频繁：26300 上 10 月至今 5 个 KB） |
| R6 | 强制降级（指纹不匹配）与用户主动选托盘要**视觉区分**：只有前者弹气球 + 琥珀横幅 + 显示新指纹 | 补充 |
| R7 | 切换模式**不需重启**，且切换时 `DetachResult=0`、任务栏占位干净移除 | 补充 |
| R8 | 出现下一个未知指纹时，仍能自动兜底到托盘并在设置里给出可适配的哈希 | 补充 |
| R9 | 保留全部已验证链路：HID++/Bolt/蓝牙、Raw Input 跟随、快照 ABI 512B、单实例、自启、主题跟随、v4.2.1/v4.2.2 全部修复 | 补充 |

## 架构设计

### 1. 引入 `DisplayMode`（拆开 `degraded` 的双重语义）

新增 `src/DisplayMode.cs`（需加入 `build.ps1` 与 `test.ps1` 的 sources 列表）：

```
enum DisplayMode { Taskbar, Tray }
static class Mode {
    // 持久化：HKCU\Software\MouseBatteryTray\DisplayMode (DWord 0/1，非法值回落 Taskbar)
    Load() / Save(DisplayMode) / Parse(object)  // 纯函数，便于单测
}
```

`TrayContext` 用三个量替代单个 `degraded`：
- `preferredMode`（用户选择，持久化）
- `taskbarUnavailable`（本轮指纹/profile 不匹配，运行时）
- `ActiveMode` = `taskbarUnavailable ? Tray : preferredMode`（**唯一显示分支依据**）

`degraded` 的现有 9 处检查点（`Program.cs:375/406/415/436/543/558/605/629` 等）逐处改为按 `ActiveMode` / `taskbarUnavailable` 判定。关键：`cardB`（位置选择器）改为**仅 Taskbar 模式显示**，降级横幅改为**仅"用户选了 Taskbar 但不可用"时显示**。

### 2. 模式运行时切换（不需重启）

- `native` 从 `readonly` 改为**惰性创建**：Taskbar 模式才构造；Tray 模式不构造（连指纹校验都不做，零依赖）
- Taskbar→Tray：`native.Dispose()`（触发 detach）→ 置 null → `trayIcon.Visible=true` → 保存偏好
- Tray→Taskbar：`taskbarUnavailable` 为真则**禁用该选项**并给理由；否则 `new NativeHost()` → `trayIcon.Visible=false` → 保存偏好
- `RefreshDisplay()` 分支由 `ActiveMode` 决定：**互斥且完整**，两条路径都不残留对方的资源
- `TryPromoteTrayIcon()` 触发条件由 `if(degraded)` 改为 `ActiveMode==Tray`（并且见 R3 改为"仅首次提升"）
- `status.txt` 新增 `DisplayMode=` 与 `ModeUnavailable=` 两行，便于诊断

### 3. 托盘模式"原生化" + 单条记账不变量（R3/R4）

- **单一 `NotifyIcon` 实例**，整个进程只创建一次；切换模式只改 `Visible`，**绝不销毁重建**（这正是产生重复注册的风险点）
- **只用正式 EXE 路径**：`Application.ExecutablePath` 恒为 `…\app\MouseBattery.exe`，不再有任何测试探针路径参与注册 → 结构上保证最多 1 条记录
- **尊重系统设置**：`TryPromoteTrayIcon()` 改为仅在条目**尚无 `IsPromoted` 值**时写 1；用户后来在系统设置里隐藏图标，不再被覆盖（比现在"强制提升"更原生）
- 用户主动选 Tray 时：**不弹警告气球、不显琥珀横幅、不显示指纹**；只有强制降级才提示
- 默认偏好 = **Taskbar**。用户从不切到 Tray 时，设置页里"鼠标电量"记录为 **0 条**；切到 Tray 后**恰好 1 条**；来回切仍 1 条
- 不再写自动清理注册表的代码（历史 3 条已手工清除，避免触碰用户资产）

### 4. 方案 B：新 taskbar.dll 适配（高风险，设闸门）

**改动文件**：`src/NativeHost.cs`、`native/NativeHost.cpp`

**4.1 从"单一硬编码"改为"已知 profile 表"**（应对 R5/R8，用户更新频繁）：

```
struct Profile { sha256, timestamp, sizeOfImage, checksum,
                 rvaSiteVtable, rvaGetHost, rvaFrameHeight, rvaDecref,
                 elementOffset }
```
托管侧 `KnownHashes[]`、原生侧同值表；`InitializeProfile()` 按当前哈希选行，未知哈希 → 明确返回 `unsupported` → 自动托盘兜底。**未来每次更新只需加一行**。

**4.2 逆向定位新 RVA 的方法**（按可靠性排序，逐个验证，不许猜）：

1. `g_decref`：用旧 DLL 的完整函数体签名 `40 53 48 83 EC 20 48 8B D9 83 C8 FF F0 0F C1 41 08 …` 在新 .text 精确/宽松搜索，反汇编确认是引用计数递减
2. `g_getHost`：用 `48 8B 89 ?? ?? ?? ?? 48 8B 01 48 8B 80 B0 03 00 00 E8`（虚表偏移 0x3B0 调用）作不变量搜索——编译器可改指令排布但难改这个调用偏移
3. `g_frameHeight`：按 prologue 模式 `48 83 EC ?? 48 83 C1 ?? 48 83 39 00 74` 搜索（代码里的 `code[7]<=0x7F` 校验依赖它），重新提取 `elementOffset`
4. `g_siteVtable`：枚举新 .rdata 中"≥6 个连续指针指向 .text、且目标以 `48 83 E9`/`E9` 开头"的候选数组，再用 **RIP-relative `lea` 交叉引用**验证（旧代码 `TaskbarRoot` 会 `band[i]==g_siteVtable`，新代码必然仍有对该地址的引用）

**4.3 闸门 G1（硬性）**：4 个目标必须**全部**通过"反汇编语义 + 交叉引用"双验证；任一无法确认则**停止适配、如实报告**，只交付方案 A 的模式切换框架 + 托盘兜底。**绝不发布半适配的原生组件**（半适配正是此前堆损坏的来源）。

**4.4 若微软改了 XamlRoot 发现机制**（site vtable 路径整体消失）：视为 G1 不通过，报告并回退，不强行另造注入方式。

### 5. 设置窗口（R1/R3，原生优先）

- 新增**"显示方式"卡片**，用 **WinForms 原生 `RadioButton` 一对**（系统绘制、系统焦点框），文案：`任务栏（原生同构）` / `托盘图标`——**不再为这个控件写自绘代码**，这是"更原生"的落地
- 该卡片**在两种模式下都存在**（不像现在 `cardB` 被 `!degraded` 整体跳过）
- `taskbarUnavailable` 时：Taskbar 单选**禁用**，下方一行弱色小字"当前系统版本暂未适配（指纹 A52F99F7…）"，**不用大琥珀横幅**（横幅只留给"用户想要 Taskbar 却被迫降级"的情形）
- 窗口高度**按实际显示的卡片数动态计算**（header + 状态卡 + [位置卡] + 模式卡 + 命令带），不再用 `HeightWindowNormal/Degraded` 两个写死常量——v4.2.2 的按钮裁切就是写死高度造成的；验收用 UIA 确认命令带完整可见
- `note` 文案按 `ActiveMode` 而非 `degraded` 生成
- 托盘模式的右键菜单保持"打开设置/退出工具"；任务栏模式菜单保持现状，不新增自绘项

## 不改的部分（碰了即越界）

`Battery.cs`、`Hid.cs`、`TaskbarPublisher.cs`、`src/TrayIcon.cs`（仅可能被模式切换复用，不改渲染）、`native/TaskbarView.h`（视觉层不动）、快照 512B ABI、`TaskbarPosition`/`Run` 注册表语义、`Startup`、`--diagnose`/`--exit`/`--toggle-startup` 命令行。

## 实施顺序（先低风险后高风险，每步可单独验）

| 步 | 内容 | 风险 |
|---|---|---|
| S1 | `DisplayMode.cs` + `Mode.Parse/Load/Save` + 单测（非法值/默认值） | 极低 |
| S2 | 重构 `degraded` → `preferredMode/taskbarUnavailable/ActiveMode`，惰性 `NativeHost`，模式热切换 | 中 |
| S3 | 托盘单实例不变量 + 仅首次提升 + 区分"主动选"与"强制降级"的提示 | 低 |
| S4 | 设置窗口原生 RadioButton 模式卡 + 动态高度 + 条件横幅 | 中（WinForms 布局） |
| S5 | 方案 B：profile 表 + 4 个 RVA 逆向 + G1 闸门 | **高** |
| S6 | 全链回归 + 真机 + 隔夜观察 | — |

## 验证（含用户专门提的"4 个图标"）

1. `test.ps1` 48+15（新增 2-3 项 Mode 解析/持久化单测）→ 全过
2. **模式切换 3 轮**：Taskbar→Tray→Taskbar→Tray，每轮核对 `status.txt` 的 `DisplayMode=`、任务栏占位移除、`native-lifecycle.txt` 的 `DetachResult=0`
3. **记账不变量断言**：切换后 `HKCU\Control Panel\NotifyIconSettings` 中 ExecutablePath 含 `MouseBattery\app` 的条目**恰好 1 条**；含 `degraded-probe` 的 **0 条**（R4 的硬性验收）
4. **强制降级模拟**：临时假指纹构建探针 → 确认弹气球 + 琥珀横幅 + 新哈希、且**不改写已保存的 `DisplayMode`**；还原常量后自动回到用户选的模式（R5/R6/R8）
5. **方案 B 通过 G1 后**：原生重建（失败即中止，v4.2.1 纪律）→ `AppearanceProbe` 8 项 → 真机任务栏显示/悬停卡/右键菜单 → 三轮开/关设置存活
6. **隔夜观察**：堆损坏验收标准是隔夜稳定，短时压力不作数
7. 全部通过后更新 README/交接，**push 前先向你确认**（本地目前领先 origin 若干提交）

## 风险与回退

| 风险 | 处置 |
|---|---|
| G1 不通过（4 个 RVA 无法全部验证） | 停止方案 B，只交付 A + 模式切换框架；任务栏选项置灰并给出指纹；托盘模式完整可用 |
| 每次 Windows 更新都要重新逆向 | profile 表把改动收敛为"加一行"；未知哈希自动托盘兜底，工具永不失效 |
| 模式热切换引发重复注册/资源残留 | 单一 NotifyIcon 实例 + 互斥分支 + 记账不变量断言 |
| 动态高度算错导致按钮裁切 | UIA 实测命令带完整可见，不用写死常量 |
| 任何一步破坏稳定性 | `git stash` 回退到当前 HEAD，正式 EXE 用 `baseline\` 备份恢复 |

## 给评审 agent 的问题（请重点回答）

1. `degraded` 单向 bool 拆成三量的重构，是否有遗漏的检查点或生命周期陷阱（尤其 `EnterDegraded()` 在构造期与运行期各一处、`native.Dispose()` 的 StopResult 日志）？
2. 单一 NotifyIcon 实例 + 只改 Visible 的方案，是否真能保证"设置页最多 1 条"？有没有 Windows 托盘记账的其它触发路径（例如 `Icon` 换句柄、`Text` 变更、进程重启）会新增条目？
3. `TryPromoteTrayIcon` 改为"仅当 IsPromoted 缺失时写 1"是否足够原生？用户主动选 Tray 时若系统默认把新图标收进溢出区，不提升会不会导致"选了却看不见"？
4. 方案 B 的 RVA 逆向顺序与 G1 闸门是否合理？有没有更快更可靠的定位手段（例如 PDB 符号、已知 Windhawk 上游更新、MinHook 式模式扫描）？
5. profile 表放在托管与原生两处是否违反"单一事实源"？有没有更稳妥的共享方式（资源段 / 生成头文件）？
6. 动态窗口高度 + 原生 RadioButton 在 125%/150% DPI 下有没有已知坑（v4.2.2 刚因为 `manager.Scale` 只缩 bounds 不缩自绘坐标系踩过）？
7. 有没有更简单、更贴合 Windows 的第三条路（例如完全放弃内嵌、只用托盘，但把托盘做成系统级"电池设备"样式）值得优先于方案 B？


---

## 七、评审修订（2026-11-04，依据 `dual-mode-display-review.md`）

> 本节为**最终实施依据**，与前文冲突处以本节为准。评审文档已完整阅读并确认：方向性判断、7 问依据、严重缺陷（热卸载竞态 / 置灰反写 / 托盘状态维度 / 诊断混淆）、实施顺序调整。

### 7.1 五项强制修订

1. **取消跨进程无感热切换 → 设置保存 + 平滑自我重启（Restart）**
   - 模式切换不调用 `native.Dispose()`/重建；只写 `DisplayMode` 注册表，然后走"退出当前实例 + 以相同参数重新拉起"。
   - 复用现有单实例 Mutex（`Program.cs:51`）：先释放 Mutex，再 `Application.ExitThread()`，由一个新启动的进程接管；或 `ExitThread` 后由现有 `--background` 启动入口拉起。实现时保证**旧进程完全退出后新进程才抢到 Mutex**（Mutex 释放与进程退出间的竞态用短暂等待/重试消除）。
   - 理由（评审 Q1/严重缺陷 1）：`NativeDetach` 在 Explorer 端异步摘 XAML 树 + 最多 6s 等待循环，UI 线程同步调用必然假死；快速连点切换会造成 `g_lifecycle` 互斥量错乱与 XAML 节点重复插入。**重启让一切状态从冷启动重建，零竞态。**

2. **修复设置保存逻辑：`taskbarUnavailable` 时置灰项绝不覆写 `preferredMode`**
   - 保存动作只在"用户实际改变了单选按钮"时写 `Mode.Save()`；**点击"完成"不读 UI 选中态反写**。
   - 当 Taskbar 单选被禁用时，UI 显示 Tray 选中，但 `preferredMode` 保持用户原值（如 Taskbar）不变；`ActiveMode` 由 `taskbarUnavailable` 强制为 Tray。
   - 修正评审【高】缺陷 2：修改开机启动等其他选项并点完成，不得把降级态悄悄写成用户偏好。

3. **Profile 表 SSOT 收敛至 Native 层**
   - `src/NativeHost.cs` **删除**全部哈希/PE 常量；`native/NativeHost.cpp` 持有唯一 Profile 表（sha256/timestamp/size/checksum/4 RVA/elementOffset）。
   - 托管侧不再校验指纹，只调用原生导出的探测函数（或 `NativeAttach` 返回详细状态码 + 指纹字符串）；不支持时由原生返回哈希，C# 仅负责提示与托盘兜底。
   - S5-A 用**微软公共符号服务器 PDB**（`https://msdl.microsoft.com/download/symbols`）校对符号，替代纯 AOB 盲猜（评审 Q4）；仍坚持"指纹白名单 + 未知一律安全降级"，**禁止运行时 AOB 强刷**（评审"绝对不该做"第 3 条）。

4. **动态窗口高度严格基于 96-DPI 标量累加**
   - `TotalDpi96Height = Header + CardA + [CardB] + ModeCard + CommandBand + Margins`，全部用 96-DPI 逻辑常量相加，赋给 `ClientSize` 后**最后一步**统一 `manager.Scale(uiScale)`。
   - **禁止**在缩放后读取子控件 `Bottom/Height` 累加（会 ×uiScale²，评审 Q6）。
   - 模式选择用**原生 RadioButton**、固定高度 28px、`AutoSize=false`、留足宽度余量；不自绘。

5. **托盘提升基于应用自身注册表标记，仅首次一次**
   - `HKCU\Software\MouseBatteryTray\TrayPromotedOnce` (DWord)：缺失/为 0 时，首次进入 Tray 模式写系统 `IsPromoted=1` 并置本标记为 1；之后**永不覆写**系统键。
   - 理由（评审 Q3）：Win11 首次会写 `IsPromoted=0`（键不缺失），"判缺失"会变空操作；且用户主动选 Tray 必须看得见，不能收进溢出区。用户后续在系统设置里隐藏图标由系统自由管理。
   - 遵守"绝对不该做"第 2 条：**绝不代码批量删除 `NotifyIconSettings` 子项**；单路径唯一 + 进程内单 NotifyIcon 即可保证最多 1 条（评审 Q2）。

### 7.2 采纳的其它修订

- 诊断串区分：主动 Tray 输出 `Native=托盘模式（用户选择）`，强制降级才输出 `降级托盘（指纹…）`（评审【低】缺陷 4）。
- 主题切换 `OnPreferenceChanged` 对两模式分支都有效（评审 Q1 遗漏检查点）。
- 托盘模式补充状态反馈维度：图标在设备重连/充电时给出可辨细节（评审【中】缺陷 3）——本阶段先记边界，S4 视成本决定，不阻塞方案 A 闭环。
- "绝对不该做"全盘接受：不在 UI 线程同步 Detach、不删系统托盘注册表树、不运行时 AOB 强刷、不为模式卡片自绘单选控件。

### 7.3 调整后的实施顺序（S5 独立分闸）

```
S1      DisplayMode.cs + Mode.Parse/Load/Save + 单测
 └─► S2-A  托管三量模型（preferredMode/taskbarUnavailable/ActiveMode），
            冷启动按配置分流；剥离热切换；Restart 机制（保存+平滑重启）
      └─► S3  托盘单实例 + TrayPromotedOnce 首次提升 + 诊断串区分
           └─► S4  设置窗口原生 RadioButton 模式卡 + 96-DPI 标量动态高度
                + 置灰不反写 + DPI 验收
                 └─► ★ 阶段交付：方案 A 完整稳定闭环（零热切换风险）
                      └─► S5-A  PDB 符号校对 + Profile 表 SSOT 收敛至 Native
                           └─► S5-B  G1 闸门：4 项 RVA 反汇编语义断言
                                └─► S6  全链集成 + 长周期（隔夜）稳定性观察
```

- S2-A 只做**冷启动分流**，切换靠 Restart；不引入任何跨进程热卸载。
- S5 作为独立高风险模块后置，**G1 不过则安全回退**，方案 A 交付物不受影响。
- 评审"第三条路"（纯用户态 Fluent 托盘、彻底放弃注入）记录在案，作为长期演进方向；本轮仍按用户明确要求"两种方案都要"推进，S5 不通过时它自然成为默认形态。

### 7.4 本轮执行范围

按用户指令：**先执行 S1 → S2-A → S3 → S4，交付方案 A 稳定闭环**；S5 独立分闸，本轮只把 SSOT 接口/占位准备好（不改任何 RVA、不做适配、不触碰 G1），待方案 A 验收后再单独启动。
