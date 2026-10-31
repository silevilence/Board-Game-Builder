# Editor 后端选择与视口重建

2026-10-31，Windows x64，保留 MonoGame。WindowsDX 在宿主默认上下文，DesktopGL 从维护者固定发行目录 `backends/desktopgl` 验证后进入独立上下文，隔离同名 `MonoGame.Framework`。游戏内容不能指定程序集路径。后端包使用与运行插件相同的清单、哈希、依赖校验与持有文件机制。

配置面板分别显示工程声明、宿主选择、实际激活的宿主以及试玩快照。修改入口不立即切换设备；“应用后端并重建视口”先取消输入并等待旧视口／宿主释放，再重绑所有本工作区视口。实例及其推进所有者保持不变，规则配置仍须显式重开试玩。未选择后端的既有普通工程保留已记录的 WindowsDX 宿主默认；NativeAOT 仍要求入口显式且唯一选择后端。

## 复核入口

先按 `third_party/MonoGame/build-patched.ps1` 构建固定补丁，再构建 Editor 和调试 Player。分别为两者生成独立后端目录：

```powershell
dotnet run --project tools/GraphicsBackendPack -c Release -- <new-host-directory>/backends/desktopgl
Editor.exe --verify-backends <new-output> <debug-player.exe> <external-delivery-fixture>
Editor.exe --verify-runtime-documents <result.json>
```

fixture 由 `tests/Runtime.PluginDelivery.Tests` 生成；设置独立 `BGB_RUNTIME_PLUGIN_STORE`。发行时后端目录必须进入 Player 的哈希清单；GL 工程普通导出预检要求完整后端包，不以仅有元数据冒充可运行。

## 验证结果

- 同一暂停实例的两个视口执行 DX→GL→DX→GL，实例引用和原快照不变；一次单步仅增加一个 tick。DX 设备和宿主在切换至 GL 后归零。
- 显式导入的 `sample.counter` 随同一实例推进，后端重建后的计数与驱动 tick 一致；独立 Player 从同一安装目录读取其精确身份。切换文档会取消未完成的图形重建，新工程不接收迟到修改。
- 共享窗口的冲突后端、重复后端选择均明确拒绝；取消后可重试。草稿与撤销保留，包括内容改回原值后的撤销。
- 独立通用应用 Player 使用同一工程 GL 选择，暂停与单步一致；独立桌游 Player 首帧完成后才接受握手，停止时先释放图形资源。
- 桌游 GL／DX 重绑保持对局和命令版本，中文标签正常；两个窗口关闭后 DX 设备、宿主计数均为零。
- 自包含 Minimal.Player 携带 GL 包运行两次，键盘推进样例、截图和关闭成功。插件导入测试在框架依赖和自包含进程各通过 26 项。
- Editor 文档回归，以及 `Engine.Tests` 的 documents、author-workspace、content-discovery、debug-play 通过；NativeAOT 作者预检回归通过；项目边界检查通过。
- Windows UI 实际点击配置面板的“应用所选后端并重建视口”，显示来源 `68E075669E0C`；随后开始应用试玩并正常退出。此操作使用独立测试工程，未保存原样例。

机器报告、真实界面和后端截图见 [evidence/editor-backends](evidence/editor-backends/)。干净 Windows 的联合发行验收、原生桌游发行与 NativeAOT 导出面板仍按后续任务执行。
