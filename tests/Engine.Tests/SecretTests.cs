using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class SecretTests
{
    private sealed class Rules : IGameRules
    {
        public IReadOnlyList<ActionOffer> Actions(SeatView view)=>[];
        public RuleDecision Decide(SeatView view,Command command)=>new(true,"",[command]);
    }
    public static void Run()
    {
        var module=DirectoryModule.Load("samples/quick-cards");
        var engine=new GameEngine(module.Initial,42,module.Fingerprint,new Rules());
        Command Open(int steps=6,string missing="abstain",string reveal="all-locked")=>new() {Kind="secret-open",Collection=new("choice",["p1","p2"],0,10,steps,missing,3,true,reveal)};
        Check.That(engine.Submit("p1",Open()).Accepted);
        Check.That(engine.Submit("p2",new() {Kind="secret-submit",Value=7}).Accepted,"Simultaneous seat was rejected");
        Check.That(engine.Project("p1").Secret is {OwnValue:null,Revealed:null});
        Check.That(engine.Project("p2").Secret!.OwnValue==7);
        Check.That(!engine.Submit("p1",new() {Kind="secret-reveal"}).Accepted);
        Check.That(engine.Submit("p2",new() {Kind="secret-submit",Value=8}).Accepted);
        Check.That(engine.Submit("p2",new() {Kind="secret-lock"}).Accepted);
        Check.That(!engine.Submit("p2",new() {Kind="secret-submit",Value=9}).Accepted);
        Check.That(engine.Submit("p1",new() {Kind="secret-submit",Value=2}).Accepted);
        Check.That(engine.Submit("p1",new() {Kind="secret-lock"}).Accepted);
        Check.That(engine.Project("p1").Secret!.Revealed==null,"Lock implicitly revealed secrets");
        Check.That(engine.Submit("p1",new() {Kind="secret-reveal"}).Accepted);
        Check.That(engine.Project("p1").Secret!.Revealed!["p2"]==8);
        Check.That(engine.Submit("p1",new() {Kind="secret-settle"}).Accepted);
        Check.That(!engine.Submit("p1",new() {Kind="secret-settle"}).Accepted);
        var replay=GameEngine.Replay(module,engine.ExportLog());
        Check.That(Wire.Json(replay.ExportAuthorityState())==Wire.Json(engine.ExportAuthorityState()));
        foreach(var missing in new[] {"abstain","default"})
        {
            var deadline=new GameEngine(module.Initial,42,module.Fingerprint,new Rules());
            Check.That(deadline.Submit("p1",Open(1,missing,"closed")).Accepted);
            Check.That(!deadline.Submit("p1",new() {Kind="secret-close"}).Accepted);
            Check.That(deadline.Submit("p1",new() {Kind="secret-tick"}).Accepted);
            Check.That(deadline.Submit("p1",new() {Kind="secret-close"}).Accepted);
            Check.That(!deadline.Submit("p1",new() {Kind="secret-submit",Value=4}).Accepted);
            Check.That(deadline.Submit("p1",new() {Kind="secret-reveal"}).Accepted);
            Check.That(deadline.Project("p1").Secret!.Revealed!["p2"]==(missing=="default"?3:null));
        }
        var source="""
        local function action(v)
          if not v.secret then return {kind='secret-open',collection={id='one',participants={'p1','p2'},minimum=0,maximum=9,collectionSteps=6}} end
          if v.secret.status=='revealed' then return {kind='secret-settle'} end
          if v.secret.status=='settled' then return {kind='finish',winners={'p1'}} end
          if #v.secret.locked==2 then return {kind='secret-reveal'} end
          if v.secret.ownValue then return {kind='secret-lock'} end
          return {kind='secret-submit',value=4}
        end
        return {actions=function(c) return {{id='step',label='step',command=action(c.view)}} end,
          decide=function(c) return {allowed=true,reason='',commands={action(c.view)}} end}
        """;
        var sample=module with {Rules=source,Cpu="return {decide=function(c) return c.actions[1].command end}"};
        sample=sample with {Fingerprint=ModuleSnapshot.Fingerprint(sample)};
        using var session=new PlaySession(sample,42,new Dictionary<string,string> {{"p1","human"},{"p2","cpu"}});
        for(var i=0;i<20 && !session.Ended && session.Fault==null;i++)
            if(session.IsCpu) Check.That(session.StepCpu());else Check.That(session.SubmitHuman(session.CurrentSeat,session.Actions()[0].Command).Accepted);
        Check.That(session.Ended && session.Fault==null,session.Fault?.Detail ?? "Secret round did not end");
        Check.That(Wire.Hash(Wire.Json(session.ExportRecording().Replay().ExportAuthorityState()))==session.AuthorityStateHash());
    }
}
