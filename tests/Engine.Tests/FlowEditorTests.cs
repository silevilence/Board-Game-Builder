using BoardGame.Authoring;
using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class FlowEditorTests
{
    public static void Lifetime() => LifetimeAsync().GetAwaiter().GetResult();
    private static async Task LifetimeAsync()
    {
        // Signals establish ordering; this is only a deadlock watchdog for loaded CI machines.
        var watchdog=TimeSpan.FromSeconds(30);
        using var documents = new DocumentWorkspace(); documents.Set(ModuleDocument.Open("samples/flow-minimal"));
        await using var app = new EditorApplication();
        FlowEditorWork? work = null; var reports = new List<string>();
        app.Install(new("documents", 1, ["documents"], [], scope => scope.Provide("documents", documents)));
        app.Install(new("flow-work", 1, [], ["documents"], scope => work = new(scope, reports.Add)));
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await app.Enable("flow-work");
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = false;
            work!.Run(async token =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { cancelled.SetResult(); await release.Task; finished = true; }
            });
            var stopping = app.Disable("flow-work");
            try
            {
                await cancelled.Task.WaitAsync(watchdog);
                Check.That(!stopping.IsCompleted, "Plugin cleanup did not await its gesture work");
            }
            finally { release.TrySetResult(); }
            await stopping.WaitAsync(watchdog);
            Check.That(finished && reports.Count == 0, "Cancelled work reported a late error");
            work.Run(_ => throw new Exception("Disposed work executed"));
        }
        await app.Enable("flow-work");
        work!.Run(_ => throw new InvalidOperationException("visible failure"));
        Check.That(reports.SequenceEqual(new[] { "visible failure" }));
        var delayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Run(async _ => { await delayed.Task; throw new InvalidOperationException("old document"); });
        documents.Set(ModuleDocument.Open("samples/flow-minimal"));
        delayed.SetResult();
        await app.Disable("flow-work");
        Check.That(reports.Count == 1, "Old document work updated the new panel");
        app.Install(new("failed-work", 1, [], ["documents"], scope =>
        {
            work = new(scope, reports.Add);
            work.Run(token => Task.Delay(Timeout.Infinite, token));
            throw new InvalidOperationException("factory failed");
        }));
        try { await app.Enable("failed-work").WaitAsync(watchdog); throw new Exception("Expected activation failure"); }
        catch (InvalidOperationException error) when (error.Message == "factory failed") { }
    }
    public static void Conflicts() => ConflictsAsync().GetAwaiter().GetResult();
    private static async Task ConflictsAsync()
    {
        using var documents = new DocumentWorkspace(); documents.Set(ModuleDocument.Open("samples/flow-cards"));
        var flow = new FlowWorkspace(documents); var snapshot = flow.Load(); var nodes = snapshot.Model.Definition.Nodes.Take(2).ToArray();
        foreach (var node in nodes)
        {
            _ = flow.Properties(snapshot, "flow.node", node.Id);
            documents.EditForm(flow.FormKey("flow.node", node.Id), Wire.Json(node with { Results = ["changed"] }));
        }
        foreach (var node in nodes) Check.Throws(() => flow.View(), flow.FormKey("flow.node", node.Id));
        var names = string.Join(", ", nodes.Select(n => flow.FormKey("flow.node", n.Id)).Order(StringComparer.Ordinal));
        Check.Throws(() => flow.View(), names);
        Check.Throws(() => documents.ApplyDrafts(), names);
        flow.ApplyProperties(snapshot, "flow.node", nodes[0].Id);
        Check.That(documents.HasPendingForms, "Applying one form discarded another");
        Check.Throws(() => flow.ApplyProperties(snapshot, "flow.node", nodes[1].Id), "版本");
        documents.DiscardForm(flow.FormKey("flow.node", nodes[1].Id)); documents.Undo();
        Check.That(flow.Read().Definition.Nodes[0].Results.SequenceEqual(nodes[0].Results));
        var path = snapshot.Model.Path;
        documents.EditDraft(path, documents.Draft(path).Text + "\n");
        var yaml = new DocumentDraftTarget(path, true);
        documents.EditDraft(yaml, documents.Draft(yaml).Text + "\n");
        Check.Throws(() => flow.Load(), "JSON and YAML");
        Check.That(documents.Draft(path).Dirty && documents.Draft(yaml).Dirty);
        documents.DiscardDraft(path); documents.DiscardDraft(yaml);
        var change = FlowDesign.Change(flow.View(), flow.Read().Definition with { Edges = [] });
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var rejected = false; try { await flow.Apply(change, cancellation.Token); } catch (OperationCanceledException) { rejected = true; }
        Check.That(rejected && flow.Read().Success);
        using (var mode = new WorkspaceEditMode(documents, flow.Identity))
        {
            Check.Throws(() => mode.ApplyInline(change, cancellation.Token));
            Check.That(flow.Read().Success, "Cancelled inline edit changed the document");
        }
        using (var dispatched = new WorkspaceEditMode(documents, flow.Identity, action => Task.FromResult(action())))
            Check.Throws(() => dispatched.ApplyInline(change), "awaited");
        var directory = Path.Combine(Path.GetTempPath(), "bgb-flow-external-" + Guid.NewGuid());
        try
        {
            TestModules.CopyGame("samples/flow-cards", directory); documents.Set(ModuleDocument.Open(directory));
            var old = flow.Load(); File.AppendAllText(Path.Combine(directory, "turn.lua"), "\n-- external");
            rejected = false; try { await flow.Delete(old, "flow.node", "play"); } catch (AuthorOperationException e) { rejected = e.Code == "workspace.external"; }
            Check.That(rejected && documents.Current!.Text("turn.lua") == old.View.Text("turn.lua"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    public static void Playback() => PlaybackAsync().GetAwaiter().GetResult();
    private static async Task PlaybackAsync()
    {
        using var documents = new DocumentWorkspace(); documents.Set(ModuleDocument.Open("samples/flow-cards"));
        var flow = new FlowWorkspace(documents); var source = flow.View(); var module = documents.Current!.Snapshot();
        using var session = new DebugSession(module, 42, module.Initial.Seats.ToDictionary(s => s.Id, _ => "human"), "p1");
        Check.That(session.Handle(new("frame")).AuthorityState == null);
        var reply = session.Handle(new("observe", Authority: true)); var state = reply.AuthorityState!.Flow!;
        Check.That(state.Status == "waiting" && state.Seat == "p1" && state.Trace.Count > 0);
        var before = reply.Frame.StateHash;
        Check.That(session.Handle(new("frame")).Frame.StateHash == before, "Query reentered a node");
        Check.That(FlowPlayback.Matches(source, flow.Load(), false));
        var snapshot = flow.Load(); await flow.Update(snapshot, snapshot.Model.Definition, new() { Zoom = .75 });
        Check.That(FlowPlayback.Matches(source, flow.Load(), false), "View-only edit invalidated graph playback");
        documents.EditDraft("turn.lua", documents.Draft("turn.lua").Text + "\n");
        Check.That(!FlowPlayback.Matches(source, flow.Load(), false));
        documents.DiscardDraft("turn.lua"); Check.That(!FlowPlayback.Matches(source, flow.Load(), true));
        documents.Set(ModuleDocument.Open("samples/flow-cards")); Check.That(!FlowPlayback.Matches(source, flow.Load(), false));
    }
    public static void Validation() => ValidationAsync().GetAwaiter().GetResult();
    private static async Task ValidationAsync()
    {
        using var documents = new DocumentWorkspace(); documents.Set(ModuleDocument.Open("samples/flow-minimal"));
        var flow = new FlowWorkspace(documents); var snapshot = flow.Load();
        var graph = new FlowDefinition(1, [new() { Id = "s", Kind = "start" }, new() { Id = "a", Kind = "system" }, new() { Id = "b", Kind = "system" }, new() { Id = "e", Kind = "end", Winners = [] }],
            [new("sa", "s", "a"), new("ab", "a", "b"), new("ba", "b", "a"), new("exit", "b", "e", Condition: new("false"))]);
        await flow.Update(snapshot, graph);
        var model = flow.Read();
        Check.That(model.Diagnostics.Count(d => d.Code == "no-terminal-path") == 3);
        Check.That(model.Diagnostics.Any(d => d.Code == "false-exit" && d.Edge == "exit"));
        Check.That(Wire.Json(model.Diagnostics) == Wire.Json(FlowOperations.Read(flow.View()).Diagnostics), "CLI/editor diagnostics differ");
        graph = graph with { Edges = graph.Edges.Select(e => e.Id == "exit" ? e with { Condition = new("eq", new(Path: "/turn"), new(Literal: System.Text.Json.JsonSerializer.SerializeToElement(5))), Priority = 1 } : e).ToList() };
        await flow.Update(flow.Load(), graph);
        Check.That(flow.Read().Success && flow.Read().Diagnostics.Any(d => d.Code == "conditional-exit"));
        graph.Nodes[1] = graph.Nodes[1] with { Script = new("missing.lua") };
        await flow.Update(flow.Load(), graph);
        Check.That(flow.Read().Diagnostics.Any(d => d.Code == "script-missing" && d.Node == "a"));
        documents.Set(ModuleDocument.Open("samples/flow-cards"));
        Check.That(flow.Read().Success && flow.Read().Source.Identity.Generation != model.Source.Identity.Generation);
    }
    public static void Properties() => PropertiesAsync().GetAwaiter().GetResult();
    private static async Task PropertiesAsync()
    {
        using var documents = new DocumentWorkspace(); documents.Set(ModuleDocument.Open("samples/flow-cards"));
        var flow = new FlowWorkspace(documents); var snapshot = flow.Load(); var node = snapshot.Model.Definition.Nodes.First(n => n.Kind == "player");
        _ = flow.Properties(snapshot, "flow.node", node.Id); var key = flow.FormKey("flow.node", node.Id);
        documents.EditForm(key, "{"); Check.Throws(() => flow.ApplyProperties(snapshot, "flow.node", node.Id));
        Check.That(flow.Properties(flow.Load(), "flow.node", node.Id).Text == "{");
        Check.Throws(() => flow.View(), "fields");
        documents.EditForm(key, Wire.Json(node with { Seat = new(Fixed: "missing") }));
        flow.ApplyProperties(snapshot, "flow.node", node.Id);
        Check.That(flow.Read().Definition.Nodes.First(n => n.Id == node.Id).Seat!.Fixed == "missing");
        Check.That(flow.Read().Diagnostics.Any(d => d.Code == "seat-reference" && d.Node == node.Id));
        _ = flow.Properties(flow.Load(), "flow.node", node.Id); documents.Undo();
        Check.That(flow.Read().Definition.Nodes.First(n => n.Id == node.Id).Seat == node.Seat, "Clean form prevents graph undo");
        snapshot = flow.Load(); _ = flow.Properties(snapshot, "flow.node", node.Id);
        documents.EditForm(key, Wire.Json(node with { Parameters = new() }));
        documents.EditDraft(snapshot.Model.Path, FlowFile.Write(snapshot.Model.Definition) + "\n");
        Check.Throws(() => flow.ApplyProperties(snapshot, "flow.node", node.Id), "版本");
        Check.That(documents.HasPendingForms && documents.Draft(snapshot.Model.Path).Dirty);
        documents.DiscardForm(key); documents.DiscardDraft(snapshot.Model.Path);
        await flow.CreateScript(flow.Load(), node.Id, "seat", "scripts/seat.lua");
        Check.That(documents.Current!.Paths.Contains("scripts/seat.lua"));
        using (var game = new PlaySession(documents.Current.Snapshot())) Check.That(game.CurrentSeat == "p1" && game.Fault == null);
        documents.Undo(); Check.That(!documents.Current.Paths.Contains("scripts/seat.lua"));
        documents.Redo(); Check.That(documents.Current.Paths.Contains("scripts/seat.lua"));
        documents.EditDraft("scripts/seat.lua", "invalid Lua preserved");
        Check.That(new FlowWorkspace(documents).Load().Model.Definition.Nodes.First(n => n.Id == node.Id).Seat!.Script!.Path == "scripts/seat.lua");
        documents.Set(ModuleDocument.Create()); await flow.Create("flow.json");
        await flow.Update(flow.Load(), new(1, [new() { Id = "start", Kind = "start" }, new() { Id = "player", Kind = "player", Seat = new(Current: true) }, new() { Id = "end", Kind = "end", Winners = [] }],
            [new("enter", "start", "player"), new("finish", "player", "end")]));
        foreach (var role in new[] { "seat", "actions", "decide" }) await flow.CreateScript(flow.Load(), "player", role, role + ".lua");
        await flow.CreateScript(flow.Load(), "start", "execute", "execute.lua");
        Check.That(flow.Read().Success);
        using (var game = new PlaySession(documents.Current!.Snapshot()))
        {
            Check.That(game.Fault == null && game.CurrentSeat == "p1");
            Check.That(game.SubmitHuman("p1", game.Actions()[0].Command).Accepted && game.Ended, "Generated player script templates cannot complete a game");
        }
        var sourceIdentity = documents.Current.Snapshot().Fingerprint; var source = flow.Load();
        _ = flow.Name(source, "player"); documents.EditForm(flow.NameKey("player"), "等待操作"); flow.ApplyName(source, "player");
        Check.That(documents.Current.Snapshot().Fingerprint == sourceIdentity && flow.Read().Definition.Nodes.Any(n => n.Id == "player"));
    }
    public static void Canvas() => CanvasAsync().GetAwaiter().GetResult();
    private static async Task CanvasAsync()
    {
        using var documents = new DocumentWorkspace(); documents.Set(ModuleDocument.Open("samples/flow-cards"));
        var flow = new FlowWorkspace(documents); var original = flow.Load();
        var player = original.Model.Definition.Nodes.First(n => n.Kind == "player");
        await flow.Update(original, original.Model.Definition, original.Design with { Positions = new() { [player.Id] = new(400, 300) } });
        var positioned = flow.Load();
        await flow.Delete(positioned, "flow.node", player.Id);
        Check.That(flow.Read().Definition.Edges.All(e => e.From != player.Id && e.To != player.Id));
        documents.Undo(); Check.That(Wire.Json(flow.Read().Definition) == Wire.Json(positioned.Model.Definition));
        Check.That(FlowDesign.ReadView(flow.View(), positioned.Model.Path).Positions[player.Id] == new FlowPoint(400, 300));
        documents.Redo(); Check.That(flow.Read().Definition.Nodes.All(n => n.Id != player.Id)); documents.Undo();
        await flow.Connect(flow.Load(), player.Id, player.Results[0], player.Id);
        Check.That(flow.Read().Definition.Edges.Any(e => e.From == player.Id && e.To == player.Id)); documents.Undo();
        var stale = flow.Load(); documents.EditDraft(stale.Model.Path, FlowFile.Write(stale.Model.Definition) + "\n");
        var rejected = false; try { await flow.Delete(stale, "flow.node", player.Id); } catch (InvalidOperationException) { rejected = true; }
        Check.That(rejected && documents.Draft(stale.Model.Path).Dirty); documents.DiscardDraft(stale.Model.Path);
        var temp = Path.Combine(Path.GetTempPath(), "bgb-flow-design-" + Guid.NewGuid());
        try
        {
            documents.Current!.Save(temp); documents.Set(ModuleDocument.Open(temp));
            Check.That(flow.Read().Success && flow.Load().Design.Positions[player.Id] == new FlowPoint(400, 300));
            var graph = flow.Load();
            await flow.Update(graph, graph.Model.Definition with { Edges = graph.Model.Definition.Edges.Where(e => e.From == player.Id && e.To == player.Id).ToList() });
            Check.That(flow.Read().Diagnostics.Any(d => d.Code == "no-terminal-path"));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
    public static void Model() => ModelAsync().GetAwaiter().GetResult();
    private static async Task ModelAsync()
    {
        using var documents = new DocumentWorkspace(); documents.Set(ModuleDocument.Create());
        var flow = new FlowWorkspace(documents);
        await flow.Create("graphs/main.json");
        Check.That(flow.Read().Success && flow.Read().Path == "graphs/main.json");
        var first = documents.Current!.Snapshot().Fingerprint;
        _ = documents.Draft("cpu.lua"); documents.Undo();
        Check.That(!flow.Supported, "Opening a clean script draft blocked create Undo"); documents.Redo();
        var view = flow.View(); var graph = flow.Read().Definition;
        await flow.Apply(FlowDesign.Change(view, graph, new() { PanX = 25, Zoom = 1.5 }));
        Check.That(documents.Current.Snapshot().Fingerprint == first, "Design metadata changed runtime identity");
        documents.Undo(); Check.That(FlowDesign.ReadView(flow.View(), "graphs/main.json").Zoom == 1);
        documents.Redo(); Check.That(FlowDesign.ReadView(flow.View(), "graphs/main.json").Zoom == 1.5);
        await flow.Update(flow.Load(), graph, new() { Names = new() { ["start"] = null! } });
        Check.Throws(() => flow.Load(), "design"); documents.Undo();
        var stale = FlowDesign.Change(flow.View(), graph);
        documents.Set(ModuleDocument.Open("samples/flow-minimal"));
        Check.That(!documents.Current.SupportsStructuredEditing && flow.Read().Success, "Flow must be independent of the main definition format");
        var rejected = false; try { await flow.Apply(stale); } catch (AuthorOperationException) { rejected = true; }
        Check.That(rejected, "Old workspace generation accepted");
        documents.EditDraft("flow.json", "{");
        Check.Throws(() => flow.Read());
        Check.That(documents.Draft("flow.json").Text == "{");
    }
}
