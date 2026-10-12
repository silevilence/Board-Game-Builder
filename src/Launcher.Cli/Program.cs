using System.Text.Json;
using BoardGame.Launcher;

try
{
    var arguments=args.ToList(); string? home=null;
    var delimiter=arguments.IndexOf("--");var homeIndex=arguments.Take(delimiter<0?arguments.Count:delimiter).ToList().IndexOf("--home");
    if(homeIndex>=0) { if(homeIndex+1>=arguments.Count) throw new ArgumentException("--home 缺少目录。"); home=arguments[homeIndex+1]; arguments.RemoveRange(homeIndex,2); }
    var registry=new InstallationRegistry(home);
    using var http=new HttpClient {Timeout=TimeSpan.FromSeconds(45)};
    using var cancellation=new CancellationTokenSource();
    Console.CancelKeyPress+=(_,e)=> {e.Cancel=true;cancellation.Cancel();};
    var catalog=new ReleaseCatalog(http,registry.Home);
    var installer=new InstallationService(registry,http);
    var execution=new ToolExecution(registry);var maintenance=new InstallationMaintenance(registry);
    if(arguments.ToArray() is ["run",var runVersion,"--",..var forwarded]) return await execution.Run(runVersion=="default"?null:runVersion,"cli",forwarded,cancellation.Token);
    object result=arguments.ToArray() switch
    {
        ["status"]=>registry.Inspect(),
        ["settings"]=>registry.Read(),
        ["register",var directory]=>registry.Register(directory),
        ["default",var version]=>SetDefault(registry,version),
        ["storage",var directory]=>SetStorage(registry,directory),
        ["catalog"]=>CatalogSummary(catalog.ReadCache(),registry),
        ["catalog","refresh"]=>CatalogSummary(await catalog.Refresh(cancellation.Token),registry),
        ["runtime",var version]=>RuntimePolicy.Inspect(registry,version),
        ["runtime-plan",var version,var mode]=>RuntimePlan(catalog,version,mode),
        ["plan",var version,var mode]=>new ContentStore(registry.Read().StorageRoot).Plan(RuntimePolicy.Select(Find(catalog,version),mode).ReadManifest()),
        ["install",var version,var mode]=>await installer.Install(Find(catalog,version),mode,cancellation:cancellation.Token),
        ["import",var archive,var checksum]=>await installer.Import(archive,checksum,cancellation:cancellation.Token),
        ["recover"]=>await Recover(installer),
        ["storage-report"]=>StorageDiagnostics.Measure(new ContentStore(registry.Read().StorageRoot)),
        ["launch"]=>Launch(execution,null,[]),
        ["launch",var version,..var editorArguments]=>Launch(execution,version,editorArguments),
        ["processes",var version]=>maintenance.Running(version),
        ["uninstall",var version]=>await maintenance.Uninstall(version,cancellation.Token),
        ["collect"]=>new {collectedObjects=await maintenance.Collect(cancellation.Token)},
        ["repair",var version]=>await new InstallationRepair(registry,http).Repair(version,cancellation:cancellation.Token),
        ["repair",var version,"--components",var directory]=>await RepairLocal(registry,version,directory,cancellation.Token),
        [] or ["help"]=>new { version=typeof(InstallationRegistry).Assembly.GetName().Version?.ToString(3), commands=new[]{"status","settings","register <directory>","default <version>","storage <directory>","catalog [refresh]","runtime <version>","runtime-plan <version> <private|system>","plan <version> <private|system>","install <version> <private|system>","import <zip> <sha256-file>","recover","storage-report","launch [version] [editor arguments]","run <version|default> -- <CLI arguments>","processes <version>","uninstall <version>","repair <version> [--components <directory>]","collect"}, option="--home <launcher-settings-directory>" },
        _=>throw new ArgumentException("未知命令。使用 bgb-launcher help 查看用法。")
    };
    Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true})); return 0;
}
catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }

static object SetDefault(InstallationRegistry registry,string version) { registry.SetDefault(version); return registry.Read(); }
static object SetStorage(InstallationRegistry registry,string directory) { registry.SetStorageRoot(directory); return registry.Read(); }
static async Task<object> Recover(InstallationService installer) {await installer.Recover(); return new {recovered=true};}
static object Launch(ToolExecution execution,string? version,string[] arguments) {using var process=execution.Start(version,"editor",arguments);return new {processId=process.Id,startedAt=process.StartTime.ToUniversalTime(),image=process.StartInfo.FileName};}
static async Task<object> RepairLocal(InstallationRegistry registry,string version,string directory,CancellationToken cancellation)
{
    using var client=ComponentArchives.Open(directory);var result=await new InstallationRepair(registry,client).Repair(version,cancellation:cancellation);
    return new {result.Versions,result.ReplacedObjects,source="local-components",readBytes=result.DownloadedBytes};
}
static CatalogVersion Find(ReleaseCatalog catalog,string version)=>catalog.ReadCache().Versions.SingleOrDefault(v=>v.Version==ProductManifest.NormalizeVersion(version))??throw new InvalidOperationException("缓存中没有此版本，请先刷新列表。");
static object RuntimePlan(ReleaseCatalog catalog,string version,string mode)
{
    version=ProductManifest.NormalizeVersion(version);
    var product=catalog.ReadCache().Versions.SingleOrDefault(v=>v.Version==version)??throw new InvalidOperationException("缓存中没有此版本，请先刷新列表。");
    return RuntimePolicy.Resolve(RuntimePolicy.Select(product,mode).ReadManifest());
}
static object CatalogSummary(CatalogSnapshot snapshot,InstallationRegistry registry)=>new {snapshot.CheckedAt,snapshot.Cached,snapshot.Diagnostic,versions=snapshot.Versions.Select(v=>new {v.Version,v.Tag,v.Notes,v.Url,v.Diagnostic,installed=registry.Read().Installations.Any(i=>i.Version==v.Version),packages=v.Packages.Select(p=>new {p.Mode,archive=p.Archive.Name,size=p.Archive.Size,components=p.ReadManifest().Components.Count,runtime=p.ReadManifest().Json.TryGetProperty("programs",out var programs)?programs.EnumerateArray().SelectMany(r=>r.GetProperty("frameworks").EnumerateArray()).Select(r=>r.Clone()).ToArray():[]})})};
