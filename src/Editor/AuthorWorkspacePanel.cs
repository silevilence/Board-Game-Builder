using Avalonia.Controls;
using BoardGame.Authoring;
using System.Text.Json;

namespace BoardGame.Editor;

public sealed class AuthorWorkspacePanel : StackPanel
{
    public AuthorWorkspacePanel(EditorPluginScope scope)
    {
        Spacing=8;
        var documents=scope.Service<DocumentWorkspace>("documents");
        var commands=scope.Service<EditorCommands>("commands");
        var status=new TextBox {IsReadOnly=true,AcceptsReturn=true,Height=220};
        var merge=new TextBox {AcceptsReturn=true,Height=140,PlaceholderText="合并结果：相对路径到 UTF-8 文本或 null 的 JSON 对象"};
        ProjectIdentity Identity()=>new(documents.Current?.DirectoryPath ?? throw new InvalidOperationException("先将工程保存到目录"),"editor","gui",documents.WorkspaceId,documents.Generation);
        void Show(ExternalStatus observed) => status.Text=$"工作区：{observed.Identity.Workspace}\n文档代次：{observed.Identity.Generation}\n未保存：{observed.Dirty}\n"+
            (observed.Changes.Length==0?"没有外部文件变更":string.Join("\n",observed.Changes.Select(c=>$"{c.Kind}: {c.Path}")));
        void Refresh()
        {
            try
            {
                documents.ExternalMergeBaseline=documents.ExternalStatus(Identity());
                Show(documents.ExternalMergeBaseline);
            }
            catch(Exception error) {documents.ExternalMergeBaseline=null;status.Text=error.Message;}
        }
        void Button(string id,string title,Action action,Func<bool> enabled)
        {
            commands.Register(scope,new(id,title,()=>{try{action();}catch(Exception error){status.Text=error.Message;}return Task.CompletedTask;},enabled));
            var button=new Button {Content=title};button.Click+=async (_,_)=>await commands.Execute(id);Children.Add(button);
            void Update()=>button.IsEnabled=commands.CanExecute(id);
            commands.Changed+=Update;scope.Own(new AuthorCleanup(()=>commands.Changed-=Update));Update();
        }
        bool HasProject()=>documents.Current?.DirectoryPath!=null;
        bool CanResolve()=>HasProject() && documents.ExternalMergeBaseline!=null;
        Button("workspace.external.inspect","检查外部文件修改",Refresh,HasProject);
        void Resolve(string choice)
        {
            var baseline=documents.ExternalMergeBaseline ?? throw new InvalidOperationException("先检查外部文件修改");
            var files=choice=="merge"?JsonSerializer.Deserialize<JsonElement>(merge.Text ?? "{}"):JsonSerializer.SerializeToElement(new {});
            documents.ResolveExternal(Identity(),Guid.NewGuid().ToString("N"),JsonSerializer.SerializeToElement(new {baseline.DiskVersion,baseline.WorkspaceVersion,choice,files},AuthorProfiles.Json));
            RestoreInput();
            Refresh();
        }
        Button("workspace.external.reload","重载磁盘（仅无草稿时）",()=>Resolve("reload"),()=>CanResolve() && !documents.Dirty);
        Button("workspace.external.disk","使用磁盘并放弃工作区草稿",()=>Resolve("disk"),CanResolve);
        Button("workspace.external.workspace","保留工作区并写入磁盘",()=>Resolve("workspace"),CanResolve);
        Children.Add(status);Children.Add(merge);Button("workspace.external.merge","应用合并结果并保存",()=>Resolve("merge"),CanResolve);
        var restoring=false;
        void RestoreInput()
        {
            restoring=true;
            try {merge.Text=documents.Current==null?"":documents.Form(DocumentWorkspace.ExternalMergeForm,()=>"{}",baseline:"").Text;}
            catch(Exception error) {status.Text=error.Message;}
            finally {restoring=false;}
        }
        merge.TextChanged+=(_,_)=> {if(!restoring && documents.Current!=null) documents.EditForm(DocumentWorkspace.ExternalMergeForm,merge.Text ?? "");};
        void Replaced() {RestoreInput();Refresh();}
        documents.Replaced+=Replaced;scope.Own(new AuthorCleanup(()=>documents.Replaced-=Replaced));
        scope.OnDocumentChanged(()=>{RestoreInput();commands.Refresh();});
        RestoreInput();
        if(documents.ExternalMergeBaseline is {} baseline) Show(baseline);else Refresh();
        commands.Refresh();
    }
}
