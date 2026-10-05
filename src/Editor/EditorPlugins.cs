using Avalonia.Controls;
using Avalonia.Input;
using BoardGame.Kernel;
namespace BoardGame.Editor;

public sealed partial class EditorWindow
{
    private readonly PluginHost plugins=new();
    private readonly List<TabItem> pluginTabs=[];
    private readonly List<KeyBinding> pluginKeys=[];
    private readonly List<Button> pluginMenus=[];
    private void InitializePlugins()
    {
        plugins.RegistrationsChanged+=RefreshPlugins;
        plugins.Install(Wire.Json(new PluginManifest("official.workspace",1,["workspace"],[])),()=>new WorkspacePlugin());
        Add("启用工作区插件",()=>Run(()=>plugins.Enable("official.workspace")));
        Add("停用工作区插件",()=>Run(()=>plugins.Disable("official.workspace")));
        Add("重载工作区插件",()=>Run(()=>plugins.Reload("official.workspace")));
        plugins.Enable("official.workspace");
        Closed+=(_,_)=>plugins.Dispose();
    }
    private void RefreshPlugins()
    {
        foreach(var tab in pluginTabs) tabs.Items.Remove(tab); pluginTabs.Clear();
        foreach(var key in pluginKeys) KeyBindings.Remove(key); pluginKeys.Clear();
        foreach(var button in pluginMenus) toolbar.Children.Remove(button); pluginMenus.Clear();
        foreach(var extension in plugins.Extensions)
        {
            if(extension.Kind is ExtensionKind.Panel or ExtensionKind.Inspector && extension.Value is Control panel)
            { var tab=new TabItem {Header=extension.Id,Content=panel}; tabs.Items.Add(tab); pluginTabs.Add(tab); }
            if(extension.Kind==ExtensionKind.Shortcut && extension.Value is KeyBinding binding)
            { KeyBindings.Add(binding); pluginKeys.Add(binding); }
            if(extension.Kind is ExtensionKind.Menu or ExtensionKind.Importer && extension.Value is Func<Task> action)
            {
                var button=new Button {Content=extension.Id};
                button.Click+=async (_,_)=> { try { await action(); } catch(Exception e) { status.Text=e.Message; } };
                toolbar.Children.Add(button); pluginMenus.Add(button);
            }
        }
    }
    private sealed class WorkspacePlugin : IEditorPlugin
    {
        public void Activate(PluginContext context)
        {
            var text=new TextBlock();
            void Refresh() => text.Text=context.Document==null?"没有模块文档":string.Join("\n",context.Document.Paths);
            context.Register(ExtensionKind.Panel,"资源树",text);
            context.OnDocumentChanged(Refresh); Refresh();
        }
    }
}
