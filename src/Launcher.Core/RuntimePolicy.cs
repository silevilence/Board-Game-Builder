using System.Text.Json;

namespace BoardGame.Launcher;

public sealed record FrameworkResolution(string Framework,string RequiredVersion,string ActualVersion,string? Location,string Policy);
public sealed record ProgramRuntime(string Entry,FrameworkResolution[] Frameworks);
public sealed record RuntimeResolution(string Version,string Mode,ProgramRuntime[] Programs);

/// <summary>The package mode is the runtime policy; it is never silently changed when a framework disappears.</summary>
public static class RuntimePolicy
{
    public static CatalogPackage Select(CatalogVersion version,string mode)
    {
        if(version.Diagnostic!=null) throw new InvalidOperationException(version.Diagnostic);
        var packageMode=mode switch {"private"=>"standalone","system"=>"slim",_=>throw new ArgumentException("运行时模式必须为 private 或 system。")};
        var package=version.Packages.SingleOrDefault(p=>p.Mode==packageMode || p.Mode=="legacy" && mode=="private")??throw new InvalidOperationException("此版本未提供所选运行时模式。");
        _=Resolve(package.ReadManifest()); return package;
    }
    public static RuntimeResolution Inspect(InstallationRegistry registry,string version)
    {
        version=ProductManifest.NormalizeVersion(version);
        var item=registry.Read().Installations.SingleOrDefault(i=>i.Version==version)??throw new InvalidOperationException("版本尚未安装。");
        var status=registry.Inspect(item); if(status.State!="available") throw new InvalidOperationException(status.Diagnostic);
        return Resolve(ProductManifest.Read(Path.Combine(item.Directory,"toolkit.json")),item.Directory);
    }
    public static RuntimeResolution Resolve(ProductManifest manifest,string? directory=null,string? entry=null)
    {
        if(manifest.Legacy) return new(manifest.Version,"legacy",[new("原包入口",[new("原包内置 Runtime","旧清单未声明","旧清单未声明",directory,"固定原包")])]);
        var result=new List<ProgramRuntime>();
        foreach(var program in manifest.Json.GetProperty("programs").EnumerateArray())
        {
            if(entry!=null && Text(program,"entry")!=".payload/"+ProductManifest.RequiredEntries[entry]) continue;
            var frameworks=new List<FrameworkResolution>();
            if(manifest.Mode=="slim")
            {
                var resolved=SystemRuntime.Resolve(program);
                foreach(var requirement in program.GetProperty("frameworks").EnumerateArray())
                {
                    var framework=Text(requirement,"framework"); var actual=resolved.Frameworks.Single(f=>f.Name==framework);
                    frameworks.Add(new(framework,Text(requirement,"minimumVersion"),actual.Version,actual.Path,"LatestPatch"));
                }
            }
            else foreach(var requirement in program.GetProperty("frameworks").EnumerateArray())
            {
                var framework=Text(requirement,"framework"); var version=Text(requirement,"privateVersion");
                var library="runtimepack."+framework+".Runtime.win-x64/"+version;
                var representative=framework=="Microsoft.NETCore.App"?"coreclr.dll":"System.Windows.Forms.dll";
                var files=manifest.Json.GetProperty("files").EnumerateArray().Where(f=>Path.GetFileName(Text(f,"path"))==representative && f.GetProperty("libraries").EnumerateArray().Any(l=>l.GetString()==library)).ToArray();
                if(files.Length==0) throw new InvalidDataException("私有框架缺少准确的运行时文件："+library);
                var location=directory==null?null:ProductManifest.SafePath(directory,Text(files[0],"storage"));
                if(location!=null) ProductManifest.VerifyFile(location,files[0].GetProperty("size").GetInt64(),Text(files[0],"sha256"));
                frameworks.Add(new(framework,version,version,location,"ExactPrivate"));
            }
            result.Add(new(Text(program,"entry"),frameworks.ToArray()));
        }
        return new(manifest.Version,manifest.Mode=="slim"?"system":"private",result.ToArray());
    }
    private static string Text(JsonElement value,string key)=>ProductManifest.Text(value,key);
}
