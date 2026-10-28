using BoardGame.Kernel;
namespace BoardGame.Runtime;

public static class BoardRecordingPlugin
{
    public static RuntimePluginDefinition Create(Func<LoadedModule> module,SessionRecording? restored=null)=>
        RecordingPlugin.Create(module,value=>GameRuntime.ValidateSnapshot(value),restored);
    public static ISeatProjection Observe(SessionRecording recording,string authorizedSeat)=>
        RecordingPlugin.Observe(recording,authorizedSeat,value=>GameRuntime.ValidateSnapshot(value));
}
