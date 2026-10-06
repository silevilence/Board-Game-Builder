namespace BoardGame.Editor;

// Document lifetime belongs to a required foundation plugin; views only subscribe.
public sealed class DocumentWorkspace : IDisposable
{
    public ModuleDocument? Current { get; private set; }
    public event Action? Changed;
    public event Action? Replaced;
    public void Set(ModuleDocument? document)
    {
        if(Current!=null) Current.Changed-=Notify;
        Current=document;
        if(Current!=null) Current.Changed+=Notify;
        Replaced?.Invoke();Notify();
    }
    private void Notify()=>Changed?.Invoke();
    public void Dispose() {if(Current!=null) Current.Changed-=Notify;Current=null;Changed=null;Replaced=null;}
}
