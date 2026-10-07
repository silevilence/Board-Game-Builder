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

## 媒介接口

权限 `component`、`command`、`snap`、`container` 对应注册位 1、2、4、8；
`bgb_init` 必须返回清单权限的位和。清单中没有权限就不能使用对应能力。
配置 `wasm.<id>` 组件的数据为 `{value,min,max}`，映射为同名带范围属性；
`wasm.<id>.container` 为 `{capacity,ordered,contents}`。

Lua 派生命令 `wasm.<id>.command` 调用 `bgb_command(current, value)` 返回新属性值；
`wasm.<id>.snap` 使用 `bgb_snap(current, requested)` 分别计算整数 X／Y；
`wasm.<id>.container` 使用 `bgb_container(count, requested)` 返回新容量。
这些出口均为 `(i32,i32)->i32`。每条扩展效果在全新受限实例计算，只获得目标的可见数值，
转换后的 set／move／resize-container 再由 Kernel 结构校验；日志记录最终效果，回放无需重新执行 WASM。
每个规则批次中的扩展计算以提交前投影为输入，避免依赖插件内存；需连续计算时分成多次提交。

示例 `samples/wasm-counter` 的二进制来自同目录 WAT（整数加法）。Editor 的可选
`author.extensions` 插件提供组件编辑，停用后仅移除面板，模块仍可试玩／导出。
