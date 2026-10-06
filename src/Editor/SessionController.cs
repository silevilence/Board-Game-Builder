using Avalonia.Threading;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Editor;

// Foundation plugin owns the session, scheduling and command generation. Views can come and go.
public sealed class SessionController : IDisposable
{
    private readonly DocumentWorkspace documents;
    private readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMilliseconds(100)};
    private IDebugSession? session;
    private int generation;
    private bool pendingPause;
    public LoadedModule? Module { get; private set; }
    public DebugFrame? Frame { get; private set; }
    public DebugReply? Reply { get; private set; }
    public bool Busy { get; private set; }
    public bool Active=>session!=null;
    public bool CanInput=>!Busy && Frame is {Cpu:false,View.Ended:false,Fault:null,IsReplay:false};
    public string Status { get; private set; }="未开始试玩";
    public string Seed { get; set; }="42";
    public uint? ActiveSeed { get; private set; }
    public bool External { get; set; }
    public string Executable { get; set; }=EditorPaths.InWorkspace("src/Player/bin/Release/net10.0-windows/Player.exe");
    public Dictionary<string,bool> CpuSeats { get; }=[];
    public string? Observer { get; set; }
    public bool Reveal { get; set; }
    public bool AuthorityLog { get; set; }
    public string LogPath { get; set; }=EditorPaths.InWorkspace("artifacts/debug-session.json");
    public event Action? Changed;
    public SessionController(DocumentWorkspace documents)
    {
        this.documents=documents;documents.Replaced+=DocumentChanged;
        timer.Tick+=async (_,_)=> {if(Frame is {Cpu:true,View.Ended:false,Fault:null,Paused:false,IsReplay:false} && !Busy) await Send(new("cpu",Frame.Revision));};
        DocumentChanged();
    }
    private void DocumentChanged()
    {
        Stop();ActiveSeed=null;CpuSeats.Clear();Reveal=false;
        try {SyncSeats(documents.Current?.Snapshot());} catch(Exception error) {Status=error.Message;}
        Changed?.Invoke();
    }
    public void SyncSeats(LoadedModule? module)
    {
        var previous=new Dictionary<string,bool>(CpuSeats);CpuSeats.Clear();
        foreach(var seat in module?.Initial.Seats ?? []) CpuSeats[seat.Id]=previous.GetValueOrDefault(seat.Id,seat.Controller=="cpu");
        if(Observer==null || !CpuSeats.ContainsKey(Observer)) Observer=CpuSeats.Keys.FirstOrDefault();
    }
    public async Task Start()
    {
        Stop();var current=generation;Busy=true;Changed?.Invoke();
        try
        {
            if(!uint.TryParse(Seed,out var seed)) throw new InvalidDataException("种子必须是无符号整数");
            documents.ApplyDrafts();Module=documents.Current?.Snapshot() ?? throw new InvalidOperationException("请打开模块");SyncSeats(Module);
            var controllers=CpuSeats.ToDictionary(p=>p.Key,p=>p.Value?"cpu":"human");
            var observer=Module.Initial.Seats.FirstOrDefault(s=>controllers[s.Id]=="human")?.Id ?? Module.Initial.Seats[0].Id;
            var created=External?(IDebugSession)await ExternalDebugSession.Launch(Executable,Module,seed,controllers,observer):new InProcessDebugSession(Module,seed,controllers,observer);
            if(current!=generation) {created.Dispose();return;}
            session=created;ActiveSeed=seed;Busy=false;
            await Send(new("frame"));if(current==generation) timer.Start();
        }
        catch(Exception error) {if(current==generation) {Busy=false;Status="试玩故障："+error.Message;Changed?.Invoke();}}
    }
    public async Task<DebugReply?> Send(DebugRequest request)
    {
        if(session==null) return null;
        if(Busy) {if(request.Operation=="pause") pendingPause=true;return null;}
        Busy=true;Changed?.Invoke();var current=generation;var active=session;
        try
        {
            var reply=await active.SendAsync(request);if(current!=generation) return null;
            Reply=reply;Frame=reply.Frame;Status=reply.Message.Length>0?reply.Message:reply.Result?.Reason ?? "";return reply;
        }
        catch(Exception error) {if(current==generation) {timer.Stop();Status="试玩故障："+error.Message;}return null;}
        finally
        {
            if(current==generation)
            {
                Busy=false;Changed?.Invoke();
                if(pendingPause) {pendingPause=false;await Send(new("pause"));}
            }
        }
    }
    public async Task<string> ReloadScripts(LoadedModule candidate)
    {
        if(session==null) return "未开始试玩，新局将使用当前脚本。";
        if(Busy) throw new InvalidOperationException("当前命令尚未完成，请在命令边界重试。");
        var reply=await Send(new("reload",Module:candidate));
        return reply?.Message ?? Status;
    }
    public async Task SaveRecording()
    {
        var reply=await Send(new("recording"));if(reply?.Recording==null) return;
        var path=Path.GetFullPath(LogPath);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,Wire.Json(reply.Recording));
        Status="日志已保存："+path;Changed?.Invoke();
    }
    public void Stop()
    {
        generation++;timer.Stop();session?.Dispose();session=null;Frame=null;Reply=null;Module=null;Busy=false;pendingPause=false;
        Status="已停止；作者初始局面保持不变。";Changed?.Invoke();
    }
    internal IDebugSession? Session {get=>session;set=>session=value;}
    internal void SuspendScheduler()=>timer.Stop();
    public void Dispose() {documents.Replaced-=DocumentChanged;Stop();Changed=null;}
}
