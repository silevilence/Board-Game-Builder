// Research replacement appended to the unchanged production executor by run.ps1.
public sealed class WasmCall
{
    private Instance? instance;
    internal WasmCall(Instance instance) => this.instance = instance;
    internal void Close() => instance = null;
    private Instance Active => instance ?? throw new ObjectDisposedException(nameof(WasmCall));
    private Function Require(string export, int parameters)
    {
        var function = Active.GetFunction(export);
        // Preserve the exact integer ABI and reject mismatches before executing guest code.
        if (function == null || function.Parameters.Count != parameters ||
            function.Parameters.Any(kind => kind != ValueKind.Int32) ||
            function.Results.Count != 1 || function.Results[0] != ValueKind.Int32)
            throw new InvalidDataException("Missing WASM export: " + export);
        return function;
    }
    public int ReadInt(string export) => (int)Require(export, 0).Invoke()!;
    public int ApplyInt(string export, int first, int second) => (int)Require(export, 2).Invoke(new ValueBox[] { first, second })!;
}
