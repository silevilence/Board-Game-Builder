using System.Diagnostics;
using System.Text.Json;
using BoardGame.Authoring;

namespace Engine.Tests;

public static class AuthorCliTests
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var root = Path.Combine(Path.GetTempPath(), "bgb-cli-integration-" + Guid.NewGuid());
        var product = Path.Combine(root, "author product");var project = Path.Combine(root, "game project");
        try
        {
            var source = Path.GetFullPath($"src/Cli/bin/{configuration}/net10.0");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                if (relative.StartsWith("win-", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("plugins" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(product, relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file, target);
            }
            async Task<JsonElement> Cli(int exit, params string[] arguments)
            {
                var start = new ProcessStartInfo(Path.Combine(product, "bgb.exe"))
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in arguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();var errors = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                var text = await output;var error = await errors;
                Check.That(process.ExitCode == exit, $"CLI exit {process.ExitCode}, expected {exit}: {text} {error}");
                return JsonSerializer.Deserialize<JsonElement>(text);
            }
            var help = await Cli(0, "help");
            Check.That(help.GetProperty("options").EnumerateArray().Any(o=>o.GetString()=="--instance <ID>") &&
                help.GetProperty("options").EnumerateArray().Any(o=>o.GetString()=="--args <JSON>"));
            Check.That(help.GetProperty("aliases").GetProperty("create-module").GetString()=="project.create.module");
            var created = await Cli(0, "create-module", "--project", project, "--request", "create-once");
            var repeated = await Cli(0, "call", "project.create.module", "--project", project, "--request", "create-once");
            Check.That(created.GetProperty("result").GetRawText()==repeated.GetProperty("result").GetRawText());
            await Cli(0, "validate", "--project", project);
            var beforeConflict=File.ReadAllBytes(Path.Combine(project,"module.json"));
            var conflict=await Cli(3,"call","configuration.replace","--project",project,"--args","{\"version\":\"stale\",\"configuration\":{}}");
            Check.That(conflict.GetProperty("code").GetString()=="project.conflict" && beforeConflict.SequenceEqual(File.ReadAllBytes(Path.Combine(project,"module.json"))));
            var duplicate=await Cli(3,"create-module","--project",project);
            Check.That(duplicate.GetProperty("code").GetString()=="template.conflict");
            var plugins = Path.Combine(product, "plugins", "author");Directory.CreateDirectory(plugins);
            File.Copy($"samples/Author.SamplePlugin/bin/{configuration}/net10.0/Author.SamplePlugin.dll", Path.Combine(plugins, "Author.SamplePlugin.dll"));
            var operations = await Cli(0, "operations", "--project", project);
            Check.That(operations.GetProperty("result").EnumerateArray().Any(o=>o.GetProperty("operation").GetProperty("id").GetString()=="sample.count" && !o.GetProperty("enabled").GetBoolean()));
            File.WriteAllText(Path.Combine(project, AuthorProfiles.ConfigurationFile), """
                {"defaultProfile":"custom","profiles":{"custom":{"format":1,"plugins":[{"id":"edit.disk"},{"id":"author.files"},{"id":"sample.author"}]}}}
                """);
            var description = await Cli(0, "describe", "sample.count", "--project", project);
            Check.That(description.GetProperty("result").GetProperty("plugin").GetString()=="sample.author");
            var counted = await Cli(0, "call", "sample.count", "--project", project);
            var files = await Cli(0, "call", "files.list", "--project", project);
            Check.That(counted.GetProperty("result").GetProperty("count").GetInt32()==files.GetProperty("result").GetProperty("paths").GetArrayLength());
            var unavailable = await Cli(1, "validate", "--project", project);
            Check.That(unavailable.GetProperty("code").GetString()=="operation.unavailable");
            var broken = Path.Combine(plugins, "Broken.dll");File.WriteAllText(broken, "not an assembly");
            var error = await Cli(1, "plugins", "--project", project);
            Check.That(error.GetProperty("code").GetString()=="plugin.load" && error.GetProperty("path").GetString()==broken && error.GetProperty("message").GetString()!.Contains("Broken.dll"));
            File.Delete(broken);await Cli(0, "call", "sample.count", "--project", project);
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    public static void ConnectionLifetime() => ConnectionLifetimeAsync().GetAwaiter().GetResult();
    private static async Task ConnectionLifetimeAsync()
    {
        var project = Path.Combine(Path.GetTempPath(), "bgb-long-connection-" + Guid.NewGuid());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var server = new WorkspaceConnectionServer(async (request, token) =>
        {
            if(Interlocked.Increment(ref calls)==1) await Task.Delay(TimeSpan.FromSeconds(36),token);
            else
            {
                entered.TrySetResult();
                try {await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){cancelled.TrySetResult();throw;}
            }
            var view = new ProjectView(request.Identity, new Dictionary<string,byte[]>());
            return new WorkspaceView(view.Source, view.CopyFiles());
        });
        server.Publish("long-workspace", 1, project);
        using var mode = new GuiEditMode(project, "gui", server.Instance);
        Check.That((await mode.Read().WaitAsync(TimeSpan.FromSeconds(45))).Source.Identity.Workspace=="long-workspace");
        using var cancellation = new CancellationTokenSource();var pending = mode.Read(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cancellation.Cancel();
        try {await pending;throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
