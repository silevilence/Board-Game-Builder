# M4 联机验收与整体审核

日期：2026-10-08。基线 `db0fce255a7388294384d27fbe6a40023d866f02`。M2 和 M3.5 已有单人闭环验收是本阶段的前置条件。

## 状态与范围

七项中六项通过本机验收；独立服务器的实现、发布及本机多进程验收通过，但“两台机器上的客户端接入”尚未验证。用户明确没有第二台机器，要求将该项保留为阻塞，因此 ROADMAP 的独立服务器顶层条目不勾选，也不宣称 M4 全部完成。原有 ROADMAP 阶段搬移属于用户已有修改，实施提交仅纳入本阶段的勾选与状态说明。

可用入口为 `Server.exe` 和 `NetworkClient.exe`，并提供可复用的 `OnlineClient`。联机客户端是命令行工具；现有图形 Player、编辑器试玩与本地导出继续独立工作。运行方式、HTTPS 部署、恢复凭据、超时配置及记录权限见 [联机使用说明](../networking.md)。

| ROADMAP 任务 | 验收结果 |
| --- | --- |
| 权威会话与席位身份 | 两身份、版本拒绝、席位冒用拒绝、主动离开通过 |
| 有序命令与投影 | 请求重发／跳号／过期、迟到帧、双人及 CPU 混合终局、与权威回放投影一致通过 |
| 断线重连 | 真人／CPU 回合断线，凭据轮换，丢失回执后重发，等待／结束／CPU 接管策略通过 |
| 观战与记录 | 公共身份无操作权限，中途追赶，个人历史投影回放，提前帧不含后来揭示值通过 |
| 秘密提交 | 原拍卖规则复用，重发／锁定／迟到／CPU／恢复、服务端定时逻辑截止及揭示隔离通过 |
| 独立服务器 | 实现及本机 HTTP、HTTPS、自包含发行通过；跨机人工验收阻塞 |
| 联机故障与本地回归 | 延迟、失联、重复乱序、身份冒用、非法请求、CPU 故障、主动离开、正常关闭／不可达及本地导出通过 |

## 自动化与发行证据

证据保留在忽略目录 `artifacts/m4/`，不将测试凭据或私钥提交 Git。

- `full-regression.log`：全量默认 57 个组通过，包括权威／脚本／秘密提交／WASM／格式／编辑器文档／两种试玩／卡牌／六角／群岛及原有本地热座。此轮发生在审核修复前；修复限定在联机层，随后重跑相关组。
- `online-final.log`：审核修复后的 10 个联机组全部通过：identity、ordering、reconnect、audience、secrets、http、processes、faults、review、client-review。
- `lifecycle-final.log`：补充持久生命周期日志后，重连、故障、动作与观众回收、真实交互客户端四组通过。断线、连接超时、CPU 接管及观众失效有服务端时间记录，不记录连接凭据。
- `http-*/result.json`：初版仅记录 success、sample、resumed、revision，不能单独证明全部断言。丢失回执重发、身份隔离及终局投影与权威回放相等由 `OnlineTransportTests.RunAsync` 的断言和 `online-http` 测试结果证明。本轮后续版本在所有断言通过后才写 result，并增加 duplicateReceiptEqual、identityIsolation、finalProjectionMatchesReplay 字段；旧证据文件未改写。
- `http-*/process-result.json`：单独服务器进程和两个独立自动客户端进程完成卡牌对局。
- `http-*/close.json`、`watch-quit.json`、`lobby-quit.json`：真实交互客户端未输入动作时响应对方离开；观战与大厅可直接退出。故障服务器的 `records/events.jsonl` 记录 CPU 故障，`diagnostics.json` 保留详细错误；公共数据不含私密诊断。
- `local-gate.log`：M3.5 门槛最终通过。卡牌、六角、拍卖、群岛各 20 个固定种子，共 80 局终局、重跑和回放一致；七个示例目录／包一致，空当接龙合法通关、WASM 和第二定义格式同样通过。明细位于 `artifacts/m3.5/gate/`。
- 自包含服务器／客户端通过 HTTPS 证书指纹连接。验证将 `DOTNET_ROOT`、`DOTNET_ROOT_X64` 指向空目录并禁用多级查找，不依赖开发 SDK。验证脚本为 `scripts/validate-network-release.ps1`；该证据仍是同一台机器，不冒充跨机或干净 Windows 验收。
- 首轮发行目录为 `artifacts/m4/delivery-final/`，`.bgm` HTTPS 双客户端验收证据为 `delivery-final-check/result.json`；此前目录模块及包的验证分别见 `delivery-check/result.json`、`delivery-package-check/result.json`。本轮审核修复后的发行物见下方补充记录。`artifacts/m4/demo/` 提供卡牌包及本地服务器、两名真人客户端的 JSON 配置。
- `quick-cards-gui.json`、`hex-skirmish-gui.json`：新发布的正式 Player 导出，各完成两局实际图形对局及重开一致性，`success=true`。过程中没有联机服务，Player 依赖仍不包含 Networking 或 Server。无需更改本地游戏的规则或启动入口。
- `scripts/check-boundaries.ps1` 通过。Server／NetworkClient 发布检查排除 Editor、Avalonia、MonoGame 与编辑器专用 CordiSharp 依赖。

一次 Release 重构建与正在运行的门槛进程争用 Windows DLL 锁；未中断验证进程，改用独立 Debug 输出完成审核修复回归。当时的构建成功由会话工具输出确认；`final-regression.log` 和 `lifecycle-final.log` 只有测试 PASS 行，不作为 Release 构建日志。后续审核修复单独保存构建输出，见下方补充记录。另修正 CPU 事件归属，记录实际发起席位并用混合对局覆盖。

## Standards 审核

code-review 技能的独立 Standards 审核没有发现明确项目规范违规，发现两项实现判断问题，均采纳并修复：

1. 用命令 Kind 判定保留定时动作会误拒绝同类其他动作。改为完整命令匹配非保留的合法动作；覆盖同 Kind、不同参数的合法操作及伪造定时动作。
2. 失联观众不释放名额。改为宽限结束后撤销凭据并回收名额，终局后继续维护；覆盖上限、回收、新观众加入和旧凭据拒绝。

审核者已只读复核，确认两项解决，原审核范围无剩余发现。

## Spec 审核

独立 Spec 审核发现三项需求缺口，均采纳并修复：

1. 等待控制台输入时丢弃轮询状态，不能及时显示关闭、终局、故障或动作变化。改为独立输入任务，每次循环处理最新帧；真实进程验证无需输入即可响应关闭。
2. 大厅、观战及非行动期间没有退出输入通道。现在 q/d 独立于动作列表可用性；分别验证大厅和观战直接退出。
3. CPU 故障只反映在临时帧，服务器未保存详细诊断。新增故障状态事件与受保护的诊断文件，在故障／终局／正常关闭时保存；没有新增远程权威日志接口。另补齐断线、超时、接管等生命周期事件。

审核者已只读复核，确认三项解决；未发现确定的投影泄密或范围扩张。额外发行实测修复了 HTTPS 初始化遗漏以及 Lua 启动文件的重复发布，两者均有最终发行验证。

审核计数：Standards 2 项已解决；Spec 3 项已解决。剩余阻塞仅为独立服务器跨机人工验收。恢复身份仅在同一仍存活的服务器内有效，进程崩溃后的持久恢复、图形联机界面和自动赛后全信息公开不属于已交付能力。

## 本机演示

从仓库根目录，在三个终端分别执行以下命令。演示的客户端默认等待真人输入；q 主动离开，d 暂时断线。凭据文件用于恢复同一个存活会话；开始全新会话时使用新的配置／身份文件和记录目录。

```powershell
artifacts/m4/review-followup/delivery/Server/Server.exe artifacts/m4/demo/server.json
artifacts/m4/review-followup/delivery/NetworkClient/NetworkClient.exe artifacts/m4/demo/p1.json
artifacts/m4/review-followup/delivery/NetworkClient/NetworkClient.exe artifacts/m4/demo/p2.json
```

## 外部审核意见处理（2026-10-08）

本轮按报告的顺序拆为 19 条（包含明文凭据提示；配套范围问题按原报告一组处理）：完全采纳 8 条、部分采纳 5 条、不采纳 6 条。上面的 57 组与 80 局是首轮证据，本轮没有将它们重复计为新验证，也没有调整 ROADMAP 的状态或用户自己的段落搬移。

| 编号 | 问题与严重度 | 决策 | 核实、修改或不采纳依据 |
| --- | --- | --- | --- |
| S1 | 恢复令牌没有到期限制（重要） | 部分采纳 | [OnlineSession.Resume](../../src/Networking/OnlineSession.cs#L199) 原先仅轮换连接 token。增加首次签发起算的恢复期限，默认 24 小时，最多配置 30 天，重连不续期；不做单次旋转，避免重连回执丢失后永久丢席。它始终只在单个存活会话内有效，期限内 bearer 泄漏风险仍存在，已明确写入文档。 |
| S2 | 身份文件明文保存两类 token（提示） | 不采纳 | [客户端](../../src/NetworkClient/Program.cs#L40) 使用本地身份文件恢复。没有账户体系或密钥保管设施，新增加密文件却把解密密钥放在同一目录不能解决威胁；维持操作系统账户／目录权限边界并明确不得共享。写入改为临时文件原子替换，服务于游标与凭据一致性。 |
| S3 | RoomKey 的 null 检查位置（轻微） | 部分采纳 | [配置校验](../../src/Server/Program.cs#L149) 原表达式正确，交换两个 OR 条件也不会导致所述错误；采纳可读性建议，改为先检查 null／空白，再检查长度。 |
| S4 | 观众哨兵与协议／格式魔数（轻微） | 部分采纳 | 增加 [SeatView.AudienceSeat](../../src/Kernel.Abstractions/Projection.cs)、[OnlineProtocol](../../src/Networking/OnlineProtocol.cs#L7) 常量。保留内核 null（无所属席位）与线上空字符串的既有表示，避免无需求的协议迁移。 |
| S5 | 控制器裸字符串（轻微） | 不采纳 | human/cpu 已用于作者配置、内核席位模型与运行时边界，不是新增联机独有概念。当前所有入口都有封闭值校验；改成枚举需要跨现有格式迁移，超出此轮必要修复。 |
| S6 | RPC 操作字符串重复（轻微） | 完全采纳 | [OnlineOperations](../../src/Networking/OnlineProtocol.cs#L12) 集中六个操作名，Server 与客户端共用；线上字符串及未知操作拒绝行为保持不变。 |
| S7 | SeatLease 相邻不透明 token（轻微） | 部分采纳 | [CreateLease](../../src/Networking/OnlineSession.cs#L265) 集中签发，并使用具名参数 Token／Seat／ResumeToken；不为三个字段再引入包装类型和序列化适配。 |
| S8 | 定时动作过滤重复（轻微） | 完全采纳 | [AvailableActions](../../src/Networking/OnlineSession.cs#L272) 作为提交与快照的唯一过滤路径，沿用同类不同参数动作的回归。 |
| S9 | 修改后恢复以预检 CPU 能力（轻微） | 完全采纳 | [IGameSession.SupportsCpu](../../src/Runtime/RuntimeContracts.cs#L49) 从实际 CPU 提供者读取；构造时只查询，不再临时改变控制器。无 CPU 模块的接管配置仍提前拒绝。 |
| S10 | OnlineSession 职责多（判断） | 不采纳 | 各职责共同依赖同一锁内的提交／投影一致性，不同变化原因的具体缺陷尚未给出。此轮只提取已确认重复的动作过滤与身份签发；不按行数拆出多套同步边界。 |
| S11 | README 与架构决策记录缺联机内容（轻微） | 完全采纳 | 更新 [README](../../README.md) 架构说明，在现有 [独立运行时 ADR](../adr/0001-independent-runtime-plugins.md) 补充 Server 边界、HTTP 认证选择与宿主记录权限。没有新增审核 Markdown 文件。 |
| P1 | 没有快照路径、客户端全量恢复（轻微） | 部分采纳 | [OnlineFrame](../../src/Networking/OnlineSession.cs) 本身就是完整 SeatView 快照，并非只有增量；不重复构造新快照体系。修复客户端固定 −1：保存 LastRevision，向 Resume 传入游标并应用返回快照；空追赶结果读取当前快照。独立 catch-up RPC 保留给其他客户端。 |
| P2 | 权威日志／赛后公开没有权限策略（判断） | 不采纳 | Server 无远程权威读取入口；现行策略是仅宿主可读、赛后继续不公开，权限由宿主账户与目录 ACL 控制。ROADMAP 没有要求必须新增远程管理员或公开功能；已在说明及 ADR 明确这个拒绝访问的策略。 |
| P3 | 跨机尚未验证（已知阻塞） | 完全采纳 | 保留用户确认的环境阻塞，服务器顶层与子项继续未勾选。本轮本机 HTTPS 测试仍明确 separateMachines=false。 |
| P4 | HTTPS 等配套被认为超出范围（判断） | 不采纳 | HTTPS／证书指纹／证书密码与 roomKey 保护席位身份；自包含发布、auto 模式与演示配置用于独立部署验收；请求／连接／版本／观众限额、stop.request、过期回收与生命周期日志支持故障处理。它们支撑 M4 任务 1／6／7，没有扩大游戏规则或引擎依赖边界，故不删除。 |
| P5 | 过期提交仍推进共享版本（重要） | 完全采纳 | [Submit](../../src/Networking/OnlineSession.cs#L142) 对过期及普通无效提议只消耗自身请求序号，回执可幂等重试，不追加快照。仅接受的动作或新规则故障推进版本。1,000 次拒绝后仍可完成下一合法动作，版本额度不变；故障状态仍发布一次。 |
| P6 | 揭示前可见已提交席位（轻微） | 不采纳 | 已提交／锁定名单是既有 M3 公开协调元数据，秘密内容是提交值。删除它会改变原规则及行动提示契约。补充说明，沿用测试确保 OwnValue／Revealed 对无权身份仍不可见。 |
| P7 | HTTP result.json 的证据归因过宽（轻微） | 完全采纳 | 修正历史文件说明；[OnlineTransportTests](../../tests/Engine.Tests/OnlineTransportTests.cs#L126) 在终局投影比对通过后再写成功汇总，并明确记录重发相等、身份隔离和回放相等三个断言结论。 |
| P8 | 用 PASS 文件佐证 Release 重建（轻微） | 完全采纳 | 区分历史工具输出与测试日志，本轮将完整构建输出保存到 `review-followup/build.log`；不再用只有 PASS 的文件充当构建证据。 |

### 本轮验证

- `artifacts/m4/review-followup/build.log`：解决方案 Release 构建，0 警告、0 错误。
- `tests.log`：ordering、secrets、http、client-review、followup、cursor 共 6 组通过。新增用例覆盖拒绝风暴不耗版本、新规则故障仍发布、恢复凭据绝对到期、不因重连续期、丢失恢复回执的重试、增量游标及空追赶结果、真实客户端重启后游标保存。
- `regression.log`：其余 6 个联机组及 visibility、cpu、runtime-host、runtime-entry 共 10 组通过；合计 12 个联机组和 4 个相关本地组。本项目未要求此次变更的覆盖率门槛，未报告虚构的覆盖率数字。
- 工程边界及 `git diff --check` 通过。跨机验收仍是唯一需外部环境的阻塞。
- 修复后自包含发行物为 `artifacts/m4/review-followup/delivery/`；发布过程见 `publish.log`，HTTPS `.bgm` 双客户端验证见 `delivery-check/result.json`。旧发行目录保留为历史证据，不包含本轮修复。
