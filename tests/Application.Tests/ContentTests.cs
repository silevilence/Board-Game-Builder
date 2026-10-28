using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;
using ContentRuntime=BoardGame.AppRuntime.GameRuntime;
using ContentPackage=BoardGame.AppRuntime.GamePackage;

internal static class ContentTests
{
    private sealed class MotionLoader(string path) : IContentLoader
    {
        public string PayloadFormat=>"test.motion";
        public int PayloadVersion=>1;
        public JsonElement Load(IContentSource source)
        {
            using var json=JsonDocument.Parse(source.Read(path));
            var root=json.RootElement;
            if(root.GetProperty("format").GetInt32()!=1 || root.GetProperty("speed").GetDouble()<=0) throw new InvalidDataException("Invalid motion definition");
            _=source.Read(root.GetProperty("resource").GetString()!);
            return root.Clone();
        }
    }
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    private static void Check(bool value,string reason) {if(!value) throw new Exception(reason);}
    private static void Reject(Action operation,string reason)
    {
        try {operation();} catch(Exception error) when(error.ToString().Contains(reason,StringComparison.OrdinalIgnoreCase)) {return;}
        throw new Exception("Expected rejection: "+reason);
    }
    internal static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"application-content-"+Guid.NewGuid());
        var directory=Path.Combine(root,"source");Directory.CreateDirectory(directory);
        try
        {
            var activations=0;
            var loader=new RuntimePluginDefinition("motion.content","1",1,["definition"],[],s=>s.Provide("definition",new MotionLoader(s.Configuration.GetProperty("source").GetString()!)))
                {Content=new(["source","presets"],Prepare:true,Definition:c=>new(c.GetProperty("source").GetString()!,
                    c.GetProperty("presets").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!)),
                    SelectSource:(c,path)=>{var node=JsonNode.Parse(c.GetRawText())!;node["source"]=path;return JsonSerializer.SerializeToElement(node);})};
            var runtime=new RuntimePluginDefinition("motion.update","1",1,[],[],_=>activations++);
            var catalog=new ContentCatalog([loader,runtime]);
            var startup=new RuntimeStartup(1,"motion","1",[
                new(runtime.Id,"1",1,JsonSerializer.SerializeToElement(new{})),
                new(loader.Id,"1",1,JsonSerializer.SerializeToElement(new{source="fast.json",presets=new{fast="fast.json",slow="slow.json"}}))]);
            File.WriteAllText(Path.Combine(directory,ContentRuntime.Entry),JsonSerializer.Serialize(startup,Json));
            File.WriteAllText(Path.Combine(directory,"fast.json"),"""{"format":1,"speed":5,"resource":"fast.txt"}""");
            File.WriteAllText(Path.Combine(directory,"slow.json"),"""{"format":1,"speed":1,"resource":"slow.txt"}""");
            File.WriteAllText(Path.Combine(directory,"fast.txt"),"fast resource");File.WriteAllText(Path.Combine(directory,"slow.txt"),"slow resource");
            var loaded=ContentRuntime.Load(directory,catalog);
            Check(loaded.Identity.Files.Count==4 && loaded.Payload.Data.GetProperty("speed").GetDouble()==5,"Definition or preset dependencies lost");
            Check(activations==0,"Content preparation activated gameplay");
            Check(ContentRuntime.Load(Path.Combine(directory,ContentRuntime.Entry),catalog).Fingerprint==loaded.Fingerprint,"Entry differs");
            var slow=ContentRuntime.Load(directory,catalog,"slow");Check(slow.Fingerprint!=loaded.Fingerprint && slow.Payload.Data.GetProperty("speed").GetDouble()==1,"Preset did not select content");
            Reject(()=>ContentRuntime.Load(directory,catalog,"absent"),"Unknown definition preset");
            var package=Path.Combine(root,"motion.bgm");ContentPackage.Pack(directory,package,catalog);
            Check(ContentRuntime.Load(package,catalog).Fingerprint==loaded.Fingerprint,"Package identity differs");
            Check(ContentRuntime.Load(package,catalog,"slow").Fingerprint==slow.Fingerprint,"Packaged preset differs");
            var snapshot=Path.Combine(root,"motion.snapshot.json");loaded.SaveSnapshot(snapshot);
            Directory.Delete(directory,true);
            Check(ContentRuntime.Load(snapshot,catalog).Fingerprint==loaded.Fingerprint,"Snapshot requires original directory");
            Check(ContentRuntime.Load(package,catalog).Fingerprint==loaded.Fingerprint,"Package requires original directory");
            var changed=loaded.ExportSnapshot();changed.Files["slow.txt"][0]^=1;
            Reject(()=>ContentRuntime.Restore(changed,catalog),"identity mismatch");
            Reject(()=>ContentRuntime.Restore(loaded.ExportSnapshot() with {Payload=loaded.Payload with {Version=2}},catalog),"payload format/version");
            Reject(()=>ContentRuntime.Restore(loaded.ExportSnapshot() with {Payload=loaded.Payload with {Data=JsonSerializer.SerializeToElement(new{speed=900})}},catalog),"identity mismatch");
            Reject(()=>ContentRuntime.Restore(loaded.ExportSnapshot() with {Format=9},catalog),"version");
            changed=loaded.ExportSnapshot();changed.Files.Remove("slow.txt");Reject(()=>ContentRuntime.Restore(changed,catalog),"missing");
            changed=loaded.ExportSnapshot();changed.Files["escape/../x"]=[];Reject(()=>ContentRuntime.Restore(changed,catalog),"unsafe");
            changed=loaded.ExportSnapshot();changed.Files["undeclared.txt"]=[];Reject(()=>ContentRuntime.Restore(changed,catalog),"undeclared");
            changed=loaded.ExportSnapshot();changed.Files["FAST.txt"]=[];Reject(()=>ContentRuntime.Restore(changed,catalog),"Duplicate");
            Reject(()=>ContentRuntime.Restore(loaded.ExportSnapshot(),new([runtime])),"unapproved");
            // Serialized payload is initial content, not mandatory per-tick state serialization.
            var position=0.0;var speed=loaded.Payload.Data.GetProperty("speed").GetDouble();
            var update=new RuntimePluginDefinition("move","1",1,[],[],s=>s.Update("tick",(frame,_)=>ValueTask.FromResult<Action?>(()=>position+=speed*frame.Delta.TotalSeconds)))
                {Execution=new(Contributions:[new("tick",ApplicationPhase.Update)])};
            using(var instance=RuntimeHost.StartAsync(new(1,"motion","1",[new("move","1",1,JsonSerializer.SerializeToElement(new{}))]),[update],[]).GetAwaiter().GetResult())
            {instance.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult();Check(position==0.5,"Non-board state did not advance");}
            using(var zip=ZipFile.Open(package,ZipArchiveMode.Update))
            {var entry=zip.GetEntry("fast.txt")!;entry.Delete();using var writer=new StreamWriter(zip.CreateEntry("fast.txt").Open());writer.Write("tampered");}
            Reject(()=>ContentRuntime.Load(package,catalog),"hash mismatch");
            Check(activations==0,"Restore or package verification activated gameplay");
            Console.WriteLine("PASS non-board directory, entry, package, presets, snapshot and payload integrity");
        }
        finally {Directory.Delete(root,true);}
    }
}
