using System.Text.Json;
// Experimental copy of src/Lua.Execution/LuaExecution.cs. Only sandbox setup
// and the final data-only writer differ; budgets and conversion rules remain.
using BoardGame.Kernel;
using MoonSharp.Interpreter;
namespace BoardGame.Lua;
public static class LuaExecution
{
    public static string Invoke(string source,string path,string entry,string input,uint seed=1,LuaCallShape? shape=null)
    {
        shape ??=new();
        try
        {
            LuaSandbox.Initialize();
            var script=new Script(CoreModules.Preset_HardSandbox);
            foreach(var name in new[] { "print","collectgarbage" }) script.Globals.Set(name,DynValue.Nil);
            script.Globals.Get("math").Table.Set("random",DynValue.Nil);
            script.Globals.Get("math").Table.Set("randomseed",DynValue.Nil);
            var random=seed;
            script.Globals.Set("random_index",DynValue.NewCallback((_,args)=>
            {
                var count=args[0].Number;
                if(count<1 || count>1000000 || count!=Math.Floor(count)) throw new ScriptRuntimeException("invalid random bound");
                return DynValue.NewNumber(SeededRandom.Index(ref random,(int)count)+1);
            }));
            var module=Run(script,script.LoadString(source,null,path));
            if(shape.ValidateExports!=null)
            {
                foreach(var required in shape.ValidateExports)
                    if(module.Type!=DataType.Table || module.Table.Get(required).Type!=DataType.Function) throw new InvalidDataException("Missing entry function: "+required);
                return "true";
            }
            if(module.Type!=DataType.Table || module.Table.Get(entry).Type!=DataType.Function) throw new InvalidDataException("Missing entry function.");
            using var document=JsonDocument.Parse(input);
            var output=Run(script,module.Table.Get(entry),FromJson(script,document.RootElement));
            var count=0;
            return LuaJson.Result(ToObject(output,shape.ArrayResult,new HashSet<Table>(),0,ref count,shape.ArrayFields ?? []));
        }
        catch(Exception e) when(e is InterpreterException or InvalidDataException or JsonException)
        { throw new InvalidDataException($"{path}:{entry}: {(e is InterpreterException lua ? lua.DecoratedMessage : e.Message)}",e); }
    }
    private static DynValue Run(Script script,DynValue function,params DynValue[] args)
    {
        var coroutine=script.CreateCoroutine(function).Coroutine;
        coroutine.AutoYieldCounter=10000;
        var result=coroutine.Resume(args);
        for(var i=0;result.Type==DataType.YieldRequest && i<20;i++) result=coroutine.Resume();
        if(result.Type==DataType.YieldRequest) throw new InvalidDataException("Lua instruction budget exceeded.");
        return result;
    }
    private static DynValue FromJson(Script script,JsonElement element)
    {
        switch(element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj=new Table(script);
                foreach(var p in element.EnumerateObject()) obj.Set(p.Name,FromJson(script,p.Value));
                return DynValue.NewTable(obj);
            case JsonValueKind.Array:
                var list=new Table(script);var i=1;
                foreach(var item in element.EnumerateArray()) list.Set(i++,FromJson(script,item));
                return DynValue.NewTable(list);
            case JsonValueKind.String:return DynValue.NewString(element.GetString());
            case JsonValueKind.Number:return DynValue.NewNumber(element.GetDouble());
            case JsonValueKind.True:return DynValue.True;
            case JsonValueKind.False:return DynValue.False;
            default:return DynValue.Nil;
        }
    }
    private static object? ToObject(DynValue value,bool array,HashSet<Table> ancestors,int depth,ref int count,string[] arrayFields)
    {
        if(++count>8192 || depth>24) throw new InvalidDataException("Lua result budget exceeded.");
        switch(value.Type)
        {
            case DataType.Nil:case DataType.Void:return null;
            case DataType.Boolean:return value.Boolean;
            case DataType.Number:
                if(!double.IsFinite(value.Number)) throw new InvalidDataException("Non-finite result.");
                return value.Number;
            case DataType.String:
                if(value.String.Length>16384) throw new InvalidDataException("Lua string result too large.");
                return value.String;
            case DataType.Table:
                var table=value.Table;
                if(!ancestors.Add(table)) throw new InvalidDataException("Cyclic Lua result.");
                object result;
                if(array || table.Length>0)
                {
                    var items=new List<object?>();
                    if(table.Pairs.Any(p=>p.Key.Type!=DataType.Number || p.Key.Number<1 || p.Key.Number>table.Length || p.Key.Number!=Math.Floor(p.Key.Number))) throw new InvalidDataException("Mixed/sparse Lua array.");
                    for(var i=1;i<=table.Length;i++) items.Add(ToObject(table.Get(i),false,ancestors,depth+1,ref count,arrayFields));
                    result=items;
                }
                else
                {
                    var fields=new Dictionary<string,object?>();
                    foreach(var p in table.Pairs)
                    {
                        if(p.Key.Type!=DataType.String) throw new InvalidDataException("Expected string field.");
                        fields.Add(p.Key.String,ToObject(p.Value,arrayFields.Contains(p.Key.String,StringComparer.Ordinal),ancestors,depth+1,ref count,arrayFields));
                    }
                    result=fields;
                }
                ancestors.Remove(table);return result;
            default:throw new InvalidDataException("Lua result must contain data only.");
        }
    }
}
