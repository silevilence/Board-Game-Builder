using System.Text.Json;
using Avalonia.Controls;

namespace BoardGame.Player;

public sealed class BoardPlayerProvider : IPlayerProvider
{
    public bool Supports(PlayerSource source)
    {
        var header=source.Header;
        if(source.Snapshot)
        {
            if(!header.TryGetProperty("runtimeIdentity",out var identity) || !identity.TryGetProperty("startup",out header))return false;
        }
        return header.TryGetProperty("plugins",out var plugins) && plugins.ValueKind==JsonValueKind.Array && plugins.EnumerateArray().Any(p=>
            p.TryGetProperty("id",out var id) && id.GetString() is "definition.module" or "definition.state");
    }
    public Window Create(PlayerSource source,PlayerLaunch launch)=>new PlayerWindow(source.Path,launch.Smoke,launch.Actions,shell:launch.Shell);
    public static PlayerContribution Contribution()=>new(new("player.board","1.0.0",1,["player.board"],[],scope=>scope.Provide("player.board",new BoardPlayerProvider())),"player.board");
}
