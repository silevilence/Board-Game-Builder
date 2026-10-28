using BoardGame.Kernel;
namespace BoardGame.Runtime;

public sealed record InteractionIntent(string Kind,string? Entity=null,string? Target=null,double X=0,double Y=0,string? Action=null);

public static class InteractionResolver
{
    public static Command Resolve(IReadOnlyList<ActionOffer> actions,InteractionIntent intent)
    {
        if(intent.Kind=="action") return actions.FirstOrDefault(a=>a.Id==intent.Action)?.Command ?? new() { Kind="invalid-action" };
        if(intent.Kind is "shortcut" or "double-click")
            return actions.FirstOrDefault(a=>a.Shortcuts?.Contains(intent.Kind=="double-click"?"double-click":intent.Action ?? "")==true
                && (a.Command.Entity==null || a.Command.Entity==intent.Entity))?.Command ?? new() {Kind="invalid-shortcut"};
        if(intent.Kind=="click") intent=intent with {Kind="drag"};
        var offer=actions.FirstOrDefault(a=>a.Gesture==intent.Kind && a.Command.Entity==intent.Entity && a.Target==intent.Target);
        if(offer!=null) return offer.Command;
        return intent.Kind switch
        {
            "drag" => intent.Target==null ? new() { Kind="move",Entity=intent.Entity,X=intent.X,Y=intent.Y } : new() { Kind="transfer",Entity=intent.Entity,Target=intent.Target },
            "flip" => new() { Kind="flip",Entity=intent.Entity },
            "draw" => new() { Kind="draw",Entity=intent.Entity,Target=intent.Target },
            _ => new() { Kind="invalid-intent" }
        };
    }
}
