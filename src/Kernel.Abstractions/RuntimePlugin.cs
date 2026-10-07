namespace BoardGame.Kernel;

public sealed record RuntimePlugin(string Id,string Version,int Api,string File,string Sha256,List<string> Permissions,byte[] Code);
