using System.Net;

namespace BoardGame.Launcher;

/// <summary>Explicit offline transport; the same component and object integrity checks still apply.</summary>
public static class ComponentArchives
{
    public static HttpClient Open(string directory)=>new(new LocalArchives(Path.GetFullPath(directory)));
    private sealed class LocalArchives(string root):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();ReleaseCatalog.ValidateUrl(request.RequestUri!.AbsoluteUri);
            var file=ProductManifest.SafePath(root,Path.GetFileName(request.RequestUri.AbsolutePath));
            if(!File.Exists(file)) throw new FileNotFoundException("本地缺少固定组件，未尝试联网："+Path.GetFileName(file));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(File.OpenRead(file))});
        }
    }
}
