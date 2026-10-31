# 外部插件内容身份与普通发行验收

2026-10-31，Windows x64、SDK 10.0.112。此项验证普通 .NET 受控装配；NativeAOT 外部组合另由联合验收覆盖，不以本结果声明 AOT 兼容。

样例由独立外部定义、固定步长更新和绘制提供者组成。视图程序集依赖计数器程序集并通过其公共类型协作；两个插件包共同携带的依赖必须字节一致。每个选择包含清单指纹；外部代码不引用 Editor、作者服务或桌游契约。

## 可复现步骤

```powershell
& ./scripts/publish-player.ps1 -Output artifacts/external-player-base `
  -HostProject samples/Minimal.Player/Minimal.Player.csproj `
  -EntryPoint Minimal.Player.exe -PackageFormat 3 -IncludeLuaWorker $false
dotnet run --project tests/Runtime.PluginDelivery.Tests -c Release -- artifacts/external-player-base
dotnet run --project tests/Runtime.PluginImport.Tests -c Release
dotnet run --project tests/Engine.Tests -- author-export-contract author-profiles author-exports
```

集成测试在 `artifacts/external-delivery/<id>` 生成发布者包、独立作者安装、工程、数据包和普通发行；`batch.json` / `result.json` 记录实际身份。它验证：

- 精确指纹不可省略，数据包不含 DLL；未批准目录无法直接加载数据包。
- 已选择插件在共享加载上下文内协作，固定种子 42 的 12 次更新在作者目录和独立发行中得到相同值。
- `author.runtime.external` 支持校验与普通导出；`runtime.run` 用同一身份执行三个独立实例，每个完成 12 次固定更新。
- 活动集合禁止同一或另一安装服务实例移除／替换；失败后三个完整包仍可枚举。
- 导出后删除源安装仍可运行；发行目录中的实现缺失、篡改会被拒绝。
- 原有三个作者导出回归组和 22 项导入检查通过；项目依赖边界通过。

## 编辑器和干净环境

Editor 验证入口：`--verify-external-runtime <fixture> <debug-player.exe> <result.json>`。启动前将 `BGB_RUNTIME_PLUGIN_STORE` 设为新测试目录。验证实际编辑器嵌入视口和独立调试 Player：未保存草稿、暂停／单步、外部视图绘制、活动版本保护、文档切换迟到结果、提供者重复启停及关闭清理。最终图形设备和后端计数均为零。普通实例不通过 GUI 调试协议传递 DLL 或自动安装包。

在无系统 .NET 的 Windows Sandbox 将发行物复制到 `C:\ExternalOrdinary01`，直接启动 Play.exe。实际显示外部视图标题、持续增长的更新计数和移动图形，退出后进程数为零。发行目录含三个自有插件包，不需要作者目录、SDK 或源码。

固定结果、编辑器报告、环境和截图保存在 [evidence/external-plugin-delivery](evidence/external-plugin-delivery/)。UI 管理、后端安全切换和 NativeAOT 最终组合有独立任务，不计入本项通过范围。
