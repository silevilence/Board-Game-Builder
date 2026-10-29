using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Networking;

public sealed record OnlineConnection(string Server,string RoomKey,string? Seat=null,bool Audience=false,string? CertificateSha256=null)
{
    public OnlineClient Open()
    {
        if(!Uri.TryCreate(Server,UriKind.Absolute,out var url) || url.Scheme is not ("http" or "https") || url.AbsolutePath!="/" || url.Query.Length>0 || url.Fragment.Length>0 || url.UserInfo.Length>0)
            throw new ArgumentException("请输入 HTTP(S) 服务器根地址。");
        if(url.Scheme=="http" && !url.IsLoopback) throw new ArgumentException("跨机连接需要 HTTPS。");
        HttpClientHandler? handler=null;
        if(!string.IsNullOrWhiteSpace(CertificateSha256))
        {
            if(CertificateSha256.Length!=64 || !CertificateSha256.All(Uri.IsHexDigit)) throw new ArgumentException("证书指纹必须是 64 位十六进制 SHA-256。");
            handler=new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback=(_,certificate,_,_)=>certificate!=null &&
                string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.RawData)),CertificateSha256,StringComparison.OrdinalIgnoreCase);
        }
        return new(url,RoomKey,handler);
    }
}
public sealed record OnlineSavedIdentity(string Server,OnlineIdentity Identity,SeatLease Lease,long LastRevision);

// Serializes refresh, submission and recovery, retaining only this identity's projections.
public sealed class OnlinePlayerSession : IDisposable
{
    private readonly OnlineClient client;
    private readonly OnlineConnection connection;
    private readonly OnlineIdentity identity;
    private readonly string identityPath;
    private readonly SemaphoreSlim gate=new(1);
    private readonly CancellationTokenSource lifetime=new();
    private SeatLease? lease;
    private OnlineReplica? replica;
    private long nextSequence=1;
    private long inputSequence;
    private readonly RuntimeHost? inputHost;
    private readonly IBoardInteraction? interaction;
    private OnlineReceipt? inputReceipt;
    private bool disposed;
    public bool Connected {get;private set;}
    public OnlineFrame? Frame=>replica?.Frame;
    public ISeatProjection Projection {get;}
    public OnlinePlayerSession(LoadedModule module,OnlineConnection connection,string identityPath)
    {
        this.connection=connection;identity=OnlineIdentity.For(module);this.identityPath=Path.GetFullPath(identityPath);client=connection.Open();
        Projection=new RemoteProjection(this);
        try
        {
            // This root has only input binding and intent resolution, no authority or update contribution.
            if(!connection.Audience)
            {
                inputHost=BoardInteractionPlugin.StartAsync(new InputTarget(this)).GetAwaiter().GetResult();
                interaction=inputHost.Service<IBoardInteraction>(BoardInteractionPlugin.Capability);
            }
        }
        catch {client.Dispose();throw;}
    }
    public async Task ConnectAsync()
    {
        await Run(async ()=>
        {
            if(Connected) return;
            if(lease==null && File.Exists(identityPath))
            {
                var saved=Wire.Read<OnlineSavedIdentity>(await File.ReadAllTextAsync(identityPath,lifetime.Token).ConfigureAwait(false));
                if(saved.Server!=connection.Server || saved.Identity!=identity ||
                    connection.Audience!=(saved.Lease.Seat==SeatView.AudienceSeat) ||
                    (!connection.Audience && connection.Seat!=null && saved.Lease.Seat!=connection.Seat))
                    throw new OnlineException("saved-identity-mismatch");
                lease=saved.Lease;
            }
            if(lease==null)
            {
                lease=await client.JoinAsync(new(identity,connection.Seat,connection.Audience),lifetime.Token).ConfigureAwait(false);
                nextSequence=1;
            }
            else
            {
                var resumed=await client.ResumeAsync(new(identity,lease.ResumeToken,Frame?.Revision ?? -1),lifetime.Token).ConfigureAwait(false);
                lease=resumed.Lease;nextSequence=resumed.NextSequence;
                replica ??=new(lease.Seat);
                foreach(var frame in resumed.Frames) replica.Apply(frame);
            }
            replica ??=new(lease.Seat);await Save().ConfigureAwait(false);
            await Read().ConfigureAwait(false);Connected=true;
        }).ConfigureAwait(false);
    }
    private async Task Read()
    {
        var frame=(await client.SendAsync(lease!.Token,new(OnlineOperations.Frame),lifetime.Token).ConfigureAwait(false)).Frame!;
        lifetime.Token.ThrowIfCancellationRequested();
        replica!.Apply(frame);await Save().ConfigureAwait(false);
    }
    public Task RefreshAsync()=>Run(async ()=> {RequireConnection();await Read().ConfigureAwait(false);});
    public async Task<OnlineReceipt> SubmitAsync(long revision,InteractionIntent intent)
    {
        OnlineReceipt? receipt=null;
        await Run(async ()=>
        {
            RequireConnection();
            inputReceipt=null;
            var result=interaction==null?new CommandResult(0,false,"audience-read-only"):
                await interaction.SubmitAsync(++inputSequence,revision,intent,lifetime.Token).ConfigureAwait(false);
            receipt=inputReceipt ?? new(nextSequence,nextSequence,result,Frame!);
        }).ConfigureAwait(false);
        return receipt!;
    }
    private sealed class RemoteProjection(OnlinePlayerSession owner):ISeatProjection
    {
        public SeatView View()=>Wire.Copy(owner.Frame?.View ?? throw new OnlineException("disconnected"));
    }
    private sealed class InputTarget(OnlinePlayerSession owner):IBoardInteractionTarget
    {
        public InteractionFrame Read()
        {
            var frame=owner.Frame!;
            return new(frame.Revision,frame.Actions,owner.Connected && !owner.disposed && frame.Started && !frame.Closed && frame.Fault==null && !frame.View.Ended && frame.Actions.Count>0);
        }
        public async Task<CommandResult> SubmitAsync(long revision,Command command,CancellationToken cancellation)
        {
            // Called under the connection gate: lease sequence, recovery and frame publication stay serialized.
            var receipt=(await owner.client.SendAsync(owner.lease!.Token,new(OnlineOperations.Submit,
                Proposal:new(owner.nextSequence,revision,owner.lease.Seat,command)),cancellation).ConfigureAwait(false)).Receipt!;
            cancellation.ThrowIfCancellationRequested();
            owner.nextSequence=receipt.NextSequence;owner.replica!.Apply(receipt.Frame);
            await owner.Save().ConfigureAwait(false);owner.inputReceipt=receipt;return receipt.Result;
        }
    }
    public async Task<OnlineRecording> RecordingAsync()
    {
        OnlineRecording? result=null;
        await Run(async ()=> {RequireConnection();result=(await client.SendAsync(lease!.Token,new(OnlineOperations.Recording),lifetime.Token).ConfigureAwait(false)).Recording;}).ConfigureAwait(false);
        return result!;
    }
    public Task DisconnectAsync(bool leave=false)=>Run(async ()=>
    {
        RequireConnection();
        await client.SendAsync(lease!.Token,new(leave?OnlineOperations.Leave:OnlineOperations.Disconnect),lifetime.Token).ConfigureAwait(false);
        Connected=false;
        if(leave) {await OnlineIdentityFile.DeleteAsync(identityPath,lifetime.Token).ConfigureAwait(false);lease=null;replica=null;}
    });
    private void RequireConnection() {if(!Connected) throw new OnlineException("disconnected");}
    private async Task Run(Func<Task> action)
    {
        await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try {ObjectDisposedException.ThrowIf(disposed,this);await action().ConfigureAwait(false);}
        catch {Connected=false;throw;}
        finally {gate.Release();}
    }
    private Task Save()=>OnlineIdentityFile.SaveAsync(identityPath,new(connection.Server,identity,lease!,Frame?.Revision ?? -1),lifetime.Token);
    public void Dispose() {if(disposed)return;disposed=true;Connected=false;lifetime.Cancel();client.Dispose();inputHost?.Dispose();}
}
