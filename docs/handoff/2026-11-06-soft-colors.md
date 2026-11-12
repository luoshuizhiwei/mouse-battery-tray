# 设置页软色调修订

用户要求“我更希望使用软色调”。2026-11-06 00:48:26已干净部署；仅调整`src/Ui.cs`新增的Settings颜色角色和`src/Program.cs`的对应颜色调用。上一版文件、EXE及交接入口保存在`E:\MouseBattery\soft-colors-20261106\before`。

- 明色：背景#F2F4F7、卡片#FAFBFD、正文#454E56、辅助#63707F、雾蓝灰强调#7E94AB、完成按钮#DCE5EF，按钮/选中文字#405B72。
- 暗色：背景#252C36、卡片#303A46、正文#D5DEE8、辅助#AAB8C8、强调#A7BBCC、按钮#42566A，按钮文字#D2E0EB。
- 原始Catppuccin基础色和原生/托盘角色保留；没有更改任务栏胶囊、警示阈值、系统主题、布局、读取逻辑或操作行为。

## 证据

统一目录`E:\MouseBattery\soft-colors-20261106`：

- `preview-render.txt`：实际设置页源码离屏渲染明暗×80/100/未连接6种模拟状态；人工查看明暗80%，颜色和文字可读。复用上轮预览驱动，12个逻辑缩放点击目标和左右键断言通过，AX值断言通过。预览PNG是模拟渲染，不是真机截图。
- `contrast.json`：按sRGB公式计算，明/暗正文对卡片8.19/8.49，辅助文字对背景4.59/6.97，按钮文字对按钮5.58/5.63；强调色对卡片3.02/5.84。这里只证明声明色值的名义对比，不替代实际显示器验收。
- `build.txt`、`candidate/build-manifest.json`：Framework C#构建成功；部署脚本抽取资源并确认内嵌DLL仍为05B2…。
- `deployment-final.json`：优雅退出47384→进程退出→文件独占打开→复制校验→启动47444。Explorer仍12264，未重启。
- `ui-validation.md`：实际窗口配色、数值与后台存活的原始CUA核对；截图仅内联。
- CUA确认AX/视觉均80%，电量条与开关雾蓝灰、完成按钮浅底深字，底部完整。辅助小字较浅但可辨；关闭后PID47444存活。核验后重新为用户打开设置页供查看，记录`final-user-view.json`。
- `events.json`、`real-status.txt`：短时无相关崩溃事件，真实80%且原生visible。不能扩大为长时/唤醒验收。

此次是颜色修改，未重复上一轮输入和行为未变的56逻辑+18 IPC+9恢复测试；新风险由新构建、6状态渲染、颜色对比及实机窗口检查覆盖。

## 正式资产

- EXE `E:\MouseBattery\app\MouseBattery.exe`，SHA256 `F48DBAEF58F35C7C83029735A0352A91EF79D96BED6B6BE57456F8B8EB311F79`。
- 原生DLL `05B2DFD7AA161B5E61DC03DE22F6F929E4C7635D8A1A5AF722D0CF689FD272B1`。
- Program.cs `8F30AC2926EB0540BD8FE1707D9A961EA3E5D2BF2C0F37C1376C595BB77C90EB`。
- Ui.cs `29706E27C08C6EEAB8B6FDEF3CFF72736947C8F049A44423D6F124F4C4670CDC`。

原有同PID精确关联、真实隔夜唤醒、实体DPI/暗色主题切换等边界不变。没有提交或push，没有改动baseline和dump。
