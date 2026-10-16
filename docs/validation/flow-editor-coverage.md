# 流程设计验收覆盖率明细

由 scripts/summarize-flow-coverage.ps1 从本轮 Cobertura 自动生成；不混用历史局部方法表。

本轮为 **2161/2334 行，92.59%**；采集范围、原始 XML、来源及命令见[验收记录](flow-editor-review.md)。仅测量配置正向 Include 匹配的模块与源码，测试驱动不计入生产分母。
上述为工具按类计数的总量，同一源码行可能出现在多个生成类中。逐文件按行号去重后合计 **1974/2128 行**；两个口径分别保留，不将去重合计冒充工具分母。

以下完整列出本轮行覆盖率低于 90% 的 **83 个方法条目**（含编译器生成的状态机与闭包）；历史 94.46% 与本轮范围不同，不能直接比较。

| 源码 | 方法（工具原名） | 行覆盖率 |
| --- | --- | ---: |
| [src/Editor.Core/AuthorWorkspace.cs](../../src/Editor.Core/AuthorWorkspace.cs#L72) | `BoardGame.Editor.DocumentWorkspace.ApplyAuthor(BoardGame.Authoring.ProjectChanges)` | 88.89% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L61) | `BoardGame.Editor.DocumentWorkspace.ApplyAuthorForm(string, System.Action)` | 80.00% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L71) | `BoardGame.Editor.DocumentWorkspace.ApplyForm(string, System.Action<BoardGame.Editor.ModuleDocument>)` | 0.00% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L96) | `BoardGame.Editor.DocumentWorkspace.Undo()` | 25.00% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L102) | `BoardGame.Editor.DocumentWorkspace.Redo()` | 25.00% |
| [src/Editor.Core/AuthorWorkspace.cs](../../src/Editor.Core/AuthorWorkspace.cs#L134) | `BoardGame.Editor.WorkspaceEditMode.Result(string, System.Threading.CancellationToken)` | 0.00% |
| [src/Editor.Core/AuthorWorkspace.cs](../../src/Editor.Core/AuthorWorkspace.cs#L140) | `BoardGame.Editor.WorkspaceEditMode.Find(string, string, System.Threading.CancellationToken)` | 0.00% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L126) | `BoardGame.Editor.DocumentDraft.get_Yaml()` | 0.00% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L143) | `BoardGame.Editor.DocumentDraft.Undo()` | 0.00% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L144) | `BoardGame.Editor.DocumentDraft.Redo()` | 0.00% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L154) | `BoardGame.Editor.DocumentDraft.Reload(string)` | 25.00% |
| [src/Editor.Core/EditorApplication.cs](../../src/Editor.Core/EditorApplication.cs#L104) | `BoardGame.Editor.EditorApplication.Retain(string, string)` | 0.00% |
| [src/Editor.Core/EditorApplication.cs](../../src/Editor.Core/EditorApplication.cs#L244) | `BoardGame.Editor.EditorPluginScope.Emit<T>(CordiSharp.Events.EventKey<T>, T)` | 0.00% |
| [src/Editor.Core/EditorApplication.cs](../../src/Editor.Core/EditorApplication.cs#L245) | `BoardGame.Editor.EditorPluginScope.ForContext(CordiSharp.Context, bool)` | 0.00% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L97) | `BoardGame.Editor.DocumentWorkspace.<>c.<Undo>b__79_0(BoardGame.Editor.DocumentDraft)` | 0.00% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L103) | `BoardGame.Editor.DocumentWorkspace.<>c.<Redo>b__80_0(BoardGame.Editor.DocumentDraft)` | 0.00% |
| [src/Editor.Core/AuthorWorkspace.cs](../../src/Editor.Core/AuthorWorkspace.cs#L96) | `BoardGame.Editor.DocumentWorkspace.<>c__DisplayClass23_0.<ApplyAuthor>b__2(BoardGame.Editor.DocumentDraftTarget)` | 0.00% |
| [src/Editor.Core/DocumentWorkspace.cs](../../src/Editor.Core/DocumentWorkspace.cs#L110) | `BoardGame.Editor.DocumentWorkspace.<CanLeave>d__85.MoveNext()` | 0.00% |
| [src/Editor.Core/AuthorWorkspace.cs](../../src/Editor.Core/AuthorWorkspace.cs#L136) | `BoardGame.Editor.WorkspaceEditMode.<>c__DisplayClass10_0.<Result>b__0()` | 0.00% |
| [src/Editor.Core/AuthorWorkspace.cs](../../src/Editor.Core/AuthorWorkspace.cs#L142) | `BoardGame.Editor.WorkspaceEditMode.<>c__DisplayClass11_0.<Find>b__0()` | 0.00% |
| [src/Editor.Core/AuthorWorkspace.cs](../../src/Editor.Core/AuthorWorkspace.cs#L149) | `BoardGame.Editor.WorkspaceEditMode.<>c__DisplayClass12_0.<Save>b__0()` | 75.00% |
| [src/Editor.Core/EditorApplication.cs](../../src/Editor.Core/EditorApplication.cs#L104) | `BoardGame.Editor.EditorApplication.<>c__DisplayClass36_0.<Retain>b__0()` | 0.00% |
| [src/Editor.Core/FlowWorkspace.cs](../../src/Editor.Core/FlowWorkspace.cs#L65) | `BoardGame.Editor.FlowWorkspace.<>c.<ApplyForm>b__17_1(BoardGame.Authoring.AuthorDiagnostic)` | 0.00% |
| [src/Runtime/DebugSession.cs](../../src/Runtime/DebugSession.cs#L26) | `BoardGame.Runtime.DebugSession.Handle(BoardGame.Runtime.DebugRequest)` | 70.83% |
| [src/Runtime/DebugSession.cs](../../src/Runtime/DebugSession.cs#L61) | `BoardGame.Runtime.DebugSession.ReplayReply(string)` | 0.00% |
| [src/Runtime/Flow/FlowConditions.cs](../../src/Runtime/Flow/FlowConditions.cs#L48) | `BoardGame.Runtime.FlowConditions.Resolve(System.Text.Json.JsonElement, string)` | 71.43% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L75) | `BoardGame.Runtime.FlowController.CheckActions(BoardGame.Kernel.FlowState)` | 66.67% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L130) | `BoardGame.Runtime.FlowController.Actions(string)` | 87.50% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L141) | `BoardGame.Runtime.FlowController.Submit(string, BoardGame.Kernel.Command)` | 78.95% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L177) | `BoardGame.Runtime.FlowController.Apply(BoardGame.Kernel.IGameRules)` | 0.00% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L178) | `BoardGame.Runtime.FlowController.BoardGame.Kernel.IGameRules.Actions(BoardGame.Kernel.SeatView)` | 0.00% |
| [src/Runtime/Flow/FlowController.cs](../../src/Runtime/Flow/FlowController.cs#L179) | `BoardGame.Runtime.FlowController.Decide(BoardGame.Kernel.SeatView, BoardGame.Kernel.Command)` | 0.00% |
| [src/Runtime/GameRuntime.cs](../../src/Runtime/GameRuntime.cs#L199) | `BoardGame.Runtime.GameRuntime.ValidateImages(BoardGame.Kernel.GameState, BoardGame.Kernel.ResourcePool)` | 80.00% |
| [src/Runtime/PlaySession.cs](../../src/Runtime/PlaySession.cs#L22) | `BoardGame.Runtime.PlaySession.get_SupportsCpu()` | 0.00% |
| [src/Runtime/PlaySession.cs](../../src/Runtime/PlaySession.cs#L29) | `BoardGame.Runtime.PlaySession.SetController(string, string)` | 0.00% |
| [src/Runtime/PlaySession.cs](../../src/Runtime/PlaySession.cs#L61) | `BoardGame.Runtime.LocalGameSession.get_SupportsCpu()` | 0.00% |
| [src/Runtime/PlaySession.cs](../../src/Runtime/PlaySession.cs#L104) | `BoardGame.Runtime.LocalGameSession.SetController(string, string)` | 0.00% |
| [src/Runtime/PlaySession.cs](../../src/Runtime/PlaySession.cs#L157) | `BoardGame.Runtime.LocalGameSession.ReloadScriptsCore(BoardGame.Kernel.LoadedModule)` | 85.00% |
| [src/Runtime/GameRuntime.cs](../../src/Runtime/GameRuntime.cs#L97) | `BoardGame.Runtime.GameRuntime.<>c__DisplayClass8_0.<ValidateSnapshot>b__0(System.Collections.Generic.KeyValuePair<string, string>)` | 0.00% |
| [src/Kernel/GameEngine.cs](../../src/Kernel/GameEngine.cs#L12) | `BoardGame.Kernel.GameEngine.ReplaceRules(BoardGame.Kernel.IGameRules)` | 0.00% |
| [src/Kernel/GameEngine.cs](../../src/Kernel/GameEngine.cs#L21) | `BoardGame.Kernel.GameEngine.Actions(string)` | 87.50% |
| [src/Kernel/GameEngine.cs](../../src/Kernel/GameEngine.cs#L39) | `BoardGame.Kernel.GameEngine.ExportAuthoritySubmissions()` | 0.00% |
| [src/Kernel/GameEngine.cs](../../src/Kernel/GameEngine.cs#L42) | `BoardGame.Kernel.GameEngine.Submit(string, BoardGame.Kernel.Command)` | 80.00% |
| [src/Kernel/GameEngine.cs](../../src/Kernel/GameEngine.cs#L85) | `BoardGame.Kernel.GameEngine.Apply(BoardGame.Kernel.GameState, string, BoardGame.Kernel.Command, System.Collections.Generic.List<string>, bool, System.Nullable<int>, bool, bool)` | 89.83% |
| [src/Authoring/FlowOperations.cs](../../src/Authoring/FlowOperations.cs#L12) | `BoardGame.Authoring.FlowAuthorModel.get_ExitCode()` | 0.00% |
| [src/Authoring/FlowOperations.cs](../../src/Authoring/FlowOperations.cs#L33) | `BoardGame.Authoring.FlowOperations.Read(BoardGame.Authoring.ProjectView)` | 88.89% |
| [src/Authoring/FlowScriptTemplates.cs](../../src/Authoring/FlowScriptTemplates.cs#L6) | `BoardGame.Authoring.FlowScriptTemplates.Text(string)` | 87.50% |
| [src/Authoring/RunOperations.cs](../../src/Authoring/RunOperations.cs#L11) | `BoardGame.Authoring.RunResult.get_ExitCode()` | 0.00% |
| [src/Authoring/RunOperations.cs](../../src/Authoring/RunOperations.cs#L85) | `BoardGame.Authoring.RunOperations.Rebuild(BoardGame.Authoring.DebugDocument, System.Threading.CancellationToken)` | 88.89% |
| [src/Authoring/RunOperations.cs](../../src/Authoring/RunOperations.cs#L58) | `BoardGame.Authoring.RunOperations.<Cpu>g__Run\|13_2(uint, ref BoardGame.Authoring.RunOperations.<>c__DisplayClass13_0)` | 80.00% |
| [src/Authoring/ValidationOperations.cs](../../src/Authoring/ValidationOperations.cs#L10) | `BoardGame.Authoring.ValidationResult.get_ExitCode()` | 0.00% |
| [src/Authoring/FlowReferences.cs](../../src/Authoring/FlowReferences.cs#L20) | `BoardGame.Authoring.FlowReferences.<>c.<Validate>b__0_3(BoardGame.Runtime.FlowAction)` | 0.00% |
| [src/Authoring/FlowReferences.cs](../../src/Authoring/FlowReferences.cs#L20) | `BoardGame.Authoring.FlowReferences.<>c.<Validate>b__0_4(BoardGame.Runtime.FlowAction)` | 0.00% |
| [src/Editor/SessionController.cs](../../src/Editor/SessionController.cs#L120) | `BoardGame.Editor.SessionController.get_Session()` | 0.00% |
| [src/Editor/SessionController.cs](../../src/Editor/SessionController.cs#L120) | `BoardGame.Editor.SessionController.set_Session(BoardGame.Runtime.IDebugSession)` | 0.00% |
| [src/Editor/EditorHost.cs](../../src/Editor/EditorHost.cs#L34) | `BoardGame.Editor.EditorHost.<Start>d__13.MoveNext()` | 66.67% |
| [src/Editor/ExportPanel.cs](../../src/Editor/ExportPanel.cs#L20) | `BoardGame.Editor.ExportPanel.<<-ctor>b__5_2>d.MoveNext()` | 0.00% |
| [src/Editor/ExportPanel.cs](../../src/Editor/ExportPanel.cs#L20) | `BoardGame.Editor.ExportPanel.<<-ctor>b__5_3>d.MoveNext()` | 0.00% |
| [src/Editor/ExportWorkflow.cs](../../src/Editor/ExportWorkflow.cs#L33) | `BoardGame.Editor.ExportWorkflow.<>c__DisplayClass26_2.<Run>b__4()` | 75.00% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L76) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__3(object, Avalonia.Interactivity.RoutedEventArgs)` | 66.67% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L79) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__4(object, Avalonia.Interactivity.RoutedEventArgs)` | 0.00% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L82) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__5(object, Avalonia.Interactivity.RoutedEventArgs)` | 66.67% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L85) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__6(object, Avalonia.Interactivity.RoutedEventArgs)` | 0.00% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L94) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__9(object, Avalonia.Controls.SelectionChangedEventArgs)` | 0.00% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L95) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__10(object, Avalonia.Interactivity.RoutedEventArgs)` | 0.00% |
| [src/Editor/FlowInspector.cs](../../src/Editor/FlowInspector.cs#L100) | `BoardGame.Editor.FlowInspector.<>c__DisplayClass0_0.<.ctor>b__11(object, Avalonia.Interactivity.RoutedEventArgs)` | 80.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L90) | `BoardGame.Editor.FlowPanel.<>c.<.ctor>b__3_22(BoardGame.Runtime.FlowDiagnostic)` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L17) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_0.<.ctor>b__0(string)` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L53) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_0.<.ctor>b__2(object, Avalonia.Controls.SelectionChangedEventArgs)` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L78) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_0.<.ctor>b__4(object, Avalonia.Controls.SelectionChangedEventArgs)` | 75.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L98) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_0.<.ctor>b__6(object, Avalonia.Interactivity.RoutedEventArgs)` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L27) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_1.<.ctor>b__10(object, Avalonia.Interactivity.RoutedEventArgs)` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L27) | `BoardGame.Editor.FlowPanel.<>c__DisplayClass3_1.<.ctor>b__13(System.Threading.CancellationToken)` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L133) | `BoardGame.Editor.FlowEditorPlugin.<>c.<get_Definition>b__1_19(BoardGame.Runtime.FlowDiagnostic)` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L119) | `BoardGame.Editor.FlowEditorPlugin.<>c__DisplayClass1_0.<get_Definition>b__3()` | 0.00% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L132) | `BoardGame.Editor.FlowEditorPlugin.<>c__DisplayClass1_0.<get_Definition>b__13()` | 0.00% |
| [src/Editor/FormatPanel.cs](../../src/Editor/FormatPanel.cs#L85) | `BoardGame.Editor.GameFilePanel.<>c__DisplayClass0_0.<.ctor>b__3(object, Avalonia.Interactivity.RoutedEventArgs)` | 0.00% |
| [src/Editor/SessionController.cs](../../src/Editor/SessionController.cs#L37) | `BoardGame.Editor.SessionController.<<-ctor>b__71_0>d.MoveNext()` | 0.00% |
| [src/Editor/SessionController.cs](../../src/Editor/SessionController.cs#L104) | `BoardGame.Editor.SessionController.<ReloadScripts>d__76.MoveNext()` | 0.00% |
| [src/Editor/SessionController.cs](../../src/Editor/SessionController.cs#L111) | `BoardGame.Editor.SessionController.<SaveRecording>d__77.MoveNext()` | 0.00% |
| [src/Editor/SessionController.cs](../../src/Editor/SessionController.cs#L71) | `BoardGame.Editor.SessionController.<Send>d__75.MoveNext()` | 64.71% |
| [src/Editor/EditorHost.cs](../../src/Editor/EditorHost.cs#L21) | `BoardGame.Editor.EditorHost.<>c__DisplayClass12_0.<<-ctor>b__1>d.MoveNext()` | 87.50% |
| [src/Editor/FlowPanel.cs](../../src/Editor/FlowPanel.cs#L120) | `BoardGame.Editor.FlowEditorPlugin.<>c__DisplayClass1_0.<<get_Definition>b__5>d.MoveNext()` | 0.00% |
