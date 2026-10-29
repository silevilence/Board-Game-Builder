# 小核心迁移基线与兼容矩阵

日期：2026-10-28。迁移前源码：`3e86557b4edcde8286f39617770cb08d0ec1ea5d`。
本表最初为 ROADMAP「建立既有功能重构基线与兼容矩阵」产物；2026-10-29 的最终迁移复核、72 组比较、真实图形及干净发行结果见末节和 [联合验收](application-loop-review.md)。下表历史证据列保留初始补验计划，现状以最终复核为准。

## 行为基线

`tests/Engine.Tests/ApplicationBaselineTests.cs` 从正式入口加载卡牌、六角、群岛、秘密拍卖、UNO、流程卡牌和 WASM 示例，各固定种子 1、7、42。CPU 组合执行实际 CPU，无 CPU 的 WASM 执行首个合法动作。保存内容身份、完整命令日志摘要（含实际动作、随机与流程结果）、终局状态摘要、每步全部席位投影轨迹摘要和最终流程状态摘要；同时核对恢复记录的状态与每席位投影。期望值在 `application-baseline.json`，摘要不包含绝对路径或墙钟时间。

重放比较：`dotnet run --project tests/Engine.Tests -- application-baseline`。`--capture-application-baseline` 仅用于明确建立新基线，不得因迁移测试失败重录来掩盖差异。发生内容身份变化时须先按下面的兼容策略解释，再比较实际语义。最终另用 `application-regression` 完成卡牌／六角／群岛各 20 种子，原三种子身份基线保持不变。

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

2026-10-28 通用载荷补充：Application.Core 的 `BoardGame.AppRuntime` 门面新增独立 `application-content` 快照 format 1 与包 format 3；既有桌游门面仍读取原格式、保留原指纹，不静默迁移。通用载荷由版本化 `IContentLoader` 从完整内容字节重建；两种协议通过明确命名空间／ContentCatalog 重载选择，未知版本和跨格式使用均拒绝。桌游宿主与通用宿主的后续迁移证据分项列在联合验收中。

## 比较方法与阻塞追踪

2026-10-29 显式展示声明迁移：全部 11 个桌游示例、新建模板和共用生成器追加 `board.render` 版本 1／API 1／空配置；实时示例保持独立组合。迁移清单 [application-entry-migration.json](application-entry-migration.json) 保存各旧入口原文和新入口哈希。加载器不会自动添加此插件。旧入口、包和录制仍可按原指纹无界面加载／回放；新图形试玩与 Player 导出要求作者明确声明展示实现，缺声明会提示执行 `python scripts/migrate-board-presentation.py --project <目录>`，不修改旧输入。仅编辑目录后重新打包，新包不能冒充旧录制的内容版本。

无界面目录只持有 `BoardRenderContract` 元数据，图形作用域的激活入口明确要求绑定安装实现，服务器不会加载绘制程序集。图形 Player 使用真实 `BoardRenderPlugin`，作者布局预览可显式使用宿主自己的预览组合；预览不是游戏运行配置。基础 backend 仍由已验证的宿主安装组合固定为 WindowsDX，此次不开放 GL 或运行中切换。

原始 application-baseline.json 未改动。`application-baseline` 用清单中的原始入口重建相同内容，再逐字段核对原基线；`application-regression` 分别验证新旧完整录制的哈希链／回放，然后比较提交、效果、状态、投影、流程和步数。声明变化会改变内容指纹及日志头派生哈希，不能据此跳过规则语义比较。

失败原子性以失败前后权威状态、随机游标及提交记录比较；重载核对旧脚本继续可用和切换记录；生命周期核对重复启动／停止、启动失败逆序清理、取消后的迟到结果与 worker 退出。迁移后增加受控时间、不同 render 频率、多视口不增加 update 的验证。

本地性能沿用 `latency`：同机 Release 构建 quick-cards seed 42，两次开局，真人动作到 CPU 应答小于 1000 ms；记录每步值及 Lua IPC 时间，避免与并行重负载测试同时测量。方法及历史结果见 m1-latency.md。最终保留一次 1376.6 ms 冷启动波动，后续连续三轮 24 次操作最大 408.9 ms，门槛不变；不能把后续通过描述成没有发生过超时。

## 最终复核（2026-10-29）

WindowsDX 单后端契约、普通可选装配、三个导出任务及本组迁移均已交付。72 组完整语义对照见 [application-semantic-regression.json](application-semantic-regression.json)，异常、授权、脚本、流程与生命周期及性能见 [application-lifecycle-regression.json](application-lifecycle-regression.json)。真实 Motion 和 WindowsDX 多视口、输入、缩放、失败与关闭均释放全部设备／窗口；既有桌游、动画、分页、Editor 和联机真实探针见 [联合验收](application-loop-review.md)。

两轮独立 Windows Sandbox 完成无 SDK 作者创作导出，以及无源码／作者工具／系统 .NET 的桌游与非桌游移动目录图形运行；原始回执见 [application-clean-windows.json](application-clean-windows.json)。普通预编译发行已通过，GL、NativeAOT、跨平台和任意外部 .NET 运行插件仍为 ROADMAP 后续任务，不包含在本次完成结论内。
