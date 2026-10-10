# 工程入口与最小创作

本地 CLI `bgb.exe` 可独立使用，无需启动 Editor 或安装 .NET SDK。游戏入口是 `game.start.json`，Player 只需要游戏包和运行依赖。

`bgb.exe help` 显示命令。`docs` 列出主题，`docs lua` 查询脚本接口；`plugins` 和 `operations` 显示能力与启用状态，`describe <操作ID>` 显示当前版本参数和结果。可直接用文本编辑器或 Agent 创建、修改工程文件。

常用别名：`create-module`、`create-state`、`validate`、`run-cpu`、`pack`、`export-player`；它们分别调用创建模板、项目校验、CPU 运行、打包和 Player 导出操作，参数及退出码与 `call` 相同，完整映射见 `help`。

PowerShell 示例（将 CLI 目录设为当前目录；输出目录选择新的路径）：

```powershell
.\bgb.exe call project.create.module --project 'C:\Games\My Game' --request create-001
.\bgb.exe call project.validate --project 'C:\Games\My Game'
.\bgb.exe call run.cpu --project 'C:\Games\My Game' --args '{"seed":1,"count":1}'
.\bgb.exe call project.pack --project 'C:\Games\My Game' --args '{"target":"C:/Games/My Game.bgm"}'
```

`project.create.state` 提供另一种定义格式。两种模板均生成入口、定义、规则和 CPU 脚本，可直接运行一个最小结束动作；可用 `definition` 参数指定定义路径。模板不覆盖已有同名文件。

`--project` 显式指定目录，允许入口缺失或暂时无效；省略时从当前目录向上查找入口。路径统一为工程内相对路径，JSON 中使用 `/`。可用 `--args-file <文件>` 避免命令行 JSON 转义。

入口格式：`format:1`、游戏 `id`、游戏 `version`、`plugins` 数组。每个插件有 `id`、`version:"1"`、`api:1`、`configuration`。模板声明 definition.module 或 definition.state、kernel、lua.rules、lua.cpu、session.local、recording、resources、resources.images。定义路径在定义插件的 `configuration.path`，脚本在 `configuration.file`。包身份只在入口中声明。

作者 profile 属于作者工具配置，见 `docs profile`；它不替代运行插件入口，也不会成为 Player 运行依赖。无效工程仍可读取和修复，只有明确的校验、运行与打包操作才判定可运行性。
