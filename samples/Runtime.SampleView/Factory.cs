using BoardGame.AppRuntime;
using BoardGame.Rendering;
using BoardGame.Runtime;
using Example.RuntimePlugin;

namespace Example.RuntimeView;

public static class Factory
{
    public static ApprovedApplicationPlugin Create()=>new(_=>new("sample.view","1",1,[ApplicationViewPlugin.Capability],["sample.counter"],scope=>
        scope.Provide(ApplicationViewPlugin.Capability,new View(scope.Service<Counter>("sample.counter")))));
    private sealed class View(Counter counter):IApplicationView
    {
        public ApplicationViewDescription Description=>new("External runtime sample",640,360,new(18,28,38));
        public ViewInput? Translate(ViewportInput input)=>null;
        public void Draw(IDraw2D drawing,RenderFrame frame)
        {
            drawing.Text("External runtime sample",30,40,new(235,240,245),24);
            drawing.Text("Updates: "+counter.Ticks,30,100,new(140,220,180),22);
            drawing.Box(new(30+counter.Ticks%500,170,24,24),new(235,195,100));
        }
    }
}
