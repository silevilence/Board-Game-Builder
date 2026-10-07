namespace BoardGame.Kernel;

public sealed partial class GameEngine
{
    private static void ApplySecret(GameState state,string actor,Command command,bool ruleEffect,bool submittedInBatch)
    {
        void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition) {if(!condition) throw new CommandRejected("secret-condition-not-met");}
        if(command.Kind=="secret-open")
        {
            Require(ruleEffect && (state.Secret==null || state.Secret.Status=="settled"));
            var d=command.Collection;
            Require(d!=null && !string.IsNullOrWhiteSpace(d.Id) && d.Participants!=null && d.Participants.Count>0 && d.Participants.Count<=32);
            Require(d!.Participants.Distinct().Count()==d.Participants.Count && d.Participants.All(p=>state.Seats.Any(s=>s.Id==p)));
            Require(d.Minimum<=d.Maximum && d.CollectionSteps is >0 and <=4096 && d.Missing is "abstain" or "default" && d.Reveal is "all-locked" or "closed");
            Require(d.Missing!="default" || d.DefaultValue>=d.Minimum && d.DefaultValue<=d.Maximum);
            state.Secret=new() {Declaration=Wire.Copy(d),Coordinator=actor,Remaining=d.CollectionSteps};
            state.CurrentSeat=state.Seats.FindIndex(s=>s.Id==d.Participants[0]);return;
        }
        var round=state.Secret;Require(round!=null);
        var declaration=round!.Declaration;
        switch(command.Kind)
        {
            case "secret-submit":
            case "secret-abstain":
                Require(round.Status=="collecting" && round.Remaining>0 && declaration.Participants.Contains(actor) && !round.Locked.Contains(actor));
                Require(!round.Choices.ContainsKey(actor) || declaration.AllowReplace);
                Require(command.Kind=="secret-abstain" || command.Value>=declaration.Minimum && command.Value<=declaration.Maximum);
                round.Choices[actor]=command.Kind=="secret-abstain"?null:command.Value;
                if(command.Kind=="secret-abstain") round.Locked.Add(actor);
                round.Remaining--;Schedule();break;
            case "secret-lock":
                Require(round.Status=="collecting" && (round.Remaining>0 || submittedInBatch) && declaration.Participants.Contains(actor) && round.Choices.ContainsKey(actor) && !round.Locked.Contains(actor));
                round.Locked.Add(actor);Schedule();break;
            case "secret-tick":
                Require(actor==round.Coordinator && round.Status=="collecting" && round.Remaining>0);
                round.Remaining--;Schedule();break;
            case "secret-close":
                Require(ruleEffect && actor==round.Coordinator && round.Status=="collecting" && (round.Remaining==0 || round.Locked.Count==declaration.Participants.Count));
                foreach(var seat in declaration.Participants.Where(s=>!round.Locked.Contains(s)))
                {
                    round.Choices[seat]=declaration.Missing=="default"?declaration.DefaultValue:null;round.Locked.Add(seat);
                }
                round.Status="closed";Schedule();break;
            case "secret-reveal":
                Require(ruleEffect && actor==round.Coordinator && (round.Status=="closed" || declaration.Reveal=="all-locked" && round.Status=="collecting" && round.Locked.Count==declaration.Participants.Count));
                round.Status="revealed";Schedule();break;
            case "secret-settle":
                Require(ruleEffect && actor==round.Coordinator && round.Status=="revealed");round.Status="settled";break;
            default:throw new CommandRejected("unknown-command");
        }
        void Schedule()
        {
            var next=round.Status=="collecting" && round.Remaining>0?declaration.Participants.FirstOrDefault(s=>!round.Locked.Contains(s)):null;
            state.CurrentSeat=state.Seats.FindIndex(s=>s.Id==(next ?? round.Coordinator));
        }
    }
}
