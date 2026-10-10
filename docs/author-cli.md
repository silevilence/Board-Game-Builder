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
JSON 结果只写标准输出，日志写标准错误。退出码：0 成功、1 失败、2 参数错误、3 冲突、4 部分失败或未知结果、130 取消。

作者配置 `author.profile.json` 与游戏启动配置分离。显式 `--profile` 优先于项目默认，其次是内置默认；不合并配置，不回退不存在的显式配置。

```json
{"defaultProfile":"local","profiles":{"local":{"format":1,"plugins":[{"id":"edit.disk","version":"1","api":1},{"id":"author.files","version":"1","api":1}]}}}
```

核心插件由宿主补齐，profile 必须选择恰好一个编辑模式。作者扩展实现 `IAuthorPluginPackage`，DLL 安装到 CLI 旁的 `plugins/author`；示例见 `samples/Author.SamplePlugin`。扩展声明稳定操作说明并通过 `AuthorScope.Register` 注册，注册随 CordiSharp 释放。操作执行期间宿主保留插件，退出等待执行结束。安装的作者 DLL 是受信任的本机代码，游戏目录不会被扫描为插件目录。

## 项目视图与提交

操作读取固定的项目来源和内容版本；每次磁盘调用重新捕获文件。`files.write` 接受 `files` 映射，值为 UTF-8 文本或表示删除的 null。可直接用其他编辑器编写文件，不要求所有编辑经过 CLI。

变更只写目标文件；提交核对读取文件、枚举结果和目标基线。`.git` 和 `.bgb-author` 属于保留目录，不纳入作者项目视图；链接、越界路径和 Windows 特殊路径被拒绝。读取不等同于验证游戏可运行。

多文件提交先在工程的 `.bgb-author` 持久化意图，再逐文件写入；同一作者工具的写入以文件锁协调。与任意外部编辑器之间不承诺跨文件原子性，提交和恢复都会检查基线。已写入一部分时返回 `PartialFailure`，并列出已应用路径，不能将其视为全部保存成功。

调用写操作时可传 `--request <唯一标识>`。同标识、同操作参数返回原回执；不同内容拒绝。使用 `request.result`（参数 `request`）核对结果，`request.recover` 显式续写尚未完成的部分。中断后用户修改的文件不会被恢复覆盖；需先检查保留的意图和当前内容。取消可能发生在部分文件已写入之后，此时返回部分失败，随后核对回执。

回执与原意图在 `.bgb-author` 中持续保留，不自动过期。删除该目录会丢失核对依据；无保留记录时结果为 `Unknown`，不得当作未执行而自动重放。请求 ID 不应重复用于新工作。回执报告历史提交版本，后续外部修改不会改写历史回执。
