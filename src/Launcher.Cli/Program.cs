using System.Text.Json;
using BoardGame.Launcher;

try
{
    var arguments=args.ToList(); string? home=null;
    var homeIndex=arguments.IndexOf("--home");
    if(homeIndex>=0) { if(homeIndex+1>=arguments.Count) throw new ArgumentException("--home 缺少目录。"); home=arguments[homeIndex+1]; arguments.RemoveRange(homeIndex,2); }
    var registry=new InstallationRegistry(home);
    using var http=new HttpClient {Timeout=TimeSpan.FromSeconds(45)};
    using var cancellation=new CancellationTokenSource();
    Console.CancelKeyPress+=(_,e)=> {e.Cancel=true;cancellation.Cancel();};
    var catalog=new ReleaseCatalog(http,registry.Home);
    var installer=new InstallationService(registry,http);
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
        [] or ["help"]=>new { version=typeof(InstallationRegistry).Assembly.GetName().Version?.ToString(3), commands=new[]{"status","settings","register <directory>","default <version>","storage <directory>","catalog [refresh]","runtime <version>","runtime-plan <version> <private|system>","plan <version> <private|system>","install <version> <private|system>","import <zip> <sha256-file>","recover","storage-report"}, option="--home <launcher-settings-directory>" },
        _=>throw new ArgumentException("未知命令。使用 bgb-launcher help 查看用法。")
    };
    Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true})); return 0;
}
catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }

static object SetDefault(InstallationRegistry registry,string version) { registry.SetDefault(version); return registry.Read(); }
static object SetStorage(InstallationRegistry registry,string directory) { registry.SetStorageRoot(directory); return registry.Read(); }
static async Task<object> Recover(InstallationService installer) {await installer.Recover(); return new {recovered=true};}
static CatalogVersion Find(ReleaseCatalog catalog,string version)=>catalog.ReadCache().Versions.SingleOrDefault(v=>v.Version==ProductManifest.NormalizeVersion(version))??throw new InvalidOperationException("缓存中没有此版本，请先刷新列表。");
static object RuntimePlan(ReleaseCatalog catalog,string version,string mode)
{
    version=ProductManifest.NormalizeVersion(version);
    var product=catalog.ReadCache().Versions.SingleOrDefault(v=>v.Version==version)??throw new InvalidOperationException("缓存中没有此版本，请先刷新列表。");
    return RuntimePolicy.Resolve(RuntimePolicy.Select(product,mode).ReadManifest());
}
static object CatalogSummary(CatalogSnapshot snapshot,InstallationRegistry registry)=>new {snapshot.CheckedAt,snapshot.Cached,snapshot.Diagnostic,versions=snapshot.Versions.Select(v=>new {v.Version,v.Tag,v.Notes,v.Url,v.Diagnostic,installed=registry.Read().Installations.Any(i=>i.Version==v.Version),packages=v.Packages.Select(p=>new {p.Mode,archive=p.Archive.Name,size=p.Archive.Size,components=p.ReadManifest().Components.Count,runtime=p.ReadManifest().Json.TryGetProperty("programs",out var programs)?programs.EnumerateArray().SelectMany(r=>r.GetProperty("frameworks").EnumerateArray()).Select(r=>r.Clone()).ToArray():[]})})};
