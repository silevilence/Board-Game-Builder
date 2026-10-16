using Avalonia.Controls;
using BoardGame.Kernel;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using BoardGame.Authoring;
using BoardGame.Runtime;

namespace BoardGame.Editor;

internal static class FlowVerification
{
    public static Window Create(string output)
    {
        var host = new EditorHost("samples/flow-minimal", baseOnly: true);
        host.Window.Opened += async (_, _) =>
        {
            string? error = null;
            try
            {
                await host.Ready;
                var app = host.Application; var documents = app.Service<DocumentWorkspace>("documents");
                FormatPanel Wrapper() => (FormatPanel)app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t => Equals(t.Tag, "author.flow")).Content!;
                async Task Enable() { await app.Enable("author.flow"); await Wrapper().Ready; }
                async Task Reload() { await app.Reload("author.flow"); await Wrapper().Ready; }
                // Unload before child initialization has necessarily finished.
                for (var i = 0; i < 10; i++)
                {
                    documents.Set(ModuleDocument.Open("samples/flow-minimal"));
                    await app.Enable("author.flow");
                    documents.Set(ModuleDocument.Create());
                    await app.Disable("author.flow").WaitAsync(TimeSpan.FromSeconds(20));
                }
                documents.Set(ModuleDocument.Open("samples/flow-minimal"));
                for (var i = 0; i < 3; i++)
                {
                    await Enable();
                    if (!app.Service<FlowWorkspace>("flow.author").Read().Success) throw new Exception("Flow unavailable");
                    if (!app.Service<EditorCommands>("commands").Items.Any(c => c.Id == "flow.open")) throw new Exception("Missing commands");
                    documents.EditDraft("flow.json", documents.Draft("flow.json").Text + "\n");
                    await app.Disable("author.flow");
                    if (!documents.Draft("flow.json").Dirty || app.Service<EditorCommands>("commands").Items.Any(c => c.Id.StartsWith("flow."))) throw new Exception("Plugin cleanup lost drafts or commands");
                    documents.DiscardDraft("flow.json");
                }
                documents.Set(ModuleDocument.Create()); await Enable();
                await app.Service<EditorCommands>("commands").Execute("flow.create"); await Wrapper().Ready;
                if (!app.Service<FlowWorkspace>("flow.author").Read().Success) throw new Exception("Create command did not associate startup");
                var draftFlow = app.Service<FlowWorkspace>("flow.author"); var incomplete = draftFlow.Load();
                await draftFlow.Update(incomplete, incomplete.Model.Definition with { Nodes = incomplete.Model.Definition.Nodes.Select(n => n.Kind == "start" ? n with { Results = ["done", null!] } : n).ToList() });
                await app.Service<EditorCommands>("commands").Execute("flow.open"); await Task.Delay(50);
                using (var invalidPreview = new RenderTargetBitmap(new(1400, 900))) invalidPreview.Render(host.Window);
                if (draftFlow.Read().Success) throw new Exception("Invalid output was accepted");
                documents.Undo();
                documents.Undo(); await Wrapper().Ready;
                if (Wrapper().Content is not TextBlock) throw new Exception("Create undo retained flow UI");
                documents.Redo(); await Wrapper().Ready;
                documents.Set(ModuleDocument.Open("samples/flow-cards")); await Wrapper().Ready;
                var commands = app.Service<EditorCommands>("commands"); var sessions = app.Service<SessionController>("sessions");
                FlowPanel Panel() => (FlowPanel)Wrapper().Content!;
                await commands.Execute("flow.open"); await Task.Delay(150);
                await BuildPrototype(app, host.Window, Panel);
                var generated = app.Service<FlowWorkspace>("flow.author").Read().Definition;
                var playerId = generated.Nodes.Single(n => n.Kind == "player").Id;
                await commands.Execute("flow.open"); await commands.Execute("flow.play"); sessions.SuspendScheduler();
                if (sessions.Frame?.Fault != null || Panel().Canvas.ActiveNode != playerId) throw new Exception("Missing author flow playback: " + sessions.Status);
                var hash = sessions.Frame!.StateHash;
                for (var i = 0; i < 3; i++)
                {
                    await Reload();
                    if (sessions.Frame.StateHash != hash || Panel().Canvas.ActiveNode != playerId) throw new Exception("Panel reconstruction changed session");
                }
                documents.EditDraft("turn.lua", documents.Draft("turn.lua").Text + "\n");
                if (Panel().Canvas.ActiveNode != null) throw new Exception("Stale graph highlighted");
                documents.DiscardDraft("turn.lua");
                if (Panel().Canvas.ActiveNode != playerId) throw new Exception("Matching graph did not recover");
                await commands.Execute("flow.open"); documents.Select("flow.node", playerId); await Task.Delay(150);
                using (var screenshot = new RenderTargetBitmap(new(1400, 900))) { screenshot.Render(host.Window); screenshot.Save(output + ".png", PngBitmapEncoderOptions.Default); }
                for (var step = 0; step < 30 && sessions.Frame is { View.Ended: false, Fault: null }; step++)
                {
                    if (sessions.Frame.Cpu) await sessions.Send(new("step", sessions.Frame.Revision));
                    else if (sessions.Frame.Actions.Count > 0) await sessions.Send(new("submit", sessions.Frame.Revision, sessions.Frame.Actions[0].Command));
                    else throw new Exception("Human has no action");
                }
                if (sessions.Frame is not { View.Ended: true, Fault: null }) throw new Exception("Mixed game did not end");
                foreach (var seat in sessions.CpuSeats.Keys) sessions.CpuSeats[seat] = true;
                await commands.Execute("flow.play"); sessions.SuspendScheduler();
                for (var step = 0; step < 30 && sessions.Frame is { View.Ended: false, Fault: null }; step++) await sessions.Send(new("step", sessions.Frame.Revision));
                if (sessions.Frame is not { View.Ended: true, Fault: null }) throw new Exception("CPU game did not end");
                var saved = Path.GetFullPath(output + ".project");
                documents.Current!.Save(saved); documents.Set(ModuleDocument.Open(saved)); await Wrapper().Ready;
                if (Panel().Canvas.Snapshot?.Model.Definition.Nodes.Count != 8) throw new Exception("Saved graph did not reopen");
                documents.Select("flow.node", playerId);
                var flowWorkspace = app.Service<FlowWorkspace>("flow.author");
                var fields = flowWorkspace.FormKey("flow.node", playerId); _ = flowWorkspace.Properties(flowWorkspace.Load(), "flow.node", playerId);
                documents.EditForm(fields, "{"); await Reload();
                if (!documents.HasPendingForms || flowWorkspace.Properties(flowWorkspace.Load(), "flow.node", playerId).Text != "{") throw new Exception("Panel lost invalid form");
                documents.DiscardForm(fields);
                var oldCanvas = Panel().Canvas;
                documents.Set(ModuleDocument.Create()); await Wrapper().Ready;
                if (Wrapper().Content is not TextBlock) throw new Exception("Unsupported flow UI retained");
                documents.Set(ModuleDocument.Open(saved)); await Wrapper().Ready;
                if (ReferenceEquals(Panel().Canvas, oldCanvas)) throw new Exception("Format child was reused after unload");
                documents.Set(ModuleDocument.Open("samples/flow-minimal")); await Wrapper().Ready;
                var budgetFlow = app.Service<FlowWorkspace>("flow.author");
                var loop = new FlowDefinition(1, [new() { Id = "s", Kind = "start" }, new() { Id = "loop", Kind = "system" }, new() { Id = "end", Kind = "end", Winners = [] }],
                    [new("enter", "s", "loop"), new("again", "loop", "loop"), new("exit", "loop", "end", Condition: new("eq", new(Path: "/state/turn"), new(Literal: System.Text.Json.JsonSerializer.SerializeToElement(999))), Priority: 1)]);
                await budgetFlow.Update(budgetFlow.Load(), loop);
                var startup = documents.Current!.Startup;
                startup["plugins"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == "flow")!["configuration"]!["maxAutomaticSteps"] = 4;
                documents.EditDraft(GameRuntime.Entry, startup.ToJsonString());
                await commands.Execute("flow.play"); sessions.SuspendScheduler();
                if (sessions.Frame?.Fault?.Code != "flow-budget" || Panel().Canvas.ActiveNode != "loop") throw new Exception("Budget pause missing from graph: " + sessions.Status + " / " + sessions.Frame?.Fault?.Code + " / " + Panel().Canvas.ActiveNode + " / source=" + sessions.SourceView?.Source.Version + " draft=" + Panel().Canvas.Snapshot?.View.Source.Version);
                var flowRevision = sessions.Reply!.AuthorityState!.Flow!.Revision;
                await Reload();
                if (sessions.Reply.AuthorityState!.Flow!.Revision != flowRevision) throw new Exception("Budget pause resumed during reconstruction");
                await commands.Execute("flow.resume");
                if (sessions.Reply!.AuthorityState!.Flow!.Revision <= flowRevision || sessions.Frame?.Fault?.Code != "flow-budget") throw new Exception("Explicit flow continuation failed");
                documents.Set(ModuleDocument.Open(saved)); await Wrapper().Ready;
                await commands.Execute("flow.play"); sessions.SuspendScheduler();
                await app.Disable("author.flow");
                if (!sessions.Active) throw new Exception("Plugin shutdown destroyed session");
                app.Install(new("probe.flow.failure", 1, [], ["commands", "panels"], scope =>
                {
                    scope.Service<EditorCommands>("commands").Register(scope, new("flow.failure", "probe", () => Task.CompletedTask, () => true));
                    scope.Service<PanelContributions>("panels").Register(scope, "flow.failure", "probe", () => throw new InvalidOperationException("expected factory failure"));
                }));
                try { await app.Enable("probe.flow.failure"); throw new Exception("Factory failure accepted"); }
                catch (InvalidOperationException failure) when (failure.Message.Contains("expected factory failure")) { }
                if (commands.Items.Any(c => c.Id.StartsWith("flow."))) throw new Exception("Failed activation leaked commands");
                documents.Set(null);
                await app.DisposeAsync();
                if (app.Root.Fiber.DisposableCount != 0) throw new Exception("Effects leaked");
            }
            catch (Exception failure) { error = failure.ToString(); }
            finally
            {
                File.WriteAllText(output, Wire.Json(new { success = error == null, error }));
                try { host.Application.Service<DocumentWorkspace>("documents").Set(null); } catch (ObjectDisposedException) { }
                await host.Application.DisposeAsync(); host.Window.Close();
            }
        };
        return host.Window;
    }

    private static async Task BuildPrototype(EditorApplication app, Window window, Func<FlowPanel> panel)
    {
        var documents = app.Service<DocumentWorkspace>("documents"); var flow = app.Service<FlowWorkspace>("flow.author");
        var source = flow.Load(); var graph = source.Model.Definition;
        await flow.Update(source, new(1, [], []), new());
        var ids = new Dictionary<string, string>(); var points = new Dictionary<string, FlowPoint>();
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        void Press(Point point)
        {
            var canvas = panel().Canvas;
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, pointer, window, canvas.TranslatePoint(point, window)!.Value, 0,
                new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        }
        void Release(Point point)
        {
            var canvas = panel().Canvas;
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, pointer, window, canvas.TranslatePoint(point, window)!.Value, 0,
                new(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        }
        void Click(Point point) { Press(point); Release(point); }
        void Apply(string collection, string id, string json)
        {
            documents.Select(collection, id);
            var field = panel().GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "flowProperties"); field.Text = json;
            panel().GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "应用属性")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (documents.HasPendingForms) throw new Exception("Property UI did not apply");
        }
        foreach (var node in graph.Nodes)
        {
            var button = panel().GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "+ " + FlowCanvas.KindTitle(node.Kind)));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var added = flow.Read().Definition.Nodes.Last(); ids[node.Id] = added.Id;
            var index = ids.Count - 1; var point = new FlowPoint(60 + index % 3 * 270, 60 + index / 3 * 170); points[added.Id] = point;
            Press(new(90, 110)); var canvas = panel().Canvas; var destination = new Point(point.X + 30, point.Y + 30);
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, pointer, window, canvas.TranslatePoint(destination, window)!.Value, 0,
                new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None)); Release(destination);
            if (flow.Load().Design.Positions[added.Id] != point) throw new Exception("Pointer drag did not move node");
            Apply("flow.node", added.Id, Wire.Json(node with { Id = added.Id }));
            panel().GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "flowName").Text = node.Id;
            panel().GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "应用名称")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (flow.Load().Design.Names[added.Id] != node.Id) throw new Exception("Name UI did not preserve stable ID");
        }
        foreach (var edge in graph.Edges)
        {
            var from = points[ids[edge.From]]; var to = points[ids[edge.To]];
            var result = graph.Nodes.Single(n => n.Id == edge.From).Results.IndexOf(edge.Result);
            var count = flow.Read().Definition.Edges.Count;
            Click(new(from.X + 200, from.Y + 58 + result * 22)); Click(new(to.X, to.Y + 34));
            var current = flow.Read().Definition;
            if (current.Edges.Count != count + 1) throw new Exception("Port connection failed: " + edge.Id);
            var added = current.Edges.Last(); Apply("flow.edge", added.Id, Wire.Json(edge with { Id = added.Id, From = ids[edge.From], To = ids[edge.To] }));
        }
        if (!flow.Read().Success) throw new Exception("Authored graph is invalid");
        var before = Wire.Json(flow.Read().Definition); documents.Undo(); documents.Redo();
        if (Wire.Json(flow.Read().Definition) != before) throw new Exception("Authored graph Undo/Redo failed");
        var lastEdge = flow.Read().Definition.Edges.Last(); documents.Select("flow.edge", lastEdge.Id);
        panel().Canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
        if (flow.Read().Definition.Edges.Any(e => e.Id == lastEdge.Id)) throw new Exception("Delete key did not disconnect edge");
        documents.Undo();
        Press(new(900, 400));
        var movedCanvas = panel().Canvas; var movedPoint = new Point(920, 420);
        movedCanvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, movedCanvas, pointer, window, movedCanvas.TranslatePoint(movedPoint, window)!.Value, 0,
            new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None)); Release(movedPoint);
        if (flow.Load().Design.PanX != 20) throw new Exception("Canvas pan failed"); documents.Undo();
        var zoomCanvas = panel().Canvas;
        zoomCanvas.RaiseEvent(new PointerWheelEventArgs(zoomCanvas, pointer, window, zoomCanvas.TranslatePoint(new(200, 200), window)!.Value, 0, PointerPointProperties.None, KeyModifiers.None, new(0, 1)));
        if (flow.Load().Design.Zoom <= 1) throw new Exception("Wheel zoom failed"); documents.Undo();
        panel().Canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        var issueList = panel().GetLogicalDescendants().OfType<ListBox>().First(); issueList.SelectedIndex = 0;
        if (documents.Selection?.Collection != "flow.edge") throw new Exception("Diagnostic selection did not locate edge");
        documents.Select("flow.node", ids["start"]);
        panel().GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "flowScriptPath").Text = "scripts/entry.lua";
        panel().GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "创建并关联模板")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (documents.HasPendingForms || !documents.Current!.Paths.Contains("scripts/entry.lua")) throw new Exception("Script template UI failed");
        var final = flow.Load(); await flow.Update(final, final.Model.Definition, final.Design with { Zoom = .85 });
        await app.Enable("author.layout");
        var layout = (FormatPanel)app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t => Equals(t.Tag, "author.layout")).Content!;
        await layout.Ready;
        ((Control)layout.Content!).GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "应用布局")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (documents.Current!.Snapshot().Initial.Layout == null) throw new Exception("Existing layout UI did not apply");
        await app.Disable("author.layout");
    }
}
