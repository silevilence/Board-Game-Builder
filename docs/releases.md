# Windows x64 发行包

从 GitHub Releases 下载 `Board-Game-Builder-V<版本>-win-x64.zip` 和同名 `.zip.sha256`，用 `Get-FileHash <ZIP> -Algorithm SHA256` 核对后解压。不要下载 GitHub 自动生成的源码包。产品为自包含独立目录，无需安装 .NET SDK／Runtime；图形界面需要 Windows x64、Direct3D 11 图形设备。

双击 `editor/Editor.exe` 打开编辑器；命令行入口为根目录 `bgb.exe`，例如 `./bgb.exe docs entry`、`./bgb.exe help`。完整本地作者说明位于 `docs/author`，可运行示例位于 `docs/author/example`。新建工程、资源导入、编辑、保存及试玩均由 Editor 内置插件提供；外部试玩自动使用 `debug-player`，正式导出自动使用 `player`。工程、日志及导出目录保存在用户目录，选择工程保存目录时不要放进产品安装目录。

保留目录结构即可移动安装。作者 SKILL 的复制／Junction 安装、修复与卸载使用 `bgb.exe skill` 命令，具体参数查询 `bgb.exe help` 和 `bgb.exe docs entry`。移动产品后使用新路径执行修复，重新生成技能中的路径。独立导出目录含 `Play.exe`、游戏数据及运行依赖，整体复制给玩家即可双击运行，不需要作者工具。`debug-player` 是作者工具，不用于正式游戏导出。

`toolkit.json` 的 `api: 1` 表示工具包清单格式，包含产品 `version`、`platform`、源码 `commit` 以及相对 `entries`。独立附件 `toolkit.json` 必须与 ZIP 内文件一致。正式 Player 的 `player/release.json` 只用于导出预检，运行协议与插件 API 不等同产品版本。

## 维护者发布

先更新根 `changelog.md` 和 `Directory.Build.props`，提交实现、日志、版本及 ROADMAP。正式 tag 是 `V主.次.修订`（也接受小写 v），数字不带前导零。运行 `python scripts/release.py check --tag V0.1.0`、`python -m unittest discover -s scripts/tests -v`，然后 `pwsh -NoProfile -File scripts/publish-toolkit.ps1 -Tag V0.1.0` 可本地构建验证。输出目录必须不存在。

推送对应提交及 tag 后，`Release Windows toolkit` 从 tag 提交构建，读取该提交对应版本的 changelog，验证自包含运行依赖及正式导出、CPU 对局、产物版本后生成 ZIP、校验信息与清单。全部上传到草稿并回读核对后才公开；分支推送不发布。所有发布使用同一并发锁，大小写别名对应同一产品版本。

失败查看 Action 日志和 `release-diagnostics`。修复前不得把失败条目标成完成。上传中断可重跑同一 tag；匹配的草稿附件保留，补传缺失附件。既有正文、提交或附件冲突会失败，绝不自动覆盖公开版本；应调查冲突并发布新版本。同版本 tag 不可指向不同提交。干净 Windows 环境的图形创作与移动验收需另行记录，不把 CI 机器当作无 SDK 验收环境。
