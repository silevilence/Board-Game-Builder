using System.Text.Json;
using BoardGame.Launcher;

try
{
    var arguments=args.ToList(); string? home=null;
    var homeIndex=arguments.IndexOf("--home");
    if(homeIndex>=0) { if(homeIndex+1>=arguments.Count) throw new ArgumentException("--home 缺少目录。"); home=arguments[homeIndex+1]; arguments.RemoveRange(homeIndex,2); }
    var registry=new InstallationRegistry(home);
    object result=arguments.ToArray() switch
    {
        ["status"]=>registry.Inspect(),
        ["settings"]=>registry.Read(),
        ["register",var directory]=>registry.Register(directory),
        ["default",var version]=>SetDefault(registry,version),
        ["storage",var directory]=>SetStorage(registry,directory),
        [] or ["help"]=>new { version=typeof(InstallationRegistry).Assembly.GetName().Version?.ToString(3), commands=new[]{"status","settings","register <directory>","default <version>","storage <directory>"}, option="--home <launcher-settings-directory>" },
        _=>throw new ArgumentException("未知命令。使用 bgb-launcher help 查看用法。")
    };
    Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true})); return 0;
}
catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }

static object SetDefault(InstallationRegistry registry,string version) { registry.SetDefault(version); return registry.Read(); }
static object SetStorage(InstallationRegistry registry,string directory) { registry.SetStorageRoot(directory); return registry.Read(); }
