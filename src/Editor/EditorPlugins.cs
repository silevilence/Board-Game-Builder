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
            var tree=new TreeView();
            void Refresh()
            {
                tree.Items.Clear(); if(context.Document is not {} doc) return;
                var files=new TreeViewItem {Header="模块文件",IsExpanded=true};
                foreach(var path in doc.Paths) files.Items.Add(new TreeViewItem {Header=path}); tree.Items.Add(files);
                foreach(var group in new[]{"resources","entities","prototypes"})
                {
                    var branch=new TreeViewItem {Header=group,IsExpanded=true};
                    var data=doc.Configuration[group];
                    if(data is System.Text.Json.Nodes.JsonArray list) foreach(var item in list) branch.Items.Add(new TreeViewItem {Header=item!["id"]!.GetValue<string>()});
                    if(data is System.Text.Json.Nodes.JsonObject map) foreach(var item in map) branch.Items.Add(new TreeViewItem {Header=item.Key+" → "+item.Value});
                    tree.Items.Add(branch);
                }
            }
            context.Register(ExtensionKind.Panel,"资源树",tree);
            context.Register(ExtensionKind.Inspector,"原型与 YAML",new AuthorPanel(context));
            context.OnDocumentChanged(Refresh); Refresh();
        }
    }
}
