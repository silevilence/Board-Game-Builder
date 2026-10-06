using BoardGame.Modules;
namespace BoardGame.Editor;

public enum UnsavedChoice { Cancel, Save, Discard }

// Data survives panel reconstruction; controls never own author state.
public sealed class DocumentWorkspace : IDisposable
{
    private readonly Dictionary<string,DocumentDraft> drafts=new(StringComparer.Ordinal);
    public Dictionary<string,string> ViewState { get; }=new(StringComparer.Ordinal);
    public ModuleDocument? Current { get; private set; }
    public string Location { get; set; }="";
    public bool Dirty=>Current?.Dirty==true || drafts.Values.Any(d=>d.Dirty);
    public event Action? Changed;
    public event Action? Replaced;
    public (string Collection,string Id)? Selection { get; private set; }
    public event Action? SelectionChanged;
    public void Select(string collection,string id)
    {
        if(Selection==(collection,id)) return;
        Selection=(collection,id);SelectionChanged?.Invoke();
    }
    public void Set(ModuleDocument? document)
    {
        if(Current!=null) Current.Changed-=Notify;
        Current=document;drafts.Clear();ViewState.Clear();Selection=null;Location=document?.DirectoryPath ?? "";
        if(Current!=null) Current.Changed+=Notify;
        Replaced?.Invoke();Notify();
    }
    public DocumentDraft Draft(string name)
    {
        if(drafts.TryGetValue(name,out var draft)) return draft;
        var doc=Current ?? throw new InvalidOperationException("请打开模块");
        var yaml=name=="module.yaml";var path=yaml?"module.json":name;
        draft=new DocumentDraft(path,doc.Text(path),yaml?AuthorConfiguration.ToYaml(doc.Configuration):doc.Text(path),yaml);
        drafts.Add(name,draft);return draft;
    }
    public void EditDraft(string name,string text) {Draft(name).Edit(text);Changed?.Invoke();}
    public void DiscardDraft(string name) {drafts.Remove(name);Changed?.Invoke();}
    public void ApplyDrafts()
    {
        if(Current==null) return;
        var dirty=drafts.Values.Where(d=>d.Dirty).ToArray();
        if(dirty.GroupBy(d=>d.Path).Any(g=>g.Count()>1)) throw new InvalidOperationException("JSON 与 YAML 草稿同时修改同一配置，请先选择保留一份并合并；草稿均已保留。");
        var edits=dirty.Select(d=>(Draft:d,Text:d.Serialize())).ToArray();
        foreach(var edit in edits)
            if(Current.Text(edit.Draft.Path)!=edit.Draft.Baseline && Current.Text(edit.Draft.Path)!=edit.Text)
                throw new InvalidOperationException("其他面板已修改配置；草稿仍保留，请重新读取最新配置并合并修改。");
        foreach(var edit in edits) {Current.EditText(edit.Draft.Path,edit.Text);edit.Draft.Accept(edit.Text);}
        Notify();
    }
    public void Save() {ApplyDrafts();Current?.Save(string.IsNullOrWhiteSpace(Location)?null:Location);}
    public void Undo()
    {
        var pending=drafts.Values.LastOrDefault(d=>d.Dirty);
        if(pending!=null) {pending.Undo();Changed?.Invoke();} else Current?.Undo();
    }
    public void Redo()
    {
        var pending=drafts.Values.LastOrDefault(d=>d.CanRedo);
        if(pending!=null) {pending.Redo();Changed?.Invoke();} else Current?.Redo();
    }
    public bool CanUndo=>Current?.CanUndo==true || drafts.Values.Any(d=>d.Dirty);
    public bool CanRedo=>Current?.CanRedo==true || drafts.Values.Any(d=>d.CanRedo);
    public async Task<bool> CanLeave(Func<Task<UnsavedChoice>> choose)
    {
        if(!Dirty) return true;
        var choice=await choose();
        if(choice==UnsavedChoice.Save) Save();
        return choice!=UnsavedChoice.Cancel;
    }
    private void Notify()
    {
        if(Current!=null) foreach(var draft in drafts.Values)
            if(!draft.Dirty && Current.Paths.Contains(draft.Path)) draft.Reload(Current.Text(draft.Path));
        Changed?.Invoke();
    }
    public void Dispose() {if(Current!=null) Current.Changed-=Notify;Current=null;Changed=null;Replaced=null;drafts.Clear();ViewState.Clear();}
}

public sealed class DocumentDraft(string path,string baseline,string text,bool yaml)
{
    private readonly Stack<string> undo=[];
    private readonly Stack<string> redo=[];
    private string accepted=text;
    public string Path { get; }=path;
    public string Baseline { get; private set; }=baseline;
    public string Text { get; private set; }=text;
    public bool Dirty=>Text!=accepted;
    public bool CanRedo=>redo.Count>0;
    public void Edit(string value) {if(value==Text)return;undo.Push(Text);redo.Clear();Text=value;}
    public void Undo() {if(undo.Count==0)return;redo.Push(Text);Text=undo.Pop();}
    public void Redo() {if(redo.Count==0)return;undo.Push(Text);Text=redo.Pop();}
    public string Serialize()
    {
        if(yaml) return AuthorConfiguration.FromYaml(Text).ToJsonString(new() {WriteIndented=true});
        if(Path=="module.json") _=System.Text.Json.Nodes.JsonNode.Parse(Text)!.AsObject();
        return Text;
    }
    public void Accept(string source) {Baseline=source;accepted=Text;undo.Clear();redo.Clear();}
    public void Reload(string source)
    {
        if(source==Baseline) return;
        Text=yaml?AuthorConfiguration.ToYaml(System.Text.Json.Nodes.JsonNode.Parse(source)!.AsObject()):source;
        Accept(source);
    }
}
