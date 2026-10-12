using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Launcher;

internal static class CatalogChecks
{
    public static async Task Run(string home,byte[] fixture)
    {
        using var handler=new CatalogHandler(fixture); using var client=new HttpClient(handler);
        var catalog=new ReleaseCatalog(client,home);
        var first=await catalog.Refresh();
        Assert(!first.Cached && first.Versions.Select(v=>v.Version).SequenceEqual(new[]{"0.10.0","0.9.0"}),"pagination and numerical ordering");
        Assert(first.Versions.All(v=>v.Packages.Length==2 && v.Diagnostic==null),"complete packages only");
        handler.Offline=true; var offline=await catalog.Refresh();
        Assert(offline.Cached && offline.Diagnostic!=null && offline.Versions.Length==2,"offline cache survives");
        handler.Offline=false; handler.Duplicate=true;
        var duplicate=await catalog.Refresh(); Assert(duplicate.Versions.Length==2 && duplicate.Versions[0].Packages.Length==0 && duplicate.Versions[0].Diagnostic!=null,"normalized duplicate rejected");
        handler.Duplicate=false; handler.MissingComponent=true;
        var missing=await catalog.Refresh(); Assert(missing.Versions.All(v=>v.Diagnostic!=null),"missing cross-release source rejected");
        handler.MissingComponent=false; handler.Removed=true;
        var removed=await catalog.Refresh(); Assert(removed.Versions.Length==0 && removed.Diagnostic==null,"remote removal reflected without registry mutation");
        handler.Offline=true;
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        try {await catalog.Refresh(cancellation.Token); throw new Exception("Cancellation was swallowed");} catch(OperationCanceledException) { }
        Console.WriteLine("Launcher catalog pagination/cache/source checks passed.");
    }
    private static void Assert(bool condition,string name) {if(!condition) throw new Exception(name);}
    private sealed class CatalogHandler(byte[] fixture):HttpMessageHandler
    {
        public bool Offline,Duplicate,MissingComponent,Removed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if(Offline) throw new HttpRequestException("controlled offline");
            var uri=request.RequestUri!; var node=JsonNode.Parse(fixture)!;
            node["components"]![0]!["source"]!["repository"]=ReleaseCatalog.Repository;
            var source=node["components"]![0]!["source"]!;
            var component=Asset(source["asset"]!.GetValue<string>(),1,"sha256:"+new string('a',64),source["tag"]!.GetValue<string>());
            var componentRelease=Release(source["tag"]!.GetValue<string>(),new[]{component});
            if(uri.Host=="api.github.com")
            {
                var pageTwo=uri.Query.Contains("page=2",StringComparison.Ordinal);
                var rows=new List<object>();
                if(!Removed)
                {
                    if(!pageTwo) {if(!MissingComponent) rows.Add(componentRelease); rows.Add(Product("V0.9.0",node));}
                    else {rows.Add(Product("v0.10.0",node)); if(Duplicate) rows.Add(Product("V0.10.0",node)); rows.Add(Release("V9.0.0",[],true)); rows.Add(Release("V8.0.0",[],false,true));}
                }
                var response=Response(JsonSerializer.SerializeToUtf8Bytes(rows));
                if(!pageTwo && !Removed) response.Headers.Add("Link","<https://api.github.com/repos/test/releases?page=2>; rel=\"next\"");
                return Task.FromResult(response);
            }
            var segments=uri.AbsolutePath.Split('/'); var version=ProductManifest.NormalizeVersion(segments[^2]);
            node["version"]=version;
            // Tests use two standalone-equivalent manifests; slim's real format is covered by downloaded fixtures.
            if(segments[^1]=="toolkit-slim.json") MakeSlim(node);
            return Task.FromResult(Response(JsonSerializer.SerializeToUtf8Bytes(node)));
        }
        private static object Product(string tag,JsonNode template)
        {
            var version=ProductManifest.NormalizeVersion(tag); var node=template.DeepClone(); node["version"]=version;
            var assets=new List<object>();
            foreach(var mode in new[]{"standalone","slim"})
            {
                var manifest=node.DeepClone(); if(mode=="slim") MakeSlim(manifest);
                var bytes=JsonSerializer.SerializeToUtf8Bytes(manifest);
                assets.Add(Asset("toolkit-"+mode+".json",bytes.Length,"sha256:"+ProductManifest.Hash(bytes),tag));
                var name=$"Board-Game-Builder-V{version}-{mode}-win-x64.zip";
                assets.Add(Asset(name,1,null,tag));assets.Add(Asset(name+".sha256",1,null,tag));
            }
            assets.Add(Asset("source.zip",1,null,tag)); return Release(tag,assets.ToArray());
        }
        private static void MakeSlim(JsonNode node)
        {
            node["packageMode"]="slim";
            node["exportDependencies"]=new JsonObject { ["format"]="board-game-builder.player-components",["version"]=node["version"]!.DeepClone(),["api"]=1,["platform"]="win-x64",["architecture"]="x64",["files"]=new JsonArray(),["components"]=node["components"]!.DeepClone() };
            // A minimal valid export closure with the same component object set.
            var files=new JsonArray(); var component=node["components"]![0]!;
            var hashes=component["objects"]!.AsArray();
            for(var i=0;i<hashes.Count;i++) files.Add(new JsonObject{["path"]=i==0?"release.json":i==1?"Player.exe":"file"+i,["sha256"]=hashes[i]!.DeepClone(),["size"]=1,["component"]=component["id"]!.DeepClone()});
            node["exportDependencies"]!["files"]=files;
        }
        private static object Asset(string name,long size,string? digest,string tag)=>new {name,size,digest,browser_download_url=$"https://github.com/{ReleaseCatalog.Repository}/releases/download/{tag}/{name}"};
        private static object Release(string tag,object[] assets,bool draft=false,bool prerelease=false)=>new {tag_name=tag,assets,draft,prerelease,body="Release notes",html_url=$"https://github.com/{ReleaseCatalog.Repository}/releases/tag/{tag}"};
        private static HttpResponseMessage Response(byte[] bytes)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
    }
}
