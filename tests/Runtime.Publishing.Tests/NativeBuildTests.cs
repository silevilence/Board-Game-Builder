using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using BoardGame.Publishing;

internal static class NativeBuildTests
{
    internal static async Task Run(PublicationPlan original)
    {
        var directory=Path.GetFullPath("artifacts/native-aot/publishing/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var cordi=Path.GetFullPath("artifacts/native-aot/cordisharp/CordiSharp.dll");
        var plan=original with {Files=original.Files.Select(f=>f.Name=="CordiSharp.dll"?f with {Source=cordi,Sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(cordi)))}:f).ToArray()};
        var missing=await NativeToolchain.Check("win-x64",new(Dotnet:Path.Combine(directory,"missing-dotnet.exe"),VisualStudio:directory,WindowsKits:directory));
        Check(!missing.Available && missing.Diagnostics.Any(d=>d.Code=="toolchain.sdk") && missing.Diagnostics.Any(d=>d.Code=="toolchain.linker") && missing.Diagnostics.Any(d=>d.Code=="toolchain.windows-sdk"),"Incomplete toolchain accepted");
        Check(!(await NativeToolchain.Check("linux-x64")).Available,"Cross-system target silently accepted");
        var entry=new NativeEntry("Example.RuntimePlugin.HeadlessEntry","Run","原生计数样例","1");
        var destination=Path.Combine(directory,"中文 空格 native");
        var options=new NativeBuildOptions(destination,DiagnosticsDirectory:Path.Combine(directory,"logs"));
        var result=await NativeBuildService.Build(plan,entry,options,new InlineProgress(p=>{if(p.Stage!="build" || p.Message.Contains("error",StringComparison.OrdinalIgnoreCase))Console.WriteLine(p.Stage+": "+p.Message);}));
        Check(result.Success,"Native build failed: "+string.Join("; ",result.Diagnostics.Select(d=>d.Message))+"; log="+result.Log);
        using(var process=Process.Start(new ProcessStartInfo(Path.Combine(destination,"Play.exe")){UseShellExecute=false,CreateNoWindow=true})!)
        {await process.WaitForExitAsync();Check(process.ExitCode==0,"Native sample failed");}
        using(var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(destination,"result.json"))))
            Check(!json.RootElement.GetProperty("dynamicCodeSupported").GetBoolean() && json.RootElement.GetProperty("ticks").GetInt32()==3,"Generated native result differs");
        try{await NativeBuildService.Build(plan,entry,options);throw new Exception("Existing output overwritten");}catch(IOException){}
        var failure=await NativeBuildService.Build(plan,entry with {Method="MissingEntry"},options with {Destination=Path.Combine(directory,"failure")});
        Check(!failure.Success && !Directory.Exists(Path.Combine(directory,"failure")) && failure.Diagnostics.Any(d=>d.Code=="build.failed"),"Compiler failure produced a release");
        var incompatible=await NativeBuildService.Build(plan,entry with {Method="ReflectionOnly"},options with {Destination=Path.Combine(directory,"false-aot")});
        Check(!incompatible.Success && !Directory.Exists(Path.Combine(directory,"false-aot")) && File.ReadAllText(incompatible.Log).Contains("IL2026"),"False AOT compatibility bypassed strict compilation");
        using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var started=false;
        var canceled=await NativeBuildService.Build(plan,entry,options with {Destination=Path.Combine(directory,"cancelled")},new InlineProgress(p=>
        {
            if(p.Stage=="build" && p.Message.Contains("Generating native code",StringComparison.OrdinalIgnoreCase)){started=true;cancellation.Cancel();}
        }),cancellation.Token);
        Check(started && !canceled.Success && canceled.Diagnostics.Any(d=>d.Code=="build.cancelled") && !Directory.Exists(Path.Combine(directory,"cancelled")),"Active compilation cancellation failed");
        Check(!Directory.GetDirectories(directory,"*.building-*").Any(),"Failed staging release survived");
        foreach(var file in plan.Files)PublicationPlanner.Verify(file);
        File.WriteAllText(Path.Combine(directory,"verification.json"),JsonSerializer.Serialize(new {success=true,native=destination,build=result,failure,incompatible,cancelled=canceled},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS real native service: toolchain failures, exact SDK, compiled external entry, compiler failure, active cancellation, atomic output and original inputs preserved: "+directory);
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private sealed class InlineProgress(Action<NativeBuildProgress> report):IProgress<NativeBuildProgress>
    {public void Report(NativeBuildProgress value)=>report(value);}
}
