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

`PluginStore.Import` 先验证来源，再复制到暂存目录并重新核对字节，加载声明工厂完成身份核验后才登记。重名同版本默认拒绝，替换必须显式请求。`Acquire` 返回的 lease 必须覆盖整个校验、会话或发布操作；活动版本禁止替换和移除。托管实现从验证后的字节加载，安装文件不受 CLR 文件映射锁定；`AssemblyLoadContext.Unload` 仍由 GC 决定实际回收，不承诺对局中热卸载。

## NativeAOT 准入与静态构建

没有 `aot` 或 `compatible=false` 的插件可用于满足普通宿主条件的场景，但不能进入原生编译集合。`compatible=true` 必须明确目标 RID；它只代表发布者声明。建议插件工程设置 .NET 的 `IsAotCompatible=true`，同时运行裁剪／AOT 分析器，并以完整依赖真实发布、运行；参见 [.NET AOT 库分析](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries)。不得关闭分析、整库保留或把普通 .NET 运行成功作为兼容证据。

`PluginCompatibility.Check` 给出目标、声明、能力版本、依赖链、作用域及顺序诊断。静态生成器只消费调用方明确提供的完整选择集合，不扫描全量目录，也不根据试玩覆盖率删除实现；它核对编译输入哈希、拒绝同名不同字节的程序集，并生成直接工厂调用及实施程序集引用。实际 NativeAOT 编译和目标环境验证仍是后续发布步骤，声明为 true 不能跳过它们。

普通导入与静态工厂复用 `PluginRegistration.Bind`，保持身份、能力和执行声明一致。原生程序不会动态加载此清单指定的未知托管 DLL。原生库、工作进程及游戏内容的发行收集仍须由发布服务完成，生成引用清单本身不是可运行发行物。
