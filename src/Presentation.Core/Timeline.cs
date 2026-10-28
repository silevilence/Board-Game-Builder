namespace BoardGame.Presentation;

public sealed record TimelineKey(double TimeMs,float X=0,float Y=0,float Rotation=0);
public readonly record struct TimelineSample(float X,float Y,float Rotation,float Remaining);
public sealed class TimelineCurve
{
    private readonly TimelineKey[] keys;
    public double Duration=>keys[^1].TimeMs;
    public TimelineCurve(IEnumerable<TimelineKey> values)
    {
        keys=values.ToArray();
        if(keys.Length<2)throw new ArgumentException("A curve requires at least two keys");
        double previous=-1;
        foreach(var key in keys)
        {
            if(key==null || !double.IsFinite(key.TimeMs) || key.TimeMs<0 || key.TimeMs<=previous || !float.IsFinite(key.X) || !float.IsFinite(key.Y) || !float.IsFinite(key.Rotation))
                throw new ArgumentException("Invalid timeline key");
            previous=key.TimeMs;
        }
    }
    public TimelineSample Sample(double elapsed)
    {
        if(!double.IsFinite(elapsed))throw new ArgumentOutOfRangeException(nameof(elapsed));
        var time=Math.Clamp(elapsed,keys[0].TimeMs,Duration);
        var right=keys.First(k=>k.TimeMs>=time);var left=keys.Last(k=>k.TimeMs<=time);
        var ratio=right.TimeMs==left.TimeMs?0:(float)(time-left.TimeMs)/(float)(right.TimeMs-left.TimeMs);
        return new(left.X+(right.X-left.X)*ratio,left.Y+(right.Y-left.Y)*ratio,left.Rotation+(right.Rotation-left.Rotation)*ratio,
            1-(float)(time-keys[0].TimeMs)/(float)(Duration-keys[0].TimeMs));
    }
}

public sealed class TimelinePlayback
{
    private readonly double started,duration;
    public TimelinePlayback(double started,double duration)
    {
        if(!double.IsFinite(started) || !double.IsFinite(duration) || duration<0)throw new ArgumentOutOfRangeException(nameof(duration));
        this.started=started;this.duration=duration;
    }
    public double Elapsed(double now)=>double.IsFinite(now)?Math.Max(0,now-started):throw new ArgumentOutOfRangeException(nameof(now));
    public bool Complete(double now)=>Elapsed(now)>=duration;
}
