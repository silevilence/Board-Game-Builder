using Avalonia.Controls;
using BoardGame.Kernel;
using Avalonia.Media.Imaging;

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
                documents.Set(ModuleDocument.Open("samples/flow-cards")); await app.Enable("author.flow");
                var commands = app.Service<EditorCommands>("commands"); var sessions = app.Service<SessionController>("sessions");
                FlowPanel Panel() => (FlowPanel)app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t => Equals(t.Tag, "author.flow")).Content!;
                await commands.Execute("flow.open"); await commands.Execute("flow.play"); sessions.SuspendScheduler();
                if (sessions.Frame?.Fault != null || Panel().Canvas.ActiveNode != "play") throw new Exception("Missing author flow playback: " + sessions.Status);
                var hash = sessions.Frame!.StateHash;
                for (var i = 0; i < 3; i++)
                {
                    await app.Reload("author.flow");
                    if (sessions.Frame.StateHash != hash || Panel().Canvas.ActiveNode != "play") throw new Exception("Panel reconstruction changed session");
                }
                documents.EditDraft("turn.lua", documents.Draft("turn.lua").Text + "\n");
                if (Panel().Canvas.ActiveNode != null) throw new Exception("Stale graph highlighted");
                documents.DiscardDraft("turn.lua");
                if (Panel().Canvas.ActiveNode != "play") throw new Exception("Matching graph did not recover");
                await commands.Execute("flow.open"); documents.Select("flow.node", "play"); await Task.Delay(150);
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
                await app.Disable("author.flow");
                if (!sessions.Active) throw new Exception("Plugin shutdown destroyed session");
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
