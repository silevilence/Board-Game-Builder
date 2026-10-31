using BoardGame.Kernel;
using BoardGame.Modules;
namespace BoardGame.Runtime;

public sealed record ScriptSwitch(int AfterCommand,string Fingerprint);
public sealed record SessionRecording(ReplayLog Log,List<ScriptSwitch> Switches,Dictionary<string,LoadedModule> Versions,string Hash,int Format=1)
{
    public static SessionRecording Create(ReplayLog log,List<ScriptSwitch> switches,Dictionary<string,LoadedModule> versions)
    {
        var recording=RecordingCodec.Copy(new SessionRecording(log,switches,versions,"",versions.Values.Any(v=>v.RuntimeIdentity!=null)?2:1));
        return recording with {Hash=Checksum(recording)};
    }
    private static string Checksum(SessionRecording recording) => Wire.Hash(recording.Format==1
        ?Wire.Json(new LegacyRecordingInput(recording.Log,recording.Switches,recording.Versions,""),Wire.Type<LegacyRecordingInput>(RecordingJson.Default))
        :RecordingCodec.Write(recording with {Hash=""}));
    public GameEngine Replay(Action<LoadedModule> validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        if(Log==null || Switches==null || Versions==null || Switches.Any(s=>s==null) || Versions.Values.Any(v=>v==null))
            throw new InvalidDataException("Invalid authority recording structure; player projection recordings use their own reader");
        if(Format is not (1 or 2) || Versions.Values.Any(v=>(v.RuntimeIdentity!=null)!=(Format==2))) throw new InvalidDataException("Incompatible recording/runtime identity format");
        if(Switches.Count==0 || Switches[0].AfterCommand!=0 || Switches[0].Fingerprint!=Log.ModuleFingerprint) throw new InvalidDataException("Missing initial script version");
        var boundary=0;
        foreach(var change in Switches)
        {
            if(change.AfterCommand<boundary || change.AfterCommand>Log.EntryCount) throw new InvalidDataException("Invalid script boundary");
            boundary=change.AfterCommand;
            if(!Versions.TryGetValue(change.Fingerprint,out var version)) throw new InvalidDataException("Missing script version: "+change.Fingerprint);
            if(ModuleSnapshot.Fingerprint(version)!=change.Fingerprint) throw new InvalidDataException("Script version content mismatch");
            if(Format==2) validate(version);
        }
        if(Hash!=Checksum(this)) throw new InvalidDataException("Session recording checksum mismatch");
        return GameEngine.Replay(Versions[Switches[0].Fingerprint],Log);
    }
}

internal sealed class SessionRecorder : IRecordingProvider
{
    private readonly IAuthoritySession authority;
    private readonly Action<LoadedModule> validate;
    private readonly List<ScriptSwitch> switches=[];
    private readonly Dictionary<string,LoadedModule> versions=new(StringComparer.Ordinal);
    public SessionRecorder(IAuthoritySession authority,LoadedModule module,Action<LoadedModule> validate,SessionRecording? recording=null)
    {
        this.authority=authority;this.validate=validate;
        if(recording==null) RecordVersion(module);
        else {var copy=RecordingCodec.Copy(recording);switches.AddRange(copy.Switches);foreach(var version in copy.Versions) versions.Add(version.Key,version.Value);}
    }
    public void RecordVersion(LoadedModule module)
    {versions[module.Fingerprint]=Wire.Copy(module);switches.Add(new(authority.ExportLog().EntryCount,module.Fingerprint));}
    public SessionRecording Capture()=>SessionRecording.Create(authority.ExportLog(),switches,versions);
    public IAuthoritySession Replay(SessionRecording recording)=>new KernelAuthority(recording.Replay(validate));
    public static RuntimePluginDefinition Plugin(Func<LoadedModule> module,Action<LoadedModule> validate,SessionRecording? recording=null)=>new("recording","1",1,["recording"],["authority"],scope=>
        scope.Provide("recording",new SessionRecorder(scope.Service<IAuthoritySession>("authority"),module(),validate,recording)))
        {Permissions=[AuthorityPlugin.Access],ServicePermissions=new Dictionary<string,string>{{"recording",AuthorityPlugin.Access}}};
}
