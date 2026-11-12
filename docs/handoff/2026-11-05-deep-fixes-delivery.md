# 深查问题修复交付

日期：2026-11-05至11-06，Asia/Hong_Kong。用户授权“ok，你来解决吧”，范围为上一轮审查列出的7项问题。审查报告：[原始发现与证据](../proposals/deep-review-20261105.md)。不包含视觉重做、完整双模式恢复或未复现的线程/引用架构改造。

## 修复结果

| 审查项 | 改动与结果 | 证据/边界 |
|---|---|---|
| ST-1 默认混入旧DLL | build.ps1与build-probe.ps1必须显式传NativeDll；产品构建输出DLL路径、DLL/EXE完整哈希清单 | 缺参非交互构建退出1且不生成EXE；最终资源实际抽取为05B2… |
| ST-2 Raw Input吞错 | 移除空catch；Win32返回失败记录操作和错误码到RawInputError；瞬时名字查询失败不再永久缓存null | 正式运行RawInputError为空；非预期托管异常不再静默吞掉。保留最近错误直至进程重启，避免下一帧成功擦除诊断 |
| SP-1 半初始化 | 名称、Gaming、Feature、Unit只在全部发现步骤成功后提交 | feature/capabilities超时后同数据源能恢复，未再查询feature0 |
| SP-2 Bolt负缓存 | 空结果10秒后可重扫；接收器从候选列表消失时清理扫描缓存 | 睡眠→唤醒、同路径拔插模型均恢复；无每帧扫全部地址 |
| SP-3 Raw Input绑定 | 删除collection号作为配对索引的错误规则；Logitech vendor和PID下只匹配唯一UnitId；失败状态按完整vendor路径标记 | 本机C54D/C548真实路径探针均匹配；正式ActiveKey已从none变为02-02-5C-94。多只同PID仍不自动区分，歧义时返回null并用已有freshest显示策略，未声称全场景解决 |
| SP-4 有效期 | DisplayState在Apply成功时直接记录每条读数的GetTickCount64；快照使用被显示设备的LastSuccessTick | 另一设备刷新不改变活动设备tick；活动读数到期切换且使用另一设备自己的tick；不换算Stopwatch和系统tick，512字节ABI不变 |
| SP-5 托盘详情 | 单独TrayTooltip包含当前设备名、百分比、充电、连接和失败标记；NotifyIcon保留63字符上限 | 单元测试通过，原生首行仅设备名协议保持；没有切正式版到降级托盘做截图 |

特别说明：最初尝试把默认路径改到常用candidate目录，随即哈希核对发现那里也有旧DLL（3D82…），于是放弃默认路径方案，改为强制显式输入。该中间构建未运行、未注入、未部署。最终复用正式版已验证组件05B2…；没有修改本轮原生代码，不重编译或更换原生组件。

首次部署7EA3…使用模型时间到系统tick的固定偏移换算。最终复核考虑GetTickCount64通常10–16ms分辨率与Stopwatch不一致，可能产生短暂未来时间戳，因此替换为成功时直接记录系统tick，再次回归并干净部署E084…。未观察到这个潜在问题实际引发崩溃。分辨率依据 [Microsoft GetTickCount64文档](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount64)。中间候选、测试和两次部署记录均保留。

## 文件清单

- 产品：`src/Battery.cs`、`src/Program.cs`、`src/TaskbarPublisher.cs`。
- 构建：`build.ps1`、`build-probe.ps1`。
- 回归：`src/Tests.cs`、`src/PublisherTests.cs`、新增`src/RecoveryTests.cs`、`test.ps1`。
- 必要调用同步：`src/AppearanceProbe.cs`、`tools/design-sheet/SheetHost.cs`。删除旧全局MarkSuccess调用，模拟工具改用递增的模型时间；两种探针均编译验证。
- 交接：本报告与current.md。修复前入口完整保存在`2026-11-05-before-deep-fixes.md`。

既有原生profile、共享内存名字、堆修复和用户未提交资产均保留，未暂存、提交或push。

## 验证

所有本轮原始证据：`E:\MouseBattery\deep-fixes-20261105`。

1. `recovery-red.txt`：修改前9项恢复断言中5项失败；最终`tests-final.txt`：56逻辑+18 IPC+9恢复检查全部通过，新增系统tick不位于未来的检查。测试使用实际BatterySource并替换HID系统边界，不是物理睡眠/无线电实验。
2. `path-match-green.txt`：本机实际枚举C54D/C548的vendor/raw路径，两者返回physical-device；原审查对应结果均为NULL。实体双鼠标切换未验收。
3. `raw-canary.txt`：保留的UTF-16字节分配表达式从当前源码提取；5个真实Raw Input鼠标节点全部0越界字节、0失败。没有诱发堆损坏。
4. `missing-native.txt`：缺少显式NativeDll时安全失败。`build-final.txt`、`candidate-final/build-manifest.json`、`deployment-final.json`：最终产品构建成功，实际嵌入资源抽取哈希等于被验证的05B2…；本机taskbar.dll仍为A52F…。
5. `probe-final.txt`、`sheet-final.txt`：受API调整影响的AppearanceProbe和SheetHost对最终源码独立构建成功；没有运行模拟发布覆盖正式读数。
6. `mapping-cost-green.txt`：独立20000次匹配全部成功；约296B/次、10.823ms、0次Gen0；审查旧实现1432B/次、23.8059ms且0匹配。该数据仅为函数微基准，不能推导实体鼠标报告率或正式总CPU变化。
7. `deployment.json`及`deployment-final.json`：两次均优雅退出→等待进程退出和文件独占打开→复制校验→启动。最终23:59:23退出PID16716，启动PID11476；原正式版及中间版分别保存在before与before-final，无强杀覆盖。
8. `live-snapshot-final.txt`：最终真实共享快照state1、percent80、pid11476、age5688ms且无未来时间戳；`real-status-final.txt`：PRO X 2 DEX 80%、2.4G、RawInputError为空、原生visible 56×48 DIP。自动跟随在首个候选实测ActiveKey=02-02-5C-94；第二次启动后的快照暂为none，绑定源码未变化，不把重启后静止状态当回归或再次实际运动验收。
9. `post-deploy-events-final.json`：最终部署后至11-06 00:00:50没有匹配MouseBattery/Explorer的Application/1000/1001/1026记录。Explorer PID12264、启动时间11-05 21:41:51保持，未重启。
10. `ui-validation.md`、`ui-validation-final.md`：首个候选三轮设置打开/关闭通过；最终版UI代码未变，复用三轮并追加一轮冒烟通过。字段均为PRO X 2 DEX / 80% / 未充电 / 2.4G；最终窗口关闭、PID11476存活。截图仅CUA内联，不能提供不存在的持久截图路径。

## 正式资产

- `E:\MouseBattery\app\MouseBattery.exe`
- EXE SHA256：`E0849B7287DF8BDBF0B6D5D7C2632FB9BB62CDAFE0B77380C6AC4CDE2F992EC8`
- 内嵌DLL SHA256：`05B2DFD7AA161B5E61DC03DE22F6F929E4C7635D8A1A5AF722D0CF689FD272B1`
- Program.cs SHA256：`5187116F6609C99528CAC75C3C255E39ED83698C5564EE1886FF4356ACAA1ACF`
- 版本号仍4.2.1.0；以完整哈希识别交付，未混入历史双模式框架。

## 未完成与风险

- 设置窗口验证已完成，任务栏/悬停卡视觉仍未取得截图；原生visible/尺寸来自真实健康与布局状态，不能替代视觉验收。
- 同PID多个物理鼠标的parent/container关联仍未实现；现在只在唯一候选时绑定，不能称所有多鼠标自动跟随完整支持。
- 真实睡眠/隔夜唤醒、降级托盘悬停、健康冻结恢复、长时稳定性均未完成，不把故障模拟当实体验收。
- 原审查的UI线程等待、HID轮询隔离、原生引用协议、日志频率和完整双模式属于后续事项；未顺手改动这些高风险或低优先级路径。
