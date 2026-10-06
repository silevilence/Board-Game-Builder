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
