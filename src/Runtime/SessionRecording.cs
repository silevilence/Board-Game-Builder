using BoardGame.Kernel;
using BoardGame.Modules;
namespace BoardGame.Runtime;

public sealed record ScriptSwitch(int AfterCommand,string Fingerprint);
public sealed record SessionRecording(ReplayLog Log,List<ScriptSwitch> Switches,Dictionary<string,LoadedModule> Versions,string Hash,int Format=1)
{
    public static SessionRecording Create(ReplayLog log,List<ScriptSwitch> switches,Dictionary<string,LoadedModule> versions)
    {
        var recording=Wire.Copy(new SessionRecording(log,switches,versions,"",versions.Values.Any(v=>v.RuntimeIdentity!=null)?2:1));
        return recording with {Hash=Checksum(recording)};
    }
    private static string Checksum(SessionRecording recording) => Wire.Hash(recording.Format==1
        ?Wire.Json(new {recording.Log,recording.Switches,recording.Versions,Hash=""})
        :Wire.Json(recording with {Hash=""}));
    public GameEngine Replay()
    {
        if(Format is not (1 or 2) || Versions.Values.Any(v=>(v.RuntimeIdentity!=null)!=(Format==2))) throw new InvalidDataException("Incompatible recording/runtime identity format");
        if(Switches.Count==0 || Switches[0].AfterCommand!=0 || Switches[0].Fingerprint!=Log.ModuleFingerprint) throw new InvalidDataException("Missing initial script version");
        var boundary=0;
        foreach(var change in Switches)
        {
            if(change.AfterCommand<boundary || change.AfterCommand>Log.EntryCount) throw new InvalidDataException("Invalid script boundary");
            boundary=change.AfterCommand;
            if(!Versions.TryGetValue(change.Fingerprint,out var version)) throw new InvalidDataException("Missing script version: "+change.Fingerprint);
            if(ModuleSnapshot.Fingerprint(version)!=change.Fingerprint) throw new InvalidDataException("Script version content mismatch");
            if(Format==2) GameRuntime.ValidateSnapshot(version);
        }
        if(Hash!=Checksum(this)) throw new InvalidDataException("Session recording checksum mismatch");
        return GameEngine.Replay(Versions[Switches[0].Fingerprint],Log);
    }
}

internal sealed class SessionRecorder : IRecordingProvider
{
    private readonly IAuthoritySession authority;
    private readonly List<ScriptSwitch> switches=[];
    private readonly Dictionary<string,LoadedModule> versions=new(StringComparer.Ordinal);
    public SessionRecorder(IAuthoritySession authority,LoadedModule module) {this.authority=authority;RecordVersion(module);}
    public void RecordVersion(LoadedModule module)
    {versions[module.Fingerprint]=Wire.Copy(module);switches.Add(new(authority.ExportLog().EntryCount,module.Fingerprint));}
    public SessionRecording Capture()=>SessionRecording.Create(authority.ExportLog(),switches,versions);
    public IAuthoritySession Replay(SessionRecording recording)=>new KernelAuthority(recording.Replay());
    public static RuntimePluginDefinition Plugin(LoadedModule module)=>new("recording","1",1,["recording"],["authority"],scope=>
        scope.Provide("recording",new SessionRecorder(scope.Service<IAuthoritySession>("authority"),module)));
}
