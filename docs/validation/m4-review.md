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
- `http-*/result.json`：真实 HTTP 卡牌及拍卖对局，丢失回执重发、断线恢复和身份隔离；各玩家终局投影与服务器保存的权威录制回放逐一相等。
- `http-*/process-result.json`：单独服务器进程和两个独立自动客户端进程完成卡牌对局。
- `http-*/close.json`、`watch-quit.json`、`lobby-quit.json`：真实交互客户端未输入动作时响应对方离开；观战与大厅可直接退出。故障服务器的 `records/events.jsonl` 记录 CPU 故障，`diagnostics.json` 保留详细错误；公共数据不含私密诊断。
- `local-gate.log`：M3.5 门槛最终通过。卡牌、六角、拍卖、群岛各 20 个固定种子，共 80 局终局、重跑和回放一致；七个示例目录／包一致，空当接龙合法通关、WASM 和第二定义格式同样通过。明细位于 `artifacts/m3.5/gate/`。
- 自包含服务器／客户端通过 HTTPS 证书指纹连接。验证将 `DOTNET_ROOT`、`DOTNET_ROOT_X64` 指向空目录并禁用多级查找，不依赖开发 SDK。验证脚本为 `scripts/validate-network-release.ps1`；该证据仍是同一台机器，不冒充跨机或干净 Windows 验收。
- 最终发行目录为 `artifacts/m4/delivery-final/`，`.bgm` HTTPS 双客户端验收证据为 `delivery-final-check/result.json`；此前目录模块及包的验证分别见 `delivery-check/result.json`、`delivery-package-check/result.json`。`artifacts/m4/demo/` 提供卡牌包及本地服务器、两名真人客户端的 JSON 配置，可按下述命令直接运行。
- `quick-cards-gui.json`、`hex-skirmish-gui.json`：新发布的正式 Player 导出，各完成两局实际图形对局及重开一致性，`success=true`。过程中没有联机服务，Player 依赖仍不包含 Networking 或 Server。无需更改本地游戏的规则或启动入口。
- `scripts/check-boundaries.ps1` 通过。Server／NetworkClient 发布检查排除 Editor、Avalonia、MonoGame 与编辑器专用 CordiSharp 依赖。

一次 Release 重构建与正在运行的门槛进程争用 Windows DLL 锁；未中断验证进程，改用独立 Debug 输出完成审核修复回归。门槛结束后再次完成 Release 构建与受影响联机组验证（`final-regression.log`），不把该环境冲突写成测试失败或绕过检查。另修正 CPU 事件归属，记录实际发起席位并用混合对局覆盖。

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
artifacts/m4/delivery-final/Server/Server.exe artifacts/m4/demo/server.json
artifacts/m4/delivery-final/NetworkClient/NetworkClient.exe artifacts/m4/demo/p1.json
artifacts/m4/delivery-final/NetworkClient/NetworkClient.exe artifacts/m4/demo/p2.json
```
