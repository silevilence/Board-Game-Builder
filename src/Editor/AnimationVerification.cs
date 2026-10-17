using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using BoardGame.Authoring;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;

namespace BoardGame.Editor;

internal static class AnimationVerification
{
    public static Window Create(string source,string output)
    {
        var host=new EditorHost(source,baseOnly:true);
        host.Window.Opened+=async (_,_)=>
        {
            var checks=new List<string>();string? error=null;
            try
            {
                await host.Ready;var app=host.Application;var docs=app.Service<DocumentWorkspace>("documents");
                FormatPanel Wrapper()=> (FormatPanel)app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,"author.animation")).Content!;
                AnimationPanel Panel()=>(AnimationPanel)Wrapper().Content!;
                T Find<T>(string name) where T:Control=>Panel().GetLogicalDescendants().OfType<T>().Single(c=>c.Name==name);
                void Click(string name)=>Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await app.Enable("author.animation");await Wrapper().Ready;
                var workspace=app.Service<AnimationWorkspace>("animation.author");var original=workspace.Read();
                Find<TextBox>("animationDefinition").Text=AnimationFile.Write(original.Definition)+"\n";
                if(!docs.HasPendingForms)throw new Exception("Field draft not retained");
                await app.Reload("author.animation");await Wrapper().Ready;
                if(!Find<TextBox>("animationDefinition").Text!.EndsWith('\n'))throw new Exception("Panel rebuild lost fields");
                Click("animationApply");if(docs.HasPendingForms)throw new Exception("Apply retained pending form");
                docs.Undo();docs.Redo();docs.Save();docs.Set(ModuleDocument.Open(source));await Wrapper().Ready;
                checks.Add("edit-apply-undo-redo-save-reopen-panel-rebuild");
                for(var cycle=0;cycle<2;cycle++)
                {
                    var tabs=app.Service<WorkspaceShell>("workspace").Tabs;tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,"author.animation"));
                    Click("animationPreview");await Task.Delay(250);
                    if(Panel().PreviewTable is not {} preview || preview.RenderedFrames==0)throw new Exception("Preview did not render");
                    preview.SaveImage(output+".preview-"+cycle+".png");
                    await app.Disable("author.animation");
                    if(DesktopViewport.LiveDevices!=0 || app.Service<EditorCommands>("commands").Items.Any(c=>c.Id.StartsWith("animation.")))throw new Exception("Plugin leaked viewport or commands");
                    await app.Enable("author.animation");await Wrapper().Ready;
                }
                checks.Add("real-preview-two-enable-disable-cycles");
                var invalid=original.Definition with {Clips=[original.Definition.Clips[0] with {Tracks=[original.Definition.Clips[0].Tracks[0] with {Entity="missing"}]}]};
                Find<TextBox>("animationDefinition").Text=AnimationFile.Write(invalid);Click("animationPreview");
                if(Panel().PreviewTable!=null || !docs.HasPendingForms)throw new Exception("Failed preview leaked or erased draft");
                docs.DiscardForms();
                Click("animationPreview");docs.Set(ModuleDocument.Create());await Wrapper().Ready;
                if(DesktopViewport.LiveDevices!=0 || Wrapper().Content is not TextBlock)throw new Exception("Document switch retained preview");
                checks.Add("failed-preview-and-document-switch-cleanup");
                await app.DisposeAsync();
            }
            catch(Exception e) {error=e.ToString();}
            finally
            {
                await host.Application.DisposeAsync();
                File.WriteAllText(output,Wire.Json(new{success=error==null,error,checks}));host.Window.Close();
            }
        };
        return host.Window;
    }
}
