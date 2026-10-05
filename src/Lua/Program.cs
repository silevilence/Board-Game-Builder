using System.Diagnostics;
using BoardGame.Kernel;
using BoardGame.Lua;

if(args.Length==2 && args[0]=="--server" && int.TryParse(args[1],out var parentId))
{
    // A parent that crashes cannot leave an idle or non-yielding worker behind.
    using var parent=Process.GetProcessById(parentId);
    _=Task.Run(async ()=> { await parent.WaitForExitAsync(); Environment.Exit(0); });
    using var input=Console.OpenStandardInput();using var output=Console.OpenStandardOutput();
    while(true)
    {
        LuaResponse response;
        try
        {
            var request=Wire.Read<LuaRequest>(await LuaProtocol.Read(input,LuaProtocol.RequestLimit));
            if(request.Source.Length>262144 || request.Input.Length>2097152) throw new InvalidDataException("Lua input budget exceeded.");
            response=new(true,LuaEvaluator.Invoke(request.Source,request.Path,request.Entry,request.Input,request.Seed),"");
        }
        catch(EndOfStreamException) { return 0; }
        catch(Exception error) { response=new(false,"",error.Message.Length>65536?error.Message[..65536]:error.Message); }
        await LuaProtocol.Write(output,Wire.Json(response),LuaProtocol.ResponseLimit);
        if(!response.Success) return 1;
    }
}
try
{
    var request=Wire.Read<LuaRequest>(Console.In.ReadToEnd());
    var json=LuaEvaluator.Invoke(request.Source,request.Path,request.Entry,request.Input,request.Seed);
    Console.Write(Wire.Json(new LuaResponse(true,json,"")));return 0;
}
catch(Exception error)
{
    Console.Write(Wire.Json(new LuaResponse(false,"",error.Message)));return 1;
}
