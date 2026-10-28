using BoardGame.Kernel;

namespace BoardGame.View;

// Explicit module-authored regions consume only the observer's offered actions and visible scene.
public sealed record BoardActionRegion(string Kind,string Label,float X,float Y,float Width,float Height,
    string? Anchor=null,int? Value=null,string? Property=null,string? Target=null,bool Direct=false,bool Outline=false,bool AutoOpen=false);
public sealed record BoardActionMap(float Width,float Height,BoardActionRegion[] Regions,TableRect? EndRegion=null);
public sealed class BoardActions(BoardActionMap map)
{
    private sealed record Group(string Key,BoardActionRegion Region,TableRect Bounds,IReadOnlyList<ActionOffer> Offers);
    private readonly List<Group> groups=[];
    private string? opened;
    private int page;
    public void Update(TableScene scene,IReadOnlyList<ActionOffer> offers)
    {
        groups.Clear();opened=null;page=0;
        var mapped=new HashSet<string>(StringComparer.Ordinal);
        for(var i=0;i<map.Regions.Length;i++)
        {
            var rule=map.Regions[i];
            var matches=offers.Where(o=>!mapped.Contains(o.Id) && o.Command.Kind==rule.Kind && (rule.Value==null || o.Command.Value==rule.Value)
                && (rule.Property==null || o.Command.Property==rule.Property) && (rule.Target==null || o.Command.Target==rule.Target));
            foreach(var batch in matches.GroupBy(o=>rule.Anchor?.Replace("{entity}",o.Command.Entity ?? "").Replace("{value}",o.Command.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? ""))
            {
                var anchor=rule.Anchor==null?null:scene.Items.FirstOrDefault(item=>item.Entity==batch.Key);
                if(rule.Anchor!=null && anchor==null)continue;
                var bounds=new TableRect((anchor?.Bounds.X ?? 0)+rule.X,(anchor?.Bounds.Y ?? 0)+rule.Y,
                    rule.Width==0?anchor!.Bounds.Width:rule.Width,rule.Height==0?anchor!.Bounds.Height:rule.Height);
                var items=batch.ToArray();groups.Add(new(i+":"+batch.Key,rule,bounds,items));
                foreach(var item in items)mapped.Add(item.Id);
            }
        }
        opened=groups.FirstOrDefault(g=>g.Region.AutoOpen)?.Key;
    }
    public IReadOnlyList<string> MappedActions=>groups.SelectMany(g=>g.Offers).Select(a=>a.Id).ToArray();
    public string? Activate(string token)
    {
        if(token=="close"){opened=null;page=0;return null;}
        if(token=="previous"){page=Math.Max(0,page-1);return null;}
        if(token=="next")
        {var group=groups.FirstOrDefault(g=>g.Key==opened);if(group!=null)page=Math.Min((group.Offers.Count-1)/6,page+1);return null;}
        if(token.StartsWith("menu:",StringComparison.Ordinal))
        {var key=token[5..];if(groups.Any(g=>g.Key==key)){opened=key;page=0;}return null;}
        if(token.StartsWith("offer:",StringComparison.Ordinal))
        {var id=token[6..];if(groups.Any(g=>g.Offers.Any(a=>a.Id==id)))return id;}
        return null;
    }
    public TableScene Present(TableScene scene,bool ended,string? endLabel)
    {
        var items=scene.Items.ToList();
        foreach(var group in groups)
        {
            var direct=group.Region.Direct && group.Offers.Count==1;
            items.Add(new(null,group.Region.Outline?"ui-hotspot":"ui-button",group.Region.Label,group.Bounds,
                Action:direct?"offer:"+group.Offers[0].Id:"menu:"+group.Key,ActionIds:group.Offers.Select(o=>o.Id).ToArray()));
        }
        if(ended && endLabel!=null && map.EndRegion is {} end)items.Add(new(null,"ui-button",endLabel,end,Action:"host:new-game"));
        var active=groups.FirstOrDefault(g=>g.Key==opened);
        if(active!=null)
        {
            const float w=440;var h=102+Math.Min(6,active.Offers.Count)*58;
            var x=Math.Clamp(active.Bounds.X+active.Bounds.Width/2-w/2,16,map.Width-w-16);
            var y=Math.Clamp(active.Bounds.Y+active.Bounds.Height/2-h/2,16,map.Height-h-16);
            items.Add(new(null,"ui-shade","",new(0,0,map.Width,map.Height),Action:"close"));
            items.Add(new(null,"ui-panel",active.Region.Label.Length>0?active.Region.Label:"选择操作",new(x,y,w,h),Action:"noop"));
            items.Add(new(null,"ui-button","×",new(x+w-40,y+6,32,28),Action:"close"));
            var visible=active.Offers.Skip(page*6).Take(6).ToArray();
            for(var i=0;i<visible.Length;i++)
                items.Add(new(null,"ui-button",Wrap(visible[i].Label),new(x+14,y+45+i*58,w-28,52),Action:"offer:"+visible[i].Id,ActionIds:[visible[i].Id]));
            if(page>0)items.Add(new(null,"ui-button","上一页",new(x+14,y+h-44,100,30),Action:"previous"));
            if((page+1)*6<active.Offers.Count)items.Add(new(null,"ui-button","下一页",new(x+w-114,y+h-44,100,30),Action:"next"));
            items.Add(new(null,"ui-caption",$"{page+1} / {(active.Offers.Count+5)/6}",new(x+w/2-25,y+h-36,50,24)));
        }
        return scene with {Items=items.Select(i=>i.Action!=null || i.Kind=="ui-caption"?i with {Layer=20000}:i).ToArray()};
    }
    private static string Wrap(string text)=>string.Join('\n',Enumerable.Range(0,(text.Length+27)/28).Select(i=>text.Substring(i*28,Math.Min(28,text.Length-i*28))));
    public static void Validate(BoardActionMap map,LoadedModule module)
    {
        if(!float.IsFinite(map.Width) || !float.IsFinite(map.Height) || map.Width is <480 or >4096 || map.Height is <500 or >4096
            || map.Regions is not {Length:>0 and <=128})throw new InvalidDataException("player.presentation: invalid boardActions canvas or regions");
        foreach(var r in map.Regions)
        {
            if(r==null || string.IsNullOrWhiteSpace(r.Kind) || r.Label==null || r.Label.Length>120 || !float.IsFinite(r.X) || !float.IsFinite(r.Y)
                || !float.IsFinite(r.Width) || !float.IsFinite(r.Height) || Math.Abs(r.X)>4096 || Math.Abs(r.Y)>4096 || r.Width is <0 or >4096 || r.Height is <0 or >4096
                || r.Anchor==null && (r.Width==0 || r.Height==0))throw new InvalidDataException("player.presentation: invalid boardActions region");
            if(r.Anchor!=null && !r.Anchor.Contains('{') && !module.Initial.Entities.ContainsKey(r.Anchor))throw new InvalidDataException("player.presentation: unknown boardActions anchor "+r.Anchor);
        }
        if(map.EndRegion is {} e && (!float.IsFinite(e.X) || !float.IsFinite(e.Y) || !float.IsFinite(e.Width) || !float.IsFinite(e.Height)
            || e.X<0 || e.Y<0 || e.Width<=0 || e.Height<=0 || e.X+e.Width>map.Width || e.Y+e.Height>map.Height))throw new InvalidDataException("player.presentation: invalid boardActions endRegion");
    }
}
