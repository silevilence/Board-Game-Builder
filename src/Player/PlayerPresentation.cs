using System.Text;
using BoardGame.Kernel;
namespace BoardGame.Player;

// Player-only metadata travels as a normal hashed module resource, outside rule state.
public sealed record PlayerPresentation(string? Title=null,string? Help=null,bool ShowActions=true,int? Width=null,int? Height=null,string? StatusEntity=null)
{
    public static PlayerPresentation Read(LoadedModule module)
    {
        if(module.Resources==null || !module.Resources.Binaries.ContainsKey("player.presentation")) return new(module.Id);
        var bytes=module.Resources.GetBinary("player.presentation");
        if(bytes.Length>32*1024) throw new InvalidDataException("player.presentation exceeds 32 KiB");
        var value=Wire.Read<PlayerPresentation>(Encoding.UTF8.GetString(module.Resources.GetBinary("player.presentation").Span));
        if(value.Title?.Length>120 || value.Help?.Length>12000) throw new InvalidDataException("player.presentation text is too long");
        if(value.Width is <800 or >3840 || value.Height is <600 or >2160)throw new InvalidDataException("player.presentation: width 800..3840, height 600..2160 required");
        if(value.StatusEntity!=null && !module.Initial.Entities.ContainsKey(value.StatusEntity))throw new InvalidDataException("player.presentation: unknown statusEntity "+value.StatusEntity);
        return value with {Title=string.IsNullOrWhiteSpace(value.Title)?module.Id:value.Title};
    }
}

public enum PlayerShell { Game,Tools }

internal sealed record PlayerLaunch(string? Module=null,PlayerShell? Shell=null,string? Smoke=null,string? Actions=null)
{
    public static PlayerLaunch Parse(string[] args)
    {
        string? module=null,smoke=null,actions=null;PlayerShell? shell=null;
        var seen=new HashSet<string>();
        for(var i=0;i<args.Length;i++)
        {
            var arg=args[i];
            if(!arg.StartsWith("--",StringComparison.Ordinal))
            {
                if(module!=null) throw new ArgumentException("Only one module path is allowed");
                module=arg;continue;
            }
            if(!seen.Add(arg) || i+1>=args.Length) throw new ArgumentException("Missing or duplicate option: "+arg);
            var value=args[++i];
            switch(arg)
            {
                case "--shell": shell=value switch {"game"=>PlayerShell.Game,"tools"=>PlayerShell.Tools,_=>throw new ArgumentException("--shell expects game or tools")};break;
                case "--smoke": smoke=value;break;
                case "--actions": actions=value;break;
                default: throw new ArgumentException("Unknown GUI option: "+arg);
            }
        }
        if(actions!=null && smoke==null) throw new ArgumentException("--actions requires --smoke");
        return new(module,shell,smoke,actions);
    }
}
