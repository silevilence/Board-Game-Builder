using Avalonia.Controls;
using BoardGame.Kernel;

namespace BoardGame.Editor;

internal static class FoundationVerification
{
    public static Window Create(string output)
    {
        var host=new EditorHost(baseOnly:true);
        host.Window.Opened+=async (_,_)=>
        {
            var errors=new List<string>();
            try
            {
                await host.Ready;
                var app=host.Application;
                var document=app.Service<DocumentWorkspace>("documents").Current ?? throw new Exception("No empty document");
                if(document.Snapshot().Id!="untitled") throw new Exception("Wrong empty document");
                app.Install(new("probe.optional",1,[],["documents"],_=>throw new Exception("optional probe failure")));
                try {await app.Enable("probe.optional");throw new Exception("Missing failure");}
                catch(Exception e) when(e.Message=="optional probe failure") { }
                if(!ReferenceEquals(document,app.Service<DocumentWorkspace>("documents").Current)) throw new Exception("Optional failure changed document");
                var panels=app.Service<PanelContributions>("panels");
                var baseline=panels.Count;var released=0;
                app.Install(new("probe.panel",1,[],["panels"],scope=>
                {
                    scope.Own(new Cleanup(()=>released++));
                    scope.Service<PanelContributions>("panels").Register(scope,"probe.panel","示例面板",()=>new TextBlock {Text="Independent panel"});
                }));
                for(var cycle=0;cycle<3;cycle++)
                {
                    await app.Enable("probe.panel");
                    if(panels.Count!=baseline+1) throw new Exception("Duplicate or missing panel");
                    await app.Disable("probe.panel");
                    if(panels.Count!=baseline || released!=cycle+1) throw new Exception("Panel effects leaked");
                }
                app.Install(new("probe.factory",1,[],["panels"],scope=>
                {
                    panels.Register(scope,"probe.temporary","temporary",()=>new TextBlock());
                    panels.Register(scope,"probe.throwing","throwing",()=>throw new Exception("factory failed"));
                }));
                try {await app.Enable("probe.factory");throw new Exception("Factory failure ignored");}
                catch(Exception e) when(e.Message=="factory failed") { }
                if(panels.Count!=baseline) throw new Exception("Factory failure leaked registration");
                app.Install(new("probe.duplicate",1,[],["panels"],scope=>panels.Register(scope,"base.settings","duplicate",()=>new TextBlock())));
                try {await app.Enable("probe.duplicate");throw new Exception("Duplicate accepted");}
                catch(InvalidDataException e) when(e.Message.Contains("Duplicate panel")) { }
                if(panels.Count!=baseline) throw new Exception("Unrelated panel removed");
                try {await app.Disable("base.documents");throw new Exception("Required plugin was disabled");}
                catch(InvalidOperationException e) when(e.Message.Contains("必需")) { }
                await app.DisposeAsync();
                if(app.Root.Fiber.DisposableCount!=0) throw new Exception("Root effects leaked");
            }
            catch(Exception error) {errors.Add(error.ToString());}
            finally {File.WriteAllText(Path.GetFullPath(output),Wire.Json(new {success=errors.Count==0,errors}));host.Window.Close();}
        };
        return host.Window;
    }
}
