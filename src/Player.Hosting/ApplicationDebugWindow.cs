using Avalonia.Controls;
using Avalonia.Threading;
using BoardGame.AppRuntime;
using BoardGame.Rendering;

namespace BoardGame.Player;

public sealed class ApplicationDebugWindow : Window
{
    public ApplicationDebugWindow(ApplicationCatalog catalog,GraphicsConfiguration graphics)
    {
        Title="Player · 应用调试";Width=900;Height=600;
        Content=new TextBlock{Text="等待作者应用快照…",Margin=new(24)};
        var lifetime=new CancellationTokenSource();var finished=false;
        Closing+=(_,e)=>{if(!finished){e.Cancel=true;lifetime.Cancel();}};
        Opened+=async(_,_)=>
        {
            ApplicationViewport? viewport=null;
            try
            {
                await Task.Run(()=>ApplicationDebugProtocol.Serve(catalog,async instance=>
                {
                    await Dispatcher.UIThread.InvokeAsync(()=>
                    {
                        lifetime.Token.ThrowIfCancellationRequested();
                        viewport=new(graphics,instance);Content=viewport;
                        Title=instance.Service<IApplicationView>(ApplicationViewPlugin.Capability).Description.Title+" · 作者调试";
                    });
                    var rendered=false;
                    for(var wait=0;wait<300;wait++)
                    {
                        rendered=await Dispatcher.UIThread.InvokeAsync(()=>
                        {
                            if(viewport!.Error!=null)throw new InvalidDataException(viewport.Error);
                            return viewport.Frames>0;
                        });
                        if(rendered)break;await Task.Delay(25,lifetime.Token);
                    }
                    if(!rendered)throw new TimeoutException("Debug Player viewport did not render");
                },lifetime.Token));
            }
            catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
            catch(Exception error){await Console.Error.WriteLineAsync(error.ToString());}
            finally
            {
                viewport?.Dispose();Content=null;if(viewport!=null)await viewport.Shutdown;
                finished=true;lifetime.Dispose();Close();
            }
        };
    }
}
