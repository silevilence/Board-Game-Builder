using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BoardGame.Launcher;

public sealed record ReleaseAsset(string Name,long Size,string? Digest,string Url);
public sealed record CatalogPackage(string Mode,ReleaseAsset Archive,ReleaseAsset Checksum,byte[] Manifest)
{
    public ProductManifest ReadManifest()=>ProductManifest.Parse(Manifest);
}
public sealed record CatalogVersion(string Version,string Tag,string Notes,string Url,CatalogPackage[] Packages,string? Diagnostic);
public sealed record CatalogSnapshot(DateTimeOffset CheckedAt,bool Cached,string? Diagnostic,CatalogVersion[] Versions);

/// <summary>Only published products become choices. A refresh is atomic; offline use retains the last validated catalog.</summary>
public sealed class ReleaseCatalog(HttpClient client,string home)
{
    public const string Repository="silevilence/Board-Game-Builder";
    private readonly string cache=ProductManifest.SafePath(home,"catalog.json");
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) { WriteIndented=true };
    public CatalogSnapshot ReadCache()
    {
        if(!File.Exists(cache)) return new(DateTimeOffset.MinValue,true,"尚无已验证的在线版本缓存。",[]);
        if(new FileInfo(cache).Length>64*1024*1024) throw new InvalidDataException("版本缓存过大。");
        var result=JsonSerializer.Deserialize<CatalogSnapshot>(File.ReadAllBytes(cache),Json)??throw new InvalidDataException("无效版本缓存。");
        if(result.Versions==null || result.Versions.Length>1000) throw new InvalidDataException("无效版本缓存。");
        var versions=new HashSet<string>();
        foreach(var version in result.Versions)
        {
            if(ProductManifest.NormalizeVersion(version.Version)!=version.Version || !versions.Add(version.Version)) throw new InvalidDataException("缓存版本冲突。");
            foreach(var package in version.Packages)
            {
                var manifest=package.ReadManifest();
                if(manifest.Version!=version.Version || manifest.Mode!=package.Mode) throw new InvalidDataException("缓存清单身份冲突。");
                ValidateUrl(package.Archive.Url); ValidateUrl(package.Checksum.Url);
            }
        }
        return result with {Cached=true};
    }
    public async Task<CatalogSnapshot> Refresh(CancellationToken cancellation=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var requestToken=timeout.Token;
        try
        {
            var releases=await ReadReleases(requestToken);
            var byTag=releases.GroupBy(r=>Text(r,"tag_name"),StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
            var candidates=new Dictionary<string,List<JsonElement>>();
            foreach(var release in releases)
            {
                if(release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
                string version;
                try {version=ProductManifest.NormalizeVersion(Text(release,"tag_name"));} catch(InvalidDataException) {continue;}
                if(!candidates.TryGetValue(version,out var group)) candidates[version]=group=[];
                group.Add(release);
            }
            var rows=new List<CatalogVersion>();
            foreach(var pair in candidates.OrderByDescending(p=>System.Version.Parse(p.Key)))
            {
                requestToken.ThrowIfCancellationRequested();
                var release=pair.Value[0]; var tag=Text(release,"tag_name");
                var packages=new List<CatalogPackage>(); string? diagnostic=null; var unsupportedPlatform=false;
                try
                {
                    if(pair.Value.Count!=1) throw new InvalidDataException("同一规范化版本存在多个 Release，无法确定来源。");
                    var assets=Assets(release);
                    foreach(var mode in pair.Key=="0.1.0" ? new[]{"legacy"} : new[]{"standalone","slim"})
                    {
                        var name=mode=="legacy"?"toolkit.json":"toolkit-"+mode+".json";
                        var manifestAsset=One(assets,name);
                        var manifestBytes=await DownloadMetadata(manifestAsset,16*1024*1024,requestToken);
                        using(var metadata=JsonDocument.Parse(manifestBytes))
                            if(metadata.RootElement.TryGetProperty("platform",out var platform) && platform.GetString()!="win-x64") {unsupportedPlatform=true;break;}
                        var manifest=ProductManifest.Parse(manifestBytes);
                        if(manifest.Version!=pair.Key || manifest.Mode!=mode) throw new InvalidDataException("Release 与版本清单不一致。");
                        var archiveName=$"Board-Game-Builder-V{pair.Key}-"+(mode=="legacy"?"":mode+"-")+"win-x64.zip";
                        var archive=One(assets,archiveName); var checksum=One(assets,archiveName+".sha256");
                        foreach(var component in manifest.Components.Concat(manifest.Json.TryGetProperty("exportDependencies",out var export)?ProductManifest.ReadComponents(export.GetProperty("components")):[]))
                        {
                            if(component.Source.Repository!=Repository) throw new InvalidDataException("组件不在官方仓库。");
                            if(!byTag.TryGetValue(component.Source.Tag,out var sources) || sources.Length!=1 || sources[0].GetProperty("draft").GetBoolean()) throw new InvalidDataException("组件来源已失效："+component.Id);
                            var source=One(Assets(sources[0]),component.Source.Asset);
                            if(source.Size!=component.Size || source.Digest!="sha256:"+component.Sha256) throw new InvalidDataException("组件附件与清单身份冲突："+component.Id);
                        }
                        packages.Add(new(mode,archive,checksum,manifestBytes));
                    }
                }
                catch(Exception e) when(e is InvalidDataException or JsonException or KeyNotFoundException) {diagnostic=e.Message;packages.Clear();}
                if(!unsupportedPlatform) rows.Add(new(pair.Key,tag,release.TryGetProperty("body",out var body)?body.GetString()??"":"",Text(release,"html_url"),packages.ToArray(),diagnostic));
            }
            var snapshot=new CatalogSnapshot(DateTimeOffset.UtcNow,false,null,rows.ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            var temporary=cache+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {var bytes=JsonSerializer.SerializeToUtf8Bytes(snapshot,Json); if(bytes.Length>64*1024*1024) throw new InvalidDataException("版本缓存超过大小限制，保留原缓存。"); await File.WriteAllBytesAsync(temporary,bytes,requestToken); File.Move(temporary,cache,true);}
            finally {if(File.Exists(temporary)) File.Delete(temporary);}
            return snapshot;
        }
        catch(Exception e) when(e is HttpRequestException or IOException or InvalidDataException or JsonException or KeyNotFoundException or OperationCanceledException && !cancellation.IsCancellationRequested)
        {
            return ReadCache() with {Diagnostic="在线刷新失败，保留上次已验证列表："+e.Message,Cached=true};
        }
    }
    private async Task<List<JsonElement>> ReadReleases(CancellationToken cancellation)
    {
        var result=new List<JsonElement>();
        for(var page=1;page<=100;page++)
        {
            using var request=Request($"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);
            CheckStatus(response);
            var bytes=await ReadBounded(response,16*1024*1024,cancellation);
            using var document=JsonDocument.Parse(bytes); ProductManifest.Unique(document.RootElement);
            var rows=document.RootElement.EnumerateArray().Select(r=>r.Clone()).ToArray(); result.AddRange(rows);
            if(rows.Length<100 && !(response.Headers.TryGetValues("Link",out var links)&&links.Any(l=>l.Contains("rel=\"next\"",StringComparison.Ordinal)))) return result;
        }
        throw new InvalidDataException("Release 分页超过安全上限，未替换缓存。");
    }
    internal static HttpRequestMessage Request(string url)
    {
        var request=new HttpRequestMessage(HttpMethod.Get,url); request.Headers.UserAgent.ParseAdd("BoardGameBuilder-Launcher/0.1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json")); return request;
    }
    private async Task<byte[]> DownloadMetadata(ReleaseAsset asset,int limit,CancellationToken cancellation)
    {
        ValidateUrl(asset.Url);
        if(asset.Size>limit) throw new InvalidDataException("清单附件过大。");
        using var request=Request(asset.Url); using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation); CheckStatus(response);
        var bytes=await ReadBounded(response,limit,cancellation);
        if(bytes.LongLength!=asset.Size || (asset.Digest!=null && asset.Digest!="sha256:"+ProductManifest.Hash(bytes))) throw new InvalidDataException("清单附件校验失败。");
        return bytes;
    }
    internal static void ValidateUrl(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="github.com" || !uri.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal) || !uri.IsDefaultPort || uri.UserInfo.Length!=0) throw new InvalidDataException("非官方 Release 下载地址。");
    }
    private static void CheckStatus(HttpResponseMessage response)
    {
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) throw new HttpRequestException("GitHub 限流或拒绝访问；请稍后手动刷新。");
        response.EnsureSuccessStatusCode();
    }
    private static async Task<byte[]> ReadBounded(HttpResponseMessage response,int limit,CancellationToken cancellation)
    {
        if(response.Content.Headers.ContentLength>limit) throw new InvalidDataException("响应过大。");
        await using var input=await response.Content.ReadAsStreamAsync(cancellation); using var output=new MemoryStream(); var buffer=new byte[81920];
        for(int count;(count=await input.ReadAsync(buffer,cancellation))!=0;) {if(output.Length+count>limit) throw new InvalidDataException("响应过大。"); await output.WriteAsync(buffer.AsMemory(0,count),cancellation);}
        return output.ToArray();
    }
    private static ReleaseAsset[] Assets(JsonElement release)=>release.GetProperty("assets").EnumerateArray().Select(a=>new ReleaseAsset(Text(a,"name"),a.GetProperty("size").GetInt64(),a.TryGetProperty("digest",out var digest)?digest.GetString():null,Text(a,"browser_download_url"))).ToArray();
    private static ReleaseAsset One(ReleaseAsset[] assets,string name)
    {
        var matches=assets.Where(a=>a.Name==name).ToArray();
        if(matches.Length!=1 || matches[0].Size<=0) throw new InvalidDataException("附件缺失或重复："+name);
        ValidateUrl(matches[0].Url); return matches[0];
    }
    private static string Text(JsonElement element,string key)=>ProductManifest.Text(element,key);
}
