namespace BoardGame.Editor;

public enum CommandFocus { Global, Author, Viewport }
public sealed record EditorCommand(string Id,string Title,Func<Task> Execute,Func<bool> Enabled,
    string? Shortcut=null,CommandFocus Focus=CommandFocus.Author);
public sealed class EditorCommands(Func<string,string>? normalizeShortcut=null)
{
    private readonly Dictionary<string,(EditorCommand Command,CancellationToken Token)> commands=new(StringComparer.Ordinal);
    private readonly HashSet<string> running=[];
    public event Action? Changed;
    public IReadOnlyList<EditorCommand> Items=>commands.Values.Select(v=>v.Command).ToArray();
    public void Register(EditorPluginScope owner,EditorCommand command)
    {
        if(string.IsNullOrWhiteSpace(command.Id) || commands.ContainsKey(command.Id)) throw new InvalidDataException("Duplicate/empty command: "+command.Id);
        if(command.Shortcut!=null) command=command with {Shortcut=normalizeShortcut?.Invoke(command.Shortcut) ?? command.Shortcut.Trim()};
        if(command.Shortcut!=null && commands.Values.Any(v=>string.Equals(v.Command.Shortcut,command.Shortcut,StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Shortcut conflict: "+command.Shortcut);
        commands.Add(command.Id,(command,owner.Cancellation));
        owner.Own(new Cleanup(()=> {commands.Remove(command.Id);Refresh();}));Refresh();
        owner.Guard(()=>running.Contains(command.Id)?"命令正在执行，请等待完成":null);
    }
    public bool CanExecute(string id)=>commands.TryGetValue(id,out var item) && !item.Token.IsCancellationRequested && !running.Contains(id) && item.Command.Enabled();
    public async Task Execute(string id)
    {
        if(!CanExecute(id)) return;
        var command=commands[id].Command;running.Add(id);Refresh();
        try {await command.Execute();} finally {running.Remove(id);Refresh();}
    }
    public void Refresh()=>Changed?.Invoke();
}

public sealed record OwnedContribution(string Owner,string Kind,string Id,object Value);
public sealed record ExportContext(string ModuleDirectory,string Destination);
public sealed class EditorContributions
{
    private readonly List<OwnedContribution> entries=[];
    private readonly Dictionary<string,int> leases=[];
    public IReadOnlyList<OwnedContribution> Entries=>entries.ToArray();
    public void Register(EditorPluginScope owner,string kind,string id,object value)
    {
        if(entries.Any(e=>e.Kind==kind && e.Id==id)) throw new InvalidDataException("Duplicate contribution: "+kind+"/"+id);
        var entry=new OwnedContribution(owner.Id,kind,id,value);entries.Add(entry);
        owner.Own(new Cleanup(()=>entries.Remove(entry)));
        owner.Guard(()=>leases.GetValueOrDefault(owner.Id)>0?"附加步骤正在执行，请等待完成":null);
    }
    public IDisposable Acquire(string kind)
    {
        var owners=entries.Where(e=>e.Kind==kind).Select(e=>e.Owner).Distinct().ToArray();
        foreach(var id in owners) leases[id]=leases.GetValueOrDefault(id)+1;
        return new Cleanup(()=> {foreach(var id in owners) {if(--leases[id]==0) leases.Remove(id);}});
    }
}
