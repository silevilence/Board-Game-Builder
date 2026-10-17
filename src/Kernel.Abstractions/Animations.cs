namespace BoardGame.Kernel;

public sealed record AnimationKey(int TimeMs,float X=0,float Y=0,float Rotation=0);
public sealed record AnimationTrack(string Entity,IReadOnlyList<AnimationKey> Keys,string? Source=null,string? BackResource=null);
public sealed record AnimationTrigger(string Entity,string Property);
public sealed record AnimationClip(string Id,AnimationTrigger Trigger,IReadOnlyList<AnimationTrack> Tracks);
public sealed record AnimationLibrary(int Format,IReadOnlyList<AnimationClip> Clips);
