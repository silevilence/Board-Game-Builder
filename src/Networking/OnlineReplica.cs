using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Networking;

// A frame is a complete projection. Delayed or duplicate frames can safely be ignored.
public sealed class OnlineReplica(string seat):ISeatProjection
{
    public OnlineFrame? Frame { get; private set; }
    public SeatView View()=>Wire.Copy(Frame?.View ?? throw new OnlineException("missing-projection"));
    public bool Apply(OnlineFrame frame)
    {
        if (frame.View.Seat != seat) throw new OnlineException("identity-mismatch");
        if (Frame != null && frame.Revision <= Frame.Revision) return false;
        Frame = Wire.Copy(frame);
        return true;
    }
}
