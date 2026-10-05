using Avalonia.Controls;
using BoardGame.Modules;
using BoardGame.Runtime;
using BoardGame.View;
namespace BoardGame.Player;
public static class TablePreview
{
    public static Window Create(string path)
    {
        var module=DirectoryModule.Load(path); var session=new PlaySession(module,42);
        session.SubmitHuman("p1",session.Actions()[0].Command);
        var scene=TableScene.From(session.View("p1"),module.ResourceData);
        string? selected=null; var view=new DesktopViewport();
        view.Paint=r=>ScenePainter.Draw(r,scene,selected);
        view.Input+=(kind,x,y)=> { if(kind=="down") selected=scene.Hit(x,y)?.Entity; };
        var window=new Window { Title="Board Game Builder — seat projection",Width=1000,Height=850,Content=view };
        window.Closed+=(_,_)=>view.Dispose(); return window;
    }
}
