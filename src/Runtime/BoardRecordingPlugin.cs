using BoardGame.Kernel;

namespace BoardGame.Runtime;

/// <summary>Board recording ownership. Captures the authority's transaction log rather
/// than reconstructing it from notifications. Playback never installs gameplay providers.</summary>
public static class BoardRecordingPlugin
{
    public static RuntimePluginDefinition Create(Func<LoadedModule> module,SessionRecording? restored=null)
        =>SessionRecorder.Plugin(module,restored);

    /// <summary>The trusted host authorizes the seat before handing this view to a renderer.
    /// Authority recordings contain secrets and must never be sent to a player.</summary>
    public static ISeatProjection Observe(SessionRecording recording,string authorizedSeat)
    {
        var engine=Wire.Copy(recording).Replay();
        return new RecordedProjection(engine.Project(authorizedSeat));
    }

    private sealed class RecordedProjection(SeatView view) : ISeatProjection
    {
        public SeatView View()=>Wire.Copy(view);
    }
}
