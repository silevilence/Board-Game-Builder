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

运行时要求从实际 runtimeconfig 读取，包含 `Microsoft.NETCore.App` 和按需使用的 `Microsoft.WindowsDesktop.App`。`privateVersion` 锁定私有版本，`minimumVersion` 设为同一已构建版本，系统选择策略为 `LatestPatch`，不允许跳到其他主次版本。原生入口按包模式落实该策略。策略语义参考 [.NET 版本选择](https://learn.microsoft.com/en-us/dotnet/core/versions/selection)。

## 组件身份与来源

组件归档按 `objects/<SHA-256>` 存放原始文件内容。多个程序可通过不同路径引用同一个对象，所有组件中同一哈希只出现一次；同名不同内容拥有不同哈希。运行时包、第三方包和产品代码分别分组，组件 ID 由分组身份和排序后的内容哈希集合产生；文件最终归属明确记录在清单中。

来源固定为 `repository`、`tag: components-<内容集合哈希>` 和准确 `asset` 文件名。归档大小与 SHA-256 独立校验。来源不允许 `latest`、父组件或历史补丁链；清单单独包含恢复本版本文件所需的全部对象及组件。相同内容重建必须得到相同归档，否则报告不可变组件冲突。

当前步骤仅生成可校验的本地组件和固定来源声明，不声称附件已经上传。组件发布工作尚未验收前，发布器明确阻止 API 2 产品公开，避免发布无法下载依赖的版本；现有 API 1 发布流程保留。

## 旧版与拒绝条件

原 V0.1.0 为 `api: 1`、`platform: win-x64`、原有五个固定入口，无 `format`／`components`。仅这个明确的旧格式被识别为 `legacy`；导入仍须验证原 ZIP `.sha256` 和正式 Player 清单。旧包不生成、不推断共享依赖，保持原目录运行。未知 API、其他版本冒用旧格式或混合新旧字段均拒绝。

新格式验证覆盖大小／哈希、重复与大小写冲突、文件／目录冲突、路径穿越、Windows 设备名／数据流、软链接／Junction、缺失或额外文件、缺入口、无来源组件、重复对象和未解析依赖。验证真实目录还重新读取宿主依赖与运行插件目录，拒绝元数据与文件不一致。

## 可直接运行的去重布局

`layout: hardlinks-v1` 在每个文件记录增加 `storage`，指向该哈希唯一的规范产品文件路径。规范文件必须也在清单内并指向自身；禁止引用链、越界路径和同一内容的多个规范路径。

ZIP 只存放规范文件和清单。公开入口保持 `bgb.exe`、`editor/Editor.exe`、`player/Player.exe` 和 `debug-player/Player.exe`，由无系统 .NET 依赖的 C# Native AOT 入口承担。`entryTargets` 明确指向 `.payload/` 下的对应托管宿主；作者文档在解压后即可直接读取。

启动入口先核对所有规范文件的大小及哈希，检查安装位置支持硬链接，在 `.state/layout.lock` 保护下建立缺失的产品路径，再启动对应宿主。已有路径必须与规范文件拥有相同卷标和文件 ID，不能用另一份内容相同的复制品冒充共享。并发首次启动会等待同一锁；失败可以重试，但损坏产品须从原包修复，不在启动时下载或改写产品字节。

各宿主、正式／调试 Player 和 Lua worker 沿用各自的确定依赖路径，路径通过硬链接引用唯一物理内容；相同版本的核心与桌面运行库不会各存一份数据。每个入口继承工作目录和参数，转发标准输入／输出／错误及退出状态。图形入口不显示控制台。可写布局状态位于 `.state`，不属于共享产品清单；用户工程仍由用户选择保存位置。

安装到可写的本地 NTFS 卷；不支持硬链接或有外部重解析路径时，入口明确报错并要求调整位置。解压后的目录可在同卷移动；若用普通复制操作复制已运行目录，复制工具可能把硬链接展开为多份数据，此时应从原 ZIP 重新解压到新位置，不能宣称复制品仍已去重。不要原位编辑已安装的产品文件。

维护者使用 `publish-toolkit-entries.ps1` 构建原生入口，`compact_toolkit.py` 准备和压缩布局；完整发布脚本已串联这些步骤。构建机需要 Windows Native AOT 的 C++ 工具链，作者电脑不需要。`measure-toolkit.ps1` 按 Windows 文件 ID 校验物理共享，并使用 FileStandardInfo 测量实际分配空间，避免将硬链接逻辑文件大小当成磁盘占用。

## 精简包及独立游戏导出

`slim_toolkit.py` 从同一完整发布产物派生 `packageMode: slim`：移除仅归属 runtimepack 的文件，将各宿主的 runtimeconfig 改为 `frameworks`／`LatestPatch`，去除 deps 中对应 runtimepack 声明。产品 DLL、插件和文档保持同一构建来源，公开入口与独立包相同。最终两种包分别命名并生成校验文件。

精简包原生入口读取 Windows 官方 x64 .NET 注册位置（32 位注册表视图），无登记时检查 Program Files 下的默认位置。它检查 dotnet／核心与桌面原生库架构、框架完整性、最低补丁及桌面框架对核心版本的要求；仅对子进程设置明确的运行时根目录及补丁策略，不改变全局配置，也不切换包模式。CLI／Lua 只需要核心框架，图形宿主还需要桌面框架。

精简包在 `player/export.dependencies.json` 保存格式为 `board-game-builder.player-components` 的独立导出描述；完整工具包清单的 `exportDependencies` 与之完全一致。它包含原完整 Player 的文件、大小、SHA-256 及固定组件来源，包含正确的自包含 runtimeconfig／deps 和原 `release.json`。这套组件可独立恢复导出依赖，不依赖作者系统安装。

`PlayerDistribution.Export` 显式解析该描述，复用经过校验的本地产品文件和缓存；缺失组件从准确 Release 附件或指定本地组件目录获取。归档、对象、路径、大小、重复项和组件身份逐层校验；组件身份的标准 JSON 使用 UTF-8、两空格缩进和 LF，不能采用操作系统默认换行。下载失败／取消不发布半成品，临时传输归档清理；缓存并发互斥，完整投影才原子登记。缓存按内容硬链接复用，最终游戏通过独立复制及再次校验输出，不持有共享目录或系统 Runtime 引用。

`Preflight` 不下载组件，缺失时给出离线诊断。CLI `player.export` 增加可选 `components`、`dependencyCache` 和 `offline` 参数；GUI 继续使用同一共享导出实现及现有进度显示。
