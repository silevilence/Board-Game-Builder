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
        byte[] ReadFile(string file,int limit)
        {
            var full=DirectoryModule.SafeFile(directory,file);
            if(new FileInfo(full).Length>limit) throw new InvalidDataException("flow: file size limit exceeded: "+file);
            return File.ReadAllBytes(full);
        }
        var files=new SortedDictionary<string,byte[]>(StringComparer.Ordinal) {[path]=ReadFile(path,FlowFile.MaxBytes)};
        var graph=FlowFile.Parse(Text(files[path]));
        FlowValidation.RequireValid(graph);
        foreach(var script in FlowFile.Scripts(graph))
        {
            ModulePackage.ValidatePath(script.Path);
            if(!files.ContainsKey(script.Path))
            {
                if(files.Count>=ModuleContentLimits.MaxFiles-1) throw new InvalidDataException("flow: content file count limit exceeded");
                files[script.Path]=ReadFile(script.Path,FlowFile.MaxScriptBytes);
                if(files.Sum(p=>(long)p.Value.Length)>ModuleContentLimits.MaxTotalBytes) throw new InvalidDataException("flow: content size limit exceeded");
            }
        }
        _=Read(startup,files);return files;
    }
    public static FlowContent Read(RuntimeStartup startup,SortedDictionary<string,byte[]> files)
    {
        var path=Selection(startup)!.Configuration.GetProperty("path").GetString()!;
        if(!files.TryGetValue(path,out var graphBytes) || graphBytes==null || graphBytes.Length>FlowFile.MaxBytes || files.Count>ModuleContentLimits.MaxFiles-1 || files.Any(p=>p.Value==null) || files.Sum(p=>(long)p.Value.Length)>ModuleContentLimits.MaxTotalBytes)
            throw new InvalidDataException("flow: missing graph or content limits exceeded");
        var graph=FlowFile.Parse(Text(graphBytes));
        var scripts=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var script in FlowFile.Scripts(graph))
        {
            ModulePackage.ValidatePath(script.Path);
            if(!files.TryGetValue(script.Path,out var data)) throw new InvalidDataException("flow: missing script "+script.Path);
            if(data.Length>FlowFile.MaxScriptBytes) throw new InvalidDataException("flow: script size limit exceeded");
            scripts[script.Path]=Text(data);
        }
        if(files.Keys.Except(scripts.Keys.Append(path)).Any()) throw new InvalidDataException("flow: unexpected payload");
        FlowValidation.RequireValid(graph,scripts);
        return new(graph,scripts,Wire.Hash(Wire.Json(files.ToDictionary(p=>p.Key,p=>ModulePackage.Hash(p.Value)))));
    }
    private static string Text(byte[] bytes)=>new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
}
