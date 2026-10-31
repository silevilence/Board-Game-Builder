using BoardGame.AppRuntime;
namespace BoardGame.Runtime;

/// <summary>Independent static entry points; no enumeration of the ordinary catalog.</summary>
public static class BoardStaticFactories
{
    public static ApprovedApplicationPlugin Definition()=>new BoardApplication().DefinitionFactory();
    public static ApprovedApplicationPlugin Resources()=>new BoardApplication().ResourceFactory();
    public static ApprovedApplicationPlugin Authority()=>new BoardApplication().AuthorityFactory();
    public static ApprovedApplicationPlugin Rules()=>new BoardApplication().RulesFactory();
    public static ApprovedApplicationPlugin Recording()=>new BoardApplication().RecordingFactory();
    public static ApprovedApplicationPlugin Session()=>new BoardApplication(cpu:false,effects:false).SessionFactory();
    public static ApprovedApplicationPlugin CpuSession()=>new BoardApplication(sessionId:"session.local.cpu",cpu:true,effects:false).SessionFactory();
    public static ApprovedApplicationPlugin EffectsSession()=>new BoardApplication(sessionId:"session.local.effects",cpu:false,effects:true).SessionFactory();
    public static ApprovedApplicationPlugin CpuEffectsSession()=>new BoardApplication(sessionId:"session.local.cpu-effects",cpu:true,effects:true).SessionFactory();
}
