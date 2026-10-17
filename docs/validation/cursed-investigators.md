# 咒物调查员验收记录

标题界面、分步开局及隐藏右侧操作栏的追加交付，见 [标题界面验收](cursed-investigators-title.md)。下文保留原 v9 验收记录。

本轮范围：标准单人、战役、初学者。任务状态以 ROADMAP 原地勾选为准。本组全部验收项已完成，保持原位置并逐项本地提交。

## 字体能力（2026-10-17）

- 基线：源码 `15d019c`，用 `scripts/publish-toolkit.ps1 -Tag V0.2.1` 生成 `artifacts/cursed-investigators/baseline`；本地构建，不是新的公开 V0.2.1 Release。发布 CLI 的操作发现、自描述和 `project.create.state` 可用；启用 `resources.font` 时返回明确的未安装能力诊断。
- 实现：通用 `resources.font` 配置字体与许可证路径；静态 TTF／CFF OTF 校验、运行资源池、快照、打包、MonoGame 文字纹理及 Player 控件字体共同使用随包字节。无引擎游戏专用分支，CLI 未修改。协议见 [随包字体](../packaged-fonts.md)。
- 重建：`artifacts/cursed-investigators/font` 及 `.slim` 为包含本任务修改的完整发行物；两种包均通过发布脚本的导出与 CPU 验证。构建时基础提交仍为 `15d019c`，不能仅以 toolkit 的 commit 字段区分，工作树源码摘要和产物哈希见下列证据。
- 实际作者链路：新 `bgb.exe call files.write` 配置测试工程，`validate` 返回无诊断，`call player.export` 生成 `font-export` 独立目录。没有以源码直接调用替代这些验收。
- 独立 Player：从 `%TEMP%` 启动导出目录 `Play.exe gui --smoke ...`，自动重复开局两次并完成操作，结果一致。字体及许可证位于包内，代码直接从包字节创建字形，不查询系统字体。此验证为本机独立目录，不声称是新的 Windows Sandbox 测试。
- 必要回归：`resource-font`、`resource-plugins` 通过；包含缺件、无效路径／字体、包离开原文件、快照恢复及旧工程无字体泄漏。`tests/View.Tests` 实际加载 Noto Sans CJK SC，检查汉字、数字、标点、重复渲染一致性、清空后重载，以及关闭一个控件不影响另一个控件。

[基线诊断](evidence/cursed-investigators/fonts/baseline-font-validation.json)、[新 CLI 校验](evidence/cursed-investigators/fonts/font-validation.json)、[导出](evidence/cursed-investigators/fonts/font-export.json)、[Player 两局](evidence/cursed-investigators/fonts/font-player-smoke.json)、[源码摘要](evidence/cursed-investigators/fonts/font-source.json)、[产物哈希](evidence/cursed-investigators/fonts/artifacts.json)、[字形渲染](evidence/cursed-investigators/fonts/font-render.png)。二进制发行目录留在本机 artifacts，不提交到源码库。

## 动画作者能力（2026-10-17）

通用 `resources.animations` 库通过对象、触发属性、来源对象、关键帧时间与相对位置／旋转描述动画。`author.animation` 是可释放的 CordiSharp 插件，编辑走工作区字段草稿和版本核对，预览使用独立投影，不运行权威对局。协议见 [动画作者说明](../author/animation.md)。

基线无此能力；完整本地发行目录 `artifacts/cursed-investigators/animation-v3` 包含 CLI、Editor、Lua worker、正式／调试 Player、模板与文档。先前两次完整 ZIP 候选后发现退出码缺陷，最终按 [本地构建脚本](evidence/cursed-investigators/animations/publish-local.ps1) 重建所有组件并运行官方 `validate-toolkit.ps1`，没有复用旧程序集。本次最终候选是自包含目录，未再次制作 ZIP 或公开 Release；源码基础提交为 `bfa41cc`，具体修改以 [源码摘要](evidence/cursed-investigators/animations/source.json) 为准。

发布 CLI 实际执行 `animation.create/read/replace/validate`；新 CLI 复验 [版本冲突](evidence/cursed-investigators/animations/animation-v3-stale.json) 返回退出码 3。既有 CLI 曾将领域冲突错误返回 1，已修复，`author-cli` 回归覆盖配置和模板冲突；未增加 CLI 命令／协议。动画领域操作沿用作者插件发现。

发布 Editor 的 [实测结果](evidence/cursed-investigators/animations/animation-v3-editor.json) 覆盖编辑、字段草稿、应用、Undo／Redo、保存重开、预览、两次插件停启、失败预览及切换文档释放。真实视口截图见 [预览](evidence/cursed-investigators/animations/animation-v3-editor.json.preview-0.png)。作者测试覆盖文件草稿冲突及磁盘模式，错误引用与时序格式由共享校验定位。

## 动画运行能力（2026-10-17）

共享资源库支持目录、包与快照加载，校验触发属性、对象、来源及背面图片。播放只消费已投影场景：隐藏对象不生成动画物体；同序号刷新不重播，序号回退、换库、关闭及重开清空旧播放。结束回归权威位置／牌面；动画期间 Player 阻止重复操作。相关逻辑不包含游戏名称或发牌规则。

发布 CLI [校验](evidence/cursed-investigators/animations/animation-v3-validate.json) 和 [独立导出](evidence/cursed-investigators/animations/animation-v3-export.json) 通过。导出的正式 `Play.exe` 从临时目录启动，[连续两局完成](evidence/cursed-investigators/animations/animation-v3-player.json)，验收自动化等待真实播放结束后才操作；结束牌面与位置正确。`animation-runtime` 测试覆盖中间帧／完成／取消／序号回退、隐藏目标、包和快照一致性，`animation-author` 及既有资源／图集回归通过。本机测试不宣称另做了干净 Windows 验收。

## 可玩示例与分页文档（2026-10-17）

示例入口为 `samples/cursed-investigators/game.start.json`。标准单人、两局战役、初学者按用户确认实现；关卡和挑战者仅保留资料。全部规则、卡牌、CPU、提示和呈现数据属于 sample，状态机明确表达配置、设置、选目标、初始掷骰修正、保留选择、骰池操作、奖励、回合结束、战役换局及胜负节点。扫描缺失边界见 [实现裁定](../../samples/cursed-investigators/RULINGS.md)。核对扫描时修正了角色／职业健康格数，九种组合的初始健康都有回归。

缺失的日志／参考呈现抽象为通用 `resources.documents`：分页图片和公开投影属性绑定，不保存第二份游戏状态，不执行规则；隐藏实体／属性不显示。永久页常驻，参考页另开窗口，翻页和终局刷新不重复记录，关闭释放图片及附属窗口。`player.presentation` 的窗口尺寸和 `statusEntity` 同样由游戏配置。协议见 [分页文档](../author/documents.md)。

真实窗口暴露字体换行误判缺字，已修复 CRLF／空行／制表符排版并测试。独立导出暴露大包误用单资源 32 MiB 限制，已修正：包仍按展开内容预算校验，导出保留哈希防篡改及根目录／子路径链接检查，没有提高单资源预算。

最终完整发行物为 `artifacts/cursed-investigators/final-v9`，基础提交 `bad4452` 加工作树修改，具体见 [源码摘要](evidence/cursed-investigators/sample/source.json)。包含所有自包含组件，官方工具包校验通过；构建中的一个并发 restore 候选失败后弃用，最终构建顺序执行。此处版本号仍是本地 0.2.1，不代表新增公开版本。CLI 的最终 `files.write`、操作发现、文档、校验、打包和导出使用真实发行版，未由源码调用作者服务替代。

### 规则和确定性证据

- [九组规则回归](evidence/cursed-investigators/sample/rules.log)：14 件咒物、12 张咒灵的全部挑战组合；27 种能力的揭露／初始／支付／结束效果；知识重复格、九种健康组合、保留容量、不可修改奖励骰、职业选项、死亡优先、捕获奖励、日志与战役重置。直接使用真实 Lua 和内核事务，并限制每次效果最多 64 条命令。
- [标准模式两种子](evidence/cursed-investigators/sample/standard-two-seeds.json)：369／448 次操作正常结束，无故障，重复和回放都一致。此证据来自 v6，后续只调整呈现位置和修复导出；最终包另有新 CLI 验证。
- [单人初学者获胜](evidence/cursed-investigators/sample/winning-run.json)：种子 1，560 次操作、6 次捕获、36 声望、剩余健康 4；[真人动作序列](evidence/cursed-investigators/sample/winning-actions.json) 可供实际 Player 自动输入，仍走合法动作与状态机。
- 战役以标准规则再进行第二局，累计捕获目标 13。自动策略并不保证胜利；第二局设置、属性重置、奖励／观察保留和最终胜利分支另有规则回归，不把分支测试称为整场战役获胜。

### 画面和交互证据

[真实 Player 图形验证](evidence/cursed-investigators/sample/view.json) 检查首帧、洗牌／发牌、遭遇、日志两面、独立参考窗口、不同窗口尺寸和设备释放；[完整获胜窗口验证](evidence/cursed-investigators/sample/view-winning.json) 走完真实动作后检查终局页仍可用。示例运行图见 [发牌中](evidence/cursed-investigators/sample/dealing.png)、[当前遭遇](evidence/cursed-investigators/sample/encounter.png)、[小窗口](evidence/cursed-investigators/sample/small-window.png)。

自动记录的实图：[捕获及奖励使用](evidence/cursed-investigators/sample/journal-captures.png)、[咒灵观察次数](evidence/cursed-investigators/sample/journal-observations.png)、[参考页](evidence/cursed-investigators/sample/reference.png)。记录来自公开已提交状态，第三项咒物挑战的奖励可用与击败咒灵后的捕获勾选分开测试。最后将挑战标记缩小并放在牌面下方空隙，避免挡住完整咒灵牌文字；规则和动作序列不变。

[资源校验](evidence/cursed-investigators/sample/assets.json) 核对 40 张保留扫描、40 张运行派生图和 27 张统一能力条；原图哈希全部保持。能力条逐张目检，全部文字完整，同一能力共用一张图。字体、许可证、动画、文档和图标在游戏包内，包约 85.6 MiB；不携带原 PDF 或重复原图，不查询系统已安装字体。

### 整体审核

已核对引擎／游戏边界、隐藏投影、作者模式、资源冻结与引用、视图生命周期、动画权威状态分离、卡牌效果次序、职业／奖励分支和导出保护。未引入 sample 专用引擎分支或新 CLI 命令。新增 UI 能力属于 Player／共享视图；既有动画编辑器实现遵守 CordiSharp 插件生命周期。

[Release 构建](evidence/cursed-investigators/sample/build.log) 为 0 警告／0 错误；[引擎回归](evidence/cursed-investigators/sample/engine.log)、[字体和文档控件](evidence/cursed-investigators/sample/font-documents.log)、[大包导出](evidence/cursed-investigators/sample/large-package.log) 及 [导出防篡改](evidence/cursed-investigators/sample/export-regression.log) 通过。验证使用本机 Windows 和实际 Direct3D 视口，不声称另做干净 Windows Sandbox 验收。

最终运行指纹为 `A40916F494DA6A1BF74382558B7367345860D286A3BE0B0258A26A1C3E2A1BA9`。[最终校验](evidence/cursed-investigators/sample/validate.json)、[实际打包](evidence/cursed-investigators/sample/pack.json)、[独立导出](evidence/cursed-investigators/sample/export.json) 由 `final-v9/bgb.exe` 完成。[标准模式最终内容复验](evidence/cursed-investigators/sample/cli-run.json) 使用 v8 CLI，同种子 369 次操作正常结束、重复／回放一致；v9 只补强导出根目录检查，未改变规则、加载或对局执行。包内容指纹一致，[227 个生产源码摘要及交付文件哈希](evidence/cursed-investigators/sample/delivery-check.json) 核对通过。

[最终战役运行](evidence/cursed-investigators/sample/campaign-run.json) 为种子 1＋初学者：748 次操作，首局通关，第二局累计捕获 7 件、声望 40 后健康归零判负，无故障。这个结果验证跨局路径和失败结算，不声称自动策略赢得整场战役。

最终 `final-v9` 导出的正式 Player 从临时工作目录启动，[连续两局获胜](evidence/cursed-investigators/sample/player-two-games.json)，共 666 次按钮与 454 次双击操作，重开后的最终投影完全一致。独立目录为 `artifacts/cursed-investigators/player`，直接双击 `Play.exe`；包内资源足够运行，不需要源码、原 PDF、下载目录或系统 Noto 字体。

任务全部完成后再次整体核对：本组无未勾选项，示例与验收文档本地链接有效；最终校验／打包／导出成功，标准对局重复与回放一致，独立正式 Player 两局获胜，227 个生产源码与最终发行物构建摘要一致。追加 [根目录替换回归](evidence/cursed-investigators/sample/export-linked-root.log)，实际将导出暂存根替换为符号链接，确认拒绝发布；本机未跳过该检查。审核无剩余阻塞。关卡／挑战者不在本轮确认范围内，CPU 不保证获胜；这些不是未完成项。
