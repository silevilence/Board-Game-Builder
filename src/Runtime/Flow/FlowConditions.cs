using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoardGame.Runtime;

public static class FlowConditions
{
    public static bool? Constant(FlowCondition? condition, IReadOnlyDictionary<string,string>? scripts = null) =>
        Evaluate(condition, _ => null, script => IsFalse(script, scripts) ? false : null);

    public static bool? Evaluate(FlowCondition? c, Func<string,JsonElement?> resolve, Func<FlowScript,bool?> lua)
    {
        if (c == null || c.Op == "true") return true;
        if (c.Op == "false") return false;
        if (c.Op == "lua") return lua(c.Script!);
        if (c.Op == "not") return !Evaluate(c.Items![0], resolve, lua);
        if (c.Op is "all" or "any")
        {
            var unknown=false;
            foreach(var item in c.Items!)
            {
                var value=Evaluate(item,resolve,lua);
                if(c.Op=="all" && value==false)return false;
                if(c.Op=="any" && value==true)return true;
                unknown|=value==null;
            }
            return unknown?null:c.Op=="all";
        }
        var left=Value(c.Left!,resolve);var right=Value(c.Right!,resolve);
        if (left == null || right == null) return null;
        var booleans=left.Value.ValueKind is JsonValueKind.True or JsonValueKind.False && right.Value.ValueKind is JsonValueKind.True or JsonValueKind.False;
        if (left.Value.ValueKind != right.Value.ValueKind && !booleans) throw new InvalidDataException("condition-type: operands must have the same type");
        var kind=left.Value.ValueKind;
        if (kind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Undefined) throw new InvalidDataException("condition-type: scalar required");
        var equal=kind==JsonValueKind.Number ? left.Value.GetDecimal()==right.Value.GetDecimal() : left.Value.ToString()==right.Value.ToString();
        if (c.Op is "eq" or "ne") return c.Op=="eq"?equal:!equal;
        if (kind!=JsonValueKind.Number) throw new InvalidDataException("condition-type: numeric comparison required");
        var order=left.Value.GetDecimal().CompareTo(right.Value.GetDecimal());
        return c.Op switch { "lt"=>order<0,"le"=>order<=0,"gt"=>order>0,"ge"=>order>=0,_=>throw new InvalidDataException("condition-op: unknown operator") };
    }
    private static JsonElement? Value(FlowValue value,Func<string,JsonElement?> resolve)=>value.Path==null?value.Literal:resolve(value.Path);

    public static JsonElement Resolve(JsonElement root,string path)
    {
        var value=root;
        foreach(var segment in path.Split('/').Skip(1))
        {
            var key=segment.Replace("~1","/",StringComparison.Ordinal).Replace("~0","~",StringComparison.Ordinal);
            if(value.ValueKind==JsonValueKind.Object && value.TryGetProperty(key,out var child)) value=child;
            else if(value.ValueKind==JsonValueKind.Array && int.TryParse(key,out var index) && index>=0 && index<value.GetArrayLength()) value=value[index];
            else throw new InvalidDataException("condition-path: value is unavailable");
        }
        return value;
    }
    private static bool IsFalse(FlowScript script,IReadOnlyDictionary<string,string>? scripts)
    {
        if(scripts==null || string.IsNullOrWhiteSpace(script.Path) || string.IsNullOrWhiteSpace(script.Entry) || !scripts.TryGetValue(script.Path,out var source)) return false;
        // Deliberately recognizes only an entire trivial file, never arbitrary Lua bodies/comments.
        return Regex.IsMatch(source,@"\A\s*return\s+false\s*;?\s*\z",RegexOptions.CultureInvariant) ||
            Regex.IsMatch(source,@"\A\s*return\s*\{\s*"+Regex.Escape(script.Entry)+@"\s*=\s*function\s*\(\s*[a-zA-Z_]*\s*\)\s*return\s+false\s*;?\s*end\s*\}\s*;?\s*\z",RegexOptions.CultureInvariant);
    }
}
