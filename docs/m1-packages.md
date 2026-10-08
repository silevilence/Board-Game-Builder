# 游戏包与运行依赖

当前协议为 M3.5：`Player pack <游戏目录> <新文件.bgm>` 创建格式 2 ZIP 包，拒绝覆盖。`gui`、`play`、`batch`、`replay` 经 `GameRuntime.Load` 使用同一入口；加载 `.bgm` 时先校验，临时解包，再调用相同目录加载器，所有运行内容载入内存后删除临时目录。

固定入口 `game.start.json` 格式 1 声明游戏 ID、版本及精确运行插件版本/API/配置。定义来源由唯一加载器的 `configuration.path` 决定；内置 `definition.module` 支持原实体、组件、布局和原型，`definition.state` 支持直接状态。身份、Lua 入口和 WASM 清单只在启动文件中声明，定义中重复声明拒绝。

`package.manifest.json` 包含 `format: 2`、运行 `fingerprint` 和每个内容文件的 SHA256。打包收集启动配置、加载器声明的定义／资源／附加数据、Lua 入口和 WASM 文件；不递归复制作者目录。拒绝缺失或额外文件、重复／大小写冲突／越界路径、链接、哈希错误及不兼容版本。限制 2048 个数据文件、每文件 32 MiB、总计 128 MiB；目录文件也在读入前检查大小。完整性哈希不代表发行者签名。

模块定义可用 `runtimeFiles` 携带帮助或许可证等数据，不执行其中代码。包只能选择发行物获准的运行插件，不能通过声明加载任意 DLL。WASM 扩展在 `wasm.configuration.extensions` 中声明文件、SHA256、API 和权限，继续遵守大小、fuel、内存和 imports 限制。详见 [运行时协议](runtime-plugins.md)。

卡面资源须为空或与 faces 一一对应，引用必须存在。投影只传递可见资源标识，视图只收集投影引用的字节；不能解码的图片显示诊断。

旧目录须显式运行 `Player migrate <旧目录> <新目录>`，新目录不得存在。转换移动身份／Lua／WASM 配置并验证初始状态，之后重新打包。旧包格式 1 不作为正常启动回退；保留 `DirectoryModule`／`ModulePackage` 历史解析器供迁移与历史诊断。旧记录处理见协议兼容矩阵。

导出使用预编译、含完整运行时的 Windows Player。发行清单记录实际插件目录和文件哈希；预检核对所选能力、依赖程序集与 native 文件，并排除 Editor 程序集和 CordiSharp 编辑器扩展。允许 `CordiSharp.dll` 核心。作者导出不调用编译器，玩家双击 `Play.exe` 无需 SDK 或编辑器。
