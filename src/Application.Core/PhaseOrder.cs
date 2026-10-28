namespace BoardGame.Runtime;

public sealed record ScheduledContribution(string Plugin,RuntimeScope Scope,PhaseContribution Declaration)
{
    public string Key=>Plugin+"/"+Declaration.Id;
}

public static class PhaseOrder
{
    public static IReadOnlyList<ScheduledContribution> Sort(IEnumerable<ScheduledContribution> declarations)
    {
        var nodes=new Dictionary<string,ScheduledContribution>(StringComparer.Ordinal);
        foreach(var item in declarations)
        {
            if(string.IsNullOrWhiteSpace(item.Plugin) || string.IsNullOrWhiteSpace(item.Declaration.Id) ||
                item.Declaration.Id.Contains('/') || !Enum.IsDefined(item.Declaration.Phase) ||
                !Enum.IsDefined(item.Scope) || !nodes.TryAdd(item.Key,item))
                throw new InvalidDataException(item.Key+": invalid/duplicate phase contribution");
            if(item.Declaration.Phase==ApplicationPhase.Update && item.Scope!=RuntimeScope.Instance ||
                item.Declaration.Phase==ApplicationPhase.Render && item.Scope!=RuntimeScope.Viewport)
                throw new InvalidDataException(item.Key+": update requires instance scope; render requires viewport scope");
        }
        var predecessors=nodes.Keys.ToDictionary(k=>k,_=>new HashSet<string>(StringComparer.Ordinal),StringComparer.Ordinal);
        foreach(var node in nodes.Values)
        {
            foreach(var before in node.Declaration.Before ?? []) Edge(node.Key,before);
            foreach(var after in node.Declaration.After ?? []) Edge(after,node.Key);
        }
        List<ScheduledContribution> result=[];
        HashSet<string> visiting=[],done=[];
        foreach(var key in nodes.Keys.Order(StringComparer.Ordinal)) Visit(key);
        return result;
        void Edge(string from,string to)
        {
            if(!nodes.TryGetValue(from,out var a) || !nodes.TryGetValue(to,out var b))
                throw new InvalidDataException(from+" -> "+to+": missing phase contribution");
            if(a.Scope!=b.Scope || a.Declaration.Phase!=b.Declaration.Phase)
                throw new InvalidDataException(from+" -> "+to+": cross-phase/scope order");
            predecessors[to].Add(from);
        }
        void Visit(string key)
        {
            if(done.Contains(key)) return;
            if(!visiting.Add(key)) throw new InvalidDataException(key+": phase order cycle");
            foreach(var previous in predecessors[key].Order(StringComparer.Ordinal)) Visit(previous);
            visiting.Remove(key);done.Add(key);result.Add(nodes[key]);
        }
    }
}
