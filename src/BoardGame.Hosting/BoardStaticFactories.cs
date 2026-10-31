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
    public static ApprovedApplicationPlugin Session()=>new BoardApplication().SessionFactory();
}
