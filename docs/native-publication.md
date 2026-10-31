# NativeAOT 发布与外部运行插件

首轮目标是 Windows x64。NativeAOT 是可选作者导出方式，普通预编译 Player 仍可独立使用。当前联合验收进度见 [发行验收记录](validation/native-delivery.md)；未通过的 ROADMAP 条目不因本文存在而视为完成。

## 三种导出方式

| 作者插件 | 操作 | 产物 | 作者电脑前提 | 玩家电脑前提 |
| --- | --- | --- | --- | --- |
| `author.export.package` | `project.pack` | `.bgm` 数据包 | 匹配的作者运行目录 | 另有匹配 Player 和显式安装的运行插件 |
| `author.export.player` | `player.export` | 预编译 Player 独立目录 | 匹配的完整 Player 发行目录；不需 SDK | 随包宿主及依赖 |
| `author.export.native` | `native.export` | 专用 `Play.exe`、内容及所选原生库 | 固定原生运行 SDK、.NET SDK、C++ 与 Windows SDK | Windows x64；不需系统 .NET、SDK、Editor 或源码 |

三者分别注册、启停和保存配置。停用一种不会改写另外两种，也不会删除历史参数。旧作者 profile 中的 `author.export` 应显式改为所需提供者；不要修改游戏入口来迁移作者导出方式。Editor 的同名导出协调面板仍是通用发现 UI，不是一种发行格式。

游戏运行启用只写在 `game.start.json`。`author.profile.json` 选择作者插件；发布参数选择宿主、目标、工具链和输出位置，不能成为第二份运行插件清单。所有导出消费固定 `ProjectView`，Editor 包含当前草稿，校验、试玩和导出不隐式保存源工程。

## 已验证范围与拒绝边界

| 能力 | 普通 Windows x64 | Windows x64 NativeAOT |
| --- | --- | --- |
| WindowsDX | 支持 | 拒绝；没有原生支持声明 |
| 固定 MonoGame DesktopGL 补丁 | 支持，宿主安装 GL 包 | 严格构建及干净 Windows 图形验证 |
| 小核心实时组合 `application.desktop` | 支持 | Motion 实证；不引用桌游契约 |
| 显式命令桌游 `board.desktop` | 支持 | 无脚本、外部 CPU、WASM 及两者组合实证 |
| MoonSharp 2 Lua 规则／CPU／流程 | 现有普通路径保留 | 当前不支持；真实原生工作进程失败，不回退 `dotnet` |
| Wasmtime 48.0.2 | 固定整数 ABI | 同一有限 ABI；携带固定 `wasmtime.dll` |
| 任意外部 .NET DLL | 须显式导入且通过目录检查 | 须声明目标兼容，依赖检查、严格编译及实际运行均通过 |
| Linux、macOS、ARM64 | 不由本轮扩大承诺 | 未验证，不允许借改 RID 宣称支持 |

Wasmtime 可以在原生执行器内部 JIT 编译 WASM；`.NET NativeAOT` 不等于整个进程禁止 JIT。当前 WASM 没有 WASI、文件／网络 imports 或直接权威状态句柄，继续限制燃料、内存、模块大小和允许能力。任意第三方 C ABI 导入不在本轮交付范围。

兼容矩阵只覆盖确切版本和已测路径。详细版本、官方依据及失败证据见 [平台研究](research/native-aot-support-matrix.md)、[Lua](validation/native-aot-lua.md)、[WASM](validation/native-aot-wasm.md)、[GL 后端](validation/desktopgl-integration.md)。

## 导入与普通／静态双装配

插件发布者提供 `runtime.plugin.json`、实现 DLL 及完整依赖；不要求交付源码。入口为无参 `public static ApprovedApplicationPlugin Create()`，空内容只描述稳定能力，绑定内容后不能改变清单中的能力和阶段。声明 `IsAotCompatible=true` 并运行分析器；清单 `aot.compatible=true` 只是准入声明，不能覆盖真实 ILLink／ILC 错误。

Editor 的运行插件管理面板显式导入包；CLI 使用 `runtime.plugins.import`。导入登记可用实现，工程启用是单独的草稿编辑。精确版本和清单指纹进入游戏身份；活动试玩／导出持有 lease，移除和替换会拒绝使用中的版本。

普通宿主从获准目录受控动态加载，原生构建生成直接工厂调用和确切程序集 Reference，最终原生游戏不动态加载未知托管 DLL。普通发行只复制选中的外部包，玩家不需要作者的安装目录。游戏文件不能指定任意程序集路径并自动执行。实现细节、包字段和哈希规则见 [插件包协议](runtime-plugin-packages.md)。

## 桌游入口配置

显式命令桌游用 `definition.board-state` 加载 `{ "format": 1, "state": ... }`。它与历史 `definition.module`／Lua 作者格式是不同的显式组合；不静默迁移旧游戏。使用 `author.runtime.board-application` 注册作者运行能力；没有结构化格式面板时仍可编辑通用文件、试玩、校验及导出。

`kernel.configuration.seed` 固定种子。所选会话的 `configuration.controllers` 覆盖初始席位控制器，`maxSteps` 限制 CPU 自动运行步数。静态会话入口分别为 `session.local`、`session.local.cpu`、`session.local.effects`、`session.local.cpu-effects`；其 `session` 能力相同，必需 CPU／effects 依赖不同。无 CPU 会话不能配置 CPU 席位。选择带 CPU 的会话时，CPU 提供者必须在入口中显式存在。

桌游图形入口显式选择 `board.render`、`board.visual-input`、`platform.viewport` 和唯一后端。宿主向已选择的可视输入能力绑定已发布投影；render 不驱动规则。`board.desktop` 的宿主闭包包含桌游契约，`application.desktop` 的闭包不包含它们。游戏规则和 CPU 决策来自配置、模块或外部样例插件，不按游戏名称分支。

## 作者操作示例

在 profile 的 `plugins` 中添加所需作者插件，例如：

```json
{"format":1,"plugins":[
  {"id":"edit.disk"},{"id":"author.files"},
  {"id":"author.runtime.board-application"},
  {"id":"author.export.package"},{"id":"author.export.player"},
  {"id":"author.export.native"}
]}
```

该对象放入现有 `profiles.<name>` 并设定 `defaultProfile`。先用 CLI 自描述查询参数；`native.export` 的主要参数是 `target`（新的输出目录）、`sdk`（维护者分发目录）、`host`（桌游用 `board.desktop`，其他应用用 `application.desktop`）、`platform`（`win-x64`），以及可选 `title`、`version`、工程内 `icon`。工具链路径可用 `dotnet`、`visualStudio`、`windowsKits` 显式指定。具体 JSON 请求和 GUI 工作区定位沿用 [作者 CLI](author-cli.md)，不另建发布执行器。

Editor 导出面板显示实际闭包、精确身份、后端、原生文件、工具链检查和构建日志。修改入口后需要显式重建试玩；旧会话继续标注其捕获版本。取消、切换文档、关闭或停用导出插件会取消并等待活动构建，旧代次结果不得覆盖新文档。

## 维护者复现

工具链固定为 SDK 10.0.112、NativeAOT runtime 10.0.12，首轮实测 Visual Studio 18 Community 的 x64 C++ 14.51.36231 与 Windows SDK 10.0.26100。先按 [CordiSharp](../third_party/CordiSharp/README.md) 和 [MonoGame](../third_party/MonoGame/README.md) 构建固定补丁，保留上游身份和许可。

```powershell
dotnet build tools/NativeSdk -c Release -p:UseStaticRuntime=true --artifacts-path artifacts/native-sdk-build
dotnet artifacts/native-sdk-build/bin/NativeSdk/release/NativeSdk.dll pack artifacts/native-aot/sdk
dotnet build tools/NativeDelivery -c Release -p:UseStaticRuntime=true --artifacts-path artifacts/native-delivery
dotnet artifacts/native-delivery/bin/NativeDelivery/release/NativeDelivery.dll prepare artifacts/native-aot/delivery
dotnet artifacts/native-delivery/bin/NativeDelivery/release/NativeDelivery.dll build artifacts/native-aot/sdk artifacts/native-aot/delivery/wasm-external artifacts/native-aot/game
```

目标目录必须不存在。`prepare` 生成四个组合及 `samples/Sample.NativeCpu` 的外部包。分别替换为 `minimal`、`wasm`、`external`、`wasm-external` 验证；`verify-ui <source> <new-report-directory>` 验证普通宿主的投影、规则、CPU、回放和释放。原生运行仍须单独在干净 Windows 执行，不能拿此命令替代。

普通完整 Player 维护者发行使用 `scripts/publish-player.ps1 -IncludeDesktopGlBackend $true`；是否携带 Lua 工作进程由完整目标组合决定。泛用预编译 Player 的体积不能直接等同于选择闭包后的原生游戏；比较时记录具体输入范围。

`build-inputs.json` 记录完整编译输入；`native-release.json` 记录工具链、严格警告策略、文件大小和哈希；诊断目录保留生成项目、静态目录、锁文件和编译日志。原生符号保留在构建诊断目录，不进入游戏发行。运行时将预期内容／原生依赖哈希嵌入 EXE，修改外部审计清单不能授权篡改。

## 诊断与生命周期

- 缺 AOT 声明、未测目标、传递依赖不兼容：拒绝预检或计划生成，并保留依赖原因。
- 零／多个后端、缺 `board.render`、缺能力或执行顺序冲突：修正入口的显式选择，不静默换后端。
- SDK、C++ 或 Windows SDK 缺失：按工具链诊断补全构建机；普通 Player 导出仍可独立使用。
- 假声明、动态代码、裁剪错误：严格构建失败，不关闭警告，不产生成功发行目录。
- 缺文件、错误架构、实现指纹或内容不符：拒绝加载；专用原生入口显示路径并尝试写 `startup-error.txt`。

能力依赖规定初始化可用性；`before`／`after` 规定同阶段回调顺序，两者不互相替代。无回调插件可只提供接口。图形宿主管设备，实例只有一个 update 驱动，视口只读已发布状态并拥有自己的资源。关闭时先取消、等待异步工作，再释放子视口、实例和导入 lease。细节继续遵循 [应用循环](application-loop.md)，不另定义 AOT 专用循环。

无界面宿主使用 `HostMode.Headless`，验证所选图形插件身份而不装载设备。已有原生测试覆盖无界面核心／桌游；当前随包提供的作者宿主配置是两个桌面入口，未另承诺通用无界面 EXE 导出模板。
