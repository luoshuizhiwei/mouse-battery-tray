# 0xC0000374 根因分析与最小修复

日期：2026-11-05，Asia/Hong_Kong。源码起点：HEAD aa7d5e8（文档提交），产品代码回退提交 dcc8935。本文根因部分在修改产品代码之前写入；后续验证单独追加。

## 已确认根因

`src/Program.cs` 的 `HandleRawInput()` 读取 `RIDI_DEVICENAME` 时，把 `GetRawInputDeviceInfoW` 返回的 **UTF-16 字符数** 当成 `Marshal.AllocHGlobal` 的 **字节数**。

原始源码位置：分配行 346，原生写入行 349，读取行 350，释放行 351（原件见 `E:\MouseBattery\heap-rca-20261105\Program-before.cs`）。源码关键片段：

```csharp
uint size=0;
GetRawInputDeviceInfoW(hDevice,0x20000007,IntPtr.Zero,ref size);
var buf=Marshal.AllocHGlobal((int)size+16); // 错：按字节分配
uint cap=size+16;                        // API 却按 WCHAR 容量处理
GetRawInputDeviceInfoW(hDevice,0x20000007,buf,ref cap);
```

微软 API 文档明确指出，只有 `RIDI_DEVICENAME` 的 pcbSize 单位是字符而非字节：
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getrawinputdeviceinfow

这不是双重释放或已证明的并发竞态：Windows 按调用者声明的字符容量完成合法写入，但本程序实际分配不足。`cap > size+16` 检查比较的是字符数量，既不会发现字节单位错误，也不能撤销已发生的写入。后续 `FreeHGlobal` 或任意线程的其他堆操作才发现损坏。

## 四个 dump 的共同证据

原始 dump 全部保留在 `E:\MouseBattery\dumps`，SHA256 清单见本轮目录 `dump-sha256.txt`。通过微软官方 DbgEng 10.0.29547.1002、微软公共 PDB 和本机匹配的 Framework SOS 读取；无运行进程附加、无系统设置修改。工具/原始输出均在 `E:\MouseBattery\heap-rca-20261105`。

四份 dump 的 `ntdll!RtlpHeapFailureInfo` 都是：Version=2、FailureType=4，即 **heap_failure_multiple_entries_corruption**。统一检测栈为：

```text
ntdll!RtlReportFatalFailure+0x9
ntdll!RtlReportCriticalFailure+0xa9
ntdll!RtlpHeapHandleError+0x12
ntdll!RtlpHpHeapHandleError+0x7a
ntdll!RtlFreeHeap+0x287
```

| dump PID | 故障 OS 线程 | 被破坏的堆/块头 | 发现损坏的释放路径 |
|---|---|---|---|
| 18356 | 0x8368，UI 线程 0 | heap 0x01840000 / address 0x019360b0 | CLR ReferenceIdentity/AssemblyName，资源加载 |
| 44392 | 0x1ed8，UI 线程 0 | heap 0x012f0000 / address 0x013e29b0 | KERNELBASE FindFirstFile / CLR Fusion 资源枚举 |
| 48100 | 0xba54，线程 10 | heap 0x01610000 / address 0x016ede80 | CLR 类型加载器释放临时对象；上层 BatterySource.BoltIndices/Dictionary.Insert |
| 15848 | 0x4794，UI 线程 0 | heap 0x00aa0000 / address 0x00b78130 | wetype_tip 释放；上层 msctf 激活输入法 / WinForms MessageBox |

因此，四份 dump **没有同一个故障线程或同一个上层释放模块**，不能强行制造这样的结论。共同点是本进程堆元数据被 UTF-16 鼠标接口路径覆盖，而 CLR、文件枚举、输入法只是不同的发现者。第三方模块未公开私有符号，wetype_tip 只保留模块/RVA定位，不能把最近的导出名当成真实函数名。

证据文件：`18356-analysis.txt`、`44392-analysis.txt`、`48100-analysis.txt`、`15848-symbols.txt`（所有线程栈与托管栈）；`*-heap-info.txt`、`*-blocks.txt`（堆失败结构、原始字节）；`18356-path-proof.txt`（对象及 LFH 结构）。

### 可复核的逐字节边界（18356）

SOS 从 `TrayContext.rawNames` 的 entries 中找到了已缓存字符串对象 0x045b97b8，m_stringLength=94：

```text
\\?\HID#VID_046D&PID_C548&MI_01&Col01#7&22300789&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}
```

这是 **Raw Input 鼠标接口 GUID 378de44c-56ef-11d1-bc8c-00a0c91405dd**，不是 SetupDi 枚举使用的 HID 接口 GUID。缓存仍保存完整路径，证明原生写入/PtrToStringUni 已执行完并离开写入现场。

- 含 NUL 所需 95 WCHAR = **190 字节**。
- 原代码逻辑分配 `95+16` = **111 字节**，却向 API 声明容量 111 WCHAR。
- 路径起点 0x01936040；相邻被破坏的块头 0x019360b0，相差 **0x70=112 字节=56 WCHAR**。
- `dt _HEAP_USERDATA_HEADER 0x019353f0` → SubSegment 0x01905340；`dt _HEAP_SUBSEGMENT 0x01905340` 给出 BlockSize=8，即块跨度 8×16=128 字节，扣 16 字节堆头，用户区 112 字节，与对齐后的 111 字节分配吻合。
- 0x019360b0 的原始字节 `7b 00 33 00 37 00 38 00 64 00 65 00 34 00 ...` 恰为路径第 56 个字符起的 **`{378de4...`**，直接覆盖下一块堆头。
- 44392、48100 的对应块头也保留同一 `{378de4...`；15848 的块头/后续区被后来的对象复用，但仍残留 `de44`、`-1`、`c8c` 等同一 GUID 的 UTF-16 片段，不能将已复用区域声称为完整路径。

普通 WER dump 没有启用 UST/PageHeap 的历史分配栈，不能恢复过去的每次 alloc/free 调用栈。这里用幸存的完整托管缓存、LFH 块尺寸、覆盖偏移、API 单位契约和真实 API canary 实验共同锁定分配/写入位置，未伪造历史写入栈。`!heap`/`!htrace` 扩展不在取得的引擎包内，使用 PDB 的 dt/dq/db/SOS 代替。

## 修改前真实 API 实验

`tools/Test-RawInputNameBuffer.ps1` 从产品源码提取实际分配表达式；使用真实 GetRawInputDeviceList/GetRawInputDeviceInfoW 查询当前鼠标。实验分配足够大的物理存储，只在原逻辑边界后布置 canary，以观察超出逻辑分配的写入，**不实际损坏任何堆元数据**。

原源码运行结果：`canary-before.txt`，退出码 1，4/4 鼠标设备都破坏 canary。与 dump 完全相同的 C548 路径结果：

```text
requiredChars=95 logicalBytes=111 pathChars=94 pathBytesWithNul=190
overwrittenBytesBeyondAllocation=79
```

这排除了单凭时间段或上层崩溃栈猜测根因的做法。

## 为什么跨版本、为什么容易在唤醒/重连时出现

Git 的 e4e4624（4.2.0）、32281fe（4.2.1）、5492525（4.3.0）、fa76eb6 及当前回退源码都含相同的字节/字符混用分配。四份现存 dump 自身来自 4.4.1.0，不能把它们说成四个不同版本的 dump；跨版本结论结合用户既有事件日志与 Git 共有缺陷。

`rawNames` 以 hDevice 为键缓存名称：首次收到某设备输入，或重连获得未缓存的句柄时，才走该分配分支。因此启动、唤醒/重连后的第一批输入容易触发。**不需要等待隔夜、也不需要 discover 并发**：48100 的进程存活仅 9 秒便在另一个线程发现先前写坏的堆；15848 存活约 2 分 40 秒才由输入法释放发现。dump 不含完整电源事件链，不能单凭这四份断言当时确实发生了系统唤醒，更不能把“早晨”本身当作因果。

## 修复范围（修改前确定）

只将 Raw Input 名称分配换成与现有字符容量对应的字节数：`checked(((int)size+16)*sizeof(char))`，保留既有容量余量、查询/缓存/释放行为。无视觉、HID I/O、快照 ABI、原生加载或模式恢复改动。原生 DLL不需重建，候选显式嵌入现正式 EXE 中提取的同一 DLL并核对哈希。

当前实际部署文件版本为 4.1.0.0，回退源码版本标记为 4.2.1.0（与“4.2.2 稳定基线”的口头标记不同）；二进制和源码基线分别取证，不伪称两者完全相同。源文件修复以 aa7d5e8/dcc8935 为基准。

## 实现与验证结果

1. 产品仅更改 `src/Program.cs:347` 的分配表达式，并增加一行单位说明。原生写入现位于第 350 行；其余流程不变。新增上述 canary 工具，不改快照 ABI、HID、视觉、系统配置或双模式框架。
2. 原表达式实验退出码 1、4/4 越界；修复后的 `canary-after.txt` 退出码 0、4/4 无越界。C548 现在分配 222 字节，真实 API 写入含 NUL 共 190 字节，逻辑边界后的 canary 全部完整。
3. `powershell -NoProfile -File test.ps1`：48 逻辑 + 15 IPC 全过，退出码 0，日志 `regression.txt`。
4. `powershell -NoProfile -File build.ps1 -OutputDirectory E:\MouseBattery\heap-rca-20261105\candidate -NativeDll E:\MouseBattery\heap-rca-20261105\candidate\MouseBattery.Native.dll`：成功，日志 `build.txt`。未改原生，不重建原生 DLL；从接手正式 EXE 提取并复用，`embedded-native.json` 核对一致。
5. 正式 EXE SHA256：`6BAF0923939ABBEC1AF91F760B465D0EAD09DAD7C89D626226B0D4612FA7D4B5`。内嵌 DLL SHA256：`B84D3691A3EEDD9EB8111DB782845F08D313809A3ACE851E628D24EEF6BD99A8`。
6. 按 `--exit` → 等原 PID 退出 → 轮询独占打开 EXE → 复制并校验哈希 → 启动部署；没有 Stop-Process -Force，没有覆盖运行中的 EXE。`deployment.json`、`graceful-exit-before.txt` 保留过程。
7. 16:47:39 启动修复版 PID 49640，真实 M750 L 85%、Bolt 接收器、RawInput=on、Startup=True。原生任务栏仍因当前指纹未适配而降级托盘；没有借本轮恢复适配或双模式。
8. GPT-6 Luna 通过 CUA 完成 **3/3 设置窗口打开→读取真实设备信息→Alt+F4 正常关闭→同一 PID 存活**。过程中的两次坐标映射失败保留，不计为通过。最终设置窗口关闭、后台恢复原态，报告 `ui-three-rounds.md`。工具内联取得过真实设置截图，未另存 PNG；**未取得托盘图标自身截图，不能声称其像素可见性已验收**。
9. `historical-events.json` 本轮重读事件日志，确认 16 条 c0000374 覆盖四个版本；另外两条 c0000005/c000041d 不混入本异常的已证实归因。修复后的短时窗口无新的 MouseBattery Application/1000、1001 错误，见 `post-deploy-errors.json`。它不是隔夜稳定性证明。

## 未完成项与风险

- 尚未执行真实系统睡眠/唤醒、接收器物理重连或隔夜观察；本轮未擅自改变电源、主题、注册表或重启 Explorer。已证明的写越界已修复，**长期稳定性观察仍开放**。
- 托盘图标本身的实际截图未单独确认；已确认真实状态读数、设置内容、后台存活和原有降级路径。
- 不恢复 5492525/fa76eb6 的双模式框架，也不改 S5 profile。若后续恢复，只移植本修复并重新检查该分支的容量单位，遵守同样的干净部署纪律。
- 所有原 dump、已有 baseline 和 S5 资产保留；未提交、未 push。修复前实际正式 EXE 的额外取证副本仅在本轮新目录 `formal-before.exe`，不修改既有备份。
