# 状态矩阵取证结果与实施修订（交实现轮）

日期：2026-11-02。依据：已交付的 v4.1.0（HEAD `6a25d9c`，正式 EXE `3B045AA2…5DB`，原生 DLL `B84D3691…99A8`）真机取证，不是读码推断。
证据归档：`E:\MouseBattery\design-sheet\run-20261102-181538\`（T 表 19 格）、`E:\MouseBattery\design-sheet\tray\`（R 表 48 格）、`E:\MouseBattery\design-sheet\settings\settings-20261102-183017.png`（设置窗 **846×793 物理像素**，150% DPI）。
取证工具：`tools/design-sheet/`（全部为新增文件，未进入 `build.ps1` 的显式 sources 列表，不影响产品构建）。

---

## 0. 先修哪一条（N3 / N2 已修复，见文末修复记录；N1 已撤回）

**N3 → N2 → N1。** N3 不修，后续所有悬停卡取证都只能靠突发采样碰运气；N2 同时涉及原生行为和一条已经固化了错误期望的探针断言；N1 是设计决策，不是单纯补两行代码。

---

## N3 悬停卡会被 500ms 重绘打断（最优先）— 已修复 2026-11-02

**实测**：19 格中卡出现 16 格；前两次跑分别只出现 4/19、3/19，且缺失的是**随机子集**，不是某些电量状态渲染不出来。突发采样（每 80ms 一帧、保留变化面积最大的帧）后才恢复到 16/19。

**根因（代码可定位）**：
- [native/NativeHost.cpp:81](../../native/NativeHost.cpp#L81) 每 500ms 无条件 `Dispatch(Update)`。
- [native/TaskbarView.h](../../native/TaskbarView.h) 的 `Update()` 每次都新建 `SolidColorBrush` 重赋 `ToolTip.Background`、`BorderBrush`、`RequestedTheme`，并执行 `v.host.UpdateLayout()`、`AutomationProperties::SetName(...)`。
- 刚过显示延时的 ToolTip 在下一个 tick 被重置，因而历史上"悬停卡实截未完成"并非自动化问题。

**修法（只改显示层，不改 ABI、设备、协议）**：
1. 在 `View` 中缓存上一次已应用的主题、卡背景、卡描边、`startup` 文案、tooltip 文本等；`Update()` 只在值**实际变化**时赋值，未变化则跳过。
2. `UpdateLayout()` 只在会影响布局的值（百分比、伴随行可见性、充电图标可见性、设备名）变化时调用。
3. `AutomationProperties::SetName` 只在 `s.tooltip` 文本变化时调用。
4. 验收：连续悬停 20 次（每次进入后静止 1 秒），卡出现率应为 20/20；本表采集脚本从突发采样恢复为单帧采样后仍应全部抓到。

---

## N2 10% 刻痕在 11–20% 区间被错误点亮，且探针断言同样错误 — 已修复 2026-11-02

**实测**：
- T-04（20%）：`tickHigh=249,226,175`，`tickCritical=249,226,175`
- T-19（11%）：同样双黄
- T-05（10%）：`tickHigh=黄`、`tickCritical=红` 正确

**问题**：`critTick = percent<=10 ? Red : highTick`，在 `10 < percent <= 20` 时回落到 `highTick`（黄），导致 10% 刻痕应中性却变黄。两道阈值刻度同色，失去区分力。

**修法**：
- 原生：`10% 刻痕 = percent<=10 ? Red : NeutralTick`；不得回落到 `highTick`。
- 同步修改 [src/AppearanceProbe.cs:41-42](../../src/AppearanceProbe.cs#L41-L42)：现有断言 `critTick = value<=10 ? Red : (value<=20 ? Yellow : neutral)` **把错误行为写成了期望**。正确期望应为：
  - `percent > 20`：10% 刻痕中性
  - `10 < percent <= 20`：10% 刻痕**仍中性**（20% 刻痕黄）
  - `percent <= 10`：10% 刻痕红、20% 刻痕黄
- 本表格序 T-04/T-05/T-19 已冻结，修复后可直接 diff。

---

## N1 电竞与办公的玻璃底逐像素相同

**实测（同一位置三深度取样）**：
- T-01（PRO X，100%）与 T-07（M750L，100%）玻璃均为 `59,61,70 / 53,53,61 / 44,42,48`
- 静息底为 `35,30,34`
- 按设计稿 §4.2 的 α88（Mauve）与 α66（Sapphire）应分别得到约 `93,77,107` 与 `56,74,86`；实测既不相等理论值，也彼此完全相等

**结论（2026-11-02 撤回）**：修复重绘竞态后重测，两条产品线的玻璃亮度**按 α88/α66 正常分层**：100/85% 时 gaming 72–73、office 61–62；10% 时 63.5 vs 56.2。此前的"逐像素相同"是重绘竞态下的取证噪声（测量落在了被重置的中间态），**不是实现遗漏**。§4.2 的 alpha 档位已落地，N1 撤销；D5 形状化不再有"N1 证据"支撑，只保留色弱/反光场景的一般性理由，是否做由你另行决定。

**设计判断**：即便补齐 §4.2，"电竞比办公浓一档"在这种真实半透明底上也接近可辨阈值。不要把 N1 当成"把 alpha 改大就结束"：
1. 先确认这是**实现遗漏**还是**实现后强度仍不足**：按 §4.2 落地后重跑 T-01/T-07/T-02/T-08，若像素仍不可辨，则停止在玻璃强度上继续加码。
2. 若仍需双鼠区分，采用结构/形状通道（D5），不要把全部区分职责放在玻璃透明度上。
3. 静息态仍受"与原生时钟同构"约束，不能借 N1 给静息数字加身份色。

---

## N4 静息数字随位数左右呼吸（低优先级）

**实测墨宽**：100→21.86、85→16.17、21/10→13.77、11→11.37、0→8.09 DIP；内容宽最大 33.2，低于探针上限 42。墨迹中心基本稳定（3px 内），但左缘随位数移动约 10px。
这是比例数字 + 居中排版的正常结果，不是持续抖动；85→84 这类相邻值变化小于 1px。
**不建议为此单独改版式**。若用户实际感受到"100→85"整块跳动，再给数字块固定最小宽度，不要先改。

---

## 已完成的取证覆盖

| 项 | 结果 |
|---|---|
| PRO X 渲染分支 | 首次真实渲染：`kind=gaming` 12 格；此前 `AppearanceProbe` 从未设置 `Gaming` |
| 伴随设备行 | `other=1` 18 格；单设备 T-17 正确 `other=0` |
| 旧读数分支 | T-16 `state=2` |
| 卡尺寸 | 双设备 228×180 DIP；单设备 228×162.7 DIP，伴随带塌陷 17.3 DIP |
| 玻璃悬停反馈 | 19/19，ΔL +43%~+65%，高于 8% 阈值 |
| 静息数字对比度 | 3.83–5.95，均满足大数字 ≥3:1；0% 红档 3.83，余量偏薄 |
| R 表 | 48 格已出：`sheet-R-light.png`、`sheet-R-dark.png`（16/24/32px，1:1 未缩放） |
| 设置窗 S-full | 深色正常态 1 张，846×793 物理像素，客户区 560×502 DIP 与设计一致 |

## 未完成，不能写成"已验证"

- **Q3（卡信息层级是否一眼分层）与 Q4（灰度下双鼠是否可辨）需要人判**；图已出，但本轮没有完成视觉判读。
- **Latte（浅色）T 表 12 格未采**：本轮环境为 Mocha，仍需 `themeOverride` 或临时主题切换。
- S 组件级矩阵初版已出（见文末"S 表结果"），两个组件未按设计落地：`ToggleSwitch`、`PositionPicker`。
- 设置窗降级横幅态未截（需假指纹链路）。
- R 表和设置窗实截已生成文件，但**未做人眼判读**，不得据此声称"托盘数字清晰""设置窗版式合格"。

## 重跑命令

```powershell
powershell -File tools\design-sheet\build-sheet.ps1
powershell -File tools\design-sheet\capture-sheet.ps1
powershell -File tools\design-sheet\tile-sheet.ps1 -RunDir <run-目录>
powershell -File tools\design-sheet\build-tray.ps1
E:\MouseBattery\design-sheet\bin\TraySheet.exe
powershell -File tools\design-sheet\capture-settings-sheet.ps1
```

`capture-sheet.ps1` 会停止正式版并接管真实指针，跑完自动恢复正式版与指针位置；其余命令不需要接管指针。

---

## S 表结果（v1，生产控件离屏渲染，96-DPI 逻辑栅格）

产物：`E:\MouseBattery\design-sheet\settings\sheet-S-light.png`、`sheet-S-dark.png`（1080×1000）。渲染的是**生产类**（`ToggleSwitch` / `PositionPicker` / `RoundedPanel` / `Button`），不是仿制件；96-DPI 栅格只用于几何与状态判读，字形清晰度必须看 150% 的 PrintWindow 实截。

### N5 ToggleSwitch 没有按设计实现（§3.7 整条未落地）— 已修复 2026-11-02

**实测**：ON 轨道的强调色像素包围盒 **35×17 DIP**（设计 40×20，旧实现 36×18 → 实际仍是旧几何）。关闭/禁用两态之间只有 **60 px** 差异（等同边缘抗锯齿量级），说明 `Enabled=false` 没有设计级视觉变化；焦点环在离屏渲染中未出现（焦点态需要窗口激活，代码里仍是旧 `ControlPaint.DrawFocusRectangle` 虚线框）。

**代码依据**：[src/Program.cs:194-215](../../src/Program.cs#L194-L215) 只有 `Checked` 与 `Focused` 两个分支；没有 hover/pressed/disabled 状态，没有 40×20 轨道、⌀14→⌀12 按压、`Accent+`/`Accent++` 提亮、标签左/轨道右的版式。构造仍是四色：`ToggleSwitch(accent,border,OnAccent,foreground)`。

**结论**：这是 v4.1.0 明确漏掉的一整块设计，不是微调。修法按设计稿 §3.7 的五态表重写该类（纯托管，无原生影响）。

### N6 PositionPicker 仍是旧绘制 — 已修复 2026-11-02

**代码依据**：[src/Program.cs:104-106](../../src/Program.cs#L104-L106) 的 `pillFill`、`anchorColor`、`hoverFill` 仍是写死的雾蓝 ARGB（如 `(58,159,189,222)`），未走 token；[src/Program.cs:156](../../src/Program.cs#L156) 仍在控件底部自绘"位置：xxx"，与卡 B 已有的"显示位置"标签重复；选中胶囊仍是固定 36 DIP 的装饰块，没有按设计改成"真胶囊镜像"（identity/threshold 实时百分比）。

**实测**：hover 态确实有视觉反馈（1907 px 差异，hover 填充生效）；**sapphire token 像素 = 0**，证明产品线色没有进入 picker。

**结论**：§3.5 只落地了外框尺寸与 `SetTheme` 调用，绘制逻辑与三态语言未落地。

### N7 按钮缺 Enter/Esc 语义 — 已修复 2026-11-02

`grep AcceptButton|CancelButton` 在 `src/Program.cs` 中无匹配。设计 §3.8 要求 `AcceptButton = CancelButton = 完成`，理由是 **Esc 绝不能退出常驻进程**。当前 Enter/Esc 无绑定，属未落地项（不是缺陷行为，但设计承诺缺失）。

### S 表还暴露的取证限制

- 离屏窗体高度会被工作区高度钳制（1180 DIP 被压到约 1087），本轮已把内容压进 1000 DIP；后续加行要拆成第二张表。
- 焦点态必须在活动窗口中拍，离屏 `Focus()` 不可靠；焦点相关验收应在真机设置窗上用键盘 Tab 走一遍。

---

## 修复实施记录（2026-11-02，N3 + N2）

### 改动

| 文件 | 改动 |
|---|---|
| `native/TaskbarView.h` | ① `View` 新增 `styleSig`/`styled`；新增 `Signature()` 对 tooltip 文本做 FNV-1a 并与 light/gaming/state/percent/charging/transport/startup/position 组合；`Update()` 的整个样式块只在签名变化时执行（含 `ToolTip` 刷子、`UpdateLayout`、`SetName`）——N3。② 20% 刻痕改为 `percent<=20` 独立判据（原先在 ≤10 时回落中性，属于本轮新发现）。③ 10% 刻痕改为 `percent<=10 ? Red : neutral`——N2。 |
| `src/Program.cs` | 托管轨道 10% 刻痕同步为中性回落。 |
| `src/AppearanceProbe.cs` | 断言修正为 `10% 刻痕：≤10 红，否则中性`（原断言把错误行为当期望）。 |

### 产物（均为候选，未晋升）

- 原生 DLL：`23C6F4BF28D67FA92279EB6EDDA7083E0283F6583EEC9107A00A947B866D6FCA`
- 候选 EXE：`E49399417529AB91688919AAF06A4C859D9490D647A50DEA378E91A8E8BE13D9`（仅 N3+N2）
- 候选 EXE（含 N5/N6/N7，**最终候选**）：`C719C9F1855D846AC22C001E1F01F1ED2DD0C15A17718A31AE66F97C4BEBDB40`
- 探针：`F8C690444A4CDB75ED118FE1F40F6B9708E37A5B90AC4C8A3DEE06C89A508C6E`
- 正式版**仍是 v4.1.0**（EXE `3B045AA2…`），未被替换。

### 验证证据

- `test.ps1`：48 逻辑 + 15 IPC 全过。
- `AppearanceProbe`：8/8 通过；0/9/10% 两道刻痕双亮，20% 仅 20% 刻痕亮，21% 起全中性。
- **驻留测试**（`tools/design-sheet/probe-card-dwell.ps1`）：单格 85% PRO X，进入后第 5 帧（≈750ms 显示延时）出现卡，随后连续 **19 帧、跨 5 次 500ms 重绘，卡像素恒为 91407，零闪烁** → N3 修复成立。
- T 表重跑（`run-20261102-184449`，新 DLL）：玻璃 19/19 点亮，`medianLumaDelta=31.3`；卡 15/19（未抓到的 4 格是自动化进入时机，驻留测试已证明卡出现后不会被重绘打断）。
- 玻璃分线复测：100/85% gaming 72–73 vs office 61–62，符合 §4.2 的 α88/α66。

### N5/N6/N7 实施（同日第二轮）

| 文件 | 改动 |
|---|---|
| `src/Program.cs` | **N5** `ToggleSwitch` 重写：40×20 轨道 / ⌀14→⌀12 旋钮 / 标签左 + 轨道紧随（间距 12）/ rest·hover·pressed·disabled·focus 五态 / 焦点环改为 `Accent` 1.5 DIP 圆角环；构造改为 `ToggleSwitch(bool light)`，颜色全取 `Ui`。**N6** `PositionPicker` 重写：删除全部写死雾蓝，改为 token；选中胶囊 = 真胶囊镜像（identity 渐变 + 阈值数字 + 8.25pt）；ghost 槽 20×20 r6、hover/pressed 三态、焦点环；移除控件底部"位置：xxx"（值移到卡 B 右上 `pickerValue`），底部改为操作提示。**N7** `manager.AcceptButton=CancelButton=完成`。 |
| `tools/design-sheet/SettingsSheet.cs` | 跟随新构造函数 / `SetTheme(light)` / `Gaming` 属性。 |

**验证**：S 表重测（96-DPI 逻辑栅格）——轨道 **40×21 DIP**（原 35×17）、轨道起点 x=156（=标签宽+12）；禁用态与常态差 **940 px**、焦点态差 **163 px**（原各 60 px 抗锯齿噪声）；picker hover 差 415 px。设置窗候选实截 `E:\MouseBattery\design-sheet\settings\settings-candidate-20261102-185024.png`（846×793 物理像素，无绘制异常）。`test.ps1` 48 逻辑 + 15 IPC 全过。

### 仍未做

- **N5/N6/N7 已在本文件记录的第二轮修复中完成**（见上）。
- Q3/Q4 人判、Latte 12 格、降级横幅实截未完成。
- 晋升到 `E:\MouseBattery\app\` 需你批准；晋升前需先 `--exit` 正式版并按既有流程核对双哈希。

---

## themeOverride 实施记录（2026-11-02 第三轮，A 方案）

### 字段（`static_assert(sizeof(Snapshot)==512)` 保持不变）

```
tooltip[216] @48..479     原 232；尾部让出 32B
themeOverride @480        0=跟随宿主主题，1=强制 Latte，2=强制 Mocha
lastActiveTick @484       保留给"每设备新鲜度"（0=未知）
lastSuccessTickAny @488   保留（0=未知）
pad[5] @492..511
```

- `Read()`：`tooltip[215]=0`；`themeOverride` 越界一律归零。
- `Update()`：`light = override==1 ? true : override==2 ? false : ActualTheme==Light`；`Signature()` 已含 light，主题变化会触发一次样式刷新。
- `TaskbarPublisher.Publish(..., int themeOverride=0)`：tooltip 写入上限改为 `480-48-2=430` 字节；480 处写入 override。生产调用不传该参数，恒为 0。
- 混合版本：旧 EXE + 新 DLL —— 超长 tooltip（>216 字符）会落到 480，但被 0..2 校验挡下；新 EXE + 旧 DLL —— 旧读法强制 NUL，尾部被忽略。两侧均安全。

### 验证

- `test.ps1` 48 逻辑 + 15 IPC；`AppearanceProbe` 8/8。
- Latte 强制帧 12 格（`run-20261102-185439`，DLL `6A06D0FA…`）：`text=210,15,57`（RedLight）/`tickHigh=223,142,29`（YellowLight）证实 token 切换生效；玻璃 11/12 点亮，卡 8/12。
- **限制**：强制主题只改我方绘制色，任务栏真实底仍是深色。实测健康态文字对比度仅 **1.9**（Mocha 为 5.6）——这恰好证明"Latte 绘制 × 深色底"不能用来看可见性。**真浅色任务栏仍需一次真实浅色会话**，此项不是代码缺口。

### 产物（候选，未晋升、未提交）

- 原生 DLL：`6A06D0FA1F7D09BCCCF01AAA190AFF9DA7792865C133A545EA99880A6898BD8E`
- 候选 EXE：`E589774FEF79CF547EC5F47286A628924D95F87BF3FB035449CD22CA7AD3AE2F`
- 探针：`4D530F5AD8E12EA7B1A9A4D24D6511BF4C0CA8ADDDDC3171572BCD65E530C009`
- SheetHost：`0ADD2F469CA8E4EE5CD79107FC5FC98C6FA28A229FBA7F82F37EF2410BAC601D`
- 正式版仍是 v4.1.0（EXE `3B045AA2…`），未被替换。

---

## Latte 真实会话结果（2026-11-02 第四轮，晋升后）

环境：`AppsUseLightTheme=1`，任务栏底实测中位亮度 **219**（真浅色），12 格，跑表前后正式版均正常恢复。归档：`E:\MouseBattery\design-sheet\run-20261102-190215\`。

### L1（缺陷）浅色下阈值数字对比度不足

| 电量 | 数字 token | 实测对比度（对真实浅色任务栏） | 判定 |
|---|---|---|---|
| 100 / 85 / 21% | `Text` #4C4F69 | **3.03–3.06** | 40 DIP 大数字 ≥3:1，**勉强通过** |
| 20%（琥珀） | `Yellow` #DF8E1D | **1.65** | **不通过** |
| 10% / 0%（红） | `Red` #D20F39 | **2.80–2.84** | 低于 3:1 |
| 悬停玻璃 | identity α60/18（浅） | ΔL **−3.3%~−7.2%** | 低于设计自定的 8% 门槛（方向为变暗，符合预期） |

说明：对比度取"墨色均值 vs 底中位数"，含抗锯齿边缘，属**保守下界**；但 Yellow 与 Text 相差近一倍，相对劣势是确定的。

**影响**：浅色任务栏上，恰恰是"该充电了"的那一刻数字最难读——琥珀色在浅底上失去警示力。
**候选修法（需你定）**：
1. **浅色专用警示墨**：`WarnInk = Yellow ⊕ Text α55`（预合成约 `142,107,71`，估算对比度 ≥3.4），`CritInk = Red ⊕ Text α25`；深色不变。保持语义色，需改原生+托管各一处取色。
2. **数字保持 `Text`**，警示只交给玻璃底与两道刻痕。改动最小，但浅色下“数字本身不报警”。
3. 浅色下 20% 也用 Red（双阈值同色），语义退化，不推荐。

### L2（取证件缺陷，非产品缺陷）

- 卡出现判定阈值 30000 是按深色标的：浅色下卡与浅壁纸差异小，实测 4 格 `cardDIP=224×174.7`（卡确实出现）却被判 0。
- "玻璃点亮"只统计正 ΔL；浅色下玻璃变暗（负值），故 12 格全判未点亮。
- 修法：判定改用 `|ΔL|`，阈值按主题分别取（浅色 8000 / 深色 30000），并在 manifest 记录方向。
