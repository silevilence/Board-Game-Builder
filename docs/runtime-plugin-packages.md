# 运行插件包与兼容声明

此协议扩展获准运行目录；游戏文件不能指定程序集路径并触发加载。`game.start.json` 只选择身份、版本、API、实现指纹及配置；`runtime.plugin.json` 描述发布者提供的实现；作者发布配置指定 RID 与构建选项。这三份数据的职责互不替代。

`Runtime.Plugins` 是独立可选模块，依赖 Application.Core；小核心不反向引用导入、程序集扫描或构建能力。普通运行使用显式安装目录中的受控加载，原生运行使用生成的直接工厂调用。

## 格式 1

插件包是含 `runtime.plugin.json` 和全部声明文件的目录。清单仅接受已知字段，不允许重复 JSON 键、重复文件、路径越界或符号链接。当前导入验证 Windows x64；不从缺失的 DLL 自动下载依赖。

```json
{
  "format": 1,
  "kind": "runtime",
  "id": "sample.counter",
  "version": "1",
  "api": 1,
  "entry": {
    "assembly": "Runtime.SamplePlugin.dll",
    "type": "Example.RuntimePlugin.Factory",
    "method": "Create"
  },
  "targets": ["win-x64"],
  "aot": {
    "compatible": false,
    "targets": [],
    "reason": "This package has not passed native publication and execution verification."
  },
  "provides": [{"name": "sample.counter", "api": 1}],
  "requires": [],
  "execution": {
    "modes": "All",
    "scope": "Instance",
    "contributions": [{"id": "tick", "phase": "Update", "before": null, "after": null}]
  },
  "files": [{"path": "Runtime.SamplePlugin.dll", "sha256": "REPLACE_WITH_ACTUAL_LOWERCASE_SHA256", "role": "implementation"}]
}
```

示意哈希不是可导入值，发布者须对最终文件计算 SHA-256。`files.role` 区分 `implementation`、`managed`、`reference`、`native`、`content`；入口必须指向实现程序集。仅供编译的引用程序集不能伪装成实现。程序集身份、传递托管引用和原生架构均需匹配宿主；共享契约由宿主显式提供，不能随包替换 Application.Core、Application.Abstractions 或 CordiSharp。Editor、Editor.Core 和 CordiSharp.Plugins.Avalonia 引用被拒绝。

静态入口是无参的 `public static ApprovedApplicationPlugin Create()`。返回的工厂接受 `GameContent?`：空值只描述能力，非空值绑定指定内容。导入时与每次绑定均核对身份、能力、执行作用域和顺序声明，不在验证中调用插件 Activate。插件实际执行仍由现有 CordiSharp 根及 RuntimePluginScope 管理。导入的托管代码属于显式信任的本机扩展，此目录不是恶意 CLR 代码的安全沙箱。

能力依赖使用名称与精确 API 版本，允许不同插件 ID 提供同一兼容能力。初始化依赖与 update/render 顺序分别验证；无回调提供者可仅贡献接口。GraphicsHost／Instance／Viewport 遵守既有生命周期与依赖方向；双后端由重复 `render.backend` 能力拒绝。

## 身份、导入与生命周期

`PluginManifest.Fingerprint` 是规范化清单的 SHA-256，清单包含每个文件的哈希。外部选择须将该值写入 `RuntimePluginSelection.Implementation`；缺失或不同会在运行前拒绝。现有内置插件的空指纹保持历史 JSON 形状，不能用空值选择外部实现。保存包、内容快照和记录时，这个字段随既有启动身份参与完整性校验。

`PluginStore.Import` 先验证来源，再复制到暂存目录并重新核对字节，加载声明工厂完成身份核验后才登记。重名同版本默认拒绝，替换必须显式请求。`Acquire` / `Open` 返回的 lease 必须覆盖整个校验、会话或发布操作；活动版本禁止替换和移除。安装文件由 lease 持有只读共享锁，其他安装服务实例也不能替换或删除它们；托管实现从已验证字节加载，lease 结束后不遗留 CLR 文件映射锁。`AssemblyLoadContext.Unload` 仍由 GC 决定实际回收，不承诺对局中热卸载。

`Open(startup, builtinCatalog)` 只解析本次选择、且不在固定目录中的身份；不会自动导入。整个选择集合使用同一个受控加载上下文，允许插件通过共同依赖中的类型协作。同名托管依赖或原生库须字节一致，否则拒绝选择。公共契约由宿主共享，游戏内容不能扩充共享程序集名单。

## 作者与普通发行

Editor 与 CLI 作者安装目录默认是 `%LOCALAPPDATA%/BoardGameBuilder/runtime-plugins`，可由启动环境变量 `BGB_RUNTIME_PLUGIN_STORE` 指定；该路径不来自游戏文件。作者 profile 可启用 `author.runtime.plugins`，发现 `runtime.plugins.list/import/remove`。外部定义由 `author.runtime.external` 提供作者加载、校验及导出适配。`author.run.application` 的 `runtime.run` 用相同目录和捕获的 ProjectView 执行固定步长批次；支持 `ticks` 和 `count`，结果包含源版本、完整内容身份及完成的逻辑时间。启用或导入这些作者能力不会改写游戏入口。

数据包只包含定义、脚本、资源和精确身份。普通自包含 Player 通过 `runtime-host.json` 声明受控安装协议，维护者发布脚本将它及 `Runtime.Plugins.dll` 纳入发行哈希。`PluginPlayerDistribution` 复制本次选中外部包和依赖，记录到 `release.json`，再原子发布到新目录。正式 Player 仅从自身 `plugins/runtime` 加载，脱离作者安装仍可运行；不会从机器其他位置补齐缺件。对附加导出步骤再次检查受保护文件、开发文件与链接，失败不覆盖已有产物。

Editor 的 `runtime.plugins` 提供安装服务，`runtime.external` 将外部定义登记到会话和作者服务。嵌入会话及独立调试 Player 持有精确集合直到异步退出；实际视口先释放，再结束实例并归还安装版本。UI 管理和后端配置按各自 ROADMAP 条目交付。验收见[外部插件普通发行](validation/external-plugin-delivery.md)。

## NativeAOT 准入与静态构建

没有 `aot` 或 `compatible=false` 的插件可用于满足普通宿主条件的场景，但不能进入原生编译集合。`compatible=true` 必须明确目标 RID；它只代表发布者声明。建议插件工程设置 .NET 的 `IsAotCompatible=true`，同时运行裁剪／AOT 分析器，并以完整依赖真实发布、运行；参见 [.NET AOT 库分析](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries)。不得关闭分析、整库保留或把普通 .NET 运行成功作为兼容证据。

`PluginCompatibility.Check` 给出目标、声明、能力版本、依赖链、作用域及顺序诊断。静态生成器只消费调用方明确提供的完整选择集合，不扫描全量目录，也不根据试玩覆盖率删除实现；它核对编译输入哈希、拒绝同名不同字节的程序集，并生成直接工厂调用及实施程序集引用。实际 NativeAOT 编译和目标环境验证仍是后续发布步骤，声明为 true 不能跳过它们。

普通导入与静态工厂复用 `PluginRegistration.Bind`，保持身份、能力和执行声明一致。原生程序不会动态加载此清单指定的未知托管 DLL。原生库、工作进程及游戏内容的发行收集仍须由发布服务完成，生成引用清单本身不是可运行发行物。

`BoardStaticFactories` 和 `AssetStaticFactories` 提供独立无参入口，不枚举普通运行目录。桌游入口使用默认 BoardApplication 参数；需要定制种子／控制器的发布宿主应装配同一 BoardApplication 实例提供的单项 Factory，不得另造权威实现。`tests/Runtime.StaticRegistration.Tests` 实际生成并编译桌游／非桌游工程，核对普通与静态注册的精确实现身份、能力、作用域、初始化和阶段顺序，并检查没有引用未选的脚本、作者解析器或编辑器程序集。真实原生对局验证由 Native.Board.Tests 和后续发行任务负责。
