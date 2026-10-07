namespace BoardGame.Kernel;

// CollectionSteps is an explicit, replayable logical deadline, never a client's clock.
public sealed record SecretDeclaration(string Id,List<string> Participants,int Minimum,int Maximum,int CollectionSteps,
    string Missing="abstain",int DefaultValue=0,bool AllowReplace=true,string Reveal="all-locked");
public sealed class SecretRound
{
    public SecretDeclaration Declaration {get;set;}=null!;
    public string Coordinator {get;set;}="";
    public string Status {get;set;}="collecting";
    public int Remaining {get;set;}
    public SortedDictionary<string,int?> Choices {get;set;}=new(StringComparer.Ordinal);
    public List<string> Locked {get;set;}=[];
}
public sealed record SecretView(SecretDeclaration Declaration,string Status,int Remaining,List<string> Submitted,List<string> Locked,
    int? OwnValue,SortedDictionary<string,int?>? Revealed);
