using System.Text.Json;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

// The trusted host binds this capability to one already authorized seat. It has no seat selector.
public sealed record InteractionFrame(long Revision,IReadOnlyList<ActionOffer> Actions,bool Active);
public interface IBoardInteractionTarget
{
    InteractionFrame Read();
    Task<CommandResult> SubmitAsync(long revision,Command command,CancellationToken cancellation);
}
public interface IBoardInteraction
{
    Task<CommandResult> SubmitAsync(long sequence,long revision,InteractionIntent intent,CancellationToken cancellation=default);
}

public static class BoardInteractionPlugin
{
    public const string Access="board.input";
    public const string Target="board.submit";
    public const string Capability="board.interaction";
    public static RuntimePluginDefinition Bind(IBoardInteractionTarget target)=>new("board.input-target","1",1,[Target],[],s=>s.Provide(Target,target))
        {ServicePermissions=new Dictionary<string,string>{{Target,Access}}};
    public static RuntimePluginDefinition Create()=>new("board.interaction","1",1,[Capability],[Target],s=>
        s.Provide(Capability,s.Own(new Interaction(s.Service<IBoardInteractionTarget>(Target)))))
        {Permissions=[Access],ServicePermissions=new Dictionary<string,string>{{Capability,Access}}};
    public static Task<RuntimeHost> StartAsync(IBoardInteractionTarget target)
    {
        RuntimePluginDefinition[] plugins=[Bind(target),Create()];
        var selections=plugins.Select(p=>new RuntimePluginSelection(p.Id,p.Version,p.Api,JsonSerializer.SerializeToElement(new{}))).ToList();
        return RuntimeHost.StartAsync(new(1,"board-input","1",selections),plugins,[Capability]);
    }
    private sealed class Interaction(IBoardInteractionTarget target) : IBoardInteraction,IDisposable
    {
        private readonly object gate=new();
        private readonly CancellationTokenSource lifetime=new();
        private long lastSequence;
        private bool busy,disposed,cancellationFinished;
        public async Task<CommandResult> SubmitAsync(long sequence,long revision,InteractionIntent intent,CancellationToken cancellation=default)
        {
            lock(gate)
            {
                if(disposed)return new(0,false,"stale-or-inactive");
                if(sequence<=lastSequence)return new(0,false,"duplicate-input");
                lastSequence=sequence;
                if(busy)return new(0,false,"busy");
                busy=true;
            }
            try
            {
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,cancellation);
                linked.Token.ThrowIfCancellationRequested();
                var frame=target.Read();
                if(!frame.Active || frame.Revision!=revision)return new(0,false,"stale-or-inactive");
                var command=Wire.Copy(InteractionResolver.Resolve(frame.Actions,intent));
                return await target.SubmitAsync(revision,command,linked.Token).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(lifetime.IsCancellationRequested || cancellation.IsCancellationRequested)
            {return new(0,false,"canceled");}
            finally {lock(gate){busy=false;if(cancellationFinished)lifetime.Dispose();}}
        }
        public void Dispose()
        {
            lock(gate){if(disposed)return;disposed=true;}
            try {lifetime.Cancel();}
            finally {lock(gate){cancellationFinished=true;if(!busy)lifetime.Dispose();}}
        }
    }
}
