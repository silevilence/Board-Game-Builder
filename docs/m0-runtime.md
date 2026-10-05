# M0 运行时与脚本协议

当前需求以 ROADMAP.md 为准。本页说明 M0 已实现的接口和开发验证方式。

## 项目边界

- Kernel.Abstractions：可序列化实体、命令、投影、日志和规则协议，不依赖任何包。
- Kernel：本地权威状态、原子事务、结构校验、投影、带种子随机和回放。
- Modules：目录中的 module.json、资源和脚本加载与内容指纹。
- Lua：MoonSharp 适配和每次调用独立的资源受限执行进程。
- Runtime：共享 PlaySession、CPU 席位调度和 BatchRunner。
- Player：命令行参数、输入和显示；Editor、View.Abstractions、View.MonoGame 暂为 M1 预留项目边界。

运行时以单个调用线程顺序推进；UI 宿主应在后台线程调用耗时的脚本操作。Kernel 不引用宿主或渲染类型。发布无需 SDK 的 Player 是 M1 的验收，M0 的开发命令依赖已安装的 .NET 10 SDK／运行时。

## 目录模块

module.json 声明 format=1、id、version、rules、cpu、resources、seats、phases 和 entities。席位包含 id、team、controller（human/cpu）。每个实体包含 id 和 components 数组，组件为 type、data 及可选 required（默认 true）。组件数组无序，同类组件不能重复，容器 contents 数组的牌序有意义。

支持 owner、visibility、container、faces、position、properties。未知必需组件报错；未知可选组件被忽略。资源与脚本路径必须指向模块目录内的真实文件，不接受绝对路径、越界路径或链接。格式版本、席位、资源引用、容器容量、多重归属和循环引用均校验。实例 properties 暂为整数；更丰富的作者组件编辑属于后续里程碑。

## 投影与提交

PlaySession.View(seat) 返回分离的 SeatView。隐藏容器公开外壳和数量，不公开内容身份；自己手牌显示牌面，对方手牌省略实体；公开背面不携带牌面及属性。位置、容器外壳身份和数量按媒介元信息处理，模块作者不要在这些公开身份中编码秘密。

所有改动经 GameEngine.Submit，先在候选状态执行整批命令，全部成功后替换权威状态。移动、翻面、属性设置、容器转移、抽牌、洗牌、展示、阶段切换、顺序轮转与结束均由同一结构校验实现。规则可以为其他席位发牌或改写公开计分，但不能按猜测身份访问投影外的单张牌。

原始命令日志和诊断仅向本地权威宿主开放，含隐藏信息；玩家只获得投影、拒绝代码和不含命令参数的 PlayerLog。CLI 的日志文件是作者诊断材料，不应作为普通玩家日志分发。

## Lua

脚本返回函数表；规则定义 actions(ctx)、decide(ctx)，CPU 定义 decide(ctx)。ctx.view 为当前席位投影，规则决策还获得 ctx.command，CPU 还获得 ctx.actions。Lua 没有宿主对象或权威状态引用。

动作返回 `{id, label, command}` 的数组；规则返回 `{allowed, reason, commands}`，commands 是改写后的基础命令和补发命令的有序数组。拒绝理由保存到作者诊断，普通玩家获得稳定拒绝代码。结束通过 `{kind='finish', winners={'p1'}}`，空 winners 表示无胜者。派生命令不递归触发规则，每次最多 64 条，全部仍经过结构与可见性校验。

每次调用重新创建脚本实例，持久游戏数据必须经命令存入实体属性。导出的 table 必须是有限、无环、纯数据结构。脚本禁用文件、系统、网络、动态加载、CLR 对象和 math.random。CPU 用 random_index(n) 获取受种子控制的 1..n 索引。

执行限制为每个加载／入口约 210,000 条 VM 指令、单次工作进程 5 秒、64 MiB 托管堆和 256 MiB 进程私有内存；输入、输出、字符串、结果深度及节点数另有限制。工作进程结束后释放全部脚本状态。MoonSharp 的部分库调用不能协程抢占，因此另设独立进程的时间和内存限制。实现依据：[MoonSharp 沙箱说明](https://www.moonsharp.org/sandbox.html)、[协程抢占限制](https://www.moonsharp.org/coroutines.html)。

## 确定性与诊断

模块指纹覆盖身份、版本、规范化初始状态、规则／CPU 源码及资源内容哈希。权威日志记录初始局面、种子、API 与模块指纹、每次提交及接受后的实际命令、洗牌结果和状态哈希链。回放只执行记录的基础命令和随机结果，不重新调用规则或 CPU。哈希链用于损坏检测，不是防伪签名。

CPU 只能选择所获动作列表中的命令；没有动作时不会自行跳过，规则应显式提供 pass 或结束动作。CPU 非法输出、异常、超限及无进展停止会话；批量验证另设步数上限，保存原始模块快照、日志和诊断。重现时使用同批次 module.snapshot.json 保持原始脚本与初始配置。
