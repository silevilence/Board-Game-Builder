# 受限运行时插件

模块 `plugins` 中声明 `id`、整数文本 `version`、`api: 1`、`kind: "wasm"`、
相对 `.wasm` 文件路径 `file` 和 SHA-256 `sha256`；默认 `permissions` 为空。
数据模块包包含 WASM 文件，快照包含相同字节，哈希与依赖进入模块指纹。
原生扩展仅接受发行物的官方依赖白名单，第三方 DLL 不会装载。

启动顺序为 Kernel、官方 Runtime、模块 WASM、Lua。每个插件导出无参数 i32
函数 `bgb_api`、`bgb_version`、`bgb_init`，分别返回接口版本、插件版本和注册位。
不接受任何 WASM imports，也没有 WASI；对局期间不支持替换或卸载插件。

固定依赖 [Wasmtime .NET 48.0.2](https://www.nuget.org/packages/Wasmtime/48.0.2)。
执行使用 [Store 的 fuel 与资源限制](https://github.com/bytecodealliance/wasmtime-dotnet/blob/main/src/Store.cs)：
每次调用 100,000 fuel，4 MiB 线性内存、1 个内存／实例／表、1,024 个表元素；模块文件上限 256 KiB。
初始化失败释放整组插件，不允许保留部分注册。只加载普通 WASM，不接受预编译本机代码缓存。
