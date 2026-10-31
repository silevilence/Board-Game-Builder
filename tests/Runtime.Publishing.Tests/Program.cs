using BoardGame.Publishing;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
foreach(var id in new[]{"lua.rules","lua.cpu","flow"})
{
    Check(!BuiltinScriptSupport.Declaration(id,"1",1).Compatible,id+" incorrectly admitted");
    try{BuiltinScriptSupport.NativeFiles(id,"1",1,"win-x64",Path.GetTempPath());throw new Exception("Unsupported script admitted");}
    catch(InvalidDataException error){Check(error.Message.Contains("MoonSharp"),"Missing transitive reason");}
}
Check(BuiltinScriptSupport.Declaration("wasm","1",1).Targets.SequenceEqual(["win-x64"]),"WASM targets widened");
Check(!BuiltinScriptSupport.Declaration("wasm","2",1).Compatible,"Unknown version admitted");
try{BuiltinScriptSupport.NativeFiles("wasm","1",1,"linux-x64",Path.GetTempPath());throw new Exception("Unverified target admitted");}catch(InvalidDataException){}
var missing=Path.Combine(Path.GetTempPath(),"missing-wasm-"+Guid.NewGuid().ToString("N"));
try{BuiltinScriptSupport.NativeFiles("wasm","1",1,"win-x64",missing);throw new Exception("Missing runtime admitted");}
catch(InvalidDataException error){Check(error.Message.Contains("wasmtime.dll"),"Missing native component not diagnosed");}
Directory.CreateDirectory(missing);
try
{
    File.WriteAllBytes(Path.Combine(missing,"wasmtime.dll"),[1,2,3]);
    try{BuiltinScriptSupport.NativeFiles("wasm","1",1,"win-x64",missing);throw new Exception("Wrong native binary admitted");}
    catch(InvalidDataException error){Check(error.Message.Contains("differs"),"Binary identity not checked");}
}
finally{Directory.Delete(missing,true);}
if(args.Length>0)Check(BuiltinScriptSupport.NativeFiles("wasm","1",1,"win-x64",args[0]).Single().Role=="native","Verified runtime not collected");
Console.WriteLine("PASS script support declarations: fixed version/target, indirect flow dependency, no managed worker fallback, missing native library");
PlannerTests.Run();
BoardPlannerTests.Run();
