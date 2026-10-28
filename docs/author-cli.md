# CLI 作者工具

开发入口为 `dotnet run --project src/Cli -- help`，构建后的入口为 `bgb.exe`。
CLI 不加载 Avalonia，也不要求 Editor 运行。`--project` 可指定不完整工程；省略时从当前目录向上查找 `game.start.json`。

## Windows 发行与技能安装

维护者执行 `scripts/publish-author.ps1 -Output <新目录>` 生成 win-x64 自包含产品，包含 CLI、Lua worker、作者插件能力、离线文档、模板和 Player；可用 `-PlayerRelease <正式Player目录>` 复用既有 Player 发行物。用户解压后直接运行 `bgb.exe help`，无需 .NET SDK。

```powershell
& 'C:\Tools\Board Game Builder\bgb.exe' skill install --target 'C:\My Agent\.agents\skills' --method copy
& 'C:\Tools\Board Game Builder\bgb.exe' skill status --target 'C:\My Agent\.agents\skills'
& 'C:\Tools\Board Game Builder\bgb.exe' skill repair --target 'C:\My Agent\.agents\skills'
& 'C:\Tools\Board Game Builder\bgb.exe' skill uninstall --target 'C:\My Agent\.agents\skills'
```

`--target` 指技能父目录，安装到其 `bgb-author` 子目录；`--method junction` 可改用 Windows Junction。链接指向本用户应用数据目录中的已渲染副本，不指向源模板。重复 install 升级同一产品的安装；产品移动后从新位置 repair 会重新插值绝对路径。省略 method 时保留已有方式，新安装默认 copy。

安装记录位于目标父目录 `.bgb-author-install.json`。其他产品实例不能占用同一技能目标；使用不同父目录隔离。原技能含用户修改、额外内容或不属于本安装时会拒绝覆盖／卸载。卸载只删除本安装文件或 Junction 本身，不递归删除共享目标。若要保留自定义内容，先另存再维护。

已在 Windows 10 Pro 无 SDK 来宾验证两种方式的安装、升级、移动修复、卸载和独立运行；已用 Codex CLI 0.162.0-alpha.2 验证项目 `.agents/skills` 下复制与 Junction 的发现及实际 CLI 调用。其他 harness 尚未验证，不据通用目录安装推断其支持链接发现。完整环境、调用证据和限制见 [验收记录](validation/cli-author-review.md)。

`profile` 查询有效配置，`plugins` 查询已安装插件，`operations` 查询操作及启用状态，
`describe <ID>` 查询参数、结果和副作用，`call <ID> --args-file <JSON 文件>` 调用同一操作。
`docs` 列出离线主题，`docs <主题>` 读取完整文档；`workspaces` 列出本用户可连接的 Editor 实例。多实例使用 `--instance <ID>` 明确选择。`--args <JSON>` 和 `--args-file <路径>` 均提供操作参数。

常用别名为 `create-module` → `project.create.module`、`create-state` → `project.create.state`、`validate` → `project.validate`、`run-cpu` → `run.cpu`、`pack` → `project.pack`、`export-player` → `player.export`。例如 `bgb.exe validate --project <目录>` 等价于 `bgb.exe call project.validate --project <目录>`；别名复用参数、profile、取消、请求标识及退出码处理，不绕过插件启用状态。`help` 输出当前别名映射。
JSON 结果只写标准输出，日志写标准错误。退出码：0 成功、1 失败、2 参数错误、3 冲突、4 部分失败或未知结果、130 取消。

作者配置 `author.profile.json` 与游戏启动配置分离。显式 `--profile` 优先于项目默认，其次是内置默认；不合并配置，不回退不存在的显式配置。

```json
{"defaultProfile":"local","profiles":{"local":{"format":1,"plugins":[{"id":"edit.disk","version":"1","api":1},{"id":"author.files","version":"1","api":1}]}}}
```

核心插件由宿主补齐，profile 必须选择恰好一个编辑模式。作者扩展实现 `IAuthorPluginPackage`，DLL 安装到 CLI 旁的 `plugins/author`；示例见 `samples/Author.SamplePlugin`。扩展声明稳定操作说明并通过 `AuthorScope.Register` 注册，注册随 CordiSharp 释放。操作执行期间宿主保留插件，退出等待执行结束。安装的作者 DLL 是受信任的本机代码，游戏目录不会被扫描为插件目录。

## 扩展贡献

`AuthorPlugin` 声明 ID、版本、API、依赖／提供能力及启动函数。`Operations` 提供发现阶段的稳定说明，启动时用 `AuthorScope.Register(new AuthorOperation(info, handler))` 绑定执行；`Documents` 可贡献离线主题，重复 ID 或主题拒绝，不要求修改 CLI 的命令分支。独立示例项目 [Author.SamplePlugin](../samples/Author.SamplePlugin) 实现项目文件计数操作。

宿主核对操作声明与实际注册 ID：遗漏注册或未声明的注册都会使插件启动失败并清理已注册资源。安装 DLL 无法加载、依赖类型无法解析或包构造失败时，CLI 返回 `plugin.load`、DLL 绝对路径和底层原因；不会静默跳过坏包并提供不完整目录。`author-cli` 集成测试实际构建示例 DLL，将其安装到隔离 CLI 目录，再通过独立进程发现、启用和调用；示例插件无需随正式产品默认启用。

处理函数通过 `AuthorCall.View` 读取文件、枚举及来源，结构化编辑用 `AuthorCall.Formats` 选择作者支持。修改形成 `View.Change(requestId, changes)` 再交给 `Mode.Apply`；声明 `Effects:["project"]` 使宿主按请求标识和操作输入核对既有回执。只查询回执的操作可设 `ReadsProject:false`，因此临时无效工程不会妨碍核对。不得直接修改源目录或自行加载 GUI 文档。

长操作使用 `AuthorCall.Cancellation` 并释放会话／临时快照；结构化结果写回宿主，日志不能污染 stdout。插件服务和资源随 scope 清理，跨线程编辑由编辑模式调度。新增模式实现 `IProjectEditMode`，保留来源、依赖基线、冲突、回执和保存语义；请求输入核对实现 `IRequestLookup`，真正续写能力实现 `IRecoverableEditMode`，显式工作区保存实现 `IWorkspaceSave`。GUI 模式支持查询回执，不提供磁盘续写；`request.recover` 在 GUI 模式返回 `recovery.unavailable`，应使用 `request.result`。项目视图与运行可加载性分离，运行／打包仍使用正式 Runtime 和 GamePackage 入口。

GUI 通道只执行工作区读取、提交、查询及协调；校验、CPU 运行和导出在 CLI 进程消费快照。通道连接等待上限 5 秒，请求接收及响应发送各限 30 秒以清理停止传输的连接；工作区执行和客户端等待完成没有固定总时限，用户取消、客户端断开或插件释放会取消请求。同步写入如果已经开始，断开仍可能发生在提交之后，必须按请求标识核对，不能假定取消代表未执行。

## 项目视图与提交

操作读取固定的项目来源和内容版本；每次磁盘调用重新捕获文件。`files.write` 接受 `files` 映射，值为 UTF-8 文本或表示删除的 null。可直接用其他编辑器编写文件，不要求所有编辑经过 CLI。

变更只写目标文件；提交核对读取文件、枚举结果和目标基线。`.git` 和 `.bgb-author` 属于保留目录，不纳入作者项目视图；链接、越界路径和 Windows 特殊路径被拒绝。读取不等同于验证游戏可运行。

多文件提交先在工程的 `.bgb-author` 持久化意图，再逐文件写入；同一作者工具的写入以文件锁协调。与任意外部编辑器之间不承诺跨文件原子性，提交和恢复都会检查基线。已写入一部分时返回 `PartialFailure`，并列出已应用路径，不能将其视为全部保存成功。

调用写操作时可传 `--request <唯一标识>`。同标识、同操作参数返回原回执；不同内容拒绝。使用 `request.result`（参数 `request`）核对结果，`request.recover` 显式续写尚未完成的部分。中断后用户修改的文件不会被恢复覆盖；需先检查保留的意图和当前内容。取消可能发生在部分文件已写入之后，此时返回部分失败，随后核对回执。

回执与原意图在 `.bgb-author` 中持续保留，不自动过期。删除该目录会丢失核对依据；无保留记录时结果为 `Unknown`，不得当作未执行而自动重放。请求 ID 不应重复用于新工作。回执报告历史提交版本，后续外部修改不会改写历史回执。

## 非桌游作者能力

格式、操作、文档和运行校验由获准作者插件贡献。运行入口仍为 game.start.json，作者 profile 选择的工具不写入游戏运行列表。缺少专用作者格式时仍可 files.read／files.write；若已安装匹配运行提供者，仍可校验和试玩。缺失或冲突提供者会明确诊断，不静默改用桌游格式。

仓库中的实时示例使用独立 author.motion 插件。可运行 `dotnet run --project src/Cli -- validate --project samples/motion-lab`、`dotnet run --project src/Cli -- call configuration.read --project samples/motion-lab` 和 `dotnet run --project src/Cli -- call run.application --project samples/motion-lab`。最后一项默认执行 60 次固定 1/60 秒更新，可用 ticks 参数设置 1～10000；不宣称获得桌游终局或记录回放。GUI 模式继续核对工作区／文档代次，修改进入草稿，只有 workspace.save 显式写盘。
