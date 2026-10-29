# 鼠标电量工具 — 当前交接

更新：2026-10-30，Asia/Hong_Kong。当前正式交付 v3.2.0，独立应用，“雾蓝”主题 + 指纹失效降级托盘。

## 当前结果

用户授权按健壮性、资源与性能、功能增强（指纹失效降级托盘）、轻整理四个方向优化。全部改动仅在托管 C# 侧；原生 `native/NativeHost.cpp`、`native/TaskbarView.h` 未改动，内嵌原生 DLL 与 v3.1 完全相同。

- 正式 EXE：`E:\MouseBattery\app\MouseBattery.exe`。
- EXE SHA256：`9A6E35DE1593058DA5330E144A8C6C755C1F7A12F437A645FC75C989808B04C4`。
- 内嵌原生 DLL SHA256：`4F157C4598657842A8FD98D3E3D99E269760385AE716FE73C637723C0A3D5A08`（未变）。
- 本轮目录：`E:\MouseBattery\robust-v3.2`（`candidate`、`degraded-probe`、`baseline\MouseBattery-v3.1.exe`、`appearance-probe.txt`）。
- 真机实测 78%、无线、未充电、位置 0、登录自启开启；Explorer 未重启。
- taskbar.dll 指纹 `CA1E335A…0741` 不变；v3.1 交接见 [v3.1 历史](2026-10-29-taskbar-v3.1.0.md)。

## 本轮改动

- `src/Program.cs`：① `Poll()` 由裸 async void 改为整体 try/catch，意外异常写入既有 `failure` 错误面，不再可能击穿常驻进程；② `Startup.Enabled` 结果缓存于 TrayContext，250ms Tick 不再每次开注册表键（所有改动路径经 `SetStartup()` 刷新缓存，托盘/原生菜单 toggle 仍读实时注册表决定目标值）；③ 降级托盘：`NativeHost` 构造抛 `NotSupportedException`（指纹/profile 不匹配）或运行中 `Maintain()` 抛同异常（Explorer 侧热更新场景）时转入降级——创建 `NotifyIcon`（`TrayIcon` 渲染，仅在电量变化时重建图标）、右键菜单（打开设置/退出工具）、双击开设置、气球提示一次、`status.txt` 记录 `Native=降级托盘（…实际哈希…）`；降级模式跳过 Publish 与 Maintain，并经 Tick 节流重试把 shell 已建的 `HKCU\Control Panel\NotifyIconSettings\<…>` 条目写为 `IsPromoted=1`（按 ExecutablePath 匹配，最多尝试 10 次每次 2 秒），用户已确认选择自动提升。
- `src/Hid.cs`：`Native.Enumerate(List<string> rejected)` 单台设备 `CreateFile`/`HidD_GetAttributes`/`HidP_GetCaps` 失败改为跳过并记录，不再让一台被占用设备拖垮整轮枚举；setupapi 级失败仍抛出。
- `src/Battery.cs`：`Poll()` 把 rejected 并入最终 `LastError`。
- `src/NativeHost.cs`：`Dispose()` 生命周期日志写入包 try/catch，data 目录被占用不阻断退出清理。
- `src/TrayIcon.cs`：新增无读数占位（`--`，正常色不入阈值红/黄）。
- `src/Tests.cs`：新增 3 项占位渲染检查；`src/Probe.cs` 调用点同步签名。
- `build.ps1`：sources 增加 `TrayIcon.cs`；新增 `-NativeDll` 参数（默认指向 v3.1 原生 DLL `E:\MouseBattery\appearance-v3\MB-UI-02-v1\MouseBattery.Native.dll`），不再隐式依赖候选目录里的旧 DLL。
- `build-probe.ps1`（新增）：AppearanceProbe 编译脚本化，内置 `/resource:<原生DLL>`；手动编译漏掉该资源会因 `GetManifestResourceStream` 返回 null 空引用（本轮实测确认）。
- 版本号 3.2.0.0；README 同步降级行为。

## 评估后明确不做

- 共享内存发布去重：MMF 写入是内存操作，原生侧本就按自身 500ms 节奏重渲染，去重无实际收益且要动已验证 IPC 文件。
- HID 句柄复用：当前每轮开关句柄对设备休眠/重插更鲁棒，收益约 1-2ms/5 秒。
- 250ms Tick 降频：稳态开销可忽略，降频只拖慢菜单响应。

## 验证及准确边界

- `test.ps1`：36 项逻辑回归（33 项既有 + 3 项占位新增）+ 10 项 IPC 全部 PASS。
- `appearance-probe.txt`：8 项真实原生渲染（模拟值 0/9/10/20/21/86/100/旧值），位置 0、56×48 占位、雾蓝配色、阈值色、旧值 state=2 全过。探针需以 `/resource:<原生DLL>,MouseBattery.Native.dll` 编译，否则 `GetManifestResourceStream` 返回 null（本轮实测确认该失败形态，产品构建不受影响）。
- 降级链路（假指纹探针候选，全零 SHA256 常量）：真实读数 78% 正常、`status.txt` 出现 `Native=降级托盘（…CA1E335A…0741）`；UIA 实测托盘按钮出现在任务栏可见角且名称带真实电量；`NotifyIconSettings` 条目 `IsPromoted=1` 自动写入；注册表 Run 项全程未被动过；`--exit` 干净退出。降级模式无 `native-lifecycle.txt` 属预期（从未 attach）。
- 真机验收正式候选：默认位置显示真实 78%、第二实例 `--manage` 经单实例信号唤起设置窗口（UIA 控件树完整：设备/电量/位置/自启/按钮）、关闭窗口后台续跑、`--exit` DetachResult=0，随后已重新启动正式版并确认 `Startup=True`、真实电量恢复显示。
- 指纹常量已还原并以最终源重建，`9A6E35DE…` 为含全部改动的最终产物（早期候选 `CE88AB3A…` 不含自动提升，作废）。

已知边界：Win11 shell 对托盘按钮的 UIA 名称会把首次注册提示与当前提示拼接（形如“鼠标电量不可用 PRO X 2 DEX · 电量：78%…”），悬停 tooltip 为实时值，属 shell 命名习惯非功能缺陷；自动提升依赖 shell 先创建 NotifyIconSettings 条目（实测即时创建，仍保留 10 次重试）。`Startup` 状态语义不变：表示“自启项是否指向当前 EXE”，路径不同即 False。多显示器、真实重登录、物理拔插、Explorer 重启恢复仍未验收，底层未改动的旧证据保持原范围。

## 恢复与后续

v3.1 EXE 备份于本轮目录 `baseline\MouseBattery-v3.1.exe`。源码已于 2026-10-30 建立 Git 基线（v3.2.0 首次提交），此后每轮改动有 diff 可审、可回退。下一步：Windows 更新导致指纹失效时，工具会自动降级并显示真实哈希，届时按该哈希适配 `NativeHost.cs` 常量与 `NativeHost.cpp` 的 PE profile（TimeDateStamp/SizeOfImage/CheckSum/偏移）；降级不是长期形态。托盘 UIA 名称拼接如需修正需另查 shell 命名来源，非必需。
