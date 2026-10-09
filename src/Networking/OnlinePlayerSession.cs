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
    private bool disposed;
    public bool Connected {get;private set;}
    public OnlineFrame? Frame=>replica?.Frame;
    public OnlinePlayerSession(LoadedModule module,OnlineConnection connection,string identityPath)
    {
        this.connection=connection;identity=OnlineIdentity.For(module);this.identityPath=Path.GetFullPath(identityPath);client=connection.Open();
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
        replica!.Apply(frame);await Save().ConfigureAwait(false);
    }
    public Task RefreshAsync()=>Run(async ()=> {RequireConnection();await Read().ConfigureAwait(false);});
    public async Task<OnlineReceipt> SubmitAsync(long revision,InteractionIntent intent)
    {
        OnlineReceipt? receipt=null;
        await Run(async ()=>
        {
            RequireConnection();
            var frame=Frame!;
            if(frame.Revision!=revision || !frame.Started || frame.Closed || frame.View.Ended || frame.Actions.Count==0)
            {receipt=new(nextSequence,nextSequence,new(0,false,"stale-or-inactive"),frame);return;}
            var command=PlayerSession.Resolve(frame.Actions,intent);
            receipt=(await client.SendAsync(lease!.Token,new(OnlineOperations.Submit,Proposal:new(nextSequence,revision,lease.Seat,command)),lifetime.Token).ConfigureAwait(false)).Receipt!;
            nextSequence=receipt.NextSequence;replica!.Apply(receipt.Frame);await Save().ConfigureAwait(false);
        }).ConfigureAwait(false);
        return receipt!;
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
    public void Dispose() {if(disposed)return;disposed=true;Connected=false;lifetime.Cancel();client.Dispose();}
}
