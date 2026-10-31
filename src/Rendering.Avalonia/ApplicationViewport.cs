using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Rendering;

public sealed class ApplicationViewport : PlatformViewport
{
    private readonly RuntimeHost instance;
    private readonly IApplicationView view;
    private readonly string inputSource=Guid.NewGuid().ToString("N");
    public event Action<string>? ApplicationError;
    public ApplicationViewport(GraphicsConfiguration graphics,RuntimeHost instance) : this(graphics,instance,instance.Service<IApplicationView>(ApplicationViewPlugin.Capability)){}
    private ApplicationViewport(GraphicsConfiguration graphics,RuntimeHost instance,IApplicationView view) : base(graphics,Composition(view))
    {
        this.instance=instance;this.view=view;var description=view.Description;
        Background=description.Background;Canvas(description.Width,description.Height,true);
        MapKey=(key,control,alt)=>alt || control?null:key switch {0x25=>"left",0x26=>"up",0x27=>"right",0x28=>"down",0x20=>"space",0x1B=>"escape",_=>null};
        Input+=(kind,x,y)=>{if(kind=="error")ApplicationError?.Invoke(Error ?? "Rendering failed");else Submit(new(kind,x,y));};
        KeyChanged+=(key,pressed)=>Submit(new(pressed?"key-down":"key-up",Key:key));
    }
    private static ViewportComposition Composition(IApplicationView view)
    {
        var plugin=ApplicationViewPlugin.Create(view);
        return new([new RuntimePluginSelection(plugin.Id,plugin.Version,plugin.Api,RuntimeConfiguration.Empty)],[plugin]);
    }
    public void Submit(ViewportInput input)
    {
        if(instance.Driver.Status is ApplicationStatus.Stopping or ApplicationStatus.Stopped or ApplicationStatus.Faulted)return;
        try {if(view.Translate(input with {Source=inputSource}) is {} value)instance.Driver.Enqueue(value.Kind,value.Data);}
        catch(Exception error) {ApplicationError?.Invoke(error.Message);}
    }
    public override void Dispose(){base.Dispose();ApplicationError=null;}
}
