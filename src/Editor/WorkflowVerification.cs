using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
using BoardGame.View;
namespace BoardGame.Editor;

public sealed partial class PlayPanel
{
    internal async Task VerifyControls()
    {
        external.IsChecked=false; await Start(); timer.Stop();
        if(frame?.View.Seat!="renamed" || !cpuSeats.ContainsKey("renamed") || cpuSeats.ContainsKey("p1")) throw new InvalidDataException("Seat controls were stale");
        cpuSeats["renamed"].IsChecked=true; await Start(); timer.Stop();
        var delayed=new DelayedDebugSession(session!); session=delayed;
        var command=Send(new("cpu",frame!.Revision)); await Send(new("pause"));
        delayed.Release(); await command;
        if(frame?.Paused!=true || pendingPause) throw new InvalidDataException("Pause during command was lost");
        Stop(); if(lastReply!=null || timeline.Text!="" || table.InputEnabled) throw new InvalidDataException("Stopped session retained data or input");
    }
    private sealed class DelayedDebugSession(IDebugSession inner) : IDebugSession
    {
        private readonly TaskCompletionSource release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release()=>release.TrySetResult();
        public async Task<DebugReply> SendAsync(DebugRequest request) { if(request.Operation=="cpu") await release.Task; return await inner.SendAsync(request); }
        public void Dispose()=>inner.Dispose();
    }
    internal async Task<SessionRecording> VerifyPlay(bool independent)
    {
        external.IsChecked=independent;
        await Start();
        if(session==null || frame==null) throw new InvalidDataException(status.Text);
        await Send(new("pause"));
        await Send(new("submit",frame.Revision,new(){Kind="invalid"}));
        if(lastReply?.Result?.Accepted!=false) throw new InvalidDataException("Illegal action was not recorded");
        var startFrames=table.RenderedFrames;
        for(var i=0;i<500 && frame is {View.Ended:false,Fault:null};i++)
        {
            if(frame.Cpu) await Send(new("step",frame.Revision));
            else if(frame.Actions.Count>0) await Send(new("submit",frame.Revision,frame.Actions[0].Command));
            else throw new InvalidDataException("No available human action");
            await Task.Delay(20);
        }
        if(frame?.View.Ended!=true || frame.Fault!=null) throw new InvalidDataException(status.Text);
        await Task.Delay(200);
        if(table.RenderedFrames<=startFrames) throw new InvalidDataException("MonoGame viewport did not render");
        var final=frame.StateHash;
        var recording=(await session.SendAsync(new("recording"))).Recording!;
        await Send(new("replay"));
        if(frame.StateHash!=final || !frame.IsReplay) throw new InvalidDataException("Replay differs from live play");
        Stop(); return recording;
    }
}

public sealed partial class EditorWindow
{
    internal async Task VerifyWorkflow(string outputDirectory,string release)
    {
        var output=Path.GetFullPath(outputDirectory); Directory.CreateDirectory(output);
        try
        {
            var author=document ?? throw new InvalidDataException("Module did not open");
            source.Text+="\n "; var draft=source.Text;
            author.EditText("rules.lua",author.Text("rules.lua")+"\n-- draft preservation probe\n");
            if(source.Text!=draft) throw new InvalidDataException("Configuration draft was lost");
            ApplySource(); author.Undo(); author.Undo();
            source.Text+="\n "; draft=source.Text;
            author.EditConfiguration(c=>c["version"]="conflict-probe");
            var conflict=false; try { ApplySource(); } catch(InvalidOperationException) { conflict=true; }
            if(!conflict || source.Text!=draft) throw new InvalidDataException("Configuration conflict lost draft");
            source.Text=sourceBase; RefreshDocument(); author.Undo();
            var temporary=ModuleDocument.Create(); SetDocument(temporary);
            temporary.EditConfiguration(c=>c["seats"]![0]!["id"]="renamed");
            await play.VerifyControls(); SetDocument(author);
            var original=author.Snapshot().Fingerprint;
            Authoring.ApplyYaml(author,AuthorConfiguration.ToYaml(author.Configuration));
            if(author.Snapshot().Fingerprint!=original) throw new InvalidDataException("YAML changed module");
            author.EditConfiguration(c=>c["version"]="1.0.0-editor-validation"); var changed=author.Snapshot().Fingerprint;
            author.Undo(); if(author.Snapshot().Fingerprint!=original) throw new InvalidDataException("Undo differs"); author.Redo();
            if(author.Snapshot().Fingerprint!=changed) throw new InvalidDataException("Redo differs");
            author.EditText("rules.lua",author.Text("rules.lua")+"\n-- edited in editor workflow verification\n");
            author.EditText("cpu.lua",author.Text("cpu.lua")+"\n-- edited CPU in editor workflow verification\n");
            author.Save(Path.Combine(output,"author")); var fingerprint=author.Snapshot().Fingerprint;
            SetDocument(ModuleDocument.Open(author.DirectoryPath!));
            if(document!.Snapshot().Fingerprint!=fingerprint) throw new InvalidDataException("Reopen differs");
            for(var cycle=0;cycle<3;cycle++) { plugins.Disable("official.workspace"); if(plugins.Extensions.Count!=0) throw new InvalidDataException("Plugin extensions leaked"); plugins.Enable("official.workspace"); }
            tabs.SelectedIndex=1; await Task.Delay(300);
            var local=await play.VerifyPlay(false); var independent=await play.VerifyPlay(true);
            if(Wire.Json(local.Log)!=Wire.Json(independent.Log)) throw new InvalidDataException("Play modes differ");
            if(document.Snapshot().Fingerprint!=fingerprint) throw new InvalidDataException("Play changed author document");
            File.WriteAllText(Path.Combine(output,"in-process.json"),Wire.Json(local)); File.WriteAllText(Path.Combine(output,"independent.json"),Wire.Json(independent));
            await Task.Run(()=>PlayerDistribution.Export(release,document.DirectoryPath!,Path.Combine(output,"export")));
            File.WriteAllText(Path.Combine(output,"result.json"),Wire.Json(new {success=true,module=document.Snapshot().Id,fingerprint,commands=local.Log.EntryCount,plugins=plugins.Active.ToArray(),samePlayLog=true,replay=true,initialUnchanged=true}));
        }
        catch(Exception e) {File.WriteAllText(Path.Combine(output,"result.json"),Wire.Json(new {success=false,error=e.ToString()}));}
        finally {allowClose=true;Close();}
    }
}
