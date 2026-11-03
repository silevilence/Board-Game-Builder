using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using BoardGame.Player;
using BoardGame.View;

internal static class TeachingSamplesVerification
{
    private static async Task Until(Func<bool> ready,string message)
    {for(var i=0;i<1200;i++){if(ready())return;await Task.Delay(25);}throw new Exception(message);}

    public static async Task Play(string name,PlayBoard board,InteractiveTable table,Window window,string imagePrefix)
    {
        async Task Click(string id)
        {
            var action=board.Frame.Actions.Single(a=>a.Id==id);var revision=board.Frame.Revision;
            var button=board.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content is TextBlock t && t.Text==action.Label);
            await Until(()=>button.IsEnabled && !table.AnimationBusy,id+": button not enabled");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(()=>board.Frame.Fault!=null || board.Frame.Revision>revision,id+": no update");
            if(board.Frame.Fault!=null)throw new Exception(board.Frame.Fault.Detail);
        }
        if(name=="polyomino")
        {
            await Click("rotate");
            // Exercise the actual pointer-to-grid-to-command path, not just action submission.
            var handle=table.CurrentScene.Items.Single(i=>i.Entity=="I-0");
            var x=handle.Bounds.X+handle.Bounds.Width/2;var y=handle.Bounds.Y+handle.Bounds.Height/2;
            var revision=board.Frame.Revision;
            table.Pointer("down",x,y);table.Pointer("move",x-5*64,y);table.Pointer("up",x-5*64,y);
            await Until(()=>board.Frame.Fault!=null || board.Frame.Revision>revision,"Square drag did not submit");
            await Until(()=>board.Frame.Actions.Any(a=>a.Id=="return"),"Square drag did not place the whole piece");
            await Click("select-2");await Click("place-1-0");await Click("return");await Click("place-1-0");
            await Click("select-3");await Click("place-2-0");await Click("select-4");await Click("rotate");await Click("place-1-2");
        }
        else
        {
            await Click("rope");await Click("lamp");
            if(board.Frame.View.Ended)throw new Exception("Equipment selection unexpectedly ended the flow");
            await Click("depart");await Click("coast");
        }
        await Until(()=>board.Frame.View.Ended && !table.AnimationBusy,"Teaching sample did not finish");
        if(board.Frame.Fault!=null || !board.Frame.View.Winners.SequenceEqual(new[]{"p1"}))throw new Exception("Teaching sample did not win");
        table.SaveImage(imagePrefix+"-won.png");
        window.Width=900;window.Height=680;
        var frames=table.RenderedFrames;await Until(()=>table.RenderedFrames>frames+2,"Resize did not render");
        table.SaveImage(imagePrefix+"-resized.png");
    }
}
