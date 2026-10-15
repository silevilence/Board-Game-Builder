using System.Text;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

internal sealed record FlowContent(FlowDefinition Definition,SortedDictionary<string,string> Scripts,string Hash)
{
    public static RuntimePluginSelection? Selection(RuntimeStartup startup)=>startup.Plugins.SingleOrDefault(p=>p.Id=="flow");
    public static SortedDictionary<string,byte[]>? Load(string directory,RuntimeStartup startup)
    {
        var selection=Selection(startup);if(selection==null)return null;
        var path=selection.Configuration.GetProperty("path").GetString()!;
        var files=new SortedDictionary<string,byte[]>(StringComparer.Ordinal) {[path]=File.ReadAllBytes(DirectoryModule.SafeFile(directory,path))};
        var graph=FlowFile.Parse(Text(files[path]));
        foreach(var script in FlowFile.Scripts(graph))
            if(!files.ContainsKey(script.Path)) files[script.Path]=File.ReadAllBytes(DirectoryModule.SafeFile(directory,script.Path));
        _=Read(startup,files);return files;
    }
    public static FlowContent Read(RuntimeStartup startup,SortedDictionary<string,byte[]> files)
    {
        var path=Selection(startup)!.Configuration.GetProperty("path").GetString()!;
        var graph=FlowFile.Parse(Text(files[path]));
        var scripts=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var script in FlowFile.Scripts(graph))
        {
            ModulePackage.ValidatePath(script.Path);
            if(!files.TryGetValue(script.Path,out var data)) throw new InvalidDataException("flow: missing script "+script.Path);
            if(data.Length>262144) throw new InvalidDataException("flow: script size limit exceeded");
            scripts[script.Path]=Text(data);
        }
        if(files.Keys.Except(scripts.Keys.Append(path)).Any()) throw new InvalidDataException("flow: unexpected payload");
        FlowValidation.RequireValid(graph,scripts);
        return new(graph,scripts,Wire.Hash(Wire.Json(files.ToDictionary(p=>p.Key,p=>ModulePackage.Hash(p.Value)))));
    }
    private static string Text(byte[] bytes)=>new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
}
