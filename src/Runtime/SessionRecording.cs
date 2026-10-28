using BoardGame.Kernel;
namespace BoardGame.Runtime;

// Compatibility for the installed default catalog. Selective hosts supply their own validator.
public static class RecordingCompatibility
{
    public static GameEngine Replay(this SessionRecording recording)=>recording.Replay(module=>GameRuntime.ValidateSnapshot(module));
}
