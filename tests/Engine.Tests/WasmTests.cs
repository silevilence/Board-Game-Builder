using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
using Wasmtime;
namespace Engine.Tests;
public static class WasmTests
{
    public static void Extensions()
    {
        var module=TestModules.Load("samples/wasm-counter");
        using var session=new PlaySession(module,42);
        for(var i=0;i<3;i++) Check.That(session.SubmitHuman("p1",session.Actions()[0].Command).Accepted);
        Check.That(session.Ended && session.View("p1").Entities.Single().Properties["wasm.counter"]==3);
        Check.That(Wire.Json(session.ExportRecording().Replay().Project("p1"))==Wire.Json(session.View("p1")));
        var path=Path.Combine(Path.GetTempPath(),"wasm-"+Guid.NewGuid()+".bgm");
        try {TestModules.Pack("samples/wasm-counter",path);using var packed=new PlaySession(TestModules.Load(path));Check.That(packed.Actions().Count==1);}
        finally {File.Delete(path);}
        var p=Plugin("""
        (module (func (export "bgb_api") (result i32) i32.const 1) (func (export "bgb_version") (result i32) i32.const 1)
          (func (export "bgb_init") (result i32) i32.const 12)
          (func (export "bgb_snap") (param i32 i32) (result i32) local.get 1 i32.const 10 i32.div_s i32.const 10 i32.mul)
          (func (export "bgb_container") (param i32 i32) (result i32) local.get 1))
        """) with {Permissions=["snap","container"]};
        using var runtime=new WasmPlugins([p]);var view=session.View("p1");
        Check.That(runtime.Expand(view,new() {Kind="wasm.sample.snap",Entity="counter",X=24,Y=38}) is {X:20,Y:30});
        Check.Throws(()=>runtime.Expand(view,new() {Kind="wasm.sample.snap",Entity="hidden"}),"visible");
    }
    public static void Isolation()
    {
        var prefix="(module (func (export \"bgb_api\") (result i32) i32.const 1) (func (export \"bgb_version\") (result i32) i32.const 1) ";
        Check.Throws(()=>new WasmPlugins([Plugin("(module (import \"wasi_snapshot_preview1\" \"path_open\" (func)))")]),"imports");
        Check.Throws(()=>new WasmPlugins([Plugin("(module (import \"host\" \"authority\" (func)))")]),"imports");
        Check.Throws(()=>new WasmPlugins([Plugin(prefix+"(func (export \"bgb_init\") (result i32) (loop $forever br $forever) i32.const 0))")]),"fuel");
        Check.Throws(()=>new WasmPlugins([Plugin(prefix+"(memory 65) (func (export \"bgb_init\") (result i32) i32.const 0))")]),"memory");
        Check.Throws(()=>new WasmPlugins([Plugin(prefix+"(memory 1) (func (export \"bgb_init\") (result i32) i32.const 100 memory.grow))")]),"registration");
        Check.Throws(()=>new WasmPlugins([Plugin(prefix+"(memory 1) (func (export \"bgb_init\") (result i32) i32.const 1000000 i32.load))")]),"memory");
        Check.Throws(()=>new WasmPlugins(Enumerable.Repeat(Plugin(),33)),"32");
        Check.Throws(()=>new WasmPlugins([Plugin(prefix+"(func (export \"bgb_init\") (result i32) unreachable))")]),"unreachable");
        Check.Throws(()=>new WasmPlugins([Plugin(prefix+"(func (export \"bgb_init\") (result i32) i32.const 1))")]),"registration");
        var normal=TestModules.Load("samples/wasm-counter");
        foreach(var body in new[] {"(loop $forever br $forever) i32.const 0","unreachable","i32.const 1000"})
        {
            var bad=Plugin(prefix+"(func (export \"bgb_init\") (result i32) i32.const 3) (func (export \"bgb_command\") (param i32 i32) (result i32) "+body+"))") with {Id="counter",Permissions=["component","command"]};
            var module=normal with {RuntimePlugins=[bad]};module=TestModules.Rebuild(module);
            using var session=new PlaySession(module);var before=session.AuthorityStateHash();
            Check.That(!session.SubmitHuman("p1",new() {Kind="add"}).Accepted);
            Check.That(before==session.AuthorityStateHash(),"WASM failure partially committed");
            Check.That(session.ExportAuthorityLog().Entries.Single().Effects.Count==0);
        }
        using var recovered=new PlaySession(normal);Check.That(recovered.SubmitHuman("p1",new() {Kind="add"}).Accepted);
    }
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
        var module=TestModules.Load("samples/quick-cards") with {RuntimePlugins=[p]};
        module=TestModules.Rebuild(module);
        using var player=new PlaySession(module,1);Check.That(player.Actions().Count>0);
        using var editor=new DebugSession(module,1,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"human"),"p1");
        Check.That(editor.Handle(new("frame")).Frame.Actions.Count>0);
        Check.Throws(()=>player.ReloadScripts(TestModules.Rebuild(module with {RuntimePlugins=[]})),"restart");
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
