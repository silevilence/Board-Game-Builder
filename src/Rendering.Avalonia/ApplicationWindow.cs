using Avalonia.Controls;
using BoardGame.AppRuntime;
using BoardGame.Runtime;

namespace BoardGame.Rendering;

/// <summary>A window owns one instance pump. Additional viewports only consume that instance.</summary>
public sealed class ApplicationWindow : Window
{
    private readonly ApplicationCatalog catalog;
    private readonly GraphicsConfiguration graphics;
    private readonly string source;
    private Task? opening;
    private bool closing, released;
    public RuntimeHost? Instance {get;private set;}
    public ApplicationPump? Pump {get;private set;}
    public ApplicationViewport? Viewport {get;private set;}
    public string? Error {get;private set;}
    public Task Ready=>opening ?? Task.CompletedTask;
    public ApplicationWindow(ApplicationCatalog catalog,GraphicsConfiguration graphics,string source)
    {
        this.catalog=catalog;this.graphics=graphics;this.source=source;
        Title="Player";Width=900;Height=600;
        Content=new TextBlock{Text="Loading…",Margin=new(24)};
        Opened+=(_,_)=>opening=Open();
        Closing+=async(_,e)=>
        {
            if(released)return;
            e.Cancel=true;if(closing)return;closing=true;
            try
            {
                if(opening!=null)await opening;
                if(Pump!=null)await Pump.DisposeAsync();
            }
            catch(Exception error){Error=error.Message;}
            finally
            {
                try {Viewport?.Dispose();Content=null;if(Viewport!=null)await Viewport.Shutdown;}
                catch(Exception error){Error=error.Message;}
                try {if(Instance!=null)await Instance.DisposeAsync();}
                catch(Exception error){Error=error.Message;}
                released=true;Close();
            }
        };
    }
    private async Task Open()
    {
        try
        {
            var content=await Task.Run(()=>catalog.Load(source));
            if(closing)return;
            Instance=await catalog.StartAsync(content);
            if(closing)return;
            var description=Instance.Service<IApplicationView>(ApplicationViewPlugin.Capability).Description;
            Title=description.Title;
            Viewport=new(graphics,Instance);
            Viewport.ApplicationError+=ShowError;
            Content=Viewport;Pump=new(Instance.Driver);
            _=Observe(Pump);
        }
        catch(Exception error){if(!closing)ShowError(error.Message);}
    }
    private async Task Observe(ApplicationPump pump)
    {
        try {await pump.Completion;}
        catch(Exception error){if(!closing)ShowError(error.Message);}
    }
    private void ShowError(string message)
    {
        if(closing)return;
        Error=message;
        if(Instance!=null)Instance.Driver.Paused=true;
        Content=new TextBlock{Text=message,TextWrapping=Avalonia.Media.TextWrapping.Wrap,Margin=new(24)};
    }
}
