using BoardGame.Modules;

namespace BoardGame.Runtime;

public static class FlowValidation
{
    public static IReadOnlyList<FlowDiagnostic> Validate(FlowDefinition graph,IReadOnlyDictionary<string,string>? scripts=null)
    {
        var diagnostics=new List<FlowDiagnostic>();
        void Error(string code,string message,string? node=null,string? edge=null)=>diagnostics.Add(new(code,message,node,edge));
        if(graph.Format!=1 || graph.Nodes==null || graph.Edges==null || graph.Nodes.Count>FlowFile.MaxNodes || graph.Edges.Count>FlowFile.MaxEdges)
            return [new("flow-format","Unsupported format or graph size")];
        var nodes=new Dictionary<string,FlowNode>(StringComparer.Ordinal);
        foreach(var n in graph.Nodes)
        {
            if(n==null || string.IsNullOrWhiteSpace(n.Id) || !nodes.TryAdd(n.Id,n)) {Error("node-id","Missing or duplicate node ID",n?.Id);continue;}
            if(n.Id.Length>128 || n.Results?.Any(r=>r?.Length>128)==true) Error("node-id","Node/result IDs cannot exceed 128 characters",n.Id);
            if(!FlowNodeKinds.IsValid(n.Kind)) Error("node-kind","Unknown node type",n.Id);
            if(n.Results==null || n.Results.Count==0 || n.Results.Any(string.IsNullOrWhiteSpace) || n.Results.Distinct().Count()!=n.Results.Count) Error("node-results","Nonempty unique results required",n.Id);
            if(n.Commands==null || n.Commands.Count>64 || n.Commands.Any(c=>c==null || string.IsNullOrWhiteSpace(c.Kind))) Error("node-commands","Invalid commands",n.Id);
            if(n.Parameters==null || n.Actions==null) {Error("node-config","Parameters and actions cannot be null",n.Id);continue;}
            if(n.Kind==FlowNodeKinds.Player)
            {
                var seat=n.Seat;
                if(seat==null || (seat.Fixed!=null?1:0)+(seat.Current?1:0)+(seat.Script!=null?1:0)!=1 || seat.Fixed!=null && string.IsNullOrWhiteSpace(seat.Fixed)) Error("player-seat","Select exactly one fixed/current/script seat",n.Id);
                if((n.ActionsScript==null)!=(n.DecideScript==null) || n.ActionsScript!=null && n.Actions.Count>0) Error("player-actions","Choose static actions or paired actions/decide scripts",n.Id);
                if(n.Actions.Count>256 || n.Actions.Any(a=>a==null || string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Label) || a.Commands==null || a.Commands.Count>64 || a.Commands.Any(c=>c==null) || !(n.Results?.Contains(a.Result)??false)) || n.Actions.Select(a=>a?.Id).Distinct().Count()!=n.Actions.Count)
                    Error("player-actions","Invalid actions/results",n.Id);
            }
            else if(n.Seat!=null || n.Actions.Count>0 || n.ActionsScript!=null || n.DecideScript!=null) Error("node-config","Player configuration on non-player node",n.Id);
            if(n.Kind==FlowNodeKinds.End && n.Winners==null && n.Script==null) Error("end-result","End requires winners (empty for draw) or script",n.Id);
            if(n.Kind!=FlowNodeKinds.End && n.Winners!=null) Error("end-result","Winners only belong to end nodes",n.Id);
        }
        var starts=nodes.Values.Where(n=>n.Kind==FlowNodeKinds.Start).ToArray();
        if(starts.Length!=1) Error("start-count","Exactly one start node required");
        if(!nodes.Values.Any(n=>n.Kind==FlowNodeKinds.End)) Error("end-count","At least one end node required");
        var ids=new HashSet<string>();var usable=new List<FlowEdge>();
        foreach(var e in graph.Edges)
        {
            if(e==null || string.IsNullOrWhiteSpace(e.Id) || !ids.Add(e.Id)) {Error("edge-id","Missing or duplicate edge ID",edge:e?.Id);continue;}
            if(e.Id.Length>128) Error("edge-id","Edge IDs cannot exceed 128 characters",edge:e.Id);
            if(e.From==null || e.To==null || !nodes.TryGetValue(e.From,out var from) || !nodes.TryGetValue(e.To,out var to)) {Error("edge-endpoint","Unknown node endpoint",edge:e.Id);continue;}
            if(from.Kind==FlowNodeKinds.End || to.Kind==FlowNodeKinds.Start) Error("edge-direction","Start has no incoming edges; end has no outgoing edges",edge:e.Id);
            if(!(from.Results?.Contains(e.Result)??false)) Error("edge-result","Unknown source result",from.Id,e.Id);
            if(e.Default && e.Condition!=null) Error("edge-default","Default cannot have a condition",from.Id,e.Id);
            try
            {
                ValidateCondition(e.Condition,0);
                var constant=FlowConditions.Constant(e.Condition,scripts);
                if(constant!=false) usable.Add(e);
                if(constant==false) diagnostics.Add(new("false-exit","Constant-false exit does not provide a terminal path",e.From,e.Id,"warning"));
                if(constant==null) diagnostics.Add(new("conditional-exit","State/Lua condition: termination remains the author's responsibility",e.From,e.Id,"info"));
            }
            catch(Exception ex) when(ex is InvalidDataException or OverflowException or FormatException) {Error("edge-condition",ex.Message,from.Id,e.Id);}
        }
        foreach(var group in graph.Edges.Where(e=>e!=null && e.Default).GroupBy(e=>(e.From,e.Result)))
            if(group.Count()>1) foreach(var e in group) Error("edge-default","More than one default for result",e.From,e.Id);
        // A known-true branch also makes defaults and lower-priority exits impossible.
        foreach(var group in usable.GroupBy(e=>(e.From,e.Result)).ToArray())
        {
            var certain=group.Where(e=>!e.Default && FlowConditions.Constant(e.Condition,scripts)==true).ToArray();
            if(certain.Length>0) usable.RemoveAll(e=>e.From==group.Key.From && e.Result==group.Key.Result &&
                (e.Default || e.Priority<certain.Max(c=>c.Priority)));
        }
        if(diagnostics.Any(d=>d.Code is "node-config" or "node-id")) return diagnostics;
        foreach(var node in nodes.Values.Where(n=>n.Kind!=FlowNodeKinds.End))
            foreach(var result in node.Results ?? [])
                if(!usable.Any(e=>e.From==node.Id && e.Result==result))
                {
                    // Waiting outcomes do not leave the player node. Lua completion is only known at runtime.
                    if(node.Kind==FlowNodeKinds.Player && node.DecideScript!=null)
                        diagnostics.Add(new("result-dynamic","Result has no potential successor; Lua must only use it with complete=false: "+result,node.Id,Severity:"info"));
                    else if(node.Kind!=FlowNodeKinds.Player || node.Actions.Any(a=>a is {Complete:true} && a.Result==result))
                        Error("result-no-successor","Completing result has no potential successor: "+result,node.Id);
                }
        var scriptOwners=graph.Nodes.SelectMany(n=>FlowFile.Scripts(new(1,[n],[])).Select(s=>(Script:s,Node:(string?)n.Id,Edge:(string?)null)))
            .Concat(graph.Edges.Where(e=>e!=null).SelectMany(e=>FlowFile.Scripts(new(1,[],[e])).Select(s=>(Script:s,Node:(string?)e.From,Edge:(string?)e.Id))));
        foreach(var owner in scriptOwners)
        {
            var script=owner.Script;
            try {ModulePackage.ValidatePath(script.Path);if(string.IsNullOrWhiteSpace(script.Entry)) throw new InvalidDataException("Missing script entry");}
            catch(Exception e) when(e is InvalidDataException or ArgumentException) {Error("script-reference",e.Message,owner.Node,owner.Edge);continue;}
            if(scripts!=null && !scripts.ContainsKey(script.Path)) Error("script-missing","Missing script: "+script.Path,owner.Node,owner.Edge);
        }
        // Reverse reachability from all terminals rejects closed SCCs and every feeder into them.
        var terminal=Reach(nodes.Values.Where(n=>n.Kind==FlowNodeKinds.End).Select(n=>n.Id),usable.ToLookup(e=>e.To,e=>e.From));
        foreach(var n in nodes.Values.Where(n=>n.Kind!=FlowNodeKinds.End && !terminal.Contains(n.Id)))
            Error("no-terminal-path","No potential path to an end after excluding constant-false exits",n.Id);
        if(starts.Length==1)
        {
            var reachable=Reach([starts[0].Id],usable.ToLookup(e=>e.From,e=>e.To));
            foreach(var n in nodes.Values.Where(n=>!reachable.Contains(n.Id))) diagnostics.Add(new("unreachable","Unreachable from start",n.Id,Severity:"warning"));
        }
        return diagnostics;
    }
    public static void RequireValid(FlowDefinition graph,IReadOnlyDictionary<string,string>? scripts=null)
    {
        var errors=Validate(graph,scripts).Where(d=>d.Severity=="error").ToArray();
        if(errors.Length>0) throw new InvalidDataException(string.Join("\n",errors.Select(d=>$"flow:{d.Node ?? d.Edge ?? "graph"}:{d.Code}: {d.Message}")));
    }
    private static HashSet<string> Reach(IEnumerable<string> roots,ILookup<string,string> adjacent)
    {
        var found=new HashSet<string>();var pending=new Queue<string>(roots);
        while(pending.TryDequeue(out var id)) if(found.Add(id)) foreach(var next in adjacent[id]) pending.Enqueue(next);
        return found;
    }
    private static void ValidateCondition(FlowCondition? c,int depth)
    {
        if(c==null)return;
        if(depth>24) throw new InvalidDataException("Condition depth limit exceeded");
        if(c.Op is "true" or "false") {if(c.Left!=null || c.Right!=null || c.Items!=null || c.Script!=null) throw new InvalidDataException("Literal condition has extra operands");return;}
        if(c.Op=="lua") {if(c.Script==null || c.Left!=null || c.Right!=null || c.Items!=null) throw new InvalidDataException("Lua condition requires only script");return;}
        if(c.Op is "all" or "any" or "not")
        {
            if(c.Items==null || c.Items.Count==0 || c.Items.Any(i=>i==null) || c.Op=="not" && c.Items.Count!=1 || c.Left!=null || c.Right!=null || c.Script!=null) throw new InvalidDataException("Invalid logical operands");
            foreach(var child in c.Items) ValidateCondition(child,depth+1);return;
        }
        if(c.Op is not ("eq" or "ne" or "lt" or "le" or "gt" or "ge") || c.Items!=null || c.Script!=null) throw new InvalidDataException("Unknown comparison or extra operands");
        foreach(var operand in new[]{c.Left,c.Right})
            if(operand==null || (operand.Path==null)==(operand.Literal==null) || operand.Path!=null && !operand.Path.StartsWith('/')) throw new InvalidDataException("Operand requires literal or JSON pointer path");
    }
}
