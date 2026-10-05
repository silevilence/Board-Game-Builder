using Avalonia.Controls;
using BoardGame.Modules;
using BoardGame.Runtime;
using BoardGame.View;
namespace BoardGame.Player;
public static class TablePreview
{
    public static Window Create(string path)
    {
        var module=DirectoryModule.Load(path);
        var window=new Window { Title="Board Game Builder — seat projection",Width=1100,Height=800 };
        PlayBoard? board=null;
        var closed=false;
        window.Opened+=async (_,_)=> { var session=await PlayerSession.CreateAsync(module,42,module.Initial.Seats[0].Id); if(closed) { session.Stop();return; } board=new(session,module); window.Content=board; };
        window.Closed+=(_,_)=> { closed=true;board?.Dispose(); }; return window;
    }
}
