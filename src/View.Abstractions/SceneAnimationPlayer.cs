using BoardGame.Kernel;

namespace BoardGame.View;

// Presentation only: consumes projected scenes, never an authority/session or random generator.
public sealed class SceneAnimationPlayer
{
    private readonly Dictionary<string,int> observed=[];
    private readonly List<(AnimationClip Clip,double Started)> active=[];
    private TableScene scene=new([],ResourcePool.Empty);
    private string libraryIdentity="";
    public bool Busy=>active.Count>0;
    public void Show(TableScene next,double milliseconds)
    {
        scene=next;
        var identity=next.Resources.Animations==null?"":Wire.Hash(Wire.Json(next.Resources.Animations));
        if(identity!=libraryIdentity) {Reset();libraryIdentity=identity;}
        var starts=new List<AnimationClip>();
        foreach(var clip in next.Resources.Animations?.Clips ?? [])
        {
            var value=next.AnimationCues?.GetValueOrDefault(clip.Id) ?? 0;
            if(observed.TryGetValue(clip.Id,out var previous) && value<previous)active.Clear();
            if(value>0 && observed.GetValueOrDefault(clip.Id)!=value)starts.Add(clip);
            observed[clip.Id]=value;
        }
        if(starts.Count>0) {active.Clear();active.AddRange(starts.Select(c=>(c,milliseconds)));}
        if(next.Resources.Animations==null)Reset();
    }
    public void Reset() {active.Clear();observed.Clear();}
    public TableScene Frame(double milliseconds)
    {
        var items=scene.Items.ToArray();
        foreach(var entry in active.ToArray())
        {
            var elapsed=Math.Max(0,milliseconds-entry.Started);
            if(elapsed>=entry.Clip.Tracks.Max(t=>t.Keys[^1].TimeMs)) {active.Remove(entry);continue;}
            foreach(var track in entry.Clip.Tracks)
            {
                var index=Array.FindIndex(items,i=>i.Entity==track.Entity);
                if(index<0 || elapsed>=track.Keys[^1].TimeMs)continue; // Hidden objects never become drawable.
                var item=scene.Items.First(i=>i.Entity==track.Entity);
                var first=track.Keys[0];var last=track.Keys[^1];
                var time=Math.Max(first.TimeMs,elapsed);
                var right=track.Keys.First(k=>k.TimeMs>=time);
                var left=track.Keys.Last(k=>k.TimeMs<=time);
                var ratio=right.TimeMs==left.TimeMs?0:(float)(time-left.TimeMs)/(right.TimeMs-left.TimeMs);
                var x=left.X+(right.X-left.X)*ratio;var y=left.Y+(right.Y-left.Y)*ratio;
                var rotation=left.Rotation+(right.Rotation-left.Rotation)*ratio;
                if(track.Source is {} source && scene.Items.FirstOrDefault(i=>i.Entity==source) is {} origin)
                {
                    var remaining=1-(float)(time-first.TimeMs)/(last.TimeMs-first.TimeMs);
                    x+=(origin.Bounds.X-item.Bounds.X)*remaining;y+=(origin.Bounds.Y-item.Bounds.Y)*remaining;
                }
                items[index]=item with {Bounds=item.Bounds with {X=item.Bounds.X+x,Y=item.Bounds.Y+y},Rotation=rotation,
                    Resource=track.BackResource ?? item.Resource,Label=track.BackResource==null?item.Label:"",Layer=10000+item.Layer};
            }
        }
        return scene with {Items=items.OrderBy(i=>i.Layer).ToArray()};
    }
}
