using System.Diagnostics;
using System.Text.Json;

namespace Engine.Tests;

public static class SystemRuntimeTests
{
    public static void Run()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "system-runtime-test-" + Guid.NewGuid()));
        Directory.CreateDirectory(root);
        try
        {
            using var requirements = JsonDocument.Parse("""{"frameworks":[{"framework":"Microsoft.NETCore.App","minimumVersion":"10.0.12","rollForward":"LatestPatch"},{"framework":"Microsoft.WindowsDesktop.App","minimumVersion":"10.0.12","rollForward":"LatestPatch"}]}""");
            var program = requirements.RootElement;
            Check.Throws(() => SystemRuntime.ResolveAt(root, program), "compatible x64 .NET Runtime");
            var native = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe");
            File.Copy(native, Path.Combine(root, "dotnet.exe"));
            Add("Microsoft.NETCore.App", "10.0.11"); Add("Microsoft.NETCore.App", "11.0.0");
            Check.Throws(() => SystemRuntime.ResolveAt(root, program), "Microsoft.NETCore.App 10.0.12");
            Add("Microsoft.NETCore.App", "10.0.12"); Add("Microsoft.NETCore.App", "10.0.15");
            Check.Throws(() => SystemRuntime.ResolveAt(root, program), "Microsoft.WindowsDesktop.App");
            Add("Microsoft.WindowsDesktop.App", "10.0.13");
            var resolved = SystemRuntime.ResolveAt(root, program);
            Check.That(resolved.Frameworks[0].Version == "10.0.15" && resolved.Frameworks[1].Version == "10.0.13");
            var start = new ProcessStartInfo(); start.Environment["DOTNET_ROOT_X86"] = "unrelated";
            start.Environment["DOTNET_ROLL_FORWARD"] = "Major";
            SystemRuntime.Apply(start, resolved);
            Check.That(start.Environment["DOTNET_ROOT_X64"] == root && !start.Environment.ContainsKey("DOTNET_ROOT_X86") && start.Environment["DOTNET_ROLL_FORWARD"] == "LatestPatch");
            var bytes = File.ReadAllBytes(Path.Combine(root,"dotnet.exe"));
            var offset = BitConverter.ToInt32(bytes,0x3c) + 4;
            bytes[offset]=0x4c;bytes[offset+1]=0x01;
            File.WriteAllBytes(Path.Combine(root,"dotnet.exe"),bytes);
            Check.Throws(() => SystemRuntime.ResolveAt(root, program), "Wrong .NET Runtime architecture");
            void Add(string name, string version)
            {
                var folder = Path.Combine(root,"shared",name,version); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,name+".deps.json"),"{}");
                if(name=="Microsoft.NETCore.App") File.Copy(native,Path.Combine(folder,"coreclr.dll"));
                else
                {
                    File.Copy(native,Path.Combine(folder,"PresentationNative_cor3.dll"));
                    File.WriteAllText(Path.Combine(folder,name+".runtimeconfig.json"),"""{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.12"}}}""");
                }
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
