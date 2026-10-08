namespace BoardGame.Networking;

// Playback consumes historical projections; it never reconstructs hidden authority state.
public sealed record OnlineRecording(int Format, OnlineIdentity Identity, string Seat, IReadOnlyList<OnlineFrame> Frames)
{
    public OnlineReplica Replay()
    {
        if (Format != 1 || Identity.Protocol != 1 || Frames.Count == 0) throw new OnlineException("invalid-recording");
        var replica = new OnlineReplica(Seat);
        for (var i = 0; i < Frames.Count; i++)
        {
            if (Frames[i].Revision != i || !replica.Apply(Frames[i])) throw new OnlineException("invalid-recording");
        }
        return replica;
    }
}
