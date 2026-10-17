using System.Text.Json;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

public static class AnimationFile
{
    public static AnimationLibrary Parse(string json)
    {
        if(json.Length>1024*1024)throw new InvalidDataException("animations: file exceeds 1 MiB");
        AnimationLibrary library;
        try {library=Wire.Read<AnimationLibrary>(json);}catch(JsonException e) {throw new InvalidDataException("animations: "+e.Message,e);}
        if(library.Format!=1 || library.Clips==null || library.Clips.Count>128)throw new InvalidDataException("animations: expected format 1 and at most 128 clips");
        var ids=new HashSet<string>();var tracks=0;
        foreach(var clip in library.Clips)
        {
            if(clip==null || string.IsNullOrWhiteSpace(clip.Id) || clip.Id.Length>128 || !ids.Add(clip.Id))throw new InvalidDataException("animations: invalid/duplicate clip ID");
            void Fail(string message)=>throw new InvalidDataException("animations."+clip.Id+": "+message);
            if(clip.Trigger==null || string.IsNullOrWhiteSpace(clip.Trigger.Entity) || string.IsNullOrWhiteSpace(clip.Trigger.Property))Fail("trigger entity/property required");
            if(clip.Tracks==null || clip.Tracks.Count==0 || (tracks+=clip.Tracks.Count)>2048) {Fail("invalid track count");continue;}
            var targets=new HashSet<string>();
            foreach(var track in clip.Tracks)
            {
                if(track==null || string.IsNullOrWhiteSpace(track.Entity) || !targets.Add(track.Entity)) {Fail("invalid/duplicate track entity");continue;}
                if(track.Keys==null || track.Keys.Count is <2 or >64) {Fail(track.Entity+": expected 2..64 keys");continue;}
                var previous=-1;
                foreach(var key in track.Keys)
                {
                    if(key==null || key.TimeMs<=previous || key.TimeMs>10000 || !float.IsFinite(key.X) || !float.IsFinite(key.Y) || !float.IsFinite(key.Rotation) || Math.Abs(key.X)>20000 || Math.Abs(key.Y)>20000 || Math.Abs(key.Rotation)>3600) {Fail(track.Entity+": invalid key time/transform");continue;}
                    previous=key.TimeMs;
                }
                var last=track.Keys[^1];
                if(last.X!=0 || last.Y!=0 || last.Rotation!=0)Fail(track.Entity+": last key must return to authoritative pose (0,0,0)");
            }
        }
        return library;
    }
    public static string Write(AnimationLibrary library)=>JsonSerializer.Serialize(Parse(Wire.Json(library)),new JsonSerializerOptions(Wire.Options){WriteIndented=true});
    public static void ValidateReferences(AnimationLibrary library,GameState state,ResourcePool resources)
    {
        foreach(var clip in library.Clips)
        {
            void Entity(string id) {if(!state.Entities.ContainsKey(id))throw new InvalidDataException("animations."+clip.Id+": missing entity "+id);}
            Entity(clip.Trigger.Entity);
            if(!state.Entities[clip.Trigger.Entity].Properties.ContainsKey(clip.Trigger.Property))throw new InvalidDataException("animations."+clip.Id+": missing trigger property "+clip.Trigger.Entity+"."+clip.Trigger.Property);
            foreach(var track in clip.Tracks) {Entity(track.Entity);if(track.Source!=null)Entity(track.Source);if(track.BackResource!=null)_=resources.GetImage(track.BackResource);}
        }
    }
}

internal sealed class AnimationResourceLoader : IResourceLoader
{
    public void Load(ResourceSource source,ResourceInput input,ResourcePoolBuilder builder)
    {
        var bytes=input.Read(source.Path);
        try {builder.Animations(AnimationFile.Parse(System.Text.Encoding.UTF8.GetString(bytes)));}
        catch(Exception e) when(e is InvalidDataException or JsonException) {throw new InvalidDataException(source.Path+": "+e.Message,e);}
    }
}
