# 游戏包、独立 Player 与 NativeAOT

`project.pack` 接受新 target（.bgm 文件），通过正式 GamePackage 协议打包所选模式的固定视图。资源、脚本和定义从运行插件声明取得；作者 profile、SKILL 和 `.bgb-author` 不成为游戏运行依赖。

数据包提供者为 `author.export.package`（API 1、版本 1），独立提供 `project.pack`。它通过已注册的 `IAuthorPackageRuntime` 选择包协议：桌游使用格式 2，通用应用使用格式 3；不根据游戏名称判断，不要求 Player 目录或 SDK。接收 Player 必须已安装入口声明的插件及版本，数据包不会安装或自动执行任意 DLL。

旧 `author.export` 配置通过显式 `Includes` 组合引用 `author.export.package` 与 `author.export.player`，保留原操作名；新建默认 profile 直接列出这两个插件。只需要一种产物时只启用对应插件，不会改写游戏内容。组合版本与循环依赖会被检查。停用提供者后对应操作消失，不回退到其他产物。

`player.export` 接受新 target（目录），可用 player 指定官方预编译 Player 目录；省略时使用 CLI 旁的 player 目录。预检核对 release.json、平台、文件哈希、所需运行插件、Lua worker 和正式图形入口。导出无需 SDK，不调用编译器。

独立 Player 提供者 `author.export.player` 不依赖数据包导出插件。它复用运行时的打包能力及 `PrecompiledPlayer` 发行服务；`release.json` 声明 `entryPoint`、`packageFormat`、运行插件目录和完整文件哈希。缺少匹配的发行目录会明确失败，不改为输出数据包。旧清单默认 `Player.exe`／格式 2。桌游适配器保留 Lua worker、WASM 以及开局资源检查；通用应用适配器不读取桌游状态或强制要求桌游二进制。

非桌游示例的维护者构建命令：`./scripts/publish-player.ps1 -Output artifacts/minimal-release -HostProject samples/Minimal.Player/Minimal.Player.csproj -EntryPoint Minimal.Player.exe -PackageFormat 3 -IncludeLuaWorker $false`。发布后的目录可作为 `player.export` 的 `player` 参数；作者导出时只校验和复制预编译文件。这个静态安装组合不包含桌游、Lua 或 WASM 实现。作者需要显式安装对应的运行时作者插件；数据文件不会加载任意插件 DLL。

成功目录包含 Play.exe、game.bgm 和运行依赖，双击 Play.exe 运行。首期为 Windows x64 独立目录，需要 Direct3D 11 图形环境；它不是单一文件发行。导出的游戏不依赖 CLI 或 Editor。

输出须在工程目录之外且原目标不存在；失败不会发布被报告为成功的最终产物。回执包含目标、产物种类、来源版本与游戏指纹。GUI 导出不隐式保存草稿。

导出成功后若快照清理因文件占用失败，回执的 `warnings` 单独报告遗留路径；已完成产物仍有效。复制、打包、附加步骤或取消失败则不发布最终目录。

## NativeAOT 原生游戏

`author.export.native`（API 1、版本 1）独立贡献 `native.export`，不包含在旧 `author.export` 组合中。把它加入所用作者 profile 的 `plugins` 后，可通过 `bgb describe native.export` 查看完整参数；停用它不影响另两个导出提供者。

`native.export` 需要新的 `target` 目录、维护者提供的预编译 `sdk` 目录（默认 CLI 旁的 `native-sdk`），以及对应宿主 `host`（默认 `application.desktop`）。运行插件及唯一图形后端仍由 `game.start.json` 选择，发布参数不能代替这份启用声明。源工程需要已获准的应用作者运行目录；现有通用桌面宿主要求 `application.view` 能力，缺失宿主适配时明确拒绝。

可选 `title`、`version` 设置 EXE 产品元数据，默认使用游戏身份；`icon` 是当前 ProjectView 内的 ICO 相对路径，读取草稿而非另行读取磁盘。`platform` 当前只能为 `win-x64`。`dotnet`、`visualStudio`、`windowsKits` 仅选择构建机工具位置，不是游戏运行配置。

本方式要求 .NET SDK 10.0.112、Visual Studio x64 C++ 工具和 Windows SDK。只有本方式执行工具链预检和编译；普通数据包与 Player 包仍无需 SDK。构建失败保留日志、不改变已有产物、不回退普通发布；回执的 `log` 返回完整日志路径。活动构建持有提供者，取消后等待子进程退出并清理临时产物。

原生输出包含直接运行的 `Play.exe`、内容和实际选择的原生依赖，无需系统 .NET。它不是普通 Player 的发行清单，不能拿要求 `coreclr.dll` 的预检规则验证。详见[构建服务](../validation/native-build-service.md)、[专用入口](../validation/native-game-entry.md)及[作者提供者验收](../validation/native-author-export.md)。
