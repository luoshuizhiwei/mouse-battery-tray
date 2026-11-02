# 前端重构实施规格 —— Catppuccin 主题（交给实现者）

版本目标：v4.0.0。性质：三层前端重构 + 换主题。**只改视觉/交互层，不改任何设备/协议/生命周期逻辑。**

## 0. 项目位置与铁律（违反即返工）

- 源码：`D:\develop\Code\Codex\鼠标电量显示工具`（Git 仓库，先 `git log --oneline` 看演进）
- 交付：`<部署目录>\app\MouseBattery.exe`（正在后台运行，真机在线）
- 先读：`README.md`、`docs/handoff/current.md`（若与 git log 不符以 git 为准）

**铁律（用户逐条确认过，不可违反）：**
1. 任务栏胶囊**只显示百分比数字**，不加图标/文字/动画。
2. 胶囊**左键零动作**（防误触），只有右键菜单 + 悬停。
3. 胶囊**静息纯文字无底**（与原生时钟同构），悬停才显玻璃底；**字重恒定**（切字重会跳动，已踩坑）。
4. 差异化显示不要"电竞/办公"分类词上屏，用产品线名 + 视觉语言。
5. **无位移动画**。交互跟手指的是 hover/pressed 反馈即时干净，不是加动画。
6. 托管 C# 用系统 csc 编译，**C# 5 语法上限**——无 `?.`、无插值字符串 `$""`、无表达式体成员、无 `nameof`。原生 Clang C++23/WinRT。
7. **原生构建失败必须中止整条部署链**（v3.8.3 崩溃教训：曾因放行旧 DLL 导致源码与二进制不符）。

## 1. 主题 token（唯一事实源，三层共用同一组值）

新建托管 `Ui` 静态类（`src/Ui.cs`，新文件）+ 原生同名常量函数（`native/TaskbarView.h` 内）。所有颜色从这两处取，**不允许在别处写死 hex**。

Catppuccin 官方 hex（已实测，直接用）：

| token | Latte (light) | Mocha (dark) |
|---|---|---|
| Base 底色 | `#EFF1F5` | `#1E1E2E` |
| Surface 卡面 | `#FFFFFF` | `#313244` |
| Border 描边 | `#CCD0DA` | `#45475A` |
| Text 主文字 | `#4C4F69` | `#CDD6F4` |
| Subtext 弱文字 | `#6C6F85` | `#A6ADC8` |
| Accent 强调 | `#1E66F5` | `#89B4FA` |
| Yellow 琥珀(≤20%) | `#DF8E1D` | `#F9E2AF` |
| Red 红(≤10%) | `#D20F39` | `#F38BA8` |
| Mauve(PRO X 产品线) | `#8839EF` | `#CBA6F7` |
| Sapphire(M750L 产品线) | `#209FB5` | `#74C7EC` |

几何：圆角 卡 10 / 胶囊 9 / 按钮 8；卡内边距 16；刻度条高 4；间距基数 4。
字阶：10.5 / 11 / 12 / 15 / 30（Display 大数字 + Text 正文，Segoe UI Variable）。

**差异化改法**：PRO X 色点+描边用 Mauve 紫，M750L 色点用 Sapphire 青（办公卡无描边）。替代原来的 accent/muted 灰点。

## 2. 改动清单

### A. 设置窗口 `src/Program.cs`
- 新增 `Ui` 静态类；`RoundedPanel`/`PositionPicker`/`ToggleSwitch`/状态卡全部改为从 `Ui` 取值。
- 布局：标题行右侧加当前设备产品线 chip；状态卡大数字右对齐 + 单位上标；ToggleSwitch 右对齐；按钮三态（rest/hover/pressed，hover 加深、pressed 再深半档）。
- `ToggleSwitch` hover 提亮 + 按下圆钮缩 1px。
- 字阶统一 token。
- **保留**：迷你任务栏位置选择器交互、降级横幅、主题实时跟随（`OnPreferenceChanged` 里记得给降级托盘图标也换色——v3.3 漏了托盘）。

### B. 任务栏胶囊 + 悬停卡 `native/TaskbarView.h`
- 胶囊：静息纯文字/悬停显底双态保留，玻璃渐变 token 换 Catppuccin 值；静息数字 15px 保持。
- 悬停卡：卡宽 224→232、padding {16,14,16,14}；产品线色点上移到设备名左侧（chip 行删除，减少重复行）；大数字 30→32pt；% 单位与充电闪电 `VerticalAlignment(Center)` 基线对齐；刻度条色值换 token；电竞卡描边=Mauve、办公卡无描边。
- 原生新增与托管同值的 token 常量函数。

### C. 降级托盘图标 `src/TrayIcon.cs`
- 电池壳圆角与胶囊统一；数字换 Segoe UI Variable Display；阈值色同步 token。

### D. 交互跟手
- 所有可点元素三态齐全 rest/hover/pressed；焦点框统一 accent 1px 虚线；无计时器动画。

## 3. 不改的部分（碰了即越界）

`Battery.cs`、`Hid.cs`、`TaskbarPublisher.cs`、`NativeHost.cs`、`NativeHost.cpp` 零改动。
设备枚举、HID++、Bolt 索引扫描、Raw Input 跟随、DisplayState 多设备模型、**快照 ABI（512B 结构+共享内存名+偏移）**、单实例、降级机制、构建链，全部保留。

## 4. 构建与部署（严格按序，失败即停）

```powershell
# 1. 逻辑回归（视觉重构不应破坏；破坏了说明误动逻辑）
.\test.ps1          # 48 逻辑 + 12 IPC 必须全过

# 2. 原生重建（涉及 TaskbarView.h 时；失败即中止，不许用旧 DLL 继续）
.\build-standalone-native.ps1

# 3. 托管候选（-NativeDll 指向第 2 步的新产物）
.\build.ps1 -OutputDirectory '<部署目录>\robust-v3.2\candidate-v400' -NativeDll '<部署目录>\standalone\candidate\MouseBattery.Native.dll'

# 4. 原生渲染回归（重建 AppearanceProbe 用新 DLL，必须带 /resource）
.\build-probe.ps1
# 正式版 --exit 后运行探针，8 项必须全过（刻度断言、内容宽约束）

# 5. 真机验收：三轮开/关设置存活（v3.8.3 崩溃回归）、双鼠 kind 行、DetachResult=0

# 6. 晋升：备份旧 EXE 到 baseline，再替换 <部署目录>\app\MouseBattery.exe
```

## 5. 验收清单（每项都要真机证据）

- [ ] test.ps1 48+12 全过
- [ ] AppearanceProbe 8 项全过（含刻度线点亮断言）
- [ ] 设置窗口 PrintWindow 实截：正常态 / 降级横幅态 / 位置选择器
- [ ] 悬停卡实截（`<部署目录>\robust-v3.2\capture-hover.ps1` 实时坐标版）
- [ ] 三轮开/关设置存活
- [ ] 双鼠差异化：PRO X=Mauve、M750L=Sapphire 正确
- [ ] 正常退出 DetachResult=0
- [ ] 深浅双主题各自截图（不要改用户系统主题去凑——可用离屏渲染或记边界）

## 6. 回退

任何一步出问题：`git stash` 回退到 v3.8.3（commit 6844302），正式 EXE 用 `<部署目录>\robust-v3.2\baseline\` 里的备份恢复。

## 7. 交付后

更新 `docs/handoff/current.md`（v4.0.0、新 EXE/DLL 双哈希、验证证据、未验收边界）、README.md、git commit。Obsidian 记忆库由主会话负责，不用你管。
