namespace BoardGame.Kernel;
public sealed partial class GameEngine
{
    public SeatView Project(string seat) => Project(state, seat);
    public IReadOnlyList<PlayerEvent> PlayerLog(string seat)
    {
        _ = Project(seat);
        return submissions.Select(s => new PlayerEvent(s.Sequence, s.Actor, s.Accepted, s.Reason)).ToArray();
    }
    private static bool ContentVisible(Entity e, string seat) => e.Visibility == "public" || (e.Visibility == "owner" && e.Owner == seat);
    private static bool IdentityVisible(GameState state, Entity entity, string seat)
    {
        if (!entity.IsContainer && !ContentVisible(entity, seat)) return false;
        for (var parent = entity.Container; parent != null; parent = state.Entities[parent].Container)
            if (!ContentVisible(state.Entities[parent], seat)) return false;
        return true;
    }
    private static SeatView Project(GameState state, string seat)
    {
        if (!state.Seats.Any(s => s.Id == seat)) throw new ArgumentException("Unknown seat.");
        var visible = state.Entities.Values.Where(e => IdentityVisible(state,e,seat)).ToArray();
        var ids = visible.Select(e => e.Id).ToHashSet();
        var entities = visible.Select(e =>
        {
            var face = ContentVisible(e,seat) && (e.FaceUp || e.Owner == seat);
            return new VisibleEntity(e.Id,e.Owner,e.Container,e.IsContainer,e.Contents.Count,e.FaceUp,
                face && e.Faces.Count > 0 ? e.Faces[e.Face] : null,e.X,e.Y,
                face ? new(e.Properties,StringComparer.Ordinal) : new(StringComparer.Ordinal),
                e.Contents.Where(ids.Contains).ToList(),face && e.Face < e.FaceResources.Count ? e.FaceResources[e.Face] : null);
        }).ToList();
        return new(seat,Wire.Copy(state.Seats),state.Seats[state.CurrentSeat].Id,state.Phase,state.Turn,state.Ended,[..state.Winners],entities);
    }
}
