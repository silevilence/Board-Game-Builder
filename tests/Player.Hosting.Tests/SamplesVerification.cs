using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using BoardGame.AppRuntime;
using BoardGame.Player;
using BoardGame.Rendering;
using BoardGame.View;
using Sample.Motion;

internal static class SamplesVerification
{
    private static async Task Until(Func<bool> ready,string message)
    {for(var i=0;i<1200;i++){if(ready())return;await Task.Delay(25);}throw new Exception(message);}
    public static async Task Run(PlayerCatalog catalog,ApplicationCatalog motionCatalog,string output)
    {
        var directory=Path.GetDirectoryName(output)!;Directory.CreateDirectory(directory);
        var results=new List<object>();
        foreach(var entry in Directory.GetFiles("samples","game.start.json",SearchOption.AllDirectories).Order())
        {
            var source=Path.GetDirectoryName(entry)!;var name=Path.GetFileName(source);var package=Path.Combine(directory,name+".bgm");
            // The same installed content catalog decides which package protocol applies.
            var supports=motionCatalog.Content.Plugins.Where(p=>p.Provides.Contains("definition")).Select(p=>p.Id).ToHashSet();
            using var startup=JsonDocument.Parse(File.ReadAllText(entry));
            var application=startup.RootElement.GetProperty("plugins").EnumerateArray().Any(p=>supports.Contains(p.GetProperty("id").GetString()!));
            if(application)GamePackage.Pack(source,package,motionCatalog.Content);
            else BoardGame.Runtime.GamePackage.Pack(source,package);
            foreach(var path in new[]{source,package})
            {
                long frames;string kind;
                await using(var window=await catalog.OpenAsync(path,new(path)))
                {
                    window.Window.Show();
                    if(window.Window is ApplicationWindow realtime)
                    {
                        await realtime.Ready;
                        await Until(()=>realtime.Error!=null || realtime.Viewport?.Frames>2,name+": no realtime frame");
                        if(realtime.Error!=null)throw new Exception(realtime.Error);
                        var state=realtime.Instance!.Service<MotionState>("motion.state");
                        realtime.Viewport!.Submit(new("key-down",Key:"right"));
                        await Until(()=>state.Snapshot.Reached,name+": input did not reach target");
                        realtime.Viewport.Submit(new("key-up",Key:"right"));
                        frames=realtime.Viewport.Frames;kind="continuous-input-target";
                    }
                    else
                    {
                        PlayBoard? play=null;LocalBoard? local=null;
                        await Until(()=>
                        {
                            play=window.Window.GetLogicalDescendants().OfType<PlayBoard>().FirstOrDefault();
                            local=window.Window.GetLogicalDescendants().OfType<LocalBoard>().FirstOrDefault();
                            return play!=null || local!=null;
                        },name+": no board");
                        if(local!=null)
                        {
                            var handoff=local.GetLogicalDescendants().OfType<Button>().SingleOrDefault(b=>b.Content is string text && text.StartsWith("请交给 ",StringComparison.Ordinal));
                            handoff?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            InteractiveTable? shown=null;
                            await Until(()=>(shown=local.GetLogicalDescendants().OfType<InteractiveTable>().FirstOrDefault())?.RenderedFrames>2,name+": local table did not render");
                            frames=shown!.RenderedFrames;
                            shown.SaveImage(Path.Combine(directory,name+(path==source?"-directory":"-package")+".png"));
                            var action=local.GetLogicalDescendants().OfType<Button>().First(b=>b.Content is TextBlock && b.IsEffectivelyVisible && b.IsEnabled);
                            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await Until(()=>local.GetLogicalDescendants().OfType<Button>().Any(b=>b.IsEnabled && (b.Content is TextBlock || b.Content is string text && text.StartsWith("请交给 ",StringComparison.Ordinal))),name+": local action did not complete");
                            if(local.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text?.StartsWith("对局故障：",StringComparison.Ordinal)==true))throw new Exception(name+": local session faulted");
                            kind="hotseat-handoff-and-action";
                        }
                        else
                        {
                            var board=play ?? throw new Exception(name+": board disappeared");
                            var setup=board.GetLogicalDescendants().OfType<PlayerSetupView>().SingleOrDefault();
                            for(var step=0;setup?.IsVisible==true && step<32;step++)
                            {
                                var previous=setup.CurrentStep;
                                var next=setup.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="SetupNext");
                                await Until(()=>next.IsEnabled,name+": setup cannot advance");
                                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                await Until(()=>!setup.IsVisible || setup.CurrentStep!=previous,name+": setup did not advance");
                            }
                            if(setup?.IsVisible==true)throw new Exception(name+": setup exceeded navigation limit");
                            var table=board.GetLogicalDescendants().OfType<InteractiveTable>().Single();
                            await Until(()=>table.RenderedFrames>2 && (board.Frame.Actions.Count>0 || board.Frame.View.Ended || board.Frame.Fault!=null),name+": no playable frame");
                            if(board.Frame.Fault!=null)throw new Exception(name+": "+board.Frame.Fault.Detail);
                            await Until(()=>!table.AnimationBusy,name+": presentation did not finish");
                            var revision=board.Frame.Revision;
                            if(!board.Frame.View.Ended)
                            {
                                var action=board.Frame.Actions[0];
                                var item=table.CurrentScene.Items.LastOrDefault(i=>i.Action=="offer:"+action.Id && table.CurrentScene.Hit(i.Bounds.X+i.Bounds.Width/2,i.Bounds.Y+i.Bounds.Height/2)==i);
                                if(item!=null)
                                {
                                    var x=item.Bounds.X+item.Bounds.Width/2;var y=item.Bounds.Y+item.Bounds.Height/2;
                                    table.Pointer("down",x,y);table.Pointer("up",x,y);
                                }
                                else await board.Act(new("action",Action:action.Id),revision);
                                await Until(()=>board.Frame.Fault!=null || board.Frame.Revision>revision,name+": action did not advance");
                                if(board.Frame.Fault!=null)throw new Exception(name+": "+board.Frame.Fault.Detail);
                            }
                            await Until(()=>!table.AnimationBusy,name+": animation did not finish");
                            table.SaveImage(Path.Combine(directory,name+(path==source?"-directory":"-package")+".png"));
                            frames=table.RenderedFrames;kind="board-action";
                        }
                    }
                }
                await Until(()=>WindowsDxBackend.LiveDevices==0 && WindowsDxBackend.LiveBackends==0 && PlatformViewport.LiveNativeWindows==0,name+": graphics retained");
                results.Add(new{sample=name,source=path,frames,kind,devices=0,backends=0,nativeWindows=0});
                File.WriteAllText(output+".samples.json",JsonSerializer.Serialize(new{success=false,completed=results}));
            }
        }
        File.WriteAllText(output+".samples.json",JsonSerializer.Serialize(new{success=true,completed=results}));
    }
}
