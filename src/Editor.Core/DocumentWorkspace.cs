using BoardGame.Authoring;
using BoardGame.Modules;
namespace BoardGame.Editor;

public enum UnsavedChoice { Cancel, Save, Discard }
public sealed record DocumentDraftTarget(string Path,bool Yaml=false);

// Data survives panel reconstruction; controls never own author state.
public sealed partial class DocumentWorkspace : IDisposable
{
    private readonly Dictionary<DocumentDraftTarget,DocumentDraft> drafts=new();
    private readonly Dictionary<string,DocumentDraft> forms=new(StringComparer.Ordinal);
    public Dictionary<string,string> ViewState { get; }=new(StringComparer.Ordinal);
    public GameAuthorFormats AuthorFormats {get;}=new();
    public DocumentWorkspace() {AuthorFormats.Changed+=Notify;}
    public ModuleDocument? Current { get; private set; }
    public string Location { get; set; }="";
    private IEnumerable<DocumentDraft> AllDrafts=>drafts.Values.Concat(forms.Values);
    public bool Dirty=>Current?.Dirty==true || AllDrafts.Any(d=>d.Dirty);
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
        Generation++;authorUndo.Clear();authorRedo.Clear();ExternalMergeBaseline=null;
        if(Current!=null) Current.Changed-=Notify;
        Current=document;drafts.Clear();forms.Clear();ViewState.Clear();Selection=null;Location=document?.DirectoryPath ?? "";
        if(Current!=null) {Current.AuthorFormats=AuthorFormats;Current.Changed+=Notify;}
        Replaced?.Invoke();Notify();
    }
    public DocumentDraft Draft(string path)=>Draft(new DocumentDraftTarget(path));
    public DocumentDraft Draft(DocumentDraftTarget target)
    {
        if(drafts.TryGetValue(target,out var draft)) return draft;
        var doc=Current ?? throw new InvalidOperationException("请打开模块");
        var source=doc.Text(target.Path);
        draft=new DocumentDraft(target.Path,source,target.Yaml?AuthorConfiguration.ToYaml(System.Text.Json.Nodes.JsonNode.Parse(source)!.AsObject()):source,target.Yaml);
        drafts.Add(target,draft);return draft;
    }
    public void EditDraft(string path,string text)=>EditDraft(new DocumentDraftTarget(path),text);
    public void EditDraft(DocumentDraftTarget target,string text) {Draft(target).Edit(text);Changed?.Invoke();}
    public void DiscardDraft(string path)=>DiscardDraft(new DocumentDraftTarget(path));
    public void DiscardDraft(DocumentDraftTarget target) {drafts.Remove(target);Changed?.Invoke();}
    public DocumentDraft Form(string name,Func<string> initial,string? baseline=null)
    {
        baseline??=Current is {} current?current.Text(current.DefinitionPath):throw new InvalidOperationException("请打开模块");
        if(forms.TryGetValue(name,out var draft) && (draft.Dirty || draft.Baseline==baseline)) return draft;
        return forms[name]=new(name,baseline,initial(),false);
    }
    public void EditForm(string name,string text) {forms[name].Edit(text);Changed?.Invoke();}
    public void DiscardForm(string name) {forms.Remove(name);Changed?.Invoke();}
    public bool HasPendingForms=>forms.Values.Any(d=>d.Dirty);
    public void DiscardForms() {forms.Clear();Changed?.Invoke();}
    public void ApplyForm(string name,Action<ModuleDocument> apply)
    {
        var draft=forms[name];var document=Current ?? throw new InvalidOperationException("请打开模块");
        if(document.Text(document.DefinitionPath)!=draft.Baseline) throw new InvalidOperationException("其他面板已修改配置；字段草稿已保留，请放弃并重新读取后合并。");
        apply(document);forms.Remove(name);Notify();
    }
    public void ApplyDrafts()
    {
        if(Current==null) return;
        if(forms.Values.Any(d=>d.Dirty)) throw new InvalidOperationException("存在未应用的字段草稿："+string.Join("、",forms.Where(p=>p.Value.Dirty).Select(p=>p.Key))+"。请回到对应面板应用或放弃，再保存或试玩。");
        var dirty=drafts.Values.Where(d=>d.Dirty).ToArray();
        if(dirty.GroupBy(d=>d.Path).Any(g=>g.Count()>1)) throw new InvalidOperationException("JSON 与 YAML 草稿同时修改同一配置，请先选择保留一份并合并；草稿均已保留。");
        var edits=dirty.Select(d=>(Draft:d,Text:d.Serialize())).ToArray();
        foreach(var edit in edits)
            if(Current.Text(edit.Draft.Path)!=edit.Draft.Baseline && Current.Text(edit.Draft.Path)!=edit.Text)
                throw new InvalidOperationException("其他面板已修改配置；草稿仍保留，请重新读取最新配置并合并修改。");
        foreach(var edit in edits) {Current.EditText(edit.Draft.Path,edit.Text);edit.Draft.Accept(edit.Text);}
        Notify();
    }
    public void Save()
    {
        var edit=Current!=null && authorUndo.Count>0 && authorUndo.Peek().AfterVersion==AuthorVersion()?authorUndo.Peek():null;
        ApplyDrafts();Current?.Save(string.IsNullOrWhiteSpace(Location)?null:Location);
        if(edit!=null) {authorUndo.Pop();authorUndo.Push(edit with {After=AuthorStateCopy(),AfterVersion=AuthorVersion()});}
    }
    public void Undo()
    {
        if(UndoAuthor()) return;
        var pending=AllDrafts.LastOrDefault(d=>d.Dirty);
        if(pending!=null) {pending.Undo();Changed?.Invoke();} else Current?.Undo();
    }
    public void Redo()
    {
        if(RedoAuthor()) return;
        var pending=AllDrafts.LastOrDefault(d=>d.CanRedo);
        if(pending!=null) {pending.Redo();Changed?.Invoke();} else Current?.Redo();
    }
    public bool CanUndo=>authorUndo.Count>0 || Current?.CanUndo==true || AllDrafts.Any(d=>d.Dirty);
    public bool CanRedo=>authorRedo.Count>0 || Current?.CanRedo==true || AllDrafts.Any(d=>d.CanRedo);
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
    public void Dispose() {AuthorFormats.Changed-=Notify;if(Current!=null) Current.Changed-=Notify;Current=null;Changed=null;Replaced=null;SelectionChanged=null;drafts.Clear();forms.Clear();ViewState.Clear();}
}

public sealed class DocumentDraft(string path,string baseline,string text,bool yaml)
{
    internal bool Yaml=>yaml;
    internal DocumentDraft Copy()
    {
        var copy=new DocumentDraft(Path,Baseline,Text,yaml){accepted=accepted};
        foreach(var entry in undo.Reverse())copy.undo.Push(entry);
        foreach(var entry in redo.Reverse())copy.redo.Push(entry);
        return copy;
    }
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
        if(Path.EndsWith(".json",StringComparison.OrdinalIgnoreCase)) _=System.Text.Json.Nodes.JsonNode.Parse(Text);
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
