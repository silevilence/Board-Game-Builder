# 游戏运行时协议（M3.5）

## 职责与版本

固定启动文件为 `game.start.json`，协议版本 `format: 1`。它是包 ID、包版本和运行插件启用配置的唯一来源。插件条目包含 `id`、精确实现 `version`、接口 `api: 1` 和 `configuration`。不接受 DLL 路径、程序集名称或运行时编译请求。发行物提供获准目录，包只能选择其中实现。

游戏定义由唯一 `definition` 能力提供，返回 `GameDefinition`：初始状态及按能力命名的配置数据；资源由资源插件加载后装配到公共定义的资源池中。公共结果不要求 Lua 源码或 WASM 字段。规则提供 `IRuleProvider`，CPU 提供 `ICpuDecisionProvider`，扩展提供 `IEffectProvider`；具体数据和内容依赖由各插件解释。权威提交、结构校验、访问控制、投影、随机与确定性是必需能力，不允许通过去掉插件绕过。

`IAuthoritySession` 只暴露受控提交和复制数据。`IGameSession` 管理席位与推进。`IRecordingProvider` 保存及验证记录。作者全信息接口不得交给普通玩家、规则或 CPU。执行顺序固定为规则判断 → 扩展转标准命令 → 内核校验和原子提交 → 记录；不依赖事件订阅顺序。

插件 API、插件实现、启动协议、定义格式和记录格式分别版本化。内置 module 加载器解析定义格式 1；它的入口路径来自插件配置。第二种加载器有独立格式版本。运行身份格式 1 包含实际插件版本、规范化有效配置、定义来源和所有运行内容哈希。原始命令日志格式仍为 1；新快照／会话录制使用版本化运行身份绑定启动配置。

## 兼容矩阵

| 输入 | 正常启动 | 迁移／读取策略 |
| --- | --- | --- |
| 含 `game.start.json` 的目录 | 校验版本、获准目录和必需能力后启动 | 身份只从启动文件读取；定义中重复身份或插件声明拒绝 |
| 缺少启动入口的旧目录 | 拒绝，提示迁移 | 显式转换工具生成入口，将身份、Lua 和 WASM 配置移出定义；不猜测文件名后静默回退 |
| 旧 `.bgm` 格式 1 | 拒绝作为新局启动 | 解包旧来源并显式迁移后重打包；保留旧格式解析器用于历史诊断与测试 |
| 新 `.bgm` 格式 2 | 校验全部文件、启动配置及运行指纹后启动 | 目录与包使用相同入口和插件选择 |
| 旧无运行身份快照／录制 | 拒绝作为新运行时启动 | 旧命令日志可通过显式历史读取接口配套原始快照验证；不冒充新配置 |
| 新快照／录制 | 校验格式、内容及配置指纹 | 未知版本、缺失内容或哈希不符拒绝；脚本切换继续记录命令边界 |
| 直接传入无身份的 `LoadedModule` | `PlaySession` 拒绝 | 仅用于显式历史解析或直接内核测试；不得根据源码是否为空合成插件图 |
| 命令日志格式 1 | 仅匹配其快照及运行指纹 | 回放应用已记录标准命令和随机结果，不执行 Lua／WASM |

旧解析器是显式兼容工具，不是正常启动的回退路径。相同包版本下改变有效配置或内容同样改变运行指纹。未知配置项拒绝；启动配置的对象键顺序、缩进及插件列表顺序不影响身份，定义文件和资源使用原始内容哈希。当前静态规则缺省 actions 为空，与显式空数组仍保留不同配置身份。

`GameRuntime.Load` 也接受固定入口文件 `game.start.json` 的路径，将其解释为所属游戏目录；不会把任意定义文件名当作入口。`definition.state` 的必需字段、字段类型和状态校验错误包含插件 ID、定义来源及字段位置。测试夹具同样保留运行身份，历史测试显式调用旧解析器，修改脚本／状态的夹具通过新格式加载器重新生成身份。

## 内置目录与开发入口

| 插件（实现版本均为 1，API 均为 1） | 能力／配置 |
| --- | --- |
| kernel | authority、projection、random、rule-binding、flow-transactions；空配置 |
| definition.module | definition；`path` 指向实体／组件定义，可选 `presets` 声明开局定义预设 |
| definition.state | definition；`path` 指向 `{format:1,state:GameState}` |
| resources | resources；管理资源池与原始文件，`sources` 声明读取来源 |
| resources.images | resources.images；依赖 resources，`sources` 加载单图 |
| resources.texturepacker | resources.texturepacker；依赖 resources，`sources` 加载 TexturePacker JSON Hash |
| resources.font | resources.font；依赖 resources，显式加载随包字体和许可证，见 [字体协议](packaged-fonts.md) |
| resources.animations | resources.animations；依赖 resources，加载关键帧和公开状态触发配置，见 [动画格式](author/animation.md) |
| resources.documents | resources.documents；依赖 resources，加载分页图片与公开属性叠加，见 [分页文档](author/documents.md) |
| lua.rules | rules；`file` 指向 Lua 规则入口 |
| rules.commands | rules；可选 `actions` 标准动作列表，启动前校验结构 |
| flow | rules、flow；`path` 指向版本化流程图，`maxAutomaticSteps` 限定自动推进；与其他 rules 提供者互斥 |
| lua.cpu | cpu；`file` 指向 CPU 脚本 |
| wasm | effects；`extensions` 含 id/version/api/kind/file/sha256/permissions |
| session.local | session；空配置，按实际组合依赖 CPU／扩展／flow |
| recording | recording；空配置，依赖 authority |

完整最小配置见 `samples/state-minimal/game.start.json`；Lua 示例见 `samples/quick-cards/game.start.json`；WASM 见 `samples/wasm-counter/game.start.json`。新增内置实现使用 `RuntimePluginDefinition` 声明 Provides/Requires，在 `RuntimeComposition.Catalog` 注册，通过 scope 获取声明服务和提供能力，资源通过 `scope.Own` 归属 effect。不得订阅事件来决定权威命令次序。目录加载器提供 `GameDefinition` 与内容依赖；获准目录保持静态，不承诺运行时任意第三方插件发现。

可选流程模式详见 [流程协议](flow-runtime.md)，配置示例位于 `samples/flow-minimal`，Lua 卡牌流程位于
`samples/flow-cards`。流程文件及全部引用脚本独立收集进内容身份、快照和包；系统事务使用明确 system
身份、流程续行效果与普通效果一起原子记录。第一版流程不组合 WASM／秘密收集调度，以免多个调度器争夺控制权。

`GameRuntime.Load` 供宿主读取新输入，`PlaySession` 是公开门面，委托 `IGameSession`；旧 `LoadedModule` 字段仅为迁移适配，进入新会话须与公共定义和启动配置一致。快照／录制交叉验证插件版本、配置、定义来源、Lua 路径／内容、资源字节及 WASM 权限／载荷哈希，不在回放中执行脚本。发行清单的 `runtimePlugins` 由实际编译目录生成，导出同时验证 `.deps.json` 执行依赖。

## 定义预设与统一图片区域（M4 展示补充）

`definition.module` 可配置 `{"path":"module-4.json","presets":{"4 人":"module-4.json","5 人":"module-5.json","6 人":"module-6.json"}}`。默认 path 必须属于声明的预设；通过 `GameRuntime.Load(source, preset)` 选择，目录与 `.bgm` 语义一致。选择只替换加载插件的有效 path，人数和规则仍由各定义及脚本声明。所有预设的定义、资源和额外运行文件都进入包与内容哈希；打包验证每个预设。未知预设和越界路径拒绝加载。快照已包含选定配置，不能再指定预设。改变预设改变运行身份，联机各端必须一致。

图集使用 TexturePacker JSON Hash：在 `game.start.json` 启用 `resources` 与 `resources.texturepacker`，后者的 `sources` 指向图集 JSON，牌面直接引用 `frames` 中的名称（如 `"card_hearts_A"`、`"card_back"`）。图集 JSON 与 PNG 同放在 assets，`meta.image` 相对 JSON 所在目录解析，加载器自动收集图片、计算内容指纹并纳入包，无需重复声明图片或在牌面填写坐标。

支持 `frame`、`rotated`（顺时针打包后还原）、`trimmed`、`spriteSourceSize`、`sourceSize` 和 `meta.size`；视图与打印按原始透明画布尺寸还原，GPU 仍共享整张图集纹理。帧名在所有资源中全局唯一；重复帧、缺失图片、越界坐标、不一致的裁剪尺寸及越出模块的路径在加载时拒绝。使用单页 JSON Hash；多张图集分别声明，不自动跟随 multi-pack 链接。正常运行不再解释定义内的资源表或坐标字符串，旧格式仅由显式历史解析接口支持。

## 资源加载配置（2026-10-08）

资源声明统一放在启动插件配置中。例如下面三个条目可加入 `plugins`：

```json
[
  {"id":"resources","version":"1","api":1,"configuration":{
    "sources":[{"path":"player.json","name":"player.presentation"}]
  }},
  {"id":"resources.images","version":"1","api":1,"configuration":{
    "sources":[{"path":"assets/images","pattern":"**/*.png","prefix":"ui/"}]
  }},
  {"id":"resources.texturepacker","version":"1","api":1,"configuration":{
    "sources":[{"path":"assets/atlases","pattern":"*.json"}]
  }}
]
```

- `path` 是相对游戏根目录的文件或目录；`"."` 表示游戏根目录。禁止绝对路径、上级目录、反斜线和符号链接。`path` 不含通配符。
- 文件来源直接读取该文件；目录来源通过可选 `pattern` 筛选，省略时递归扫描整目录，单图插件选择 PNG／JPEG／GIF／BMP，图集插件选择 JSON，基础插件选择所有文件。`*.png` 只匹配该目录，`**/*.png` 包含子目录，`?` 匹配单个非路径分隔字符；匹配区分大小写、路径分隔符为 `/`。不支持字符集合模式。空匹配明确报错。
- 单图和原始文件默认以相对于来源目录的路径去掉最后扩展名命名，例如 `icons/star.png` → `icons/star`；单文件默认以文件名去扩展名命名，也可用 `name` 指定名称。`prefix` 直接加在名称前，分隔符由配置者提供。
- 图集使用 JSON 内的帧名，可设置 `prefix`，不能设置 `name`。目录筛选只选择图集描述文件，配套图片由 `meta.image` 自动读取；同目录混有 PNG 时使用 `pattern: "*.json"`。省略 pattern 时按上述扩展名选取，便于 JSON、PNG 与许可文件共存；显式 pattern 匹配到不支持的文件会报错，不静默跳过。
- 单图插件识别 PNG、JPEG、GIF、BMP 的图片尺寸；完整解码仍由视图／打印后端执行。原始文件插件不解释内容，普通 JSON 不会因包含 `frames` 字段而被当作图集。
- `sources` 可省略或为空；资源图像加载插件必须同时启用基础 `resources`。重复或跨插件冲突的名称拒绝，读取同一物理文件可复用字节。扫描和载荷沿用模块数量／大小限制。

加载插件在所属 CordiSharp effect 中向唯一资源服务登记加载器。全部加载成功后冻结为 `ResourcePool`，包含源文件字节、命名图片描述与命名原始文件。单图和图集归一化到相同图片描述；视图、Player、编辑器与打印按名称消费资源池，不解析作者图集格式。GPU 纹理由各视口拥有，同一内容共享缓存，随视口释放。

目录、包、快照和录制使用同一资源配置。源文件及图集依赖纳入内容哈希；快照保存资源池，恢复时仅从其中字节重建并交叉核对配置和描述，不读取原目录。迁移旧项目时将定义内 `resources` 移到相应启动插件中；旧资源快照须从迁移后的项目重新生成，不静默兼容。编辑器图片／TTS 导入同时写入文件、定义及单图插件配置，作为一个可撤销操作。

标准 `draw` 命令新增可选 `visibility`（owner／hidden／public），缺省仍为 owner；公开摸牌及将旧弃牌以隐藏状态移入有序牌堆均可由规则组合声明。归属仍取目标容器的 owner 或执行席位。隐藏抽取不向规则返回牌 ID，后续查询遵循目标容器及牌自身可见性。

## 生命周期与组合

每局有独立 CordiSharp 0.2.0 根上下文。编辑器和运行时不共享根、目录、插件实例或可变服务。游戏加载的临时文件由加载操作拥有；加载结果持有独立数据。插件创建的 worker、WASM 执行器和服务由其 CordiSharp effect 拥有，失败与关闭反向释放。图在启动前验证，运行期间不提供增删接口；改图需要停止后创建新局。

最小组合为定义加载、权威内核、无脚本规则、本地席位及记录插件；全真人不需要 CPU、Lua 或 WASM。Lua 组合分别启用规则和 CPU 能力，CPU 只接收本席位投影和合法动作；WASM 组合另加扩展能力，仍遵守原有 imports／WASI 禁止及资源、权限限制。Lua 重载先验证所有候选，再在串行命令边界整体切换；失败保留旧版本。

## 验收追踪

实施和实际验证证据记录在 `docs/validation/m3.5-review.md`。ROADMAP 勾选仅表示对应任务已经验收。干净 Windows、真实窗口及无控制台闪现需要真实环境证据，不能由单元测试替代。
