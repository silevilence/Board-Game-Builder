using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.VisualTree;
namespace BoardGame.View;

public static class TableKeyInput
{
    // Native viewport keys arrive via WM_KEYDOWN; toolbar/action-button keys arrive via Avalonia.
    public static void Attach(Control root,Action<string> handle)
    {
        var pressed=new HashSet<Key>();
        root.AddHandler(InputElement.KeyDownEvent,(_,e)=>
        {
            if(e.Source is Avalonia.Visual visual && visual.GetSelfAndVisualAncestors().OfType<TextBox>().Any()) return;
            var code=TableKeys.Named(e.Key.ToString(),e.KeyModifiers.HasFlag(KeyModifiers.Control),e.KeyModifiers.HasFlag(KeyModifiers.Alt));
            if(code==null) return;
            e.Handled=true; if(pressed.Add(e.Key)) handle(code);
        },RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.KeyUpEvent,(_,e)=>pressed.Remove(e.Key),RoutingStrategies.Tunnel);
        root.LostFocus+=(_,_)=>pressed.Clear();
    }
}
