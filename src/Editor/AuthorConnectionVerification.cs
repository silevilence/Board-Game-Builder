using Avalonia.Controls;
using BoardGame.Authoring;
using BoardGame.Kernel;
using System.Diagnostics;
using System.Text.Json;

namespace BoardGame.Editor;

internal static class AuthorConnectionVerification
{
    public static Window Create(string output, string cli)
    {
        output=Path.GetFullPath(output);cli=Path.GetFullPath(cli);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var host=new EditorHost(baseOnly:true);
        host.Window.Opened+=async (_,_)=>
        {
            string? failure=null;var checks=new List<string>();
            void Check(bool condition,string name) {if(!condition)throw new Exception(name);checks.Add(name);File.WriteAllText(output+".progress",name);}
            try
            {
                await host.Ready;await host.Application.Enable("author.runtime.board");await host.Application.Enable("author.module");
                var app=host.Application;var documents=app.Service<DocumentWorkspace>("documents");
                var project=output+".project";var initial=ModuleDocument.Create();initial.Save(project);
                var selections=AuthorProfiles.Default.Plugins.Select(p=>p.Id=="edit.disk"?new AuthorPluginSelection("edit.gui"):p).ToArray();
                File.WriteAllText(Path.Combine(project,AuthorProfiles.ConfigurationFile),JsonSerializer.Serialize(new {defaultProfile="connected",profiles=new {connected=new AuthorProfile(1,selections)}},AuthorProfiles.Json));
                documents.Set(ModuleDocument.Open(project));
                await app.Enable("author.workspace");await app.Enable("author.connection");await app.Enable("author.files");
                var commands=app.Service<EditorCommands>("commands");
                Check(commands.Items.Count(c=>c.Id.StartsWith("workspace.external."))==5,"Workspace commands registered once");
                async Task<JsonElement> Call(string operation,object parameters,bool success=true)
                {
                    var instance=WorkspaceConnections.Discover().Single(c=>c.Process==Environment.ProcessId && c.Project==project).Instance;
                    var start=new ProcessStartInfo(cli){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                    foreach(var arg in new[]{"call",operation,"--project",project,"--profile","connected","--instance",instance,"--args",JsonSerializer.Serialize(parameters,AuthorProfiles.Json),"--request",Guid.NewGuid().ToString("N")})start.ArgumentList.Add(arg);
                    using var process=Process.Start(start)!;var text=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                    var result=await text;var log=await error;
                    if(success && process.ExitCode!=0)throw new Exception(operation+": "+result+log);
                    if(!success && process.ExitCode==0)throw new Exception("Expected failure: "+operation);
                    return JsonSerializer.Deserialize<JsonElement>(result);
                }
                var script=documents.Current!.Text("rules.lua");documents.EditDraft("rules.lua",script+"\n-- UI draft");
                var read=await Call("files.read",new {path="rules.lua"});Check(read.GetProperty("result").GetProperty("text").GetString()!.Contains("UI draft"),"CLI reads unsaved UI draft");
                var import=await Call("resources.import.image",new {source=Path.GetFullPath("samples/freecell/assets/card_clubs_A.png"),target="assets/imported.png",name="imported"});
                Check(!import.GetProperty("result").GetProperty("saved").GetBoolean() && !File.Exists(Path.Combine(project,"assets/imported.png")),"Import applied only to workspace");
                documents.Undo();Check(!documents.Current.Paths.Contains("assets/imported.png"),"UI Undo removes complete import");documents.Redo();
                Check(documents.Current.Paths.Contains("assets/imported.png") && documents.Draft("rules.lua").Dirty,"UI Redo preserves draft");
                var valid=await Call("project.validate",new {});Check(valid.GetProperty("result").GetProperty("success").GetBoolean(),"Validation uses workspace snapshot");
                await Call("project.pack",new {target=output+".bgm"});Check(!File.Exists(Path.Combine(project,"assets/imported.png")),"Packaging does not save workspace");
                await Call("workspace.save",new {});Check(File.Exists(Path.Combine(project,"assets/imported.png")) && !documents.Dirty,"Explicit save persists workspace");
                File.AppendAllText(Path.Combine(project,"rules.lua"),"\n-- external");
                await Call("project.validate",new {},false);
                var status=(await Call("workspace.external.status",new {})).GetProperty("result");
                await Call("workspace.external.resolve",new {choice="reload",diskVersion=status.GetProperty("diskVersion").GetString(),workspaceVersion=status.GetProperty("workspaceVersion").GetString()});
                Check(documents.Current.Text("rules.lua").Contains("external"),"Explicit reload coordinates disk");
                File.AppendAllText(Path.Combine(project,"rules.lua"),"\n-- command reload");
                await commands.Execute("workspace.external.inspect");
                Check(commands.CanExecute("workspace.external.reload"),"Command availability shares workspace state");
                await commands.Execute("workspace.external.reload");
                Check(documents.Current.Text("rules.lua").Contains("command reload"),"Registered command executes reconciliation");
                documents.EditForm(DocumentWorkspace.ExternalMergeForm,"{unfinished merge");
                Check(!commands.CanExecute("workspace.external.reload"),"Dirty workspace disables clean reload command");
                await app.Disable("author.workspace");
                Check(!commands.Items.Any(c=>c.Id.StartsWith("workspace.external.")),"Workspace commands removed with plugin");
                await app.Enable("author.workspace");
                Check(commands.Items.Count(c=>c.Id.StartsWith("workspace.external."))==5,"Workspace commands restored without duplicates");
                Check(documents.Form(DocumentWorkspace.ExternalMergeForm,()=>"{}").Text=="{unfinished merge" && documents.Dirty,"Merge draft survives panel reconstruction");
                documents.DiscardForm(DocumentWorkspace.ExternalMergeForm);
                for(var i=0;i<3;i++)
                {
                    await app.Disable("author.connection");Check(!WorkspaceConnections.Discover().Any(c=>c.Process==Environment.ProcessId && c.Project==project),"Listener removed "+i);
                    await app.Enable("author.connection");await Call("files.list",new {});
                }
                documents.Set(ModuleDocument.Open(Path.GetFullPath("samples/state-minimal")));
                documents.EditForm(DocumentWorkspace.ExternalMergeForm,"{unfinished generic merge");
                Check(documents.Dirty,"Generic merge input works without structured author format");
                project=output+".motion-project";Directory.CreateDirectory(project);
                foreach(var file in ProjectPaths.Read("samples/motion-lab"))File.WriteAllBytes(Path.Combine(project,file.Key),file.Value);
                File.WriteAllText(Path.Combine(project,AuthorProfiles.ConfigurationFile),JsonSerializer.Serialize(new{defaultProfile="connected",profiles=new{connected=new AuthorProfile(1,[new("edit.gui"),new("author.files"),new("author.motion"),new("author.configuration"),new("author.validation")])}},AuthorProfiles.Json));
                await app.Enable("runtime.motion");documents.Set(ModuleDocument.Open(project));
                Check(!documents.Current!.SupportsStructuredEditing,"Unknown Editor format remains without board panels");
                var motionDisk=File.ReadAllBytes(Path.Combine(project,"motion.json"));
                var motionRead=(await Call("configuration.read",new{})).GetProperty("result");
                var motionConfig=System.Text.Json.Nodes.JsonNode.Parse(motionRead.GetProperty("configuration").GetRawText())!.AsObject();
                motionConfig["title"]="Connected motion draft";
                var motionWrite=await Call("configuration.replace",new{version=motionRead.GetProperty("source").GetProperty("version").GetString(),configuration=motionConfig});
                Check(!motionWrite.GetProperty("result").GetProperty("saved").GetBoolean() && motionDisk.SequenceEqual(File.ReadAllBytes(Path.Combine(project,"motion.json"))),"Generic CLI configuration stays in GUI draft");
                var motionValid=await Call("project.validate",new{});Check(motionValid.GetProperty("result").GetProperty("success").GetBoolean(),"Generic GUI snapshot validation");
                var motionRun=await Call("run.application",new{ticks=12});Check(motionRun.GetProperty("result").GetProperty("ticks").GetInt64()==12,"Generic GUI snapshot fixed-time run");
                documents.Undo();Check(!documents.Current.Text("motion.json").Contains("Connected motion draft"),"Generic configuration Undo");documents.Redo();
                var application=app.Service<ApplicationSessions>("sessions");await application.Start();
                Check(application.Active && application.SourceView!.Text("motion.json").Contains("Connected motion draft"),"Preview shares CLI draft version");application.Stop();
                await Call("workspace.save",new{});Check(File.ReadAllText(Path.Combine(project,"motion.json")).Contains("Connected motion draft"),"Explicit generic workspace save");
                documents.Set(null);await app.DisposeAsync();Check(app.Root.Fiber.DisposableCount==0,"Plugin effects released");
            }
            catch(Exception error) {failure=error.ToString();}
            finally
            {
                File.WriteAllText(output,Wire.Json(new {success=failure==null,checks,error=failure}));
                Environment.ExitCode=failure==null?0:1;
                host.Window.Close();
            }
        };
        return host.Window;
    }
}
