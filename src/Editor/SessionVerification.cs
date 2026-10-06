using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;

namespace BoardGame.Editor;

internal static class SessionVerification
{
    public static Window Create(string module,string output)
    {
        var host=new EditorHost(module);
        host.Window.Opened+=async (_,_)=>
        {
            var errors=new List<string>();var recordings=new List<SessionRecording>();
            try
            {
                await host.Ready;var app=host.Application;
                var documents=app.Service<DocumentWorkspace>("documents");var initial=documents.Current!.Snapshot().Fingerprint;
                var sessions=app.Service<SessionController>("sessions");
                foreach(var external in new[]{false,true})
                {
                    sessions.External=external;
                    foreach(var seat in sessions.CpuSeats.Keys.ToArray()) sessions.CpuSeats[seat]=true;
                    await sessions.Start();await sessions.Send(new("pause"));
                    if(sessions.Frame==null || !sessions.Frame.Paused) throw new Exception(sessions.Status);
                    foreach(var seat in documents.Current!.Snapshot().Initial.Seats)
                    {
                        var observed=await sessions.Send(new("observe",Seat:seat.Id));
                        if(observed?.Frame.View.Seat!=seat.Id || observed.AuthorityState!=null) throw new Exception("Observer projection is incorrect");
                    }
                    var authority=await sessions.Send(new("observe",Seat:sessions.Observer,Authority:true));
                    if(authority?.AuthorityState==null) throw new Exception("Explicit authority inspection unavailable");
                    await sessions.Send(new("observe",Seat:documents.Current.Snapshot().Initial.Seats[0].Id,Authority:false));
                    var author=documents.Current!;
                    author.EditText("rules.lua",author.Text("rules.lua")+"\n-- m25 rules reload\n");
                    await app.Reload("author.scripts");
                    var beforeReload=sessions.Frame.Revision;
                    await sessions.ReloadScripts(author.Snapshot());
                    if(sessions.Frame.Revision!=beforeReload+1) throw new Exception("Rules reload failed: "+sessions.Status);
                    author.EditText("cpu.lua",author.Text("cpu.lua")+"\n-- m25 CPU reload\n");
                    await sessions.ReloadScripts(author.Snapshot());
                    if(sessions.Frame.Revision!=beforeReload+2) throw new Exception("CPU reload failed: "+sessions.Status);
                    var validHash=sessions.Frame.StateHash;
                    author.EditText("rules.lua","broken lua ?");await sessions.ReloadScripts(author.Snapshot());author.Undo();
                    if(sessions.Frame.Revision!=beforeReload+2 || sessions.Frame.StateHash!=validHash) throw new Exception("Broken Lua damaged current session");
                    var active=sessions.Session;var revision=sessions.Frame.Revision;
                    for(var cycle=0;cycle<3;cycle++)
                    {
                        await app.Reload("play.controls");Select(app,"play.controls");await Task.Delay(150);
                        if(!ReferenceEquals(active,sessions.Session) || sessions.Frame.Revision!=revision) throw new Exception("Panel reconstruction restarted/advanced session");
                    }
                    var invalid=await sessions.Send(new("submit",sessions.Frame.Revision,new() {Kind="invalid"}));
                    if(invalid?.Result?.Accepted!=false) throw new Exception("Illegal action not diagnosed");
                    var delayed=new DelayedSession(sessions.Session!);sessions.Session=delayed;
                    var pending=sessions.Send(new("step",sessions.Frame.Revision));await sessions.Send(new("pause"));delayed.Release();await pending;
                    if(sessions.Frame?.Paused!=true) throw new Exception("Busy pause lost");
                    for(var i=0;i<500 && sessions.Frame is {View.Ended:false,Fault:null};i++) await sessions.Send(new("step",sessions.Frame.Revision));
                    if(sessions.Frame?.View.Ended!=true || sessions.Frame.Fault!=null) throw new Exception("Did not finish: "+sessions.Status);
                    var final=sessions.Frame.StateHash;
                    var recording=(await sessions.Send(new("recording")))?.Recording ?? throw new Exception("Missing recording");recordings.Add(recording);
                    if(recording.Switches.Count<3) throw new Exception("Reload versions missing");
                    await sessions.Send(new("replay"));if(sessions.Frame.StateHash!=final || !sessions.Frame.IsReplay) throw new Exception("Replay differs");
                    if(((PlayPanel)((TabItem)app.Service<WorkspaceShell>("workspace").Tabs.SelectedItem!).Content!).Table.RenderedFrames==0) throw new Exception("No rendered play frames");
                    sessions.Stop();
                    if(sessions.Active || sessions.Reply!=null || sessions.Frame!=null) throw new Exception("Stop left old state");
                    await sessions.Start();sessions.SuspendScheduler();
                    var old=new DelayedSession(sessions.Session!);sessions.Session=old;
                    var late=sessions.Send(new("step",sessions.Frame!.Revision));sessions.Stop();old.Release();await late;
                    if(sessions.Active || sessions.Frame!=null || sessions.Busy) throw new Exception("Late result repopulated stopped session");
                    if(external)
                    {
                        var existing=System.Diagnostics.Process.GetProcessesByName("Player").Select(p=>p.Id).ToHashSet();
                        await sessions.Start();sessions.SuspendScheduler();
                        var child=System.Diagnostics.Process.GetProcessesByName("Player").Single(p=>!existing.Contains(p.Id));
                        child.Kill(true);await child.WaitForExitAsync();child.Dispose();
                        await sessions.Send(new("frame"));
                        if(sessions.Active || sessions.Frame!=null || !sessions.Status.Contains("故障")) throw new Exception("Crashed Player left an interactive stale frame");
                        await sessions.Start();await sessions.Send(new("pause"));
                        if(sessions.Frame==null) throw new Exception("Player crash prevented restart");sessions.Stop();
                    }
                    author.Undo();author.Undo();
                }
                if(Wire.Json(recordings[0].Log)!=Wire.Json(recordings[1].Log)) throw new Exception("Play modes differ");
                if(documents.Current.Snapshot().Fingerprint!=initial) throw new Exception("Play changed author state");
                await app.DisposeAsync();if(DesktopViewport.LiveDevices!=0) throw new Exception("Devices leaked");
            }
            catch(Exception e) {errors.Add(e.ToString());}
            finally
            {
                File.WriteAllText(Path.GetFullPath(output),Wire.Json(new {success=errors.Count==0,errors,commands=recordings.Select(r=>r.Log.EntryCount).ToArray(),devices=DesktopViewport.LiveDevices}));
                await host.Application.DisposeAsync();host.Window.Close();
            }
        };
        return host.Window;
    }
    internal static void Select(EditorApplication app,string id)
    {
        var tabs=app.Service<WorkspaceShell>("workspace").Tabs;
        tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,id));
    }
    private sealed class DelayedSession(IDebugSession inner) : IDebugSession
    {
        private readonly TaskCompletionSource release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release()=>release.TrySetResult();
        public async Task<DebugReply> SendAsync(DebugRequest request) {if(request.Operation=="step") await release.Task;return await inner.SendAsync(request);}
        public void Dispose()=>inner.Dispose();
    }
}
