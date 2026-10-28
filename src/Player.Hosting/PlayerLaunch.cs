namespace BoardGame.Player;

public enum PlayerShell { Game,Tools }

public sealed record PlayerLaunch(string? Module=null,PlayerShell? Shell=null,string? Smoke=null,string? Actions=null)
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
