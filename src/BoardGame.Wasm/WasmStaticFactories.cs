using BoardGame.AppRuntime;
namespace BoardGame.Runtime;

public static class WasmStaticFactories
{
    public static ApprovedApplicationPlugin Create()
    {
        var board=new BoardApplication();
        return new(content=>WasmRuntimePlugin.Create(()=>board.Definition(content ?? throw new InvalidOperationException("WASM requires bound game content"))));
    }
}
