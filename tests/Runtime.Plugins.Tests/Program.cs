using BoardGame.Plugins;
using BoardGame.Runtime;

var checks=0;
void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
void Reject(Action action,string message)
{try{action();}catch(InvalidDataException){checks++;return;}throw new Exception(message);}
PluginManifest Manifest(string id)=>new(1,"runtime",id,"1",1,new("Plugin.dll","Example.Factory","Create"),["win-x64"],
    new(true,["win-x64"],"Declaration only; requires native verification"),[new(id,1)],[],new(),[new("Plugin.dll",new('a',64),"implementation")]);
var first=Manifest("producer");var consumer=Manifest("consumer") with {Requires=[new("producer",1)]};
Check(PluginManifest.Read(first.Write()).Id==first.Id,"Manifest round trip");
Reject(()=>PluginManifest.Read(first.Write().Replace("\"format\": 1","\"format\": 1, \"format\": 1")),"Duplicate fields accepted");
Reject(()=>PluginManifest.Read(first.Write().Replace("\"format\": 1","\"unexpected\": 1, \"format\": 1")),"Unknown fields accepted");
Reject(()=>PluginManifest.Read(first.Write().Replace("\"format\": 1,","")),"Missing required field accepted");
Reject(()=>(first with {Kind="editor"}).Validate(),"Editor package accepted");
Reject(()=>(first with {Entry=first.Entry with {Type="Example.Factory; Evil()"}}).Validate(),"Source injection entry accepted");
Reject(()=>(first with {Files=[new("Plugin.dll",new('a',64),"reference")]}).Validate(),"Reference-only entry accepted");
Reject(()=>(first with {Files=[new("../Plugin.dll",new('a',64),"implementation")]}).Validate(),"Escaping path accepted");
Reject(()=>(first with {Files=[first.Files[0],first.Files[0] with {Path="plugin.dll"}]}).Validate(),"Windows duplicate path accepted");
var supported=PluginCompatibility.Check([consumer,first],"win-x64",true,HostMode.Headless);
Check(supported.Success && supported.Ordered[0].Id=="producer","Capability dependency order");
Check(!PluginCompatibility.Check([first with {Aot=null}],"win-x64",true,HostMode.Headless).Success,"Undeclared AOT accepted");
Check(PluginCompatibility.Check([first with {Aot=null}],"win-x64",false,HostMode.Headless).Success,"Ordinary requires AOT declaration");
Check(!PluginCompatibility.Check([first],"linux-x64",false,HostMode.Headless).Success,"Wrong platform accepted");
var unsupported=first with {Aot=new(false,[],"Unresolved NativeAOT warnings")};
var transitive=PluginCompatibility.Check([consumer,unsupported],"win-x64",true,HostMode.Headless);
Check(transitive.Diagnostics.Any(d=>d.Chain.SequenceEqual(new[]{"consumer","producer"})),"Missing incompatible dependency chain");
Check(!PluginCompatibility.Check([consumer,first with {Provides=[new("producer",2)]}],"win-x64",false,HostMode.Headless).Success,"Capability version mismatch accepted");
Check(PluginCompatibility.Check([consumer,first with {Id="replacement"}],"win-x64",false,HostMode.Headless).Success,"Equivalent capability tied to plugin ID");
Check(!PluginCompatibility.Check([consumer],"win-x64",false,HostMode.Headless).Success,"Missing provider accepted");
Check(!PluginCompatibility.Check([first,first with {Version="2"}],"win-x64",false,HostMode.Headless).Success,"Two versions accepted");
Check(!PluginCompatibility.Check([consumer,first with {Requires=[new("consumer",1)]}],"win-x64",false,HostMode.Headless).Success,"Cycle accepted");
Check(!PluginCompatibility.Check([consumer with {Execution=new(HostMode.Graphical,RuntimeScope.GraphicsHost)},first],"win-x64",false,HostMode.Graphical).Success,"Shorter-lived dependency accepted");
var update=first with {Execution=new(HostMode.All,RuntimeScope.Instance,[new("tick",ApplicationPhase.Update,Before:["consumer/tick"])])};
var later=consumer with {Execution=new(HostMode.All,RuntimeScope.Instance,[new("tick",ApplicationPhase.Update)])};
Check(PluginCompatibility.Check([later,update],"win-x64",true,HostMode.Headless).Success,"Cross-plugin phase order rejected");
Check(!PluginCompatibility.Check([later with {Execution=new(HostMode.All,RuntimeScope.Instance,[new("tick",ApplicationPhase.Update,Before:["producer/tick"])])},update],"win-x64",true,HostMode.Headless).Success,"Phase cycle accepted");
Check(!PluginCompatibility.Check([first with {Provides=[new("render.backend",1)]},consumer with {Provides=[new("render.backend",1)],Requires=[]}],"win-x64",false,HostMode.Graphical).Success,"Two render providers accepted");
Console.WriteLine($"PASS {checks} plugin manifest / compatibility checks");
