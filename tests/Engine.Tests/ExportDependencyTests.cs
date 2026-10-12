using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class ExportDependencyTests
{
    public static void Run()
    {
        using (var fixture = new Fixture(longPath: true))
        {
            var prepared = fixture.Resolve();
            Check.That(prepared.Length > 260 && File.Exists(Path.Combine(prepared,"Player.exe")), "Long cache paths must materialize successfully");
        }
        using (var fixture = new Fixture())
        {
            var output = Path.Combine(fixture.Root, "game");
            Check.Throws(() => PlayerDistribution.Export(fixture.Source, "samples/quick-cards", output,
                dependencies: new(fixture.Cache, AllowDownload: false)), "Offline export dependencies are missing");
            Check.That(!Directory.Exists(output) && !Directory.GetDirectories(fixture.Root, "game.staging-*").Any());
            var results = new string[4];
            Parallel.For(0, results.Length, i => results[i] = fixture.Resolve());
            Check.That(results.Distinct().Count() == 1 && File.ReadAllText(Path.Combine(results[0], "Player.exe")) == "player fixture");
            Check.That(!Directory.GetFiles(fixture.Cache, "*.zip").Any());
            File.Delete(fixture.Archive);
            Check.That(fixture.Resolve() == results[0], "Verified export cache was not reusable offline");
            File.WriteAllText(Path.Combine(results[0], "Player.exe"), "corrupt");
            Check.Throws(() => fixture.Resolve(), "corrupt");
        }
        using (var fixture = new Fixture())
        {
            using var handler = new ArchiveHandler(File.ReadAllBytes(fixture.Archive));
            using var client = new HttpClient(handler);
            var prepared = ExportDependencies.Resolve(fixture.Source, new(fixture.Cache, Client: client));
            Check.That(File.Exists(Path.Combine(prepared, "release.json")) && handler.Requests == 1);
            Check.That(handler.LastUri == "https://github.com/test/repo/releases/download/" + fixture.Manifest.Components[0].Source.Tag + "/" + Path.GetFileName(fixture.Archive));
            ExportDependencies.Resolve(fixture.Source, new(fixture.Cache, Client: client));
            Check.That(handler.Requests == 1, "Cached content was downloaded again");
        }
        foreach (var fault in new[] { "archive-hash", "object-hash", "archive-path", "descriptor-path", "identity", "duplicate", "cancel" })
        {
            using var fixture = new Fixture();
            switch (fault)
            {
                case "archive-hash": File.AppendAllText(fixture.Archive, "changed"); break;
                case "object-hash": fixture.RewriteArchive("objects/" + fixture.Manifest.Files[0].Sha256, "wrong content!"); break;
                case "archive-path": fixture.RewriteArchive("../escape", "player fixture"); break;
                case "descriptor-path": fixture.Manifest = fixture.Manifest with { Files = [fixture.Manifest.Files[0] with { Path = "CON.txt" }, fixture.Manifest.Files[1]] }; fixture.Save(); break;
                case "identity": fixture.Manifest = fixture.Manifest with { Components = [fixture.Manifest.Components[0] with { Identity = "product:changed" }] }; fixture.Save(); break;
                case "duplicate": File.WriteAllText(Path.Combine(fixture.Source, ExportDependencies.DescriptorName), Wire.Json(fixture.Manifest).Replace("\"api\":1", "\"api\":1,\"api\":1")); break;
            }
            using var cancellation = new CancellationTokenSource();
            if (fault == "cancel") cancellation.Cancel();
            Check.Throws(() => fixture.Resolve(cancellation.Token));
            Check.That(!Directory.Exists(Path.Combine(fixture.Cache, "players")) || !Directory.GetDirectories(Path.Combine(fixture.Cache, "players")).Any(), fault);
            Check.That(!File.Exists(Path.Combine(fixture.Root, "escape")) && !Directory.GetFiles(fixture.Root, "*.partial-*", SearchOption.AllDirectories).Any(), fault);
        }
    }

    private sealed class ArchiveHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Requests; public string? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++; LastUri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Source => Path.Combine(Root, "source");
        public string Cache => Path.Combine(Root, "cache");
        public string Components => Path.Combine(Root, "components");
        public string Archive => Path.Combine(Components, Manifest.Components[0].Source.Asset);
        public ExportDependencyManifest Manifest;
        public Fixture(bool longPath = false)
        {
            Root = Path.GetFullPath(Path.Combine("artifacts", "export-dependencies-test-" + Guid.NewGuid() + (longPath ? new string('x',140) : "")));
            Directory.CreateDirectory(Source); Directory.CreateDirectory(Components);
            var player = Encoding.UTF8.GetBytes("player fixture"); var release = Encoding.UTF8.GetBytes("catalog fixture");
            var hashes = new[] { Hash(player), Hash(release) }.Order(StringComparer.Ordinal).ToArray();
            // Literal LF canonical form shared with Python, independent of serializer defaults on Windows.
            var identity = "{\n  \"group\": \"product:test\",\n  \"objects\": [\n    \"" + string.Join("\",\n    \"", hashes) + "\"\n  ]\n}\n";
            var key = Hash(Encoding.UTF8.GetBytes(identity)); var id = "product-" + key;
            var source = new ExportComponentSource("test/repo", "components-" + key, id + ".zip");
            Manifest = new(1, "board-game-builder.player-components", "1.0.0", "win-x64",
                [new("Player.exe", player.Length, Hash(player), id), new("release.json", release.Length, Hash(release), id)],
                [new(id, "product", "product:test", hashes, 1, new string('a',64), source)]);
            using (var zip = ZipFile.Open(Archive, ZipArchiveMode.Create))
                foreach (var bytes in new[] { player, release }) { using var stream = zip.CreateEntry("objects/" + Hash(bytes)).Open(); stream.Write(bytes); }
            File.WriteAllBytes(Path.Combine(Source,"release.json"), release);
            UpdateArchive();
        }
        public string Resolve(CancellationToken cancellation = default) => ExportDependencies.Resolve(Source, new(Cache, Components, false), cancellation: cancellation);
        public void Save() => File.WriteAllText(Path.Combine(Source, ExportDependencies.DescriptorName), Wire.Json(Manifest));
        private void UpdateArchive()
        {
            Manifest = Manifest with { Components = [Manifest.Components[0] with { Size = new FileInfo(Archive).Length, Sha256 = Hash(File.ReadAllBytes(Archive)) }] }; Save();
        }
        public void RewriteArchive(string path, string content)
        {
            using (var zip = ZipFile.Open(Archive, ZipArchiveMode.Update))
            {
                zip.GetEntry("objects/" + Manifest.Files[0].Sha256)!.Delete();
                using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write(content);
            }
            UpdateArchive();
        }
        public void Dispose() => Directory.Delete(Root, true);
        private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
