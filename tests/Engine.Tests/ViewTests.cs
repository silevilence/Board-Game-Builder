using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
using BoardGame.View;
namespace Engine.Tests;
public static class ViewTests
{
    public static void Run()
    {
        var module=DirectoryModule.Load("samples/quick-cards"); var session=new PlaySession(module,42);
        Check.That(session.SubmitHuman("p1",session.Actions()[0].Command).Accepted);
        var projection=session.View("p1"); var scene=TableScene.From(projection);
        var enemy=projection.Entities.Single(e=>e.Id=="hand-p2");
        Check.That(enemy.Count==3 && enemy.Contents.Count==0);
        Check.That(scene.Items.Count(i=>i.Kind=="back")==9);
        Check.That(scene.Items.Where(i=>i.Kind=="card").All(i=>projection.Entities.Any(e=>e.Id==i.Entity && e.Owner=="p1")));
        Check.That(scene.Items.Where(i=>i.Kind=="back").All(i=>i.Entity==null));
        var card=scene.Items.First(i=>i.Kind=="card"); Check.That(scene.Hit(card.Bounds.X+5,card.Bounds.Y+5)==card);
        var visible=new VisibleEntity("art",null,null,false,0,true,"visible",0,0,[],[],"missing");
        projection.Entities.Add(visible); Check.That(TableScene.From(projection).Items.Any(i=>i.Resource=="missing"));
    }
}
