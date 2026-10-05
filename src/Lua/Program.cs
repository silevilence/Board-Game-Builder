using BoardGame.Kernel;
using BoardGame.Lua;
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
