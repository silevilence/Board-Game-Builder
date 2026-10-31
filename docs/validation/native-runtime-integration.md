# 公共运行链的正式 NativeAOT 集成

2026-10-31，Windows x64，SDK 10.0.112、NativeAOT 10.0.12。

正式 Application.Core、Kernel、Modules.Runtime、BoardGame.Core／Session／Recording／Hosting 和 Resources 项目分别编译，未把源码并入单一实验程序集。公共内容 JSON 使用自己的源生成协议；内核、模块指纹、记录和宿主各自持有类型信息，不由底层引用可选实现。Modules.Runtime 仅保留快照、身份数据和哈希，历史作者加载与 YamlDotNet 留在 Modules，旧类型保留转发。

普通宿主默认仍使用官方 CordiSharp；原生构建显式使用 `UseStaticRuntime=true`，引用 `third_party/CordiSharp/build-static.ps1` 固定上游和补丁生成的 DLL。该路径只支持已验证的静态插件形状，详见其 README。没有修改 NuGet 缓存。

## 实际验证

- 正式 Application.Core：严格原生发布成功，加载／打包／快照／恢复、update、故障清理和关闭等待 6 / 6 通过。
- `Native.Board.Tests`：严格原生发布成功；两次种子 42 的 shuffle→draw→finish、完整录制恢复、检查点恢复后继续执行、篡改拒绝、大小写及可空 ordinal 字典、历史 format 1 记录通过。
- 同一测试在普通 JIT，以及固定 CordiSharp 补丁加 `JsonSerializerIsReflectionEnabledByDefault=false` 的 JIT 下得到相同结果。记录 hash `F20FBCCFEE852B7DD434504CC70E41032A0635562ED08D2574E22D8223FCD948`；状态 hash `F92CB9B61DA77D007C4936626AB7D433AA5457A57C520525986FAD8021A7F4A6`。
- 最终原生 EXE SHA-256：`643A4D1BAC4915D57D82B3E49435A23EB9EB13BF40DFB0EF017CF4E342F2C5DF`。单独复制到无系统 .NET 的 Windows Sandbox 10.0.26100，全部检查通过、退出 0，无需游戏输入、SDK或源码。
- 普通回归通过 `commands replay modules packages board-recording board-update application-baseline flow-restoration secrets author-export-contract`。基线覆盖 7 个样例各 3 个种子，状态／记录与已存基线一致。依赖边界脚本通过。
- 对上述运行库启用 `IsAotCompatible` 后构建零警告、零错误；没有关闭裁剪、全局屏蔽分析或保留整个程序集。

完整输出见 [evidence/native-runtime-integration](evidence/native-runtime-integration/)。复现：

```powershell
pwsh -NoProfile -File third_party/CordiSharp/build-static.ps1
dotnet publish tests/Native.Board.Tests -c Release -r win-x64 -p:UseStaticRuntime=true --artifacts-path artifacts/native-runtime-build -o artifacts/native-runtime
artifacts/native-runtime/Native.Board.Tests.exe
dotnet run --project tests/Native.Board.Tests -c Release -p:PublishAot=false -p:UseStaticRuntime=true -p:JsonSerializerIsReflectionEnabledByDefault=false --artifacts-path artifacts/native-runtime-jit
```

## 两处受限的普通 JSON 兼容分支

`Wire.DefaultResolver` 与旧动态字典工厂的 `CreateConverter` 各有一处方法级 IL2026 注解。原因是编译期分析器不把 `JsonSerializer.IsReflectionEnabledByDefault` 当作裁剪安全 guard；两个调用同时检查该开关与 `RuntimeFeature.IsDynamicCodeSupported`，原生／关闭反射的宿主只走显式源生成及闭合转换器。它们保留普通作者路径对自定义 CLR 类型的既有支持；不是让未知类型进入原生闭包。动态方法继续标注 RequiresUnreferencedCode／RequiresDynamicCode，其他调用点仍受分析器检查。

该 JSON 开关是微软文档明确说明的链接期常量，禁用时不会保留反射 resolver；见 [System.Text.Json 源生成](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)。这里另用关闭反射的 JIT 和真实 NativeAOT 运行证明分支边界，没有伪造 FeatureGuard 或使用全局 NoWarn。

这项验收覆盖公共链与最小无脚本组合。GL 正式接入、脚本兼容准入、完整发行目录、外部插件独立发行和 Editor 发布工作仍在各自 ROADMAP 任务验收，不由本报告推定完成。
