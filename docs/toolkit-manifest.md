# 工具包完整依赖清单

`toolkit.json` API 2 使用 `format: board-game-builder.toolkit`，记录 Windows x64 产品身份、入口及完整依赖；它与正式游戏导出的 `player/release.json` 分工独立。后者仍负责正式 Player、运行插件、GUI 入口及导出内容预检。

## 从产物生成与验证

```powershell
python scripts/toolkit_manifest.py generate --root artifacts/toolkit --components artifacts/components
python scripts/toolkit_manifest.py verify --root artifacts/toolkit --components artifacts/components
```

`publish-toolkit.ps1` 在实际发布文件及正式 Player 清单生成后，通过 `release.py archive --component-format` 生成完整清单与组件包。构建机可使用 Python；消费清单不依赖 Python、源码、SDK 或旧安装。`--repository owner/repo` 或归档步骤的 `GH_REPO` 指定组件来源仓库。

清单字段：

| 字段 | 含义 |
| --- | --- |
| `api`、`format` | 必须识别的格式与版本；未知格式拒绝使用 |
| `version`、`commit`、`platform`、`architecture` | 产品版本、源码提交、平台及架构 |
| `packageMode` | `standalone` 或 `slim`；当前生成器先交付 standalone |
| `entries` | Editor、CLI、正式／调试 Player 和作者文档入口 |
| `files` | 每个产品文件的相对路径、字节数、SHA-256、组件和库身份 |
| `programs` | 各个宿主及 Lua worker 的入口、runtimeconfig、deps、库及文件依赖 |
| `libraries` | 从实际 deps 读取的库名、独立版本和类型，不统一改为产品版本 |
| `runtimePlugins` | 正式 Player 生成的运行插件版本目录 |
| `components` | 完整组件对象集合、压缩包大小／哈希及固定来源 |

文件集合覆盖目录中的全部普通文件，唯一例外是清单自身，避免循环哈希。清单自身由离线 ZIP 的 SHA-256 或后续分发协议的清单校验保护。解析清单不能代替验证外部校验信息。

运行时要求从实际 runtimeconfig 读取，包含 `Microsoft.NETCore.App` 和按需使用的 `Microsoft.WindowsDesktop.App`。`privateVersion` 锁定私有版本，`minimumVersion` 设为同一已构建版本，系统选择策略为 `LatestPatch`，不允许跳到其他主次版本。后续运行时布局及启动功能负责落实该策略；本阶段仍保留已有自包含宿主的启动语义。策略语义参考 [.NET 版本选择](https://learn.microsoft.com/en-us/dotnet/core/versions/selection)。

## 组件身份与来源

组件归档按 `objects/<SHA-256>` 存放原始文件内容。多个程序可通过不同路径引用同一个对象，所有组件中同一哈希只出现一次；同名不同内容拥有不同哈希。运行时包、第三方包和产品代码分别分组，组件 ID 由分组身份和排序后的内容哈希集合产生；文件最终归属明确记录在清单中。

来源固定为 `repository`、`tag: components-<内容集合哈希>` 和准确 `asset` 文件名。归档大小与 SHA-256 独立校验。来源不允许 `latest`、父组件或历史补丁链；清单单独包含恢复本版本文件所需的全部对象及组件。相同内容重建必须得到相同归档，否则报告不可变组件冲突。

当前步骤仅生成可校验的本地组件和固定来源声明，不声称附件已经上传。组件发布工作尚未验收前，发布器明确阻止 API 2 产品公开，避免发布无法下载依赖的版本；现有 API 1 发布流程保留。

## 旧版与拒绝条件

原 V0.1.0 为 `api: 1`、`platform: win-x64`、原有五个固定入口，无 `format`／`components`。仅这个明确的旧格式被识别为 `legacy`；导入仍须验证原 ZIP `.sha256` 和正式 Player 清单。旧包不生成、不推断共享依赖，保持原目录运行。未知 API、其他版本冒用旧格式或混合新旧字段均拒绝。

新格式验证覆盖大小／哈希、重复与大小写冲突、文件／目录冲突、路径穿越、Windows 设备名／数据流、软链接／Junction、缺失或额外文件、缺入口、无来源组件、重复对象和未解析依赖。验证真实目录还重新读取宿主依赖与运行插件目录，拒绝元数据与文件不一致。
