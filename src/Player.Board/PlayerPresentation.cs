using System.Text;
using BoardGame.Kernel;
using BoardGame.View;
namespace BoardGame.Player;

// Player-only metadata travels as a normal hashed module resource, outside rule state.
public sealed record PlayerSetupStep(string Title,string Text,string NextLabel,string[] ActionKinds,string? PreviewEntity=null,string? SubmitKind=null);
public sealed record PlayerSetup(string Entity,string Property,int Value,PlayerSetupStep[] Steps);
public sealed record PlayerPresentation(string? Title=null,string? Help=null,bool ShowActions=true,int? Width=null,int? Height=null,string? StatusEntity=null,
    bool ActionTray=false,PlayerSetup? Setup=null,string? EndActionLabel=null,BoardActionMap? BoardActions=null)
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
        if(value.EndActionLabel?.Length>120)throw new InvalidDataException("player.presentation: endActionLabel too long");
        if(value.BoardActions is {} map)BoardGame.View.BoardActions.Validate(map,module);
        if(value.Setup is {} setup)
        {
            if(!module.Initial.Entities.TryGetValue(setup.Entity,out var entity) || !entity.Properties.ContainsKey(setup.Property)
                || setup.Steps is not {Length:>=1 and <=12})throw new InvalidDataException("player.presentation: invalid setup binding or steps");
            foreach(var step in setup.Steps)
                if(step==null || string.IsNullOrWhiteSpace(step.Title) || step.Title.Length>120 || step.Text==null || step.Text.Length>4000
                    || string.IsNullOrWhiteSpace(step.NextLabel) || step.NextLabel.Length>120 || step.ActionKinds==null || step.ActionKinds.Length>32
                    || step.ActionKinds.Any(string.IsNullOrWhiteSpace) || step.PreviewEntity!=null && !module.Initial.Entities.ContainsKey(step.PreviewEntity))
                    throw new InvalidDataException("player.presentation: invalid setup step");
            if(string.IsNullOrWhiteSpace(setup.Steps[^1].SubmitKind))throw new InvalidDataException("player.presentation: final setup step needs submitKind");
        }
        return value with {Title=string.IsNullOrWhiteSpace(value.Title)?module.Id:value.Title};
    }
}

