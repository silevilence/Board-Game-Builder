using System.Text.Json;
using BoardGame.AppRuntime;

namespace Example.RuntimePlugin;

// Sample-owned headless entry, used to verify the generated native host without desktop dependencies.
public static class HeadlessEntry
{
    // Intentionally unsupported alternate entry for proving that a publisher's declaration
    // cannot bypass the native compiler. The counter factory never calls this method.
    public static int ReflectionOnly(ApprovedApplicationPlugin[] plugins,string source,string title)=>
        System.Reflection.Assembly.LoadFrom(Path.Combine(source,"dynamic.dll")).GetTypes().Length;
    public static int Run(ApprovedApplicationPlugin[] plugins,string source,string title)
    {
        var catalog=new ApplicationCatalog(plugins);var content=catalog.Load(source);
        using var host=catalog.StartAsync(content).GetAwaiter().GetResult();
        for(var i=0;i<3;i++)host.Driver.AdvanceAsync(TimeSpan.FromSeconds(1.0/30)).GetAwaiter().GetResult();
        using var output=File.Create(Path.Combine(AppContext.BaseDirectory,"result.json"));
        using var json=new Utf8JsonWriter(output,new(){Indented=true});
        json.WriteStartObject();json.WriteString("title",title);json.WriteBoolean("dynamicCodeSupported",System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported);
        json.WriteNumber("ticks",host.Service<Counter>("sample.counter").Ticks);json.WriteString("fingerprint",content.Fingerprint);json.WriteEndObject();
        return 0;
    }
}
