using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace BoardGame.Player;
public static class PlayerCli
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding=System.Text.Encoding.UTF8;
        try
        {
            if(args.Length==0 || args[0]=="--help")
            {
                Console.WriteLine("Board Game Builder M0\nbatch <module-dir|snapshot.json> [--seed N] [--count N] [--out directory] [--max-steps N]\nplay <module-dir> [--seed N] [--cpu p2,p3] [--human p1] [--log file] [--max-steps N]\nreplay <module-dir> <log-file> [--seat p1]\n输入动作编号，或 q 退出。日志含权威状态，仅用于作者诊断。" );return 0;
            }
            if(args[0]=="quit") return 0;
            if(args[0] is not ("play" or "replay" or "batch")) throw new ArgumentException("Unknown command. Use --help.");
            if(args.Length<2) throw new ArgumentException("Module directory required.");
            var module=File.Exists(args[1]) ? ModuleSnapshot.Load(args[1]) : DirectoryModule.Load(args[1]);
            if(args[0]=="batch")
            {
                var options=Options(args,2,["--seed","--count","--out","--max-steps"]);
                var results=BatchRunner.Run(module,uint.Parse(options.GetValueOrDefault("--seed","1")),int.Parse(options.GetValueOrDefault("--count","20")),
                    options.GetValueOrDefault("--out",Path.Combine("artifacts","batch",Guid.NewGuid().ToString())),int.Parse(options.GetValueOrDefault("--max-steps","500")),
                    r=>Console.WriteLine($"seed={r.Seed} success={r.Success} winners={string.Join(',',r.Winners)} steps={r.Steps} error={r.Error ?? "none"} rerun={r.RerunEqual} replay={r.ReplayEqual} log={r.LogPath}"));
                return results.All(r=>r.Success)?0:1;
            }
            if(args[0]=="replay")
            {
                if(args.Length<3) throw new ArgumentException("Log path required.");
                var options=Options(args,3,["--seat"]);
                var log=Wire.Read<ReplayLog>(File.ReadAllText(args[2]));
                var engine=GameEngine.Replay(module,log);var seat=options.GetValueOrDefault("--seat",module.Initial.Seats[0].Id);
                Display(engine.Project(seat));Console.WriteLine("Replay verified.");return 0;
            }
            var flags=Options(args,2,["--seed","--cpu","--human","--log","--max-steps"]);
            var seed=uint.Parse(flags.GetValueOrDefault("--seed","1"));
            var controllers=new Dictionary<string,string>();
            foreach(var kind in new[] { "cpu","human" })
                if(flags.TryGetValue("--"+kind,out var seats)) foreach(var seat in seats.Split(','))
                    if(!controllers.TryAdd(seat,kind)) throw new ArgumentException("Duplicate seat override.");
            var session=new PlaySession(module,seed,controllers,int.Parse(flags.GetValueOrDefault("--max-steps","500")));
            string? observer=module.Initial.Seats.FirstOrDefault(s=>controllers.GetValueOrDefault(s.Id,s.Controller)=="human")?.Id;
            var logPath=flags.GetValueOrDefault("--log",Path.Combine("artifacts","sessions",Guid.NewGuid()+".json"));
            while(!session.Ended && session.Fault==null)
            {
                if(session.IsCpu) { Console.WriteLine($"CPU {session.CurrentSeat} 正在行动…");session.StepCpu();continue; }
                var seat=session.CurrentSeat;observer=seat;Display(session.View(seat));var actions=session.Actions();
                if(session.Fault!=null) break;
                if(actions.Count==0) { Console.Error.WriteLine("Rules offer no actions. Session stopped.");Save(session,logPath);return 1; }
                for(var i=0;i<actions.Count;i++) Console.WriteLine($"{i+1}. {actions[i].Label}");
                Console.Write("选择动作 [q 退出]: ");var line=Console.ReadLine();
                if(line==null || line.Trim().Equals("q",StringComparison.OrdinalIgnoreCase)) { Save(session,logPath);return 0; }
                if(!int.TryParse(line,out var choice) || choice<1 || choice>actions.Count) { Console.WriteLine("请输入有效编号。");continue; }
                var result=session.SubmitHuman(seat,actions[choice-1].Command);
                if(!result.Accepted) Console.WriteLine("操作被拒绝: "+result.Reason);
            }
            Save(session,logPath);
            if(session.Fault!=null) { Console.Error.WriteLine("Session failed: "+session.Fault.Code);return 1; }
            Display(session.View(observer ?? module.Initial.Seats[0].Id),observer!=null);return 0;
        }
        catch(Exception e) when(e is not OutOfMemoryException) { Console.Error.WriteLine(e.Message);return 2; }
    }
    private static Dictionary<string,string> Options(string[] args,int start,string[] allowed)
    {
        var result=new Dictionary<string,string>();
        for(var i=start;i<args.Length;i+=2)
            if(i+1>=args.Length || !allowed.Contains(args[i]) || !result.TryAdd(args[i],args[i+1])) throw new ArgumentException("Invalid or duplicate option: "+args[i]);
        return result;
    }
    private static void Save(PlaySession session,string path)
    {
        var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full,Wire.Json(session.ExportAuthorityLog()));
        File.WriteAllText(full+".diagnostics.json",Wire.Json(new { session.Fault,diagnostics=session.ExportDiagnostics() }));
        Console.WriteLine("诊断日志: "+full);
    }
    private static void Display(SeatView view,bool showHand=true)
    {
        Console.WriteLine($"席位 {view.Seat} | 当前 {view.CurrentSeat} | 阶段 {view.Phase} | 行动 {view.Turn}");
        foreach(var e in view.Entities)
        {
            if(e.Id.StartsWith("score-")) Console.WriteLine(e.Id+": "+e.Properties.GetValueOrDefault("score"));
            if(showHand && e.Container=="hand-"+view.Seat) Console.WriteLine($"手牌 {e.Id}: {e.Face ?? "背面"}");
            if(e.IsContainer) Console.WriteLine($"{e.Id}: {e.Count} 张");
        }
        if(view.Ended) Console.WriteLine("对局结束。胜者: "+(view.Winners.Count==0?"平局":string.Join(", ",view.Winners)));
    }
}
