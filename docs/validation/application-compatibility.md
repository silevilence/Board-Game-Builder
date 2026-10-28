# 小核心迁移基线与兼容矩阵

日期：2026-10-28。迁移前源码：`3e86557b4edcde8286f39617770cb08d0ec1ea5d`。
本表是 ROADMAP「建立既有功能重构基线与兼容矩阵」的产物，不代表后续迁移或整组验收完成。

## 行为基线

`tests/Engine.Tests/ApplicationBaselineTests.cs` 从正式入口加载卡牌、六角、群岛、秘密拍卖、UNO、流程卡牌和 WASM 示例，各固定种子 1、7、42。CPU 组合执行实际 CPU，无 CPU 的 WASM 执行首个合法动作。保存内容身份、完整命令日志摘要（含实际动作、随机与流程结果）、终局状态摘要、每步全部席位投影轨迹摘要和最终流程状态摘要；同时核对恢复记录的状态与每席位投影。期望值在 `application-baseline.json`，摘要不包含绝对路径或墙钟时间。

重放比较：`dotnet run --project tests/Engine.Tests -- application-baseline`。`--capture-application-baseline` 仅用于明确建立新基线，不得因迁移测试失败重录来掩盖差异。发生内容身份变化时须先按下面的兼容策略解释，再比较实际语义。最终验收另需卡牌／六角／群岛各 20 种子，当前三种子基线不能替代该门槛。

| 既有能力 | 迁移归属 | 自动回归入口（Engine.Tests 组名） | 已有实际证据／补验 |
| --- | --- | --- | --- |
| 状态、命令、随机、事务、投影 | 桌游内核能力 | commands visibility turns replay review | m0-review.md、m0-seeds.json；非法操作与中途失败保持状态／记录边界 |
| Lua 规则、CPU、重载、预算 | 通用执行器＋桌游绑定 | lua cpu sandbox worker reload batch | m1-review.md；候选失败保留旧脚本，切换记录边界不变 |
| 秘密提交、系统身份 | 桌游事务／循环 | secrets auction flow-systems flow-protection | m3-review.md；普通玩家不能取得系统秘密 |
| 四类流程与恢复 | 流程能力＋桌游 update | flow-contract flow-validation flow-runtime flow-players flow-restoration flow-delivery flow-review | flow-runtime-review.md；节点轨迹／恢复不重复效果 |
| WASM 媒介与效果 | 通用执行实例＋桌游效果 | wasm wasm-extensions wasm-isolation | m3-review.md；imports、权限、fuel、内存、原子失败保持 |
| 内容、目录、包、快照、记录 | 通用内容契约＋桌游载荷 | startup runtime-entry definition-plugins alternative-definition packages runtime-recording | m3.5-review.md；无原目录恢复与篡改拒绝 |
| 原始文件、单图、图集、字体 | 通用资源能力 | resource-plugins atlas resource-font | resource-plugins-review.md、evidence/cursed-investigators/fonts；真实 GPU／中文仍须复测 |
| 布局、主题、图形、交互 | 图形宿主＋桌游 render／交互 | view interaction board-actions freecell-shortcuts local-table | player-shell-review.md、cursed-investigators-board.md；真实窗口、DPI、失焦、多视口重建补验 |
| 动画与分页文档 | 通用时间轴／排版＋桌游绑定 | animation-runtime resource-documents animation-author | evidence/cursed-investigators/animations；暂停模拟继续动画需迁移后实际验证 |
| 作者格式、草稿、Undo、CLI | 作者插件与独立 profile | author-contract author-profiles author-operations author-workspace author-connections author-cli author-connection-lifetime | cli-author-review.md；无效草稿、文档代次、断连不回退 |
| 结构化面板与流程画布 | 所属 CordiSharp 作者插件 | flow-editor-lifetime flow-editor-model flow-editor-canvas flow-editor-properties flow-editor-validation flow-editor-playback flow-editor-conflicts | flow-editor-review.md、evidence/flow-20261016-round4；真实面板重建补验 |
| 导入与打印 | 独立作者能力 | image-import tts-import print author-resources | m3-review.md；PDF 拼版视觉与 GUI 撤销补验 |
| Player、调试、热座／分屏 | 通用外壳＋桌游能力 | debug-play debug-external observers timeline freecell-modes island-modes poker-uno-modes local-table | m2-review.md、player-shell-review.md；外部模式依赖 Release Player，实际窗口补验 |
| 联机、恢复、观战 | 可选桌游网络适配 | online-identity online-ordering online-reconnect online-audience online-secrets online-http online-faults online-cursor poker-uno-online | m4-review.md、hyperv-m4-uno-review.md；网络回归不替代本地门槛 |
| 普通发行、启动器 | 独立导出能力／Launcher | export-dependencies export system-runtime（另 tests/Launcher.Tests） | launcher.md、github-release.md；干净 Windows、无 SDK／系统 .NET、移动目录、无闪窗必须实际补验 |
| 示例特殊路径 | 示例脚本／资源 | freecell hex island poker-uno flow-delivery（另 tests/CursedInvestigators.Tests） | freecell-review.md（合法通关）、island-review.md、poker-uno-review.md、cursed-investigators.md |

上述路径相对本目录。历史证据保留原日期和源码身份，不冒充本次复测。初始基线只执行必要运行契约／语义用例；真实窗口和干净环境未复测均列为后续验收义务。

## 格式与身份兼容

| 输入或接口 | 保持策略 |
| --- | --- |
| game.start.json format 1、插件 ID/version/api | 维持原 JSON 及身份规范；默认组合必须模板显式生成，不根据名称、路径或缺项补插件 |
| definition.module / definition.state | 由原加载提供者保留格式 1 的结构／配置校验，逐步移出通用门面固定分支 |
| 目录及固定入口路径 | 继续 GameRuntime.Load；路径、大小、未知字段诊断不得弱化 |
| .bgm format 2 | 继续 GamePackage.Pack；包含全部预设与内容依赖；旧 format 1 只显式历史读取／迁移 |
| 运行身份 1、快照与会话录制 | 保持指纹算法与字段，恢复校验脚本、资源和流程；引入通用载荷时采用版本化封套或明确适配，不能伪造 GameState |
| 命令日志 format 1 | 只与原快照／身份配套恢复，不重跑 Lua／WASM；未知格式、缺件和篡改拒绝 |
| 公开会话与 CLI 操作 | 保留现有桌游门面作为适配，新的通用能力不要求席位；操作名／来源版本／错误含义保持 |
| author.profile.json | 与运行配置分离，不随游戏发行；GUI 仍绑定工作区与文档代次，所有写入通过 IProjectEditMode |
| 脚本重载 | 只在安全提交边界切换已验证候选，失败保留旧版本；回放使用实际记录结果 |
| 类型所在程序集 | 拆分时保留源兼容并声明程序集移动；已有编译插件需重编译或 type-forwarding，不能声称二进制自动兼容 |

2026-10-28 通用载荷补充：Application.Core 的 `BoardGame.Application` 门面新增独立 `application-content` 快照 format 1 与包 format 3；既有桌游门面仍读取原格式、保留原指纹，不静默迁移。通用载荷由版本化 `IContentLoader` 从完整内容字节重建；两种协议通过明确命名空间／ContentCatalog 重载选择，未知版本和跨格式使用均拒绝。该格式新增不表示既有桌游宿主已完成后续迁移。

## 比较方法与阻塞追踪

失败原子性以失败前后权威状态、随机游标及提交记录比较；重载核对旧脚本继续可用和切换记录；生命周期核对重复启动／停止、启动失败逆序清理、取消后的迟到结果与 worker 退出。迁移后增加受控时间、不同 render 频率、多视口不增加 update 的验证。

本地性能沿用 `latency`：同机 Release 构建 quick-cards seed 42，两次开局，真人动作到 CPU 应答小于 1000 ms；记录每步值及 Lua IPC 时间，避免与并行重负载测试同时测量。方法及历史结果见 m1-latency.md。图形设备、真实输入、干净环境与最终 20 种子验证尚未执行，不以编译通过替代。

后续 WindowsDX 迁移依赖 ROADMAP「建立渲染能力契约与单后端约束」；最小实时示例还依赖「拆分可选运行插件与普通装配边界」；普通发行回归依赖三项独立导出／Editor 导出任务。这些未完成的范围外依赖需要先交付，不能将其验收自动视为完成。
