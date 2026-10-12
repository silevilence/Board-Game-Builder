# Windows x64 发行包

新格式提供两种 Windows x64 包，各自附有同名 `.zip.sha256`：

- `Board-Game-Builder-V<版本>-standalone-win-x64.zip`：独立包，自带运行依赖，可离线解压运行，无需安装 .NET SDK／Runtime。
- `Board-Game-Builder-V<版本>-slim-win-x64.zip`：精简包，使用系统安装的兼容 x64 .NET Runtime。各入口在启动前检查实际清单要求；图形程序还需要 Desktop Runtime。允许同主次版本的新补丁，不跨主次版本替换。无需 SDK。

用 `Get-FileHash <ZIP> -Algorithm SHA256` 核对后解压。图形界面需要 Direct3D 11 图形设备。不要下载 GitHub 自动生成的源码包或把 `components-...` 附件当作产品包。原 V0.1.0 的 `Board-Game-Builder-V0.1.0-win-x64.zip` 保持原样；新格式的上线状态以实际 Release 附件为准，本地构建候选不代表已发布。

双击 `editor/Editor.exe` 打开编辑器；命令行入口为根目录 `bgb.exe`，例如 `./bgb.exe docs entry`、`./bgb.exe help`。完整本地作者说明位于 `docs/author`，可运行示例位于 `docs/author/example`。新建工程、资源导入、编辑、保存及试玩均由 Editor 内置插件提供；外部试玩自动使用 `debug-player`，正式导出自动使用 `player`。工程、日志及导出目录保存在用户目录，选择工程保存目录时不要放进产品安装目录。

保留目录结构即可移动安装。作者 SKILL 的复制／Junction 安装、修复与卸载使用 `bgb.exe skill` 命令，具体参数查询 `bgb.exe help` 和 `bgb.exe docs entry`。移动产品后使用新路径执行修复，重新生成技能中的路径。独立导出目录含 `Play.exe`、游戏数据及运行依赖，整体复制给玩家即可双击运行，不需要作者工具。`debug-player` 是作者工具，不用于正式游戏导出。

新包内的 `toolkit.json` 使用 API 2，分别对应 Release 的 `toolkit-standalone.json`／`toolkit-slim.json`；清单列出全部文件、哈希和依赖来源。原 V0.1.0 仍使用 API 1 及 `toolkit.json` 附件。正式 Player 的 `player/release.json` 用于导出预检，运行协议与插件 API 不等同产品版本。保留所有目录，包括隐藏的 `.payload`；首次启动建立同卷硬链接，要求可写、支持硬链接的本地安装位置。

精简包的正式游戏导出仍自带完整运行依赖。首次导出按固定组件清单下载并校验缺少的可再分发文件，之后本地复用；不复制系统安装作为游戏依赖。断网缺依赖时导出会失败且不生成半成品，可改用独立包或提供已下载的组件目录：`bgb.exe call player.export --project <工程> --args-file <参数.json>`，参数含 `target`、`components` 和 `offline: true`。可选 `dependencyCache` 指定同卷、支持硬链接的缓存位置。正常精简包将缓存保存到包内 `.state/export-dependencies`；该缓存可删除，之后导出须重新获取缺失组件。

## 启动与导出故障排查

精简包启动失败时，先按提示核对缺失的框架、最低版本与架构。可用 x64 安装目录中的 `dotnet.exe --list-runtimes` 查看已安装框架；`--list-sdks` 为空不影响运行。只有核心 Runtime 时 CLI 可能可用，但 Editor 和图形 Player 仍需要 Desktop Runtime。安装兼容的 x64 Desktop Runtime，或改用独立包；不要把 DLL 从系统目录手工复制进精简包。

| 提示或现象 | 处理方式 |
| --- | --- |
| 缺少兼容 x64 框架、版本过旧或架构错误 | 按该包清单安装同主次版本且不低于最低补丁的 x64 Runtime；其他主次版本不能代替。 |
| 硬链接不受支持、位置不可写或目录被链接替换 | 将完整 ZIP 解压到可写、支持硬链接的本地目录；产品入口、内容与缓存保持同卷。 |
| 文件哈希不一致或正式导出依赖损坏 | 保留诊断，重新下载并核对 ZIP 校验文件，解压到新目录；不要覆盖正在运行的产品文件。 |
| 精简包离线导出缺少依赖 | 联网获取一次固定导出组件，或提供事先下载的组件目录；也可从独立包导出。 |

需要离线导出时，将完整组件 ZIP 保持原名放在同一个目录，并用参数文件明确指定，例如：

```json
{
  "target": "D:/作品/我的游戏",
  "components": "D:/发行组件",
  "dependencyCache": "D:/作者缓存/export-dependencies",
  "offline": true
}
```

执行 `bgb.exe call player.export --project <工程目录> --args-file <参数文件>`。组件来源与哈希由工具包内的导出清单锁定，不需要安装旧产品；缺失或错误组件会阻止导出。缓存须与 Player 产品文件位于同卷。正式输出是独立目录，分发整个目录即可，玩家不需要这些组件 ZIP 或作者缓存。

## 维护者发布

先更新根 `changelog.md` 和 `Directory.Build.props`，提交实现、日志、版本及 ROADMAP。正式 tag 是 `V主.次.修订`（也接受小写 v），数字不带前导零。运行 `python scripts/release.py check --tag V<版本>`、`python -m unittest discover -s scripts/tests -v`，然后 `pwsh -NoProfile -File scripts/publish-toolkit.ps1 -Tag V<版本>` 可本地构建验证。输出目录必须不存在。

推送对应提交及 tag 后，`Release Windows toolkit` 从 tag 提交构建，读取该提交对应版本的 changelog，验证两种包、正式导出、CPU 对局、产物版本后生成各自 ZIP、校验信息与清单。也可先运行 `python scripts/release.py plan --tag V<版本> --output <构建输出>`，只检查本地完整组件集合并打印发布计划，不访问或修改 GitHub。产物必须对应当前提交；旧工作区候选不能直接当作新提交的发行物。

API 2 组件按内容身份分别存放在 `components-<哈希>` Release 中，标记为预发布且不设为 latest；它们不是可安装产品版本。首次上传组件，后续版本读取已有固定附件并校验后复用，只上传新增组件及新的用户包／清单。产品公开前回读所有直接和导出依赖，确认完整后才公开产品草稿。原 V0.1.0 的三个附件和旧 tag 保持不变。分支推送不发布，所有正式发布使用同一并发锁，大小写别名对应同一产品版本。

失败查看 Action 日志和 `release-diagnostics`。修复前不得把失败条目标成完成。上传中断可重跑同一 tag；匹配的草稿附件保留，补传缺失附件。既有正文、提交或附件冲突会失败，绝不自动覆盖公开版本；应调查冲突并发布新版本。同版本 tag 不可指向不同提交。干净 Windows 环境的图形创作与移动验收需另行记录，不把 CI 机器当作无 SDK 验收环境。

发布前检查 [GitHub 的附件限制](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)：每个 Release 最多 1,000 个附件，单附件必须小于 2 GiB。上传中断产生的空 `starter` 附件仅在本仓库未公开草稿中清理后重试，依据 [Release assets API](https://docs.github.com/en/rest/releases/assets)；已公开附件不删除、不覆盖。被任一产品引用的组件 Release、tag 和附件必须长期保留，不可用一般的旧版本清理规则删除；如需纠错，应发布新的内容身份和产品版本。
