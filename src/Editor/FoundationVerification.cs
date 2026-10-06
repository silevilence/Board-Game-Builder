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
