namespace BoardGame.Kernel;
public static class StateValidation
{
    public static void Validate(GameState state)
    {
        state.Layout?.Validate();
        if(state.Seats==null || state.Seats.Count==0 || state.Seats.Any(s=>s==null || string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Team) || s.Controller is not ("human" or "cpu")) || state.Seats.Select(s=>s.Id).Distinct().Count()!=state.Seats.Count)
            throw new InvalidDataException("seats: invalid or duplicate identity/controller");
        if(state.Phases==null || state.Phases.Count==0 || state.Phases.Any(string.IsNullOrWhiteSpace) || state.Phases.Distinct().Count()!=state.Phases.Count || !state.Phases.Contains(state.Phase) || state.CurrentSeat<0 || state.CurrentSeat>=state.Seats.Count || state.Turn<0)
            throw new InvalidDataException("turn/phases: invalid state");
        if(state.Winners==null || state.Winners.Distinct().Count()!=state.Winners.Count || state.Winners.Any(w=>!state.Seats.Any(s=>s.Id==w))) throw new InvalidDataException("winners: invalid seat");
        if(state.Entities==null) throw new InvalidDataException("entities: required");
        foreach(var pair in state.Entities)
        {
            var e=pair.Value;
            if(e==null || string.IsNullOrWhiteSpace(e.Id) || e.Id!=pair.Key) throw new InvalidDataException("entities: invalid identity");
            void Fail(string field) => throw new InvalidDataException($"entities[{e.Id}].{field}: invalid value");
            if(e.Owner!=null && !state.Seats.Any(s=>s.Id==e.Owner)) Fail("owner");
            if(e.Visibility is not ("public" or "owner" or "hidden") || (e.Visibility=="owner" && e.Owner==null)) Fail("visibility");
            if(!double.IsFinite(e.X) || !double.IsFinite(e.Y)) Fail("position");
            if(e.Faces==null || e.Faces.Any(string.IsNullOrWhiteSpace) || e.Face<0 || (e.Faces.Count==0?e.Face!=0:e.Face>=e.Faces.Count)) Fail("faces");
            if(e.FaceResources==null || (e.FaceResources.Count!=0 && e.FaceResources.Count!=e.Faces!.Count) || e.FaceResources.Any(string.IsNullOrWhiteSpace)) Fail("face resources");
            if(e.Properties==null || e.Properties.Keys.Any(string.IsNullOrWhiteSpace)) Fail("properties");
            if(e.CounterBounds==null || e.CounterBounds.Any(p=>p.Value==null || p.Value.Min>p.Value.Max || !e.Properties!.TryGetValue(p.Key,out var value) || value<p.Value.Min || value>p.Value.Max)) Fail("counter");
            if(e.DiceFaces==null || e.DiceFaces.Count>256 || (e.DiceFaces.Count>0 && (!e.Properties!.TryGetValue("value",out var roll) || !e.DiceFaces.Contains(roll)))) Fail("dice");
            if(e.Contents==null) throw new InvalidDataException($"entities[{e.Id}].contents: required");
            if(e.Capacity<0 || e.Contents.Count>e.Capacity || e.Contents.Distinct().Count()!=e.Contents.Count || (!e.IsContainer && (e.Contents.Count!=0 || e.Capacity!=0 || e.Ordered))) Fail("capacity/contents");
            foreach(var child in e.Contents)
                if(child==null || !state.Entities.TryGetValue(child,out var value) || value==null || value.Container!=e.Id) Fail("contents");
            if(e.Container!=null && (!state.Entities.TryGetValue(e.Container,out var container) || container==null || !container.IsContainer || !container.Contents.Contains(e.Id))) Fail("container");
            var visited=new HashSet<string> { e.Id };
            for(var parent=e.Container;parent!=null;parent=state.Entities[parent].Container)
            {
                if(!state.Entities.ContainsKey(parent) || !visited.Add(parent)) Fail("container cycle/reference");
            }
        }
    }
}
