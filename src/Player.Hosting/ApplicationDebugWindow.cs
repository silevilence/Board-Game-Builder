using Avalonia.Controls;
using Avalonia.Threading;
using BoardGame.AppRuntime;
using BoardGame.Rendering;
using BoardGame.Plugins;

namespace BoardGame.Player;

public sealed class ApplicationDebugWindow : Window
{
    public ApplicationDebugWindow(ApplicationCatalog catalog,GraphicsConfiguration graphics,PluginStore? installation=null,GraphicsBackendPackages? backends=null)
    {
        Title="Player · 应用调试";Width=900;Height=600;
        Content=new TextBlock{Text="等待作者应用快照…",Margin=new(24)};
        var lifetime=new CancellationTokenSource();var finished=false;
        Closing+=(_,e)=>{if(!finished){e.Cancel=true;lifetime.Cancel();}};
        Opened+=async(_,_)=>
        {
            ApplicationViewport? viewport=null;
            PluginCatalogLease? imported=null;
            GraphicsBackendLease? backendLease=null;
            var selectedGraphics=graphics;var approved=GraphicsBackendContract.Extend(catalog);
            try
            {
                await Task.Run(()=>ApplicationDebugProtocol.Serve(approved,async instance=>
                {
                    await Dispatcher.UIThread.InvokeAsync(()=>
                    {
                        lifetime.Token.ThrowIfCancellationRequested();
                        viewport=new(selectedGraphics,instance);Content=viewport;
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
                },lifetime.Token,snapshot=>
                {
                    var startup=snapshot.Identity.Startup;
                    if(startup.Plugins.Any(p=>GraphicsBackendContract.Descriptions.Any(d=>d.Id==p.Id)))
                    {
                        backendLease=(backends ?? throw new InvalidDataException("This debug host has no selectable backend distribution")).Open(startup);
                        selectedGraphics=new(backendLease.Startup,[backendLease.Definition]);
                    }
                    imported=installation?.Open(startup,approved);
                    return imported?.Catalog ?? approved;
                },async()=>
                {
                    var previous=viewport;
                    await Dispatcher.UIThread.InvokeAsync(()=>{previous?.Dispose();Content=null;});
                    if(previous!=null)await previous.Shutdown;
                    viewport=null;
                }));
            }
            catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
            catch(Exception error){await Console.Error.WriteLineAsync(error.ToString());}
            finally
            {
                viewport?.Dispose();Content=null;if(viewport!=null)await viewport.Shutdown;
                imported?.Dispose();
                backendLease?.Dispose();
                finished=true;lifetime.Dispose();Close();
            }
        };
    }
}
