using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
using Wasmtime;
namespace Engine.Tests;
public static class WasmTests
{
    public static RuntimePlugin Plugin(string wat="(module (func (export \"bgb_api\") (result i32) i32.const 1) (func (export \"bgb_version\") (result i32) i32.const 1) (func (export \"bgb_init\") (result i32) i32.const 0))")
    {
        var bytes=Module.ConvertText(wat);return new("sample","1",1,"sample.wasm",ModulePackage.Hash(bytes),[],bytes);
    }
    public static void Run()
    {
        var p=Plugin();using(var plugins=new WasmPlugins([p])) Check.That(plugins.Ids.SequenceEqual(new[] {"sample"}));
        Check.Throws(()=>new WasmPlugins([p with {Version="2"}]),"version");
        Check.Throws(()=>new WasmPlugins([p with {Sha256=new string('0',64)}]),"hash");
        Check.Throws(()=>new WasmPlugins([p with {Permissions=["filesystem"]}]),"permission");
        Check.Throws(()=>ModulePackage.ValidateDependencies([new("third-party","1",1)]),"unsupported");
        var module=DirectoryModule.Load("samples/quick-cards") with {RuntimePlugins=[p]};
        module=module with {Fingerprint=ModuleSnapshot.Fingerprint(module)};
        using var player=new PlaySession(module,1);Check.That(player.Actions().Count>0);
        using var editor=new DebugSession(module,1,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"human"),"p1");
        Check.That(editor.Handle(new("frame")).Frame.Actions.Count>0);
        Check.Throws(()=>player.ReloadScripts(module with {RuntimePlugins=[]}),"restart");
        var directory=Path.Combine(Path.GetTempPath(),"bgb-wasm-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory,"module.json"),"""
            {"format":1,"id":"wasm-test","version":"1","rules":"rules.lua","cpu":"cpu.lua","resources":{},"seats":[{"id":"p1","team":"one"}],"phases":["play"],"entities":[]}
            """);
            File.WriteAllText(Path.Combine(directory,"rules.lua"),"return {}");File.WriteAllText(Path.Combine(directory,"cpu.lua"),"return {}");
            var root=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"module.json")))!;
            root["plugins"]=System.Text.Json.JsonSerializer.SerializeToNode(new[] {new RuntimeDependency(p.Id,p.Version,p.Api,Kind:"wasm",File:p.File,Sha256:p.Sha256)},Wire.Options);
            File.WriteAllText(Path.Combine(directory,"module.json"),root.ToJsonString());
            Check.Throws(()=>DirectoryModule.Load(directory),"missing file");
            File.WriteAllBytes(Path.Combine(directory,p.File),p.Code);
            var loaded=DirectoryModule.Load(directory);var packed=Path.Combine(directory,"test.bgm");ModulePackage.Pack(directory,packed);
            Check.That(ModulePackage.Load(packed).Fingerprint==loaded.Fingerprint);
            using var runtime=new WasmPlugins(ModulePackage.Load(packed).RuntimePlugins);
        }
        finally {Directory.Delete(directory,true);}
    }
}
