using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Runtime;

internal static class PreparationIsolationTests
{
    private sealed class Loader(Action load):IContentLoader
    {
        public string PayloadFormat=>"isolation";
        public int PayloadVersion=>1;
        public JsonElement Load(IContentSource source){load();using var json=JsonDocument.Parse(source.Read("state.json"));return json.RootElement.Clone();}
    }
    public static async Task Run()
    {
        using var release=new ManualResetEventSlim();var block=false;
        var definition=new RuntimePluginDefinition("definition.test","1",1,["definition"],[],scope=>scope.Provide("definition",new Loader(()=>{if(block)release.Wait();})))
            {Content=new([],Prepare:true,Definition:_=>new("state.json",new Dictionary<string,string>()))};
        var catalog=new ApplicationCatalog([new(_=>definition)]);
        var content=GameRuntime.LoadFiles(new Dictionary<string,byte[]>{
            ["game.start.json"]=Encoding.UTF8.GetBytes("""{"format":1,"id":"test","version":"1","plugins":[{"id":"definition.test","version":"1","api":1,"configuration":{}}]}"""),
            ["state.json"]=Encoding.UTF8.GetBytes("{}")},catalog.Content);
        block=true;
        var unblock=Task.Run(async()=>{await Task.Delay(1000);release.Set();});
        var timer=Stopwatch.StartNew();var starting=catalog.StartAsync(content);var returned=timer.Elapsed;
        await using var instance=await starting;await unblock;
        if(returned>TimeSpan.FromMilliseconds(500))throw new Exception("StartAsync synchronously blocked its caller during content preparation: "+returned);
        Console.WriteLine("PASS content preparation never synchronously blocks the application caller");
    }
}
