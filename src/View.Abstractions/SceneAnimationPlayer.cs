using BoardGame.Kernel;
using BoardGame.Presentation;

namespace BoardGame.View;

// Presentation only: consumes projected scenes, never an authority/session or random generator.
public sealed class SceneAnimationPlayer
{
    private readonly Dictionary<string,int> observed=[];
    private readonly List<(AnimationClip Clip,TimelinePlayback Playback)> active=[];
    private readonly Dictionary<AnimationTrack,TimelineCurve> curves=[];
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
        if(starts.Count>0) {active.Clear();active.AddRange(starts.Select(c=>(c,new TimelinePlayback(milliseconds,c.Tracks.Max(t=>t.Keys[^1].TimeMs)))));}
        if(next.Resources.Animations==null)Reset();
    }
    public void Reset() {active.Clear();observed.Clear();curves.Clear();}
    public TableScene Frame(double milliseconds)
    {
        var items=scene.Items.ToArray();
        foreach(var entry in active.ToArray())
        {
            var elapsed=entry.Playback.Elapsed(milliseconds);
            if(entry.Playback.Complete(milliseconds)) {active.Remove(entry);continue;}
            foreach(var track in entry.Clip.Tracks)
            {
                var index=Array.FindIndex(items,i=>i.Entity==track.Entity);
                if(index<0 || elapsed>=track.Keys[^1].TimeMs)continue; // Hidden objects never become drawable.
                var item=scene.Items.First(i=>i.Entity==track.Entity);
                if(!curves.TryGetValue(track,out var curve))curves[track]=curve=new(track.Keys.Select(k=>new TimelineKey(k.TimeMs,k.X,k.Y,k.Rotation)));
                var sample=curve.Sample(elapsed);var x=sample.X;var y=sample.Y;
                if(track.Source is {} source && scene.Items.FirstOrDefault(i=>i.Entity==source) is {} origin)
                {
                    x+=(origin.Bounds.X-item.Bounds.X)*sample.Remaining;y+=(origin.Bounds.Y-item.Bounds.Y)*sample.Remaining;
                }
                items[index]=item with {Bounds=item.Bounds with {X=item.Bounds.X+x,Y=item.Bounds.Y+y},Rotation=sample.Rotation,
                    Resource=track.BackResource ?? item.Resource,Label=track.BackResource==null?item.Label:"",Layer=10000+item.Layer};
            }
        }
        return scene with {Items=items.OrderBy(i=>i.Layer).ToArray()};
    }
}
