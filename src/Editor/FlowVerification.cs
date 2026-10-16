using Avalonia.Controls;
using BoardGame.Kernel;

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
                for (var i = 0; i < 3; i++)
                {
                    await app.Enable("author.flow");
                    if (!app.Service<FlowWorkspace>("flow.author").Read().Success) throw new Exception("Flow unavailable");
                    if (!app.Service<EditorCommands>("commands").Items.Any(c => c.Id == "flow.open")) throw new Exception("Missing commands");
                    documents.EditDraft("flow.json", documents.Draft("flow.json").Text + "\n");
                    await app.Disable("author.flow");
                    if (!documents.Draft("flow.json").Dirty || app.Service<EditorCommands>("commands").Items.Any(c => c.Id.StartsWith("flow."))) throw new Exception("Plugin cleanup lost drafts or commands");
                    documents.DiscardDraft("flow.json");
                }
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
}
