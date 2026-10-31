using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using BoardGame.Authoring;
using BoardGame.Plugins;
using BoardGame.Runtime;

namespace BoardGame.Editor;

internal static class RuntimePluginManagementVerification
{
    public static Window Create(string fixture,string output)
    {
        Directory.CreateDirectory(output);var host=new EditorHost("samples/motion-lab");
        host.Window.Opened+=async(_,_)=>
        {
            var errors=new List<string>();var checks=new List<string>();var app=host.Application;
            void Check(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
            try
            {
                await host.Ready;var management=app.Service<RuntimePluginManagement>("runtime.management");var commands=app.Service<EditorCommands>("commands");
                var documents=app.Service<DocumentWorkspace>("documents");var configuration=app.Service<RuntimeConfiguration>("runtime.configuration");
                var before=documents.Current!.Revision;var startup=documents.Current.Text(GameRuntime.Entry);
                management.PackagePath=Path.Combine(fixture,"view");await commands.Execute("runtime.plugins.import");
                Check(management.Status.Contains("sample.counter") && management.Installed.Count==0,"Missing capability import not diagnosed");
                foreach(var name in new[]{"counter","definition","view"}){management.PackagePath=Path.Combine(fixture,name);await commands.Execute("runtime.plugins.import");Check(!management.Status.Contains("失败"),management.Status);}
                Check(management.Installed.Count==3 && documents.Current.Revision==before && !documents.Dirty,"Import changed the project");
                management.PackagePath=Path.Combine(fixture,"counter");await commands.Execute("runtime.plugins.import");
                Check(management.Status.Contains("replacement must be explicit") && management.Installed.Count==3,"Duplicate identity silently replaced");
                Check(PluginCompatibility.Check(management.Installed,"win-x64",true,HostMode.Graphical).Diagnostics.Any(p=>p.Code=="aot.undeclared-or-incompatible"),"Missing AOT declaration accepted");
                management.Selection=management.Installed.Single(p=>p.Id=="sample.counter");var selected=management.Selection;
                await commands.Execute("runtime.plugins.enable");Check(configuration.Startup().Plugins.Any(p=>p.Id==selected.Id && p.Implementation==selected.Fingerprint),"Manager selection not pinned");
                documents.Undo();Check(documents.Draft(GameRuntime.Entry).Text==startup,"Manager enable cannot be undone");documents.Redo();
                var sessions=app.Service<ApplicationSessions>("sessions");await sessions.Start();Check(sessions.Active,sessions.Status);
                management.Selection=selected;await commands.Execute("runtime.plugins.remove");Check(management.Status.Contains("in use") && management.Installed.Count==3,"Active version removed");
                await commands.Execute("runtime.plugins.replace");Check(management.Status.Contains("in use"),"Active version replaced");
                await sessions.StopAsync();management.Selection=selected;await commands.Execute("runtime.plugins.remove");Check(management.Installed.Count==2,"Unused selected version not removed");
                Check(configuration.Startup().Plugins.Any(p=>p.Id==selected.Id) && configuration.Describe().Contains("缺少匹配"),"Removal silently edited project or replaced identity");
                await commands.Execute("runtime.plugins.import");Check(management.Installed.Count==3,"Removed version cannot be reimported");
                checks.Add("commands-import-dependency-conflict-no-auto-enable-aot-readonly-undo-and-active-version-protection");
                string Variant(string name,Action<JsonObject> edit)
                {
                    var folder=Path.Combine(Path.GetFullPath(output),name);Directory.CreateDirectory(folder);
                    foreach(var file in Directory.GetFiles(Path.Combine(fixture,"counter")))File.Copy(file,Path.Combine(folder,Path.GetFileName(file)));
                    var path=Path.Combine(folder,PluginManifest.FileName);var manifest=JsonNode.Parse(File.ReadAllText(path))!.AsObject();edit(manifest);File.WriteAllText(path,manifest.ToJsonString());return folder;
                }
                management.PackagePath=Variant("wrong-platform",m=>{m["targets"]=new JsonArray("linux-x64");});await commands.Execute("runtime.plugins.replace");Check(management.Status.Contains("unsupported target"),"Wrong target accepted");
                management.PackagePath=Variant("editor-kind",m=>m["kind"]="editor");await commands.Execute("runtime.plugins.replace");Check(management.Status.Contains("Unsupported runtime plugin"),"Editor package accepted as runtime");
                management.PackagePath=Variant("replacement",m=>m["aot"]=new JsonObject{["compatible"]=false,["targets"]=new JsonArray(),["reason"]="This version is ordinary only"});
                await commands.Execute("runtime.plugins.replace");Check(!management.Status.Contains("失败"),management.Status);
                Check(configuration.Startup().Plugins.Single(p=>p.Id==selected.Id).Implementation==selected.Fingerprint && configuration.Describe().Contains("指纹不一致"),"Replacement silently changed project fingerprint");
                checks.Add("wrong-platform-editor-kind-explicit-replacement-and-stale-project-diagnostic");
                SessionVerification.Select(app,"author.runtime.management");await Task.Delay(200);
                using(var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new((int)host.Window.Bounds.Width,(int)host.Window.Bounds.Height),new(96,96)))
                {bitmap.Render(host.Window);bitmap.Save(Path.Combine(output,"management.png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);}
                for(var cycle=0;cycle<2;cycle++)
                {
                    await app.Disable("author.runtime.management");Check(!commands.Items.Any(c=>c.Id=="runtime.plugins.import"),"Management command leaked");
                    await app.Enable("author.runtime.management");Check(commands.Items.Count(c=>c.Id=="runtime.plugins.import")==1,"Management command duplicated");
                }
                checks.Add("management-plugin-disable-reenable-keeps-installed-packages-and-project");
            }
            catch(Exception error){errors.Add(error.ToString());}
            finally
            {
                try{app.Service<DocumentWorkspace>("documents").Set(null);await app.DisposeAsync();}catch(Exception error){errors.Add(error.ToString());}
                File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{success=errors.Count==0,checks,errors}));host.Window.Close();
            }
        };
        return host.Window;
    }
}
