# 鼠标电量工具 — 当前交接

更新：2026-11-05，Asia/Hong_Kong。当前任务只有跨版本 0xC0000374 的 dump 定位和最小修复；视觉、系统适配及双模式开发不在本轮范围。

## 当前交付

- 源码起点：aa7d5e8（文档提交），产品代码回退 dcc8935；本轮未提交、未 push。
- 正式 EXE：`E:\MouseBattery\app\MouseBattery.exe`。
- 修复版 EXE SHA256：`6BAF0923939ABBEC1AF91F760B465D0EAD09DAD7C89D626226B0D4612FA7D4B5`。
- 内嵌原生 DLL：`B84D3691A3EEDD9EB8111DB782845F08D313809A3ACE851E628D24EEF6BD99A8`；从原正式 EXE 提取复用，未修改原生源码、未重建原生。
- 版本标记澄清：接手时实际正式文件为 4.1.0.0、SHA256 `3B045AA2AFD21995FD865A75E8118217CA880743FEAD579ED5F5A4830A2DF5DB`；回退源码标记为 4.2.1.0。历史“v4.2.2 基线”不是两者的准确文件版本。本轮保留源码版本标记，仅用完整哈希区分修复产物。
- 修复版于 16:47:39 启动，PID 49640；当前托盘降级显示 Signature Plus M750 L 85%、Bolt 接收器、RawInput=on、Startup=True。PID/读数是本次取证快照。
- Explorer PID 30384，11:37:00 启动；本轮未重启 Explorer，未改系统主题、通知图标注册表或自启项。

## 已由 dump 证据确认的根因

`HandleRawInput()` 读取 RIDI_DEVICENAME 时，pcbSize 单位是 UTF-16 字符；原代码按 `size+16` 字节分配，却向 API 声明 `size+16` 字符容量。

四份 full dump 都显示 `heap_failure_multiple_entries_corruption`，损坏块头含同一 Raw Input 鼠标接口 GUID `{378de44c-56ef-11d1-bc8c-00a0c91405dd}` 的 UTF-16 片段。18356 的 rawNames 缓存保留完整 94 字符路径；所需 190 字节，逻辑分配仅 111 字节，LFH 对齐后用户区 112 字节。下一块头恰在路径第 56 字符 `{` 开始处被覆盖。四份的发现线程/释放者不同，不能把 CLR、输入法或 BoltIndices 的释放栈当作写坏源。

准确的栈、地址、字节、LFH 结构、取证边界和触发机制见 [根因报告](../proposals/heap-corruption-rca.md)。原有交接中“已关闭”“双模式引入”“系统更新/CIG”“13 次都是部署覆盖”等互相冲突的归因全部属于历史，不再作为本轮结论或当前授权。干净部署纪律仍保留。

## 改动范围

- `src/Program.cs:347`：唯一产品逻辑变动为 `AllocHGlobal(checked(((int)size+16)*sizeof(char)))`，加单位说明。未改 HID、Raw Input 注册/缓存策略、快照 ABI、模式选择、视觉或注入生命周期。
- `tools/Test-RawInputNameBuffer.ps1`：新增真实 API canary 检查，从产品源码提取分配表达式，物理存储足够大，安全观测逻辑边界越界，不诱发真实堆破坏。
- 本根因报告、历史交接快照和当前交接。

## 验证与原始证据

统一目录：`E:\MouseBattery\heap-rca-20261105`。

- 4 个原 dump 完整保留，哈希见 `dump-sha256.txt`；微软 DbgEng + PDB + Framework SOS 的命令行输出为 `*-analysis.txt`、`*-heap-info.txt`、`*-blocks.txt`、`18356-path-proof.txt` 等。
- 修改前 `canary-before.txt`：4 个实际鼠标设备全部越界，退出码 1；与 dump 相同的 C548 路径正好超出 79 字节。
- 修改后 `canary-after.txt`：4/4 无越界，退出码 0。
- `regression.txt`：`powershell -File test.ps1`，48 逻辑 + 15 IPC 全过，退出码 0。
- `build.txt`：`build.ps1 -NativeDll <显式复用的 DLL>` 构建成功；`embedded-native.json` 证明正式内嵌 DLL 与原文件一致。
- `graceful-exit-before.txt`、`deployment.json`：--exit→等待原进程结束→独占打开文件成功→复制校验→启动，无强杀覆盖。新任务目录 `formal-before.exe` 保存接手产物；未触动已有 baseline 备份。
- `post-deploy-status.txt`、`process-after.json`、`alive-observation.json`、`post-deploy-errors.json`：真实读数、同一后台进程、短时存活观察；查询起点 16:47:38，记录范围内无新的本工具 Application/1000、1001 错误。
- `ui-three-rounds.md`：GPT-6 Luna 已完成 3/3 设置窗口打开、读取真实信息、Alt+F4 正常关闭及同一 PID 存活，最终窗口已关闭。先前两次坐标映射错误保留，不计入通过；实际托盘图标截图仍未取得。

## 未完成与停止点

1. 托盘图标自身的实际截图未取得，不能将日志或设置窗口推为该图标像素验收。真实隔夜/睡眠唤醒验证未执行：不为测试擅自使用户电脑睡眠、切换主题或插拔设备。此次修复是对确定的越界写的修复，不能扩大成长期稳定性已关闭。
2. 任务栏内嵌仍因回退版的当前系统指纹不适配而自动托盘降级，本轮未恢复 5492525/fa76eb6 的双模式框架或 S5 profile。先完成当前验证，再另行评估恢复；不要以“堆损坏已解决”推导“任务栏适配完成”。
3. 普通 WER dump 无 UST/PageHeap 历史分配栈；本轮以块内容、LFH、缓存路径和真实 API 红→绿实验锁定写入源，不伪造历史 alloc/free 栈。

历史设计、历次推翻及恢复参考见 [修复前交接原文](2026-11-05-before-heap-corruption-rca.md)。四个 dump、已有 baseline、参考 Git 提交和 S5 资产均保留。
