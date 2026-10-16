using BoardGame.Authoring;
using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class FlowEditorTests
{
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
        var view = flow.View(); var graph = flow.Read().Definition;
        await flow.Apply(FlowDesign.Change(view, graph, new() { PanX = 25, Zoom = 1.5 }));
        Check.That(documents.Current.Snapshot().Fingerprint == first, "Design metadata changed runtime identity");
        documents.Undo(); Check.That(FlowDesign.ReadView(flow.View(), "graphs/main.json").Zoom == 1);
        documents.Redo(); Check.That(FlowDesign.ReadView(flow.View(), "graphs/main.json").Zoom == 1.5);
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
