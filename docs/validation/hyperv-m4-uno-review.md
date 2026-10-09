# M4／扑克牌 UNO 主机与 Hyper-V 验证

日期：2026-10-09。基线 `775a83528d1b5175e68f09251c676c36dc6e1b30`，包含本轮身份文件保存修复及验证入口增强。按用户指定，使用本机与 Hyper-V 虚拟机作为两个独立 Windows 环境；不称为两台物理电脑。

## 结论

- M4 独立服务器跨机验收通过，七个顶层任务均已有通过证据。
- UNO 跨机图形对局、两端重连、观众隔离及记录回放通过，完成「M4 展示验收与能力覆盖清单」。
- 虚拟机无 .NET SDK，本地图形验证及独立图形联机通过；2026-10-09 用户确认启动／游玩／退出无控制台闪现及目标 DPI／窗口尺寸下的交互，「干净 Windows 人工验收」完成。人工结论来自用户确认，与截图和自动化证据分别记录。

## 环境与产物

| 项目 | 实际环境 |
| --- | --- |
| 主机客户端 | `SILSURFACE10`，Hyper-V Default Switch 地址 `172.30.128.1`，图形玩家 p2 与公共观众 |
| 虚拟机客户端 | `DESKTOP-BA23QVD`，Windows 10 Pro 10.0.19045，地址 `172.30.134.163`，图形玩家 p4 与公共观众 |
| 独立服务器 | 虚拟机 SYSTEM 非交互任务／会话 0；自包含 Server，无编辑器或渲染窗口；HTTPS 5081、固定证书指纹 |
| 规则与控制器 | 同一 UNO 模块，六人预设、种子 1；p2／p4 为真人身份，其余由服务器 CPU 推进 |
| 最终游戏发行 | `artifacts/hyperv-20261009/uno-verified/`；虚拟机 `C:/BgbTest-20261009/uno-verified/` |
| 最终网络发行 | `artifacts/hyperv-20261009/network-verified-release/`；虚拟机服务器位于 `verified-server/Server/` |

两端最终 `game.bgm` 的 SHA-256 均为 `983F8950BD299DE7AEA515C8DD67FBB06AF608B8AB3976E1F2708ECB336871EC`。六人模块指纹为 `02C077950E3700D329D61A37E34AA9C4662D82CFED5298B72406BD5F3A654E7B`。包文件哈希与模块语义指纹是不同指标。

虚拟机 `dotnet --list-sdks` 为空，已有 .NET 10.0.10 运行时；没有部署本项目编辑器或 SDK。运行的是自包含导出包，不将该环境描述为完全没有 .NET 运行时。RDP 观察尺寸为 1440×960，未遍历所有 DPI。环境详见 `guest-evidence/final-environment.json`。

## 实际验证与证据

以下路径均相对 `artifacts/hyperv-20261009/`；原始记录及测试凭据位于 Git 忽略目录，不将房间密钥、恢复身份或证书私钥提交到仓库。

| 验证 | 结果与证据 |
| --- | --- |
| 第一轮六人跨机图形验证 | `auto6-host/result.json`、`guest-evidence/auto6-guest.json` 均成功；两端分别在真人和 CPU 回合恢复；最终 revision 50、p3 获胜。`auto6-audit.json` 和 `authority-replay-check.log` 校验投影及权威重放。 |
| 最终发行包六人复验 | `verified6-host/result.json`、`guest-evidence/verified6-guest.json` 均成功；主机玩家／观众渲染 297／297 帧，虚拟机 125／125 帧；同一终局 revision 50、p3 获胜。 |
| 最终复验恢复边界 | 主机真人回合 5→5，CPU 回合 9→11；虚拟机真人回合 7→7，CPU 回合 9→13。均恢复原席位，CPU 回合期间权威端继续推进。 |
| 隐藏信息与有序记录 | `verified6-audit.json`：四份身份记录各含连续 revision 0～50；玩家仅含本人手牌，观众无手牌及操作；未泄露隐藏牌堆实体。权威日志 48 条连续且接受的命令，只有一次结束结算。 |
| 权威重放一致 | `verified6-authority-replay.log` 实际执行权威日志重放，逐一与两玩家、两观众记录重放后的最终投影完整比较，全部相等；不只比较胜者。 |
| 版本拒绝 | `auto6-host/wrong-preset.result.json`：主机用五人预设连接虚拟机六人服务器，返回 `version-mismatch`，退出码 1。最终发行未修改模块指纹或版本准入逻辑。 |
| 普通图形界面走查 | 四人局 p4 使用虚拟机普通 Player；通过 Computer Use 操作联机入口、出牌、摸牌、结束回合、暂时断线／恢复和保存本人记录；观察到弃牌回收再洗牌，终局 p1 获胜，行动 76。`manual4-finished.jpg`、`guest-evidence/manual4-p4.recording.json`。该轮穿插诊断及辅助客户端重启，不作为一次无中断的最终版验收。 |
| 四人记录核对 | `manual4-authority-replay.log`：重放 113 条权威命令，p4 最终投影与其保存记录相等，revision 115、p1 获胜。 |
| 虚拟机本地图形验证 | `guest-evidence/local-gui.json`：4／5／6 人均完成；热座、分视角、全 CPU、人数设置／取消通过；53 张图集帧共享 1 张纹理。附 atlas 与各人数 PNG。另实际操作了普通本地界面的出牌／摸牌；不把局部鼠标走查称为人工完整离线验收。 |
| 服务生命周期 | 两轮六人及四人服务器保留 `events.jsonl`、权威与身份记录；`stop.request` 正常关闭，最终记录含 `host-closed-session` 与 `server-stopped`。正常关闭／不可达的客户端契约沿用既有 M4 故障测试证据。 |

六人图形验证由 `--verify-online-gui` 驱动真实 OnlineWindow、会话与渲染视口，未使用 CLI 操作牌局。普通四人走查由 Computer Use 操作 p4，另一端使用图形验证辅助程序；这两类证据与用户亲自观察的人工验收分开记录。

## 本轮发现及修复

普通界面走查时，主机图形辅助客户端间歇报告断开。保留最早异常后定位到 `OnlinePlayerSession.Save` 的 `File.Move(..., overwrite: true)` 返回拒绝访问，服务器没有规则故障。`manual4-host-retry/result.json` 保存该异常。此前只有 `disconnected` 的二次错误及串行化重连尝试不作为根因结论。

独立、单会话、单路径调用实际保存方法 10,000 次，出现 418 次相同拒绝访问；改用 `File.Replace` 的独立实验仍有 52／2,000 次失败。因此并非仅改变替换 API 即可解决。确认的是 Windows 文件替换短暂失败，未识别具体外部占用进程。最小复现及实验位于明确标为调试用途的 `debug-identity-save/`、`debug-file-replace/`。

新增真实客户端回归 `online-identity-persistence`，先在修复前用临时禁止删除共享的文件句柄复现拒绝访问，再验证释放锁后仍保持连接、永久只读仍上报错误、原身份可恢复、主动离开时清理文件能容忍临时锁。`persistence-red.log` 记录修复前失败。CLI 恢复回归也暴露同类访问失败，因此两种客户端共用 `OnlineIdentityFile`：保留原子替换，对 Windows 错误 5／32／33 最多异步等待 20、40、80、160、320 毫秒，支持取消，最终失败继续上报，不吞掉永久拒写。

修复后的原始保存压力复现为 10,000 次、0 失败（`debug-identity-save/fixed.log`）。最终共享实现的七组回归通过，随后使用重新发布的双端图形 Player 完成上述六人跨机复验。

其他失败如实保留：过期观众身份重用被拒绝后改为新观众；四人辅助程序等待鼠标操作达到 600 秒上限退出；其后接续程序在首次恢复动作之前就看到已结束对局，因此未满足验证程序的强制恢复断言。它们没有计为成功的自动整局。原版虚拟机 p4 在四人终局后也出现过断开显示，不能用该轮证明修复后双端稳定性。最终六人复验双方均使用最终包并全部通过。

## 构建、回归与收尾

- `fix-build.log`：解决方案 Release 构建通过，0 警告、0 错误。
- `fix-tests.log`：`poker-uno-online`（4／5／6 人）、`online-identity-persistence`、`online-ordering`、`online-reconnect`、`online-audience`、`online-client-review`、`online-cursor` 七组通过。
- `verified-publish.log`、`verified-export.log`、`network-verified-publish.log`：最终自包含发布与游戏导出成功。`uno-final/` 是诊断过程中替换过 DLL 的中间目录，不作为交付物。
- `boundaries.log`：工程依赖边界通过；`git diff --check` 通过。未修改游戏规则、CPU 策略、编辑器或历史需求设计文档。
- 已正常停止测试服务器、关闭本轮 Player，移除五个临时计划任务及 HTTPS 测试防火墙规则；SSH 服务和专用访问配置保留，便于后续操作。见 `guest-evidence/cleanup.json`。
- `preflight.json` 仅是最初访问受阻时的历史预检，不代表本轮最终状态；以本报告及最终证据为准。

人工确认（2026-10-09）：在列明剩余的无控制台闪现及目标 DPI 交互检查后，用户回复「这几个我确认了，勾选并提交」。据此勾选干净 Windows 人工验收及 UNO「作者工作流、图集打包与独立交付展示」顶层条目；M4 与 UNO 本轮验收项全部完成。该确认限于实际目标环境，不扩展为所有 DPI 或其他 Windows 环境均已验证。
