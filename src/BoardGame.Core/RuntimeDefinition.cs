using System.Text.Json;
using BoardGame.Kernel;
namespace BoardGame.Runtime;

internal sealed record LuaScript(string Source,string Path);

internal static class RuntimeDefinition
{
    public static GameDefinition From(LoadedModule module)=>module.Definition ?? new(Wire.Copy(module.Initial),
        Wire.Copy(module.ResourceHashes ?? new(StringComparer.Ordinal)),Wire.Copy(module.ResourceData ?? new(StringComparer.Ordinal)),
        new(StringComparer.Ordinal)
        {
            ["lua.rules"]=JsonSerializer.SerializeToElement(new LuaScript(module.Rules,module.RulesPath),Wire.Options),
            ["lua.cpu"]=JsonSerializer.SerializeToElement(new LuaScript(module.Cpu,module.CpuPath),Wire.Options),
            ["wasm"]=JsonSerializer.SerializeToElement(module.RuntimePlugins ?? [],Wire.Options)
        },module.Resources);
    public static T Configuration<T>(GameDefinition definition,string capability)=>
        definition.CapabilityData.TryGetValue(capability,out var configuration)
            ? configuration.Deserialize<T>(Wire.Options) ?? throw new InvalidDataException(capability+": missing configuration")
            : throw new InvalidDataException(capability+": missing configuration");
}

internal sealed class CommandRuleProvider : IRuleProvider, IGameRules
{
    private readonly List<ActionOffer> offers;
    public CommandRuleProvider(JsonElement configuration)
    {
        offers=configuration.TryGetProperty("actions",out var actions)?actions.Deserialize<List<ActionOffer>>(Wire.Options) ?? throw new InvalidDataException("rules.commands: actions must be an array"):[];
        if(offers.Count>256 || offers.Any(a=>a==null || string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Label) || a.Command==null || string.IsNullOrWhiteSpace(a.Command.Kind)) || offers.Select(a=>a.Id).Distinct().Count()!=offers.Count)
            throw new InvalidDataException("rules.commands: invalid action schema or limit exceeded");
    }
    public IGameRules Rules=>this;
    public IGameRules Prepare(GameDefinition definition)=>this;
    public void Apply(IGameRules rules) { }
    public IReadOnlyList<ActionOffer> Actions(SeatView view)=>Wire.Copy(offers);
    public RuleDecision Decide(SeatView view,Command command)=>offers.Any(a=>Wire.Json(a.Command)==Wire.Json(command))
        ?new(true,"",[command]):new(false,"Command is not offered",[]);
    public static RuntimePluginDefinition Plugin=>new("rules.commands","1",1,["rules"],[],s=>s.Provide("rules",new CommandRuleProvider(s.Configuration)))
        {Content=new(["actions"],(config,startup)=>_=new CommandRuleProvider(config))};
}


public static class CommandRulesPlugin
{
    public static RuntimePluginDefinition Create()=>CommandRuleProvider.Plugin;
}
