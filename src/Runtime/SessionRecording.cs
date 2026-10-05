using BoardGame.Kernel;
using BoardGame.Modules;
namespace BoardGame.Runtime;

public sealed record ScriptSwitch(int AfterCommand,string Fingerprint);
public sealed record SessionRecording(ReplayLog Log,List<ScriptSwitch> Switches,Dictionary<string,LoadedModule> Versions,string Hash)
{
    public static SessionRecording Create(ReplayLog log,List<ScriptSwitch> switches,Dictionary<string,LoadedModule> versions)
    {
        var recording=Wire.Copy(new SessionRecording(log,switches,versions,""));
        return recording with {Hash=Checksum(recording)};
    }
    private static string Checksum(SessionRecording recording) => Wire.Hash(Wire.Json(recording with {Hash=""}));
    public GameEngine Replay()
    {
        if(Switches.Count==0 || Switches[0].AfterCommand!=0 || Switches[0].Fingerprint!=Log.ModuleFingerprint) throw new InvalidDataException("Missing initial script version");
        var boundary=0;
        foreach(var change in Switches)
        {
            if(change.AfterCommand<boundary || change.AfterCommand>Log.EntryCount) throw new InvalidDataException("Invalid script boundary");
            boundary=change.AfterCommand;
            if(!Versions.TryGetValue(change.Fingerprint,out var version)) throw new InvalidDataException("Missing script version: "+change.Fingerprint);
            if(ModuleSnapshot.Fingerprint(version)!=change.Fingerprint) throw new InvalidDataException("Script version content mismatch");
        }
        if(Hash!=Checksum(this)) throw new InvalidDataException("Session recording checksum mismatch");
        return GameEngine.Replay(Versions[Switches[0].Fingerprint],Log);
    }
}
