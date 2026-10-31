# Editor NativeAOT 导出验收

2026-10-31，Windows x64，SDK 10.0.112、NativeAOT 10.0.12、VS C++ 14.51.36231。

`author.export.native` 独立登记 `native-directory` 方式和 `native.export` 公共命令，复用 CLI 的 `NativeExporter`。通用面板按提供者元数据显示参数，右侧展示来源版本、代次、实际插件指纹、后端、原生依赖、工具链和构建日志。运行插件仍只由 `game.start.json` 选择。

## 复核

```powershell
dotnet build src/Editor -c Debug --artifacts-path artifacts/editor-native
$env:BGB_RUNTIME_PLUGIN_STORE = '<isolated-store>'
artifacts/editor-native/bin/Editor/debug/Editor.exe --verify-native-export <native-sdk> <external-delivery-fixture> <new-output>
```

本次使用维护者分发 `artifacts/native-aot/sdk-motion-02` 和外部插件 fixture `artifacts/external-delivery/f09ae9918992441683363fc4efd8f700`。最终产物在 `artifacts/editor-native-final-fcf6c587dfee4e519e4fe9c0cb3349c9/game`。验证不是模拟构建：一次在 ILC 开始后取消，一次完整生成原生 `Play.exe`，另两次在实际构建开始后切换文档和关闭应用。

## 结果

- 三种导出提供者独立发现；关闭再启用原生提供者保留配置，原生提供者不依赖另外两种导出。
- 缺后端、多后端、缺工具链、未声明 AOT 兼容的外部插件均阻止发布，并保留可定位诊断。
- 公共命令发布未保存标题和入口草稿，结果来源版本与捕获的 `ProjectView` 完全相同，磁盘样例文件不变。
- 活动提供者不能被卸载；取消、换文档及正常关闭均等待构建退出，无成功目录和串入新文档的迟到消息。
- 关闭原生提供者后数据包仍可导出；普通导出回归的 14 组检查通过，涵盖独立 Player、数据包、草稿、清理失败、文档切换、关闭等待。
- 修复跨文件草稿撤销按创建顺序而非编辑顺序的问题；`Engine.Tests author-workspace` 验证跨文件撤销／重做和既有作者事务。
- `Authoring.Native.Tests` 预检与独立提供者回归通过；Editor 构建零警告；项目依赖边界检查通过。

机器报告、面板截图及详细构建信息见 [evidence/editor-native-export](evidence/editor-native-export/)。本项验证通用非桌游原生游戏；桌游原生、脚本组合和干净环境对照仍由联合验收任务负责。
