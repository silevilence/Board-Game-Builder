using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
using BoardGame.View;
namespace Engine.Tests;

public static class FreeCellShortcutTests
{
    public static void Run()
    {
        var module=DirectoryModule.Load("samples/freecell");
        using var ui=PlayerSession.CreateAsync(module,1,"p1").Result;
        void Submit(InteractionIntent intent,bool expected=true)
        { Check.That(ui.SubmitAsync(ui.Frame.Revision,intent).Result.Accepted==expected); }
        Submit(new("shortcut",Action:"confirm")); Submit(new("shortcut",Action:"confirm"));
        var input=new TableInteraction(); var gestures=new List<TableGesture>(); input.Gesture+=gestures.Add;
        var scene=TableScene.From(ui.Frame.View,module.ResourceData); input.Show(scene);
        void Click(float x,float y) {input.Pointer("down",x,y); input.Pointer("up",x,y);}
        var top=scene.Items.Last(i=>i.Container=="column-1"); var b=top.Bounds;
        Click(b.X+5,b.Y+5); Check.That(input.Selected==top.Entity && gestures.Count==0);
        Click(b.X+5,b.Y+5); Check.That(input.Selected==null && gestures.Count==0,"Second ordinary click did not cancel");
        Click(b.X+5,b.Y+5); input.Pointer("double-click",b.X+5,b.Y+5); input.Pointer("up",b.X+5,b.Y+5);
        Check.That(gestures.Count==1 && gestures[0].Kind=="double-click","Double click emitted more than one command");
        var offered=ui.Frame.Actions.Single(a=>a.Command.Entity==top.Entity && a.Shortcuts?.Contains("double-click")==true);
        Check.That(Wire.Json(PlayerSession.Resolve(ui.Frame.Actions,new("double-click",top.Entity)))==Wire.Json(offered.Command));
        var old=ui.Frame; Submit(new("double-click",top.Entity));
        Check.That(!ui.SubmitAsync(old.Revision,new("double-click",top.Entity)).Result.Accepted,"Stale duplicate double click accepted");
        Submit(new("shortcut",Action:"undo"));
        scene=TableScene.From(ui.Frame.View,module.ResourceData); input.Show(scene); gestures.Clear();
        Click(b.X+5,b.Y+5); var cell=scene.Items.Single(i=>i.Entity=="cell-1").Bounds; Click(cell.X+5,cell.Y+5);
        Check.That(gestures.Count==1 && gestures[0].Kind=="click" && gestures[0].Entity==top.Entity && gestures[0].Target=="cell-1");
        Submit(new("click",top.Entity,"cell-1")); Submit(new("shortcut",Action:"undo"));
        // Numeric selection and the same destination use the shared click command path.
        input.Show(TableScene.From(ui.Frame.View,module.ResourceData)); gestures.Clear(); input.RegionKey(1);
        Check.That(input.Selected==top.Entity); input.RegionKey(2);
        Check.That(gestures.Count==1 && gestures[0].Target=="column-2");
        // Peek is a presentation-only operation, including for buried cards.
        gestures.Clear(); var buried=scene.Items.First(i=>i.Container=="column-1"); var covered=buried.Bounds;
        var before=Wire.Json(ui.Frame.View);
        input.Pointer("right-down",covered.X+5,covered.Y+5); Check.That(input.Peeked==buried.Entity);
        input.Pointer("move",covered.X+90,covered.Y+90); Check.That(input.Peeked==buried.Entity && gestures.Count==0);
        input.Pointer("right-up",0,0); Check.That(input.Peeked==null && Wire.Json(ui.Frame.View)==before);
        input.Pointer("right-down",covered.X+5,covered.Y+5); input.Pointer("cancel",0,0); Check.That(input.Peeked==null && input.Selected==null);
        input.Pointer("down",b.X+5,b.Y+5); input.Enabled=false; input.Enabled=true; input.Pointer("up",cell.X+5,cell.Y+5);
        Check.That(gestures.Count==0,"Busy state left a pending drag");
        // Below threshold selects; a drag that returns to the origin remains a drag, never a click.
        input.Show(scene); input.Select(null); input.Pointer("down",b.X+5,b.Y+5); input.Pointer("move",b.X+7,b.Y+7); input.Pointer("up",b.X+7,b.Y+7);
        Check.That(gestures.Count==0 && input.Selected==top.Entity);
        input.Pointer("down",b.X+5,b.Y+5); input.Pointer("move",b.X+25,b.Y+5); input.Pointer("move",b.X+5,b.Y+5); input.Pointer("up",b.X+5,b.Y+5);
        Check.That(gestures.Count==1 && gestures[0].Kind=="drag");
        var hiddenScene=new TableScene([new("hidden","back","背面",new(0,0,100,100)),new(null,"back","隐藏",new(120,0,100,100))],new Dictionary<string,byte[]>());
        input.Show(hiddenScene); input.Pointer("right-down",5,5); Check.That(input.Peeked==null);
        input.Pointer("right-down",125,5); Check.That(input.Peeked==null);
        // Illegal shortcuts (buried card / full cell / occupied source cell) never become raw transfers.
        Submit(new("double-click",buried.Entity),false);
        foreach(var command in new[]{new InteractionIntent("shortcut",top.Entity,Action:"cell"),new("shortcut",Action:"undo")}) Submit(command);
        Check.That(TableKeys.Named("D1")=="1" && TableKeys.Named("NumPad8")=="8" && TableKeys.Named("D0")=="cell" && TableKeys.Named("D9")=="home");
        Check.That(TableKeys.Named("Z",true)=="undo" && TableKeys.Named("F10")=="undo" && TableKeys.Named("U")=="undo");
        Check.That(TableKeys.Named("R",true)=="restart" && TableKeys.Named("F2")=="new-game" && TableKeys.Named("F3")=="choose-seed");
        Check.That(TableKeys.Named("Escape")=="cancel" && TableKeys.Named("H")=="hint" && TableKeys.Named("Enter")=="confirm" && TableKeys.Named("F2",alt:true)==null);
        // Every home shortcut wins over the same card's cell offer, across a complete legal game.
        using var fresh=PlayerSession.CreateAsync(module,1,"p1").Result;
        var counts=new Dictionary<string,int>();
        foreach(var command in Wire.Read<List<Command>>(File.ReadAllText("samples/freecell/solution-seed-1.json")))
        {
            var offers=fresh.Frame.Actions; var action=offers.Single(a=>Wire.Json(a.Command)==Wire.Json(command));
            foreach(var home in offers.Where(a=>a.Shortcuts?.Contains("home")==true))
                Check.That(home.Shortcuts!.Contains("double-click"),"Double click did not prioritize foundation");
            var kind=action.Shortcuts?.Contains("double-click")==true?"double-click":action.Gesture=="drag"?"click":"action";
            counts[kind]=counts.GetValueOrDefault(kind)+1;
            Check.That(fresh.SubmitAsync(fresh.Frame.Revision,new(kind,command.Entity,action.Target,Action:action.Id)).Result.Accepted);
        }
        Check.That(fresh.Frame.View.Ended && fresh.Frame.View.Winners.Contains("p1"));
        Console.WriteLine("FreeCell shortcut victory: "+Wire.Json(counts));
    }
}
