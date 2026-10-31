using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Platforms;

namespace BoardGame.Lua;

internal static class LuaSandbox
{
    private static readonly object Gate=new();
    private static bool initialized;
    private const DynamicallyAccessedMemberTypes Members=DynamicallyAccessedMemberTypes.PublicMethods|DynamicallyAccessedMemberTypes.PublicFields;
    [DynamicDependency(Members,"MoonSharp.Interpreter.CoreLib.TableIteratorsModule","MoonSharp.Interpreter")]
    [DynamicDependency(Members,"MoonSharp.Interpreter.CoreLib.BasicModule","MoonSharp.Interpreter")]
    [DynamicDependency(Members,"MoonSharp.Interpreter.CoreLib.StringModule","MoonSharp.Interpreter")]
    [DynamicDependency(Members,"MoonSharp.Interpreter.CoreLib.TableModule","MoonSharp.Interpreter")]
    [DynamicDependency(Members,"MoonSharp.Interpreter.CoreLib.TableModule_Globals","MoonSharp.Interpreter")]
    [DynamicDependency(Members,"MoonSharp.Interpreter.CoreLib.MathModule","MoonSharp.Interpreter")]
    [DynamicDependency(Members,"MoonSharp.Interpreter.CoreLib.Bit32Module","MoonSharp.Interpreter")]
    internal static void Initialize()
    {
        lock(Gate)
        {
            if(initialized) return;
            Script.GlobalOptions.Platform=new SandboxPlatform();
            initialized=true;
        }
    }
    private sealed class SandboxPlatform:LimitedPlatformAccessor
    {
        public override bool IsRunningOnAOT()=>!RuntimeFeature.IsDynamicCodeSupported;
        public override CoreModules FilterSupportedCoreModules(CoreModules modules)=>modules&CoreModules.Preset_HardSandbox;
    }
}
