using System.Text.Json;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

/// <summary>Pure content preparation, independent of gameplay activation.</summary>
public sealed record PluginContentContract(string[] Fields,Action<JsonElement,RuntimeStartup>? Validate=null,
    string[]? RequiredCapabilities=null,bool Prepare=false,Func<JsonElement,DefinitionSources>? Definition=null,
    Func<JsonElement,IEnumerable<string>>? Files=null,Func<JsonElement,string,JsonElement>? SelectSource=null);
public sealed record DefinitionSources(string Source,IReadOnlyDictionary<string,string> Presets);

public static class RuntimeDiscovery
{
    public static RuntimeStartup Validate(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition> catalog)
    {
        var required=(startup.Plugins ?? []).Where(p=>p!=null).SelectMany(p=>catalog.Where(c=>c.Id==p.Id))
            .SelectMany(p=>p.Content?.RequiredCapabilities ?? []).Prepend("definition");
        RuntimeHost.Validate(startup,catalog,required);
        foreach(var selection in startup.Plugins!)
        {
            var plugin=catalog.Single(p=>p.Id==selection.Id);
            var contract=plugin.Content;
            CheckObject(selection.Configuration,contract?.Fields ?? [],selection.Id+".configuration");
            try {contract?.Validate?.Invoke(selection.Configuration,startup);}
            catch(Exception error) when(error is not OutOfMemoryException)
            {throw new InvalidDataException(selection.Id+".configuration: "+error.Message,error);}
        }
        _=Definition(startup,catalog);
        return startup with {Plugins=startup.Plugins.OrderBy(p=>p.Id,StringComparer.Ordinal).ToList()};
    }
    public static (RuntimePluginSelection Selection,DefinitionSources Sources) Definition(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition> catalog)
    {
        var matches=startup.Plugins.Where(p=>catalog.Single(c=>c.Id==p.Id).Provides.Contains("definition")).ToArray();
        if(matches.Length!=1) throw new InvalidDataException("Missing required capability: definition");
        var selection=matches[0];
        var describe=catalog.Single(c=>c.Id==selection.Id).Content?.Definition
            ?? throw new InvalidDataException(selection.Id+": definition content contract missing");
        try
        {
            var sources=describe(selection.Configuration);ContentPath.Validate(sources.Source);
            foreach(var source in sources.Presets.Values) ContentPath.Validate(source);
            return (selection,sources);
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        {throw new InvalidDataException(selection.Id+": "+error.Message,error);}
    }
    public static IEnumerable<string> Files(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition> catalog)
    {
        foreach(var selection in startup.Plugins)
            foreach(var path in catalog.Single(p=>p.Id==selection.Id).Content?.Files?.Invoke(selection.Configuration) ?? [])
            {ContentPath.Validate(path);yield return path;}
    }
    public static RuntimeStartup Preparation(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition> catalog)
    {
        var selected=catalog.Where(p=>startup.Plugins.Any(s=>s.Id==p.Id)).ToArray();
        var preparation=selected.Where(p=>p.Content?.Prepare==true).Select(p=>p.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var plugin in selected.Where(p=>preparation.Contains(p.Id)))
            foreach(var capability in plugin.Requires)
                if(!selected.Any(p=>preparation.Contains(p.Id) && p.Provides.Contains(capability)))
                    throw new InvalidDataException(plugin.Id+": content preparation depends on runtime capability "+capability);
        return startup with {Plugins=startup.Plugins.Where(p=>preparation.Contains(p.Id)).ToList()};
    }
    public static void CheckObject(JsonElement value,IEnumerable<string> fields,string path)
    {
        if(value.ValueKind!=JsonValueKind.Object) throw new InvalidDataException(path+": expected object");
        var names=new HashSet<string>();
        foreach(var property in value.EnumerateObject())
            if(!names.Add(property.Name) || !fields.Contains(property.Name)) throw new InvalidDataException(path+"."+property.Name+": duplicate/unknown configuration");
    }
}

public static class ContentPath
{
    public static void Validate(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || path.Length>240 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') ||
            path.Split('/').Any(p=>p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0))
            throw new InvalidDataException($"{path}: unsafe package path");
    }
}
