using BoardGame.Kernel;
using BoardGame.View;
namespace Engine.Tests;

public static class BoardActionTests
{
    public static void Run()
    {
        var scene=new TableScene([new("piece","sprite","",new(40,70,80,100))],ResourcePool.Empty);
        var map=new BoardActionMap(800,700,[new("choose","Choose",0,0,0,0,Anchor:"{entity}",Outline:true)],new(600,600,160,40));
        var board=new BoardActions(map);
        var offers=Enumerable.Range(0,14).Select(i=>new ActionOffer("a"+i,"Option "+i,new(){Kind="choose",Entity="piece",Value=i})).ToArray();
        board.Update(scene,offers.Append(new("secret","Secret",new(){Kind="choose",Entity="hidden"})).ToArray());
        Check.That(board.MappedActions.Count==14,"Hidden anchor must not create a target");
        var closed=board.Present(scene,false,null);var region=closed.Items.Last();
        Check.That(region.Bounds==scene.Items[0].Bounds && region.ActionIds!.Count==14);
        Check.That(board.Activate(region.Action!)==null);
        var open=board.Present(scene,false,null);
        Check.That(open.Items.Count(i=>i.Action?.StartsWith("offer:")==true)==6);
        Check.That(open.Hit(5,5)?.Action=="close","Modal must intercept background clicks");
        board.Activate("next");board.Activate("next");board.Activate("next");
        Check.That(board.Present(scene,false,null).Items.Count(i=>i.Action?.StartsWith("offer:")==true)==2);
        Check.That(board.Activate("offer:a13")=="a13" && board.Activate("offer:secret")==null);
        board.Activate("close");Check.That(!board.Present(scene,false,null).Items.Any(i=>i.Kind=="ui-shade"));
        board.Update(scene,[]);Check.That(board.Activate("offer:a13")==null,"Stale offers must not resolve");
        Check.That(board.Present(scene,true,"Return").Items.Last().Action=="host:new-game");
        var pointer=new TableInteraction();var received=new List<TableGesture>();pointer.Gesture+=received.Add;
        pointer.Show(closed);pointer.Pointer("down",50,80);pointer.Pointer("up",50,80);
        Check.That(received.Count==1 && received[0].Action==region.Action);
        pointer.Pointer("double-click",50,80);Check.That(received.Count==1);
        pointer.Pointer("down",50,80);pointer.Pointer("move",80,80);pointer.Pointer("up",50,80);Check.That(received.Count==1,"Drag away and back is not a click");
        pointer.Pointer("down",50,80);pointer.Show(closed);pointer.Pointer("up",50,80);Check.That(received.Count==1,"Scene update invalidates press");
        pointer.Enabled=false;pointer.Pointer("down",50,80);pointer.Pointer("up",50,80);Check.That(received.Count==1);
        var module=TestModules.Load("samples/quick-cards");BoardActions.Validate(map,module);
        Check.Throws(()=>BoardActions.Validate(map with {Width=float.NaN},module));
        Check.Throws(()=>BoardActions.Validate(map with {Regions=[new("x","",0,0,0,0)]},module));
        Check.Throws(()=>BoardActions.Validate(map with {EndRegion=new(799,0,20,20)},module));
    }
}
