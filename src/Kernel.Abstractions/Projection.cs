namespace BoardGame.Kernel;
public sealed record VisibleEntity(string Id, string? Owner, string? Container, bool IsContainer,
    int Count, bool FaceUp, string? Face, double X, double Y,
    SortedDictionary<string,int> Properties, List<string> Contents, string? Resource = null);
public sealed record SeatView(string Seat, List<Seat> Seats, string CurrentSeat, string Phase, int Turn,
    bool Ended, List<string> Winners, List<VisibleEntity> Entities);
public sealed record PlayerEvent(long Sequence, string Actor, bool Accepted, string Reason);
