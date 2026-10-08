using BoardGame.Kernel;

namespace BoardGame.Networking;

// A frame is a complete projection. Delayed or duplicate frames can safely be ignored.
public sealed class OnlineReplica(string seat)
{
    public OnlineFrame? Frame { get; private set; }
    public bool Apply(OnlineFrame frame)
    {
        if (frame.View.Seat != seat) throw new OnlineException("identity-mismatch");
        if (Frame != null && frame.Revision <= Frame.Revision) return false;
        Frame = Wire.Copy(frame);
        return true;
    }
}
