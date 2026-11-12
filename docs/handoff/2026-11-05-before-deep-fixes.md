# 鼠标电量工具 — 当前交接

更新：2026-11-05，Asia/Hong_Kong。用户本轮明确要求恢复任务栏内嵌显示；已完成恢复并部署。之前“本轮只修堆损坏、不恢复任务栏”的范围已由本次请求扩展，堆损坏修复保留。

## 当前正式交付

- EXE：`E:\MouseBattery\app\MouseBattery.exe`。
- EXE SHA256：`70AE1CAD10B301AEA7C524755A98D175B058C6DDCCE835768ACE6E29EFFFC7D0`。
- 内嵌 DLL SHA256：`05B2DFD7AA161B5E61DC03DE22F6F929E4C7635D8A1A5AF722D0CF689FD272B1`。
- 版本号仍为回退源码的 4.2.1.0，按完整哈希识别本次交付；未顺手恢复整个历史双模式框架。
- 18:38:27 启动 PID 48676；真实 Signature Plus M750 L 电量 85%、Bolt 接收器、RawInput=on、Startup=True。
- `Native=initialized | visible position=0 percent=85`，实际 XAML 56×48 DIP 占位，位于系统托盘与时钟区域左侧；当前没有进入降级托盘路径。
- 本轮目录：`E:\MouseBattery\taskbar-restore-20261105`，含候选、失败证据、符号解析和部署记录。此前成功的堆修复版本额外保存于本轮 `before`，未触动既有 baseline 或 dump。

## 恢复显示所需修复

1. 托管端仍校验旧 taskbar.dll SHA256 `CA1E…`，而本机已为 `A52F99F7BE8CC1EE360025D1C06C1480A489B1D309BEF230C05EB21A106CCF34`。更新为已验证的当前完整指纹，继续保留严格版本守卫。
2. 跨端对象名不一致：托管发布 v2，但原生读取 v4；托管健康读取 v3，但原生写 v4。将原生恢复为发布端的 `Local\MouseBatteryTaskbar.v2` 和 `Local\MouseBatteryTaskbar.NativeHealth.v3`，未改 512 字节快照 ABI。`tools/SnapshotDump.cs` 同步名称，避免诊断误报。
3. 旧 S5 将 `CTaskListWnd::GetHost`（0xF9C40）误认为所需的 `CTaskBand::GetTaskbarHost`。原 Windhawk 源码需求是后者，PDB 完整符号解析给出正确入口 **0x1237B0**，已修正。其余为 FrameHeight=0xB10A4、Decref=0x290B0、CTaskBand/ITaskListWndSite vtable=0x1CFA30。
4. 修正 3 个符号脚本的 SYMBOL_INFO ABI 使用：Name 在偏移 84，不是带尾部 padding 的 sizeof=88；SizeOfStruct 必须是结构尺寸，不是含字符串缓冲的总尺寸。vtable 查询还修复了 RVA 未加基址、读取未回填的 NameLen=0 两个问题。新解析结果保存在 `taskbar-symbols-corrected.txt`、`profile-symbols.txt`、`vtable-check-corrected.txt`。

## 必须保留的失败记录

最初只对齐版本和通道、沿用旧 S5 getter 的候选，在 18:29:40 触发 Explorer 的 **0xC0000005，Taskbar.dll+0xF9C63**，是错误 GetHost 内部。Explorer 随后自动恢复到 PID 21068（18:29:53 启动）。本轮没有主动强杀或重启 Explorer，但不能写“Explorer 从未重启”。后台当即恢复已修复的托盘版，停止部署错误候选。

随后按完整 PDB 符号和函数体证据修正 getter，8 项实际原生渲染检查全部通过，再部署正式版。错误事件和候选分别保留在 `probe-errors.json`、`failed-candidate`，不能删除失败痕迹或继续引用旧“G1 4/4”的通过声明。

## 验证

- `native-build-fixed-getter.txt` / `build-fixed-getter.txt`：Clang C++23 与 Framework C# 构建成功，显式内嵌本次 DLL。
- `appearance-probe-fixed.txt`：8/8 真实 XAML 渲染检查通过（输入为明确的模拟 0/9/10/20/21/86/100/100旧值）；各自电量、颜色、刻痕、56×48 占位正确；结束 DetachResult=0。不是只检查 EXE 侧 initialized。
- `real-status.txt`：正式版真实 85%，原生实际布局 visible，x=1336、width=56、height=48 DIP。
- `snapshot-final.txt`：共享快照 state=1、percent=85、readerPID=48676、pos=0、startup=1，与实际原生布局一致。
- `modules-final.txt`：本项目正式 DLL 确实在 Explorer 中加载；探针 DLL 因现有引用策略也保持映像驻留，但探针 worker 已正常停止、布局已移除。未为卸载该映像重启 Explorer。
- `deployment.json`：优雅退出→等待文件可独占打开→复制→校验→启动，无强杀覆盖。
- `post-deploy-errors.json`：修正后正式部署窗口内无新的 MouseBattery/Explorer Application/1000 或 1026 错误；此前失败单独保留。
- 48 逻辑+15 IPC 及 4 设备 canary 复用堆修复轮充分证据：这些测试的输入文件未改，Program.cs SHA256 仍为 `A7CD896D49D8BB94CB7BD8CC85B11C4B1B7282854F1273D13E48F3A5C7BB5F8B`。新增风险由新的原生 8 项检查和真机布局证据覆盖，未无意义全量重跑。

## 范围与剩余风险

- 堆损坏根因、红绿实验及 3 轮设置验收见 [RCA](../proposals/heap-corruption-rca.md) 和 [上一交接](2026-11-05-heap-fix-delivery.md)。修复表达式保持 `AllocHGlobal(checked(((int)size+16)*sizeof(char)))`，不得回滚。
- 仅适配当前 taskbar.dll 完整指纹，系统再次更新需按准确符号和原生检查重新适配。
- 实际任务栏截图、真实隔夜/睡眠唤醒、Explorer 外部重启后的自动恢复、完整双模式偏好切换未在本轮验收；不可扩大为全链路稳定性关闭。
- 不恢复/重写整个 5492525/fa76eb6，未改视觉、系统主题或 NotifyIconSettings，未启动 Windhawk、未提交或 push。

## 2026-11-05 深度审查（尚未实施优化）

用户后续要求多轮深查。完成数据恢复、生命周期/资源、设置UI、反证/多设备协议四轮，报告见 [深度审查](../proposals/deep-review-20261105.md)，原始探针、构建抽取与采样见 `E:\MouseBattery\deep-review-20261105`。

- 确认：部分发现失败会留下半初始化 Entry；Bolt 空扫描永久缓存；本机 C54D/C548 vendor/raw 路径在现有匹配函数返回 null。均有独立探针及必要正对照。
- 确认：默认 build.ps1 嵌入旧 DLL（本轮仅构建抽取，未运行）；多设备的快照 tick 是任意设备成功时间，不能准确表达被显示读数的新鲜度；降级托盘提示缺少当前设备的详情。Raw Input 的全量空 catch 另为诊断规范缺口。
- 没有修改产品源码或部署，没有重启 Explorer。正式 EXE SHA256 仍为上文 `70AE…`，Program.cs 仍为 `A7CD…`，之前堆修复与任务栏恢复全部保留。
- 本轮最后核对 23:40:26：后台 PID32592、Explorer PID12264；真实 PRO X 2 DEX 80% / 2.4G，原生 initialized/visible 56×48 DIP。与上文18:38交付记录是不同时间快照；进程后续重启原因未查明。18:38:27起至该次核对没有匹配的 Application/1000/1001/1026 错误，不能扩大成隔夜稳定通过。
- 设置只读检查未确认按钮裁切；结束已关闭窗口且后台存活。未做实体唤醒、双鼠标实际切换、托盘悬停及健康冻结恢复验收。
- 下一步先确保构建资产确定，再修恢复/映射及有效期。上述优化仍未完成；报告不能当作修复交付。
