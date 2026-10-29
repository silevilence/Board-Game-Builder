using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.AppRuntime;
using BoardGame.Authoring;
using BoardGame.Editor;
using Sample.Motion;

void Check(bool value,string message){if(!value)throw new Exception(message);}
var target=Path.GetFullPath(".scratch/application-loop/motion-author-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(target);
foreach(var file in ProjectPaths.Read("samples/motion-lab"))File.WriteAllBytes(Path.Combine(target,file.Key),file.Value);
var catalog=AuthorHost.Builtins.Append(MotionAuthorPlugin.Definition).ToArray();
var profile=AuthorProfiles.Resolve(target,null);
profile=profile with {Profile=profile.Profile with {Plugins=[..profile.Profile.Plugins,new("author.export.package")]}};
JsonElement Args(object value)=>JsonSerializer.SerializeToElement(value,AuthorProfiles.Json);
JsonElement Json(object value)=>Args(value);
await using(var host=await AuthorHost.Start(profile,catalog))
{
    var read=Json(await host.Invoke("configuration.read",Args(new{}),"read"));
    var version=read.GetProperty("source").GetProperty("version").GetString()!;
    var model=JsonNode.Parse(read.GetProperty("configuration").GetRawText())!.AsObject();model["title"]="Disk author motion";
    var receipt=(EditReceipt)await host.Invoke("configuration.replace",Args(new{version,configuration=model}),"write");
    Check(receipt.State==EditState.Applied && receipt.Saved,"Disk edit failed");
    var valid=(ValidationResult)await host.Invoke("project.validate",Args(new{}),"validate");
    Check(valid.Success && valid.Checks.Contains("initial-application-update"),string.Join(";",valid.Diagnostics.Select(d=>d.Message)));
    var run=Json(await host.Invoke("run.application",Args(new{ticks=60}),"run"));Check(run.GetProperty("ticks").GetInt64()==60,"Wrong application step count");
    Check(AuthorDocuments.Installed(catalog).Any(d=>d.Topic=="motion" && d.Plugin=="author.motion"),"Provider documentation missing");
    Check(host.Service<AuthorExports>("author.exports").Items.Single().Id=="game-package","Package provider must work independently");
    var artifact=(AuthorArtifact)await host.Invoke("project.pack",Args(new{target=target+".bgm"}),"pack");
    Check(GameRuntime.Load(artifact.Target,new ApplicationCatalog(MotionPlugins.Catalog()).Content).Fingerprint==artifact.Fingerprint,"Nonboard package load failed");
    using(var zip=System.IO.Compression.ZipFile.OpenRead(artifact.Target))Check(!zip.Entries.Any(e=>e.FullName.EndsWith(".dll") || e.FullName.EndsWith(".exe")),"Data package contains engine binaries");
}
if(Environment.GetEnvironmentVariable("BGB_TEST_MINIMAL_RELEASE") is {} minimalRelease)
{
    var playerProfile=profile with{Profile=profile.Profile with{Plugins=[..profile.Profile.Plugins.Where(p=>p.Id!="author.export.package"),new("author.export.player")]}};
    await using var playerHost=await AuthorHost.Start(playerProfile,catalog);
    Check(playerHost.Service<AuthorExports>("author.exports").Items.Single().Id=="player-directory","Player provider depends on package provider");
    var standalone=(AuthorArtifact)await playerHost.Invoke("player.export",Args(new{target=target+"-standalone",player=Path.GetFullPath(minimalRelease)}),"standalone");
    Check(File.Exists(Path.Combine(standalone.Target,"Play.exe")),"No graphical standalone entry");
    var binaries=Directory.GetFiles(standalone.Target,"*.dll",SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
    Check(!binaries.Any(p=>p!.StartsWith("BoardGame.") || p!.StartsWith("Kernel") || p is "Runtime.dll" or "Modules.dll" or "Lua.Execution.dll" or "MoonSharp.Interpreter.dll" or "wasmtime.dll"),"Nonboard Player contains board dependencies");
    Check(GameRuntime.Load(Path.Combine(standalone.Target,"game.bgm"),new ApplicationCatalog(MotionPlugins.Catalog()).Content).Fingerprint==standalone.Fingerprint,"Standalone package mismatch");
    Console.WriteLine("Standalone: "+standalone.Target);
}
var disk=File.ReadAllBytes(Path.Combine(target,"motion.json"));
using(var docs=new DocumentWorkspace(legacyBoardDefaults:false))
{
    using var runtime=docs.Runtimes.Register(new ApplicationAuthorRuntime("motion",new ApplicationCatalog(MotionPlugins.Catalog())));
    docs.Set(ModuleDocument.Open(target));
    var identity=new ProjectIdentity(target,"gui-test","gui",docs.WorkspaceId,docs.Generation);
    var mode=new WorkspaceEditMode(docs,identity);
    var plugin=new AuthorPlugin("edit.test-gui","1",1,["author.mode"],[],s=>s.Provide("author.mode",s.Own(mode)),"Test GUI mode");
    var guiProfile=profile with {Name="gui-test",Profile=profile.Profile with {Plugins=profile.Profile.Plugins.Select(p=>p.Id=="edit.disk"?new AuthorPluginSelection("edit.test-gui"):p).ToArray()}};
    await using var host=await AuthorHost.Start(guiProfile,[..catalog,plugin]);
    var read=Json(await host.Invoke("configuration.read",Args(new{}),"read"));
    var version=read.GetProperty("source").GetProperty("version").GetString()!;
    var model=JsonNode.Parse(read.GetProperty("configuration").GetRawText())!.AsObject();model["title"]="GUI author motion";
    var receipt=(EditReceipt)await host.Invoke("configuration.replace",Args(new{version,configuration=model}),"write");
    Check(receipt.State==EditState.Applied && !receipt.Saved && docs.Dirty,"GUI edit did not stay in memory");
    Check(disk.SequenceEqual(File.ReadAllBytes(Path.Combine(target,"motion.json"))),"GUI edit saved implicitly");
    var packed=(AuthorArtifact)await host.Invoke("project.pack",Args(new{target=target+"-draft.bgm"}),"draft-pack");
    var content=GameRuntime.Load(packed.Target,new ApplicationCatalog(MotionPlugins.Catalog()).Content);
    using(var zip=System.IO.Compression.ZipFile.OpenRead(packed.Target))
    using(var reader=new StreamReader(zip.GetEntry("motion.json")!.Open()))Check(reader.ReadToEnd().Contains("GUI author motion"),"Export did not use draft");
    Check(disk.SequenceEqual(File.ReadAllBytes(Path.Combine(target,"motion.json"))),"Export saved draft implicitly");
    var valid=(ValidationResult)await host.Invoke("project.validate",Args(new{}),"validate");Check(valid.Success,"GUI validation failed");
    var rejected=false;try{await host.Invoke("configuration.replace",Args(new{version,configuration=model}),"stale");}catch(AuthorOperationException e) when(e.Code=="project.conflict"){rejected=true;}
    Check(rejected,"Stale configuration overwrite accepted");
    docs.Undo();Check(docs.AuthorView(identity).Text("motion.json").Contains("Disk author motion"),"GUI Undo failed");
    docs.Redo();Check(docs.AuthorView(identity).Text("motion.json").Contains("GUI author motion"),"GUI Redo failed");
    docs.Save();Check(File.ReadAllText(Path.Combine(target,"motion.json")).Contains("GUI author motion"),"Explicit generic Save failed");
    docs.Set(ModuleDocument.Open(target));rejected=false;
    try{await host.Invoke("configuration.read",Args(new{}),"old-generation");}catch(AuthorOperationException e) when(e.Code=="workspace.stale"){rejected=true;}
    Check(rejected,"Old GUI generation accessed new document");
}
await using(var host=await AuthorHost.Start(profile with{Profile=profile.Profile with {Plugins=profile.Profile.Plugins.Where(p=>p.Id!="author.motion").ToArray()}},catalog))
{
    var valid=(ValidationResult)await host.Invoke("project.validate",Args(new{}),"unknown");
    Check(!valid.Success && valid.Diagnostics.Any(d=>d.Code=="runtime.unavailable"),"Unknown runtime silently used board parsing");
    var files=Json(await host.Invoke("files.read",Args(new{path="motion.json"}),"files"));Check(files.GetProperty("text").GetString()!.Contains("GUI author motion"),"Generic file editing unavailable");
}
Console.WriteLine("PASS provider discovery, motion configuration, runtime validation, fixed updates, disk/GUI versions, no implicit Save, Undo/Redo, generation and missing-provider diagnostics");
