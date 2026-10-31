# 正式 WASM 执行与发行依赖

2026-10-31，SDK 10.0.112 / NativeAOT 10.0.12，Windows x64。

`Wasm.Execution` 的正式固定 i32 调用改用 Wasmtime 非泛型 Function API，在执行前检查参数、返回值数量和类型。每次调用仍使用新 Store，不提供 WASI、导入、文件系统、网络或权威状态句柄；燃料、内存、实例数量限制及显式 trap 检查保持不变。Wasmtime 自身继续生成 guest 原生代码，这与 .NET 宿主使用 NativeAOT 不冲突。

`BoardGame.Wasm` 使用静态 JSON 元数据，并改为引用 `Modules.Runtime` 中的声明验证，不引用历史作者解析、YamlDotNet 或 Lua。声明验证仍拒绝重复身份、非法路径、未知权限、错误 API／版本及内容哈希；历史显式迁移复用相同验证。

`Runtime.Publishing.BuiltinScriptSupport` 为发行器提供精确版本声明和原生文件收集。WASM 1 / Wasmtime 48.0.2 仅认证 `win-x64`，收集 `wasmtime.dll` 并核对实测 SHA-256 `19B3880FBB92F1826C58AC9F5E2A472692B5FC3D012A1A7A41983CCF77544607`。缺件、不同二进制或未验证目标明确拒绝；升级必须重新验证并更新身份。

Lua rules / CPU 以及间接调用 MoonSharp 的 flow 版本 1 均明确不兼容。当前没有受支持的原生 Lua 工作进程，不能以系统 `dotnet` 或托管 worker 补位。声明覆盖整个 flow 实现，不根据一次试玩是否走到脚本节点判断兼容性。未来已支持版本需单独补齐原生 worker 收集与预算验收。

## 验证

- 正式 `Wasm.Execution.Tests`：普通和 NativeAOT 的 18 项固定签名、实例隔离、错误输入、燃料、内存、四类 trap、重复释放检查全部通过。
- 正式 `BoardGame.WasmHosting.Tests`：真实内容加载、更新输入、WASM 权限展开、录制回放和篡改拒绝通过。无 Lua、Runtime 全量包和 YamlDotNet 引用。
- 无系统 .NET 的 Windows Sandbox 10.0.26100 运行相同原生产物，18 项检查及真实桌游回放均通过，退出 0。状态 hash `908305ADD0557D420963BEF797AA18C71A611F1C1E38484C96FB9225AC324310`，记录 hash `570D00022A4A61841B170D35D3B835E21D0A1335E380ACF6A70F0ACECB81B9C0`，与普通运行一致。
- 发布声明测试覆盖 Lua／间接 flow 拒绝、版本和平台拒绝、原生依赖缺失／哈希不匹配、真实原生文件收集。此前无脚本正式 Board 和 GL 产物均不携带 Wasmtime 或 Lua 依赖。
- 普通 `packages modules wasm-executor` 回归及依赖边界通过。严格 ILLink / ILC 发布没有新增屏蔽或整库保留。

```powershell
dotnet run --project tests/BoardGame.WasmHosting.Tests
dotnet publish tests/BoardGame.WasmHosting.Tests -c Release -r win-x64 -p:PublishAot=true -p:UseStaticRuntime=true --artifacts-path artifacts/wasm-board-build -o artifacts/native-aot/wasm/production-board
dotnet publish tests/Wasm.Execution.Tests -c Release -r win-x64 -p:PublishAot=true --artifacts-path artifacts/wasm-execution-build -o artifacts/native-aot/wasm/production-execution
dotnet run --project tests/Runtime.Publishing.Tests -- artifacts/native-aot/wasm/production-board
```

报告见 [evidence/wasm-runtime-integration](evidence/wasm-runtime-integration/)。完整游戏发行构建与 Editor 发布仍由相应后续任务验收。
