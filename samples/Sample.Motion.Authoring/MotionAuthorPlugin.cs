using System.Text.Json.Nodes;
using BoardGame.AppRuntime;
using BoardGame.Authoring;

namespace Sample.Motion;

public sealed class MotionAuthorPlugin : IAuthorPluginPackage
{
    private sealed record Format(string DefinitionPath) : IGameAuthorFormat
    {
        public JsonObject Read(IAuthorFiles files)=>JsonNode.Parse(files.Text(DefinitionPath))!.AsObject();
        public string Serialize(IAuthorFiles files,JsonObject configuration)=>configuration.ToJsonString(new(){WriteIndented=true});
    }
    public static GameAuthorFormatProvider AuthorFormat=>new("motion.definition","1",1,p=>new Format(p.Configuration.GetProperty("path").GetString()!));
    private static AuthorOperationInfo RunInfo=>new("run.application","author.motion","Advance the selected application using bounded fixed logical time",
        new(){["ticks"]=new("integer",false,"1..10000 fixed 1/60 second updates, default 60")},new(){["type"]="object"},[],[]);
    public static AuthorPlugin Definition=>new("author.motion","1",1,[],["author.formats","author.runtimes","author.operations"],scope=>
    {
        var catalog=new ApplicationCatalog(MotionPlugins.Catalog());
        scope.Own(scope.Service<GameAuthorFormats>("author.formats").Register(AuthorFormat));
        scope.Own(scope.Service<AuthorRuntimes>("author.runtimes").Register(new ApplicationAuthorRuntime("motion",catalog)));
        scope.Register(new(RunInfo,async call=>
        {
            var ticks=call.Integer("ticks",60);if(ticks is <1 or >10000)throw new AuthorOperationException("arguments.invalid","ticks must be 1..10000");
            var content=GameRuntime.LoadFiles(call.View.CopyFiles(),catalog.Content);
            await using var host=await catalog.StartAsync(content,clock:new(TimeSpan.FromSeconds(1d/60)));
            host.Driver.Paused=true;for(var i=0;i<ticks;i++)await host.Driver.StepAsync(call.Cancellation);
            return new{call.View.Source,ticks=host.Driver.CompletedTick,simulationMilliseconds=host.Driver.SimulationTime.TotalMilliseconds,state=host.Service<MotionState>("motion.state").Snapshot};
        }));
    },"Motion example author format and runtime capabilities")
    {
        Operations=[RunInfo],
        Documents=[new("motion","author.motion","实时移动示例","通过 configuration.read / configuration.replace 读写 motion.json；project.validate 使用相同获准运行目录；run.application 以固定逻辑时间推进。所有操作消费当前 ProjectView，GUI 模式不隐式保存。")]
    };
    public IReadOnlyList<AuthorPlugin> Plugins=>[Definition];
}
