using BoardGame.Kernel;

namespace BoardGame.Runtime;

/// <summary>Board recording ownership. Captures the authority's transaction log rather
/// than reconstructing it from notifications. Playback never installs gameplay providers.</summary>
public static class RecordingPlugin
{
    public static RuntimePluginDefinition Create(Func<LoadedModule> module,Action<LoadedModule> validate,SessionRecording? restored=null)
        =>SessionRecorder.Plugin(module,validate,restored);

    /// <summary>The trusted host authorizes the seat before handing this view to a renderer.
    /// Authority recordings contain secrets and must never be sent to a player.</summary>
    public static ISeatProjection Observe(SessionRecording recording,string authorizedSeat,Action<LoadedModule> validate)
    {
        var engine=RecordingCodec.Copy(recording).Replay(validate);
        return new RecordedProjection(engine.Project(authorizedSeat));
    }

    private sealed class RecordedProjection(SeatView view) : ISeatProjection
    {
        public SeatView View()=>Wire.Copy(view);
    }
}
