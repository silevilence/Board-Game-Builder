using System.Diagnostics;
using System.Net;
using BoardGame.Launcher;

internal static class MaintenanceChecks
{
    public static async Task Run(string root,byte[] fixture)
    {
        Directory.CreateDirectory(root);var registry=new InstallationRegistry(Path.Combine(root,"settings"));
        using var client=new HttpClient(new Components(Path.Combine(root,"components")));var installer=new InstallationService(registry,client);
        var first=InstallationChecks.Make(root,"0.2.0",fixture,true);var second=InstallationChecks.Make(root,"0.2.1",fixture,true);
        await installer.Import(first,first+".sha256");await installer.Import(second,second+".sha256");
        var execution=new ToolExecution(registry);var maintenance=new InstallationMaintenance(registry);var repair=new InstallationRepair(registry,client);
        var relay=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase)) relay.ArgumentList.Add(typeof(MaintenanceChecks).Assembly.Location);
        relay.ArgumentList.Add("--stdio-relay");relay.ArgumentList.Add(registry.Home);
        using(var child=Process.Start(relay)!)
        {
            var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
            await child.StandardInput.WriteLineAsync("fixture input with spaces");child.StandardInput.Close();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert(child.ExitCode==37&&(await output).Contains("fixture input with spaces")&&(await error).Contains("fixture-error"),"nested redirected stdin/stdout/stderr and exit code");
        }
        Assert(await execution.Run("0.2.0","cli",["/d","/c","exit 37"])==37,"child exit code");
        using(var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
            Assert(await execution.Run("0.2.0","cli",["/d","/c","ping -n 120 127.0.0.1 > nul"],cancel.Token)==130,"cancelled child tree returns 130");
        var a=registry.Read().Installations.Single(i=>i.Version=="0.2.0");var b=registry.Read().Installations.Single(i=>i.Version=="0.2.1");
        using(var running=execution.Start("0.2.1","cli",["/d","/c","ping -n 120 127.0.0.1 > nul"]))
        {
            try
            {
                var restarted=new InstallationMaintenance(new InstallationRegistry(registry.Home));
                await Reject(()=>restarted.Uninstall("0.2.1"));Assert(!running.HasExited,"running version protected after launcher restart");
                var removed=await maintenance.Uninstall("0.2.0");Assert(removed.ChooseDefault && registry.Read().DefaultVersion==null && !running.HasExited,"other version removal keeps active shared image");
                await installer.Import(first,first+".sha256");Assert(registry.Read().DefaultVersion==null,"reinstall does not silently choose a default");
                var store=new ContentStore(registry.Read().StorageRoot);var manifest=ProductManifest.Read(Path.Combine(a.Directory,"toolkit.json"));
                var shared=manifest.Files.Single(f=>f.Path==".payload/coreclr.dll");
                File.SetAttributes(store.ObjectPath(shared.Sha256),FileAttributes.Normal);File.WriteAllText(store.ObjectPath(shared.Sha256),"controlled corruption");
                await Reject(()=>repair.Repair("0.2.0"));Assert(!running.HasExited,"shared repair refuses running other version");
            }
            finally {if(!running.HasExited) running.Kill(true);await running.WaitForExitAsync();}
        }
        var repaired=await repair.Repair("0.2.0");Assert(repaired.Versions.Length==2 && repaired.ReplacedObjects==1 && registry.Inspect().All(s=>s.State=="available"),"verified shared replacement repairs all aliases");
        var extra=Path.Combine(a.Directory,"user-project");Directory.CreateDirectory(extra);File.WriteAllText(Path.Combine(extra,"game.start.json"),"user data");
        await Reject(()=>maintenance.Uninstall("0.2.0"));Assert(File.Exists(Path.Combine(extra,"game.start.json")),"user project protected");File.Delete(Path.Combine(extra,"game.start.json"));Directory.Delete(extra);
        var held=Path.Combine(a.Directory,"docs","author","entry.md");
        using(var locked=new FileStream(held,FileMode.Open,FileAccess.Read,FileShare.None)) await Reject(()=>maintenance.Uninstall("0.2.0"));
        await installer.Recover();await repair.Repair("0.2.0");Assert(registry.Inspect().All(s=>s.State=="available"),"partial failure restored and repaired");
        await maintenance.Uninstall("0.2.0");Assert(registry.Inspect().Single().State=="available","other version preserved after unlink");
        await maintenance.Uninstall("0.2.1");Assert(!Directory.EnumerateFiles(Path.Combine(registry.Read().StorageRoot,"objects")).Any(),"last reference collection");
        // Simulate a crash after directory publication, then a direct launch before registration recovery.
        var pending=(await installer.Import(first,first+".sha256")).Installation;
        using(var running=execution.Start("0.2.0","cli",["/d","/c","ping -n 120 127.0.0.1 > nul"]))
        {
            try
            {
                var settings=registry.Read();
                File.WriteAllText(Path.Combine(registry.Home,"settings.json"),System.Text.Json.JsonSerializer.Serialize(settings with {Installations=[],DefaultVersion=null},new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
                var transaction=Path.Combine(settings.StorageRoot,"transactions",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(transaction);
                File.WriteAllText(Path.Combine(transaction,"journal.json"),System.Text.Json.JsonSerializer.Serialize(new {pending.Version,pending.ManifestHash}));
                await Reject(()=>installer.Recover());Assert(!running.HasExited&&Directory.Exists(pending.Directory),"interrupted install recovery preserves direct process");
            }
            finally {if(!running.HasExited) running.Kill(true);await running.WaitForExitAsync();}
        }
        var notes=Path.Combine(pending.Directory,"user-notes.txt");File.WriteAllText(notes,"preserve");
        await Reject(()=>installer.Recover());Assert(File.ReadAllText(notes)=="preserve","recovery retains newly created user data");File.Delete(notes);
        await installer.Recover();Assert(!Directory.Exists(pending.Directory),"stopped owned pending install recovered");
        var partial=Path.Combine(registry.Read().StorageRoot,"transactions",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(partial);File.WriteAllText(Path.Combine(partial,"journal.json.tmp"),"{incomplete");
        await installer.Recover();Assert(!Directory.Exists(partial),"interrupted journal construction recovered");await maintenance.Collect();
        Console.WriteLine("Launcher process/repair/uninstall/shared-reference checks passed.");
    }
    private static void Assert(bool condition,string name) {if(!condition) throw new Exception(name);}
    private static async Task Reject(Func<Task> action) {try {await action();} catch(Exception e) when(e is IOException or InvalidOperationException or InvalidDataException) {return;} throw new Exception("Expected protected maintenance rejection");}
    private sealed class Components(string root):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(File.OpenRead(Path.Combine(root,Path.GetFileName(request.RequestUri!.AbsolutePath))))});
        }
    }
}
