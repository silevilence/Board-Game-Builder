using BoardGame.Kernel;
namespace BoardGame.Runtime;

public interface IRecordingProvider
{
    SessionRecording Capture();
    void RecordVersion(LoadedModule module);
    IAuthoritySession Replay(SessionRecording recording);
}
