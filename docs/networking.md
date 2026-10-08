# 联机会话与独立服务器

M4 复用 `GameRuntime.Load` 和 `PlaySession` 的 CordiSharp 运行时。`Networking` 提供身份与传输边界；`Server` 是独立、无图形窗口的权威进程，`NetworkClient` 是交互式命令行客户端，也提供全自动验证模式。图形 Player 在 M4 展示补充中通过“文件 → 联机 / 公共观战”接入同一服务；离线 Player 与编辑器的本地功能不依赖服务器。见 [扑克牌 UNO 运行说明](poker-uno.md)。

图形客户端可读取房间人数、席位占用和控制方式，加入或恢复原身份，并查看、保存和回放获准的投影记录。`OnlinePlayerSession` 统一串行化刷新、提交及恢复；提交回执丢失后恢复取得权威 nextSequence，不盲目重发用户动作。通信协议现为 2，`OnlineFrame.Controllers` 携带实际控制方式，不修改规则状态中的原始席位声明；版本 1 客户端在加入时明确拒绝。历史协议 1 投影记录仍可由当前读取器回放。`describe` 接口需房间密钥，仅返回公共房间与席位信息，不返回凭据、手牌或权威记录。

服务器及命令行客户端配置均可增加 `preset`，值为定义加载插件声明的预设名称。图形客户端选取相同预设；不同预设产生不同指纹，不能互相加入。

## 构建与启动

维护者运行 `dotnet build BoardGameBuilder.slnx -c Release`。运行者可使用 `scripts/publish-network.ps1 -Output artifacts/network-release` 生成的 Windows 自包含发行目录，不需要 .NET SDK。服务器发行目录中的 `worker` 必须随程序一起分发。

创建 `server.json`，路径相对于配置文件：

```json
{
  "module": "../../samples/quick-cards",
  "listen": "http://127.0.0.1:5080/",
  "roomKey": "replace-with-a-private-random-room-key",
  "controllers": {"p1":"human", "p2":"human"},
  "seed": 42,
  "output": "records"
}
```

运行 `Server.exe server.json` 即创建一个会话。所有配置为 human 的席位加入后开始；cpu 席位由服务器推进。模块可以是目录、`game.start.json` 或 `.bgm`。更换模块或重开对局应使用新的进程、记录目录及客户端身份文件。

为每个客户端准备独立配置，如 `p1.json`：

```json
{
  "module": "../../samples/quick-cards",
  "server": "http://127.0.0.1:5080/",
  "roomKey": "replace-with-a-private-random-room-key",
  "seat": "p1",
  "credentials": "p1.identity.json",
  "recording": "p1.recording.json"
}
```

运行 `NetworkClient.exe p1.json`，按编号选择规则提供的动作。第二个客户端使用 p2 和不同身份／记录文件。`d` 暂时断线；重新执行相同配置会恢复身份并追赶状态。`q` 主动离开并结束当前会话。关闭或失联会显示 `session-closed`、`server-unreachable` 或 `server-timeout`；网络恢复后可重新执行配置重连。添加 `"auto":true` 可由客户端逐次选择第一个获准动作，用于自动验收；它仍以普通玩家身份提交，不取得 CPU 或权威权限。添加 `"audience":true` 并省略 seat 可进入公共观战。

身份文件包含恢复凭据，应只交给所属玩家。房间密钥控制加入资格，不能替代席位凭据。模块指纹包含脚本、定义、资源及实际运行插件版本／配置，任一不一致均拒绝加入和恢复。客户端不会上传脚本、插件或服务器文件路径。

恢复令牌只在签发它的存活会话内有效，默认从首次加入起 24 小时失效；`options.resumeLifetime` 可配置正的 TimeSpan，最大 30 天，例如 `"12:00:00"`。恢复不会延长期限。超过期限返回 `resume-expired`；已经在线的连接可继续对局，但再次断线后不能恢复，需要宿主结束或重新开局。主动离开或观众过期回收会立即撤销身份。恢复令牌保持稳定以便重连回执丢失后重试，每次恢复仍轮换连接 token。它是 bearer 凭据，有效期内泄露仍可能导致夺席；不得共享身份文件，目录访问权限由运行者的操作系统账户控制。

## 跨机连接

回环地址允许 HTTP。跨机监听必须使用 HTTPS：server.json 的 listen 改为 `https://0.0.0.0:5081/`，添加 `"certificate":"server.pfx"`；证书密码从 `BGB_CERT_PASSWORD` 环境变量读取。客户端 server 改为服务器地址，并使用系统信任的证书；私有测试证书可以通过 `certificateSha256` 固定证书 DER 字节的 SHA-256 指纹。服务端证书／私钥仅保留在服务器上。

两台机器需有相同游戏包；只将 NetworkClient 发行目录、游戏包、客户端配置和公开证书指纹交给玩家。按部署环境允许服务器 TCP 端口，不需要 UDP、广播发现或编辑器进程。当前跨机人工验收尚未完成，本机三个独立进程不能替代该项证据。

## 同步、断线及记录

- 每席位从 1 开始发送请求序号，并携带观察到的 revision。权威端串行校验及提交；同序号相同内容的最近一次重发返回原结果，旧序号／跳号拒绝，同序号不同内容拒绝。过期及普通无效提议消耗请求序号，调用方使用 receipt.nextSequence 继续，但不递增共享 revision、不追加投影快照、不消耗版本额度；拒绝结果在回执及服务器审计中可查。新发生的规则故障会发布一个状态版本。并发秘密提交可以刷新状态后重试；客户端时间和随机结果不参与判定。
- 状态帧就是完整的身份投影快照，revision 随状态变化单调增加，客户端忽略迟到或重复帧。`catch-up` 和恢复响应按游标补发历史快照；命令行客户端原子保存已接收的 lastRevision，重连用它追赶并应用返回帧。旧身份文件缺少游标时仍从 −1 开始；没有更新帧时直接读取当前快照。恢复时旋转连接 token，保留未过期的 resumeToken、席位、请求进度及已处理结果。恢复仅支持同一仍存活的服务器会话，不承诺进程崩溃后自动恢复。
- 默认空闲 15 秒判为断线，恢复宽限 2 分钟；超时默认继续等待，不自动接管。可配置 `options` 的 `idleTimeout`、`reconnectGrace`（TimeSpan 字符串）及 `onTimeout`：0 等待、1 结束、2 CPU 接管。CPU 接管要求该模块启用 CPU 能力；重连在命令边界收回控制权。观战断线不终止对局；超过宽限释放观众身份，之后可重新加入并取得公共时序。
- 权威逻辑截止继续使用游戏规则的收集步数。可配置 `options.timers`，例如 `[{"seat":"p1","actionId":"tick","interval":"00:00:01"}]`，由服务器时钟提交规则当前提供的动作；该动作不再提供给真人客户端。服务器不识别拍卖、卡牌或其他游戏名称。
- 观战者只能看到公共投影，没有命令权限。玩家及观战记录包含从开局起的完整状态变化时序与脱敏事件，不包含内部命令、随机状态、脚本诊断或其他席位隐藏值。秘密提交的参与席位、已提交／锁定状态是公开的协调信息，所提交的值在规则允许揭示前不可见。`OnlineRecording.Replay()` 只播放历史投影；之后揭示的信息不会倒灌进早先帧。
- 权威录制的访问策略为仅限宿主、赛后也不自动公开，无远程访问接口。它的权限边界是服务器进程及记录目录的操作系统 ACL；房间密钥和玩家／观众身份均不授予权威日志读取权。部署时应把记录目录留在宿主账户私有位置。终局、故障及正常关闭时保存 `records/authority.json` 和含详细故障的 `diagnostics.json`；脱敏运行事件写入 `events.jsonl`。在记录目录创建 `stop.request` 可请求正常关闭，服务器先通知在线客户端再退出。请使用新的记录目录启动下一局。
- 默认最多保留 4096 个状态版本、32 个观众，可用 `maxRevisions`、`maxObservers` 调整。达到版本上限结束会话并保留已接收的投影时序。HTTP 请求体上限 64 KiB，连接上限 64。发行物中不包含编辑器和渲染依赖。

## 验证

构建解决方案后运行：

```powershell
dotnet tests/Engine.Tests/bin/Release/net10.0/Engine.Tests.dll online-identity online-ordering online-reconnect online-audience online-secrets online-http online-processes online-faults
```

自动化证据位于 `artifacts/m4/`。其中身份文件／服务器配置含测试凭据，不作为公开回放分享；玩家投影记录可以交给对应玩家。
