# 流程设计验收覆盖率明细

2026-10-16，Microsoft.CodeCoverage.Console 18.6；1176/1245 行，94.46%。采集范围与通过证据见[整体报告](flow-editor-review.md)。

以下完整列出采集范围内行覆盖率低于 90% 的方法；异步状态机和闭包保留工具原名，不能将它们等同于独立用户功能。方法百分比与整体行覆盖率采用同一工具口径。主要未覆盖路径为不支持的规则接口、错误/取消回调、部分条件路径及 UI 放弃/显示回调；对应核心行为另有工作区测试。

| 源码 | 方法（工具原名） | 行覆盖率 |
| --- | --- | ---: |
| [src/Editor.Core/EditorApplication.cs](../../src/Editor.Core/EditorApplication.cs#L104) | `BoardGame.Editor.EditorApplication.Retain` | 0.00% |
| [src/Editor.Core/EditorApplication.cs](../../src/Editor.Core/EditorApplication.cs#L244) | `BoardGame.Editor.EditorPluginScope.Emit` | 0.00% |
| [src/Editor.Core/EditorApplication.cs](../../src/Editor.Core/EditorApplication.cs#L245) | `BoardGame.Editor.EditorPluginScope.ForContext` | 0.00% |
| [src/Editor.Core/EditorApplication.cs](../../src/Editor.Core/EditorApplication.cs#L104) | `BoardGame.Editor.EditorApplication.<>c__DisplayClass36_0.<Retain>b__0` | 0.00% |
| [src/Runtime/Flow/FlowConditions.cs](../../src/Runtime/Flow/FlowConditions.cs#L45) | `BoardGame.Runtime.FlowConditions.Resolve` | 71.43% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L73) | `BoardGame.Runtime.FlowController.CheckActions` | 66.67% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L128) | `BoardGame.Runtime.FlowController.Actions` | 87.50% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L139) | `BoardGame.Runtime.FlowController.Submit` | 73.68% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L174) | `BoardGame.Runtime.FlowController.Prepare` | 0.00% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L175) | `BoardGame.Runtime.FlowController.Apply` | 0.00% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L176) | `BoardGame.Runtime.FlowController.BoardGame.Kernel.IGameRules.Actions` | 0.00% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L177) | `BoardGame.Runtime.FlowController.Decide` | 0.00% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L131) | `BoardGame.Runtime.FlowController.<>c.<Actions>b__29_0` | 0.00% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L151) | `BoardGame.Runtime.FlowController.<>c__DisplayClass30_0.<Submit>b__1` | 80.00% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L153) | `BoardGame.Runtime.FlowController.<>c__DisplayClass30_0.<Submit>b__2` | 0.00% |
| [src/Runtime/Flow/FlowValidation.cs](../../src/Runtime/Flow/FlowValidation.cs#L89) | `BoardGame.Runtime.FlowValidation.<>c.<RequireValid>b__1_1` | 0.00% |
| [src/Authoring/FlowOperations.cs](../../src/Authoring/FlowOperations.cs#L12) | `BoardGame.Authoring.FlowAuthorModel.get_ExitCode` | 0.00% |
| [src/Authoring/FlowOperations.cs](../../src/Authoring/FlowOperations.cs#L33) | `BoardGame.Authoring.FlowOperations.Read` | 88.89% |
| [src/Authoring/FlowScriptTemplates.cs](../../src/Authoring/FlowScriptTemplates.cs#L6) | `BoardGame.Authoring.FlowScriptTemplates.Text` | 87.50% |
| [src/Authoring/FlowReferences.cs](../../src/Authoring/FlowReferences.cs#L20) | `BoardGame.Authoring.FlowReferences.<>c.<Validate>b__0_3` | 0.00% |
| [src/Authoring/FlowReferences.cs](../../src/Authoring/FlowReferences.cs#L20) | `BoardGame.Authoring.FlowReferences.<>c.<Validate>b__0_4` | 0.00% |
| [src/Editor/EditorHost.cs](../../src/Editor/EditorHost.cs#L34) | `BoardGame.Editor.EditorHost.<Start>d__13.MoveNext` | 66.67% |
| [src/Editor/ExportPanel.cs](../../src/Editor/ExportPanel.cs#L20) | `BoardGame.Editor.ExportPanel.<<-ctor>b__5_2>d.MoveNext` | 0.00% |
| [src/Editor/ExportPanel.cs](../../src/Editor/ExportPanel.cs#L20) | `BoardGame.Editor.ExportPanel.<<-ctor>b__5_3>d.MoveNext` | 0.00% |
| [src/Editor/ExportWorkflow.cs](../../src/Editor/ExportWorkflow.cs#L33) | `BoardGame.Editor.ExportWorkflow.<>c__DisplayClass26_2.<Run>b__4` | 75.00% |
| [src/Editor/FlowCanvas.cs](../../src/Editor/FlowCanvas.cs#L181) | `BoardGame.Editor.FlowCanvas.<Run>d__51.MoveNext` | 66.67% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L76) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__3` | 66.67% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L79) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__4` | 0.00% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L82) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__5` | 66.67% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L85) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__6` | 0.00% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L94) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__9` | 0.00% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L95) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__10` | 0.00% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L100) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__11` | 80.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L89) | `BoardGame.Editor.FlowPanel.<>c.<.ctor>b__3_19` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L52) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_0.<.ctor>b__1` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L77) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_0.<.ctor>b__3` | 75.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L97) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_0.<.ctor>b__5` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L132) | `BoardGame.Editor.FlowEditorPlugin.<>c.<get_Definition>b__1_19` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L118) | `BoardGame.Editor.FlowEditorPlugin.<>c__DisplayClass1_0.<get_Definition>b__3` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L131) | `BoardGame.Editor.FlowEditorPlugin.<>c__DisplayClass1_0.<get_Definition>b__13` | 0.00% |
| [src/Editor/FormatPanel.cs](../../src/Editor/FormatPanel.cs#L85) | `BoardGame.Editor.GameFilePanel.<>c__DisplayClass0_0.<.ctor>b__3` | 0.00% |
| [src/Editor/EditorHost.cs](../../src/Editor/EditorHost.cs#L21) | `BoardGame.Editor.EditorHost.<>c__DisplayClass12_0.<<-ctor>b__1>d.MoveNext` | 87.50% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L25) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_1.<<-ctor>b__9>d.MoveNext` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L37) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_3.<<-ctor>b__12>d.MoveNext` | 66.67% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L119) | `BoardGame.Editor.FlowEditorPlugin.<>c__DisplayClass1_0.<<get_Definition>b__5>d.MoveNext` | 0.00% |
