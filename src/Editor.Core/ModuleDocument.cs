using System.Text;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace BoardGame.Editor;

// Author data is owned by the document. Runtime instances only receive validated copies.
public sealed class ModuleDocument
{
    private Dictionary<string,byte[]> files;
    private readonly Stack<Dictionary<string,byte[]>> undo=[];
    private readonly Stack<Dictionary<string,byte[]>> redo=[];
    private string saved;
    internal GameAuthorFormats AuthorFormats {get;set;}=new();
    public string? DirectoryPath { get; private set; }
    public bool Dirty => Hash(files)!=saved;
    public bool CanUndo => undo.Count>0;
    public bool CanRedo => redo.Count>0;
    public IReadOnlyList<string> Paths => files.Keys.Order(StringComparer.Ordinal).ToArray();
    public event Action? Changed;
    private ModuleDocument(Dictionary<string,byte[]> files,string? path) { this.files=files; DirectoryPath=path; saved=path==null?"":Hash(files); }
    public static ModuleDocument Create() => new(GameAuthorFormats.Template(),null);
    public static ModuleDocument Open(string path)
    {
        path=Path.GetFullPath(path);
        _=GameRuntime.Load(path);
        return new(ReadFiles(path),path);
    }
    private static Dictionary<string,byte[]> ReadFiles(string path)
    {
        if((File.GetAttributes(path) & FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Linked author directories are unsupported");
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        Read(path,"");
        return files;
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
    public JsonObject Startup=>JsonNode.Parse(Text(GameRuntime.Entry))!.AsObject();
    public IGameAuthorFormat? AuthorFormat=>AuthorFormats.Resolve(this);
    public bool SupportsStructuredEditing=>AuthorFormat!=null;
    public string DefinitionPath=>(AuthorFormat ?? throw new InvalidOperationException("此定义格式没有专用作者界面；请使用文件编辑与试玩。")).DefinitionPath;
    public JsonObject Configuration=>(AuthorFormat ?? throw new InvalidOperationException("此定义格式没有专用作者界面；请使用文件编辑与试玩。")).Read(this);
    public string? ScriptPath(string plugin)=>Startup["plugins"]!.AsArray().FirstOrDefault(p=>p!["id"]!.GetValue<string>()==plugin)?["configuration"]?["file"]?.GetValue<string>();
    public void EditStartup(Action<JsonObject> edit) {var root=Startup;edit(root);EditText(GameRuntime.Entry,root.ToJsonString(new(){WriteIndented=true}));}
    public byte[] Bytes(string path)=>files[path].ToArray();
    public string Revision=>Hash(files);
    public void Import(string revision,JsonObject configuration,IReadOnlyDictionary<string,byte[]> additions)
    {
        if(revision!=Revision) throw new InvalidOperationException("文档已改变，请重新预览导入。");
        var candidate=new Dictionary<string,byte[]>(files,StringComparer.Ordinal);
        foreach(var file in additions)
        {
            ModulePackage.ValidatePath(file.Key);
            if(candidate.Keys.Any(p=>p.Equals(file.Key,StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("导入路径冲突："+file.Key);
            candidate.Add(file.Key,file.Value.ToArray());
        }
        candidate[DefinitionPath]=Encoding.UTF8.GetBytes(AuthorFormat!.Serialize(this,configuration));
        var probe=new ModuleDocument(candidate,null);_=probe.Snapshot();
        undo.Push(files);redo.Clear();files=candidate;Changed?.Invoke();
    }
    public void EditText(string path,string text) => Edit(path,Encoding.UTF8.GetBytes(text));
    public void EditConfiguration(Action<JsonObject> edit)
    {
        var value=Configuration; edit(value);ReplaceConfiguration(value);
    }
    public void ReplaceConfiguration(JsonObject value)
    {
        if(JsonNode.DeepEquals(Configuration,value)) return;
        EditText(DefinitionPath,AuthorFormat!.Serialize(this,value));
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
        try { Write(temp); return GameRuntime.Load(temp); }
        finally { if(Directory.Exists(temp)) Directory.Delete(temp,true); }
    }
    public void Save(string? destination=null)
    {
        var target=Path.GetFullPath(destination ?? DirectoryPath ?? throw new InvalidOperationException("Choose a module directory."));
        _=Snapshot();
        var same=string.Equals(DirectoryPath,target,StringComparison.OrdinalIgnoreCase);
        if(!same && (Directory.Exists(target) || File.Exists(target))) throw new IOException("Choose a new module directory for Save As.");
        void CheckConflict()
        {
            if(same && (!Directory.Exists(target) || Hash(ReadFiles(target))!=saved)) throw new IOException("Module files changed outside the editor. Save As to a new directory, or reopen and merge your changes.");
        }
        CheckConflict();
        // A sibling staging directory keeps validation and I/O failures away from the current document.
        var staging=target+".saving-"+Guid.NewGuid(); var backup=target+".backup-"+Guid.NewGuid();
        try
        {
            Write(staging);
            CheckConflict();
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
