using Avalonia.Controls;
using BoardGame.Kernel;

namespace BoardGame.Editor;

// Exercises the actual installed plugin services without repository paths or CLI dependencies.
internal static class ToolkitVerification
{
    public static Window Create(string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        var host=new EditorHost();
        host.Window.Opened+=async (_,_)=>
        {
            var checks=new List<string>();string? failure=null;
            void Check(bool valid,string name) {if(!valid)throw new Exception(name);checks.Add(name);}
            try
            {
                await host.Ready;
                var documents=host.Application.Service<DocumentWorkspace>("documents");
                documents.Set(ModuleDocument.Create());
                documents.Location=Path.Combine(output,"project");
                documents.Save();
                var script=documents.Current!.Text("rules.lua");
                documents.EditDraft("rules.lua",script+"\n-- installed editor draft\n");
                documents.Save();documents.Set(ModuleDocument.Open(documents.Location));
                Check(documents.Current!.Text("rules.lua").Contains("installed editor draft"),"Create, edit, save and reopen");
                var sessions=host.Application.Service<SessionController>("sessions");
                Check(File.Exists(sessions.Executable),"Bundled debug Player located");
                foreach(var external in new[]{false,true})
                {
                    sessions.External=external;
                    foreach(var seat in sessions.CpuSeats.Keys.ToArray())sessions.CpuSeats[seat]=true;
                    await sessions.Start();await sessions.Send(new("pause"));
                    Check(sessions.Frame!=null,"Start "+(external?"external":"embedded")+" session: "+sessions.Status);
                    for(var step=0;step<500 && sessions.Frame is {View.Ended:false,Fault:null};step++)
                        await sessions.Send(new("step",sessions.Frame.Revision));
                    Check(sessions.Frame is {View.Ended:true,Fault:null},"Complete "+(external?"external":"embedded")+" game");
                    sessions.Stop();
                }
                var exports=host.Application.Service<ExportWorkflow>("exports");
                Check(File.Exists(Path.Combine(exports.ReleasePath,"release.json")),"Bundled formal Player located");
                exports.Destination=Path.Combine(output,"game");
                Check(await exports.Run(true),"Formal export: "+exports.Progress);
                Check(File.Exists(Path.Combine(exports.Destination,"Play.exe")),"Independent game entry");
            }
            catch(Exception error) {failure=error.ToString();}
            finally
            {
                await host.Application.DisposeAsync();
                File.WriteAllText(Path.Combine(output,"editor.json"),Wire.Json(new {success=failure==null,checks,error=failure}));
                Environment.ExitCode=failure==null?0:1;host.Window.Close();
            }
        };
        return host.Window;
    }
}
