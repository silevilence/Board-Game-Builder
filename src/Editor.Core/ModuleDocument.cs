using System.Text;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;
namespace BoardGame.Editor;

// Author data is owned by the document. Runtime instances only receive validated copies.
public sealed class ModuleDocument
{
    private Dictionary<string,byte[]> files;
    private readonly Stack<Dictionary<string,byte[]>> undo=[];
    private readonly Stack<Dictionary<string,byte[]>> redo=[];
    private string saved;
    public string? DirectoryPath { get; private set; }
    public bool Dirty => Hash(files)!=saved;
    public bool CanUndo => undo.Count>0;
    public bool CanRedo => redo.Count>0;
    public IReadOnlyList<string> Paths => files.Keys.Order(StringComparer.Ordinal).ToArray();
    public event Action? Changed;
    private ModuleDocument(Dictionary<string,byte[]> files,string? path) { this.files=files; DirectoryPath=path; saved=path==null?"":Hash(files); }
    public static ModuleDocument Create() => new(new(StringComparer.Ordinal) {
        ["module.json"]=Encoding.UTF8.GetBytes("""
        {"format":1,"id":"untitled","version":"1.0.0","rules":"rules.lua","cpu":"cpu.lua","resources":{},"seats":[{"id":"p1","team":"one","controller":"human"}],"phases":["play"],"entities":[]}
        """),
        ["rules.lua"]=Encoding.UTF8.GetBytes("return { actions=function(v) return {{id='finish',label='结束',command={kind='finish'}}} end, decide=function(v) return {allowed=true,reason='',commands={{kind='finish',winners={}}}} end }"),
        ["cpu.lua"]=Encoding.UTF8.GetBytes("return { decide=function(ctx) return ctx.actions[1].command end }") },null);
    public static ModuleDocument Open(string path)
    {
        path=Path.GetFullPath(path);
        _=DirectoryModule.Load(path);
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        Read(path,"");
        return new(files,path);
        void Read(string folder,string prefix)
        {
            foreach(var item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if((item.Attributes & FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Linked author files are unsupported: "+item.Name);
                var relative=prefix+item.Name;
                if(item is DirectoryInfo) Read(item.FullName,relative+"/");
                else files.Add(relative,File.ReadAllBytes(item.FullName));
            }
        }
    }
    public string Text(string path) => Encoding.UTF8.GetString(files[path]);
    public JsonObject Configuration => JsonNode.Parse(Text("module.json"))!.AsObject();
    public void EditText(string path,string text) => Edit(path,Encoding.UTF8.GetBytes(text));
    public void EditConfiguration(Action<JsonObject> edit)
    {
        var value=Configuration; edit(value); EditText("module.json",value.ToJsonString(new() {WriteIndented=true}));
    }
    private void Edit(string path,byte[] data)
    {
        ModulePackage.ValidatePath(path);
        if(files.TryGetValue(path,out var old) && old.SequenceEqual(data)) return;
        undo.Push(new(files,StringComparer.Ordinal)); redo.Clear(); files=new(files,StringComparer.Ordinal) { [path]=data }; Changed?.Invoke();
    }
    public void Undo() { if(!CanUndo) return; redo.Push(files); files=undo.Pop(); Changed?.Invoke(); }
    public void Redo() { if(!CanRedo) return; undo.Push(files); files=redo.Pop(); Changed?.Invoke(); }
    public LoadedModule Snapshot()
    {
        var temp=Path.Combine(Path.GetTempPath(),"bgb-author-"+Guid.NewGuid());
        try { Write(temp); return DirectoryModule.Load(temp); }
        finally { if(Directory.Exists(temp)) Directory.Delete(temp,true); }
    }
    public void Save(string? destination=null)
    {
        var target=Path.GetFullPath(destination ?? DirectoryPath ?? throw new InvalidOperationException("Choose a module directory."));
        _=Snapshot();
        if(DirectoryPath!=target && (Directory.Exists(target) || File.Exists(target))) throw new IOException("Choose a new module directory for Save As.");
        // A sibling staging directory keeps validation and I/O failures away from the current document.
        var staging=target+".saving-"+Guid.NewGuid(); var backup=target+".backup-"+Guid.NewGuid();
        try
        {
            Write(staging);
            if(Directory.Exists(target)) Directory.Move(target,backup);
            try { Directory.Move(staging,target); }
            catch { if(Directory.Exists(backup)) Directory.Move(backup,target); throw; }
            DirectoryPath=target; saved=Hash(files); Changed?.Invoke();
        }
        finally { if(Directory.Exists(staging)) Directory.Delete(staging,true); if(Directory.Exists(backup) && Directory.Exists(target)) Directory.Delete(backup,true); }
    }
    private void Write(string target)
    {
        Directory.CreateDirectory(target);
        foreach(var file in files) { ModulePackage.ValidatePath(file.Key); var path=Path.Combine(target,file.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path,file.Value); }
    }
    private static string Hash(Dictionary<string,byte[]> values) => Wire.Hash(string.Join("\n",values.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+":"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(p.Value)))));
}
