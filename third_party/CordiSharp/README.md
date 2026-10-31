# 固定 CordiSharp 静态运行适配

上游提交 `7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f`，程序集契约 0.2.0。
上游 Directory.Build.props 声明 MIT 和 Copyright (c) CordiSharp Contributors；
源码归档未附独立许可证文件，这里按该声明附 MIT 文本。

`build-static.ps1` 校验上游 ZIP、适配生成器及可审阅 diff 的固定 SHA-256，
只在新的 scratch 目录编译。输出 DLL、许可和 build-manifest.json。
研究依据和支持边界见 [原型报告](../../docs/validation/native-aot-core-prototype.md)。

仅原生构建通过 `UseStaticRuntime=true` 显式引用此实现；普通宿主默认仍引用原 NuGet 包。
静态支持限于 Inject、IPluginObject 及确切 object 配置的 Func/Action 入口，
effect 使用 Task 或 Task<object?>，logger 拦截配置使用字典。
动态宿主保留原 Type、delegate、结构化 apply 分支及其元数据处理。
任意插件的 AOT 声明仍须经过实际编译与运行验收。
