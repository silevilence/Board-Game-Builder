using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
using BoardGame.View;
namespace BoardGame.Player;

internal static class LocalTableVerification
{
    public static Window Create(string output)
    {
        var window=new Window {Title="M3 local table verification",Width=1400,Height=900};
        window.Opened+=async (_,_)=>
        {
            var results=new List<object>();LocalBoard? board=null;
            try
            {
                foreach(var mode in Enum.GetValues<LocalTableMode>())
                foreach(var name in new[] {"quick-cards","sealed-auction"})
                foreach(var mixed in new[] {false,true})
                {
                    var module=GameRuntime.Load("samples/"+name);
                    board=await LocalBoard.Create(module,42,module.Initial.Seats.ToDictionary(s=>s.Id,s=>mixed && s.Id==module.Initial.Seats[^1].Id?"cpu":"human"),mode);window.Content=board;await board.PumpCpu();
                    var renders=0;var curtains=0;
                    for(var i=0;i<80 && !board.Frame.Ended;i++)
                    {
                        await Task.Delay(80);
                        for(var wait=0;board.LiveTables>0 && board.RenderedFrames==0 && board.RenderingFailure==null && wait<200;wait++)await Task.Delay(25);
                        if(board.RenderingFailure!=null)throw new Exception(board.RenderingFailure);
                        if(board.LiveTables>0 && board.RenderedFrames==0)throw new Exception("Visible local projection never rendered");
                        renders+=board.RenderedFrames;
                        if(board.Frame.WaitingFor!=null) {curtains++;if(board.LiveTables!=0 || board.Frame.Views.Count!=0) throw new Exception("Private viewport survived handoff");}
                        if(board.Frame.Fault!=null) throw new Exception(board.Frame.Fault.Code);
                        await board.VerifyStep();
                    }
                    if(!board.Frame.Ended || renders==0) throw new Exception("Local GUI did not render/finish");
                    results.Add(new {module=name,mode=mode.ToString(),mixed,renders,curtains,success=true});
                    board.Dispose();window.Content=null;board=null;await Task.Delay(100);
                    if(DesktopViewport.LiveDevices!=0) throw new Exception("Local table graphics device leaked");
                }
                File.WriteAllText(output,Wire.Json(new {success=true,results}));
            }
            catch(Exception error) {File.WriteAllText(output,Wire.Json(new {success=false,error=error.ToString(),results}));}
            finally {board?.Dispose();window.Close();}
        };
        return window;
    }
}
