using BoardGame.Authoring;
using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class FlowEditorTests
{
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
