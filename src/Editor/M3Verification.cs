using Avalonia.Controls;
using Avalonia.Media.Imaging;
using BoardGame.Kernel;
using BoardGame.Runtime;
namespace BoardGame.Editor;

internal static class M3Verification
{
    public static Window Create(string output)
    {
        var host=new EditorHost(baseOnly:true);
        host.Window.Opened+=async (_,_)=>
        {
            try
            {
                await host.Ready;var app=host.Application;var docs=app.Service<DocumentWorkspace>("documents");
                var panels=app.Service<PanelContributions>("panels");var commands=app.Service<EditorCommands>("commands");
                docs.Set(ModuleDocument.Open("samples/wasm-counter"));
                await app.Enable("author.extensions");await app.Disable("author.extensions");
                using(var session=new PlaySession(docs.Current!.Snapshot()))
                {
                    for(var i=0;i<3;i++) if(!session.SubmitHuman("p1",session.Actions()[0].Command).Accepted) throw new Exception("UI disable changed WASM runtime");
                    if(!session.Ended) throw new Exception("WASM did not finish");
                }
                docs.Set(ModuleDocument.Create());var baseline=panels.Count;
                var directory=Path.GetDirectoryName(Path.GetFullPath(output))!;Directory.CreateDirectory(directory);
                var source=Path.Combine(directory,"cards.png");
                using(var bitmap=new System.Drawing.Bitmap(240,320))
                {using var graphics=System.Drawing.Graphics.FromImage(bitmap);graphics.Clear(System.Drawing.Color.Orange);graphics.FillRectangle(System.Drawing.Brushes.Blue,120,160,120,160);bitmap.Save(source,System.Drawing.Imaging.ImageFormat.Png);}
                for(var cycle=0;cycle<3;cycle++)
                {
                    await app.Enable("author.images");
                    docs.EditForm("图片导入",Wire.Json(new[] {source,"cards","2","2","false"}));
                    await app.Reload("author.images");
                    if(panels.Count!=baseline+1) throw new Exception("Image panel duplicated");
                    await commands.Execute("images.preview");
                    await commands.Execute("images.apply");
                    if(docs.Current!.Snapshot().ResourceData!.Count!=4) throw new Exception("Image UI import failed");
                    if(cycle==2)
                    {
                        app.Service<WorkspaceShell>("workspace").Tabs.SelectedItem=app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,"author.images"));
                        await Task.Delay(150);
                        using var screenshot=new RenderTargetBitmap(new(1400,900));screenshot.Render(host.Window);screenshot.Save(output+".png",PngBitmapEncoderOptions.Default);
                    }
                    docs.Current.Undo();if(docs.Current.Snapshot().ResourceData!.Count!=0) throw new Exception("Import Undo failed");
                    await app.Disable("author.images");
                    if(panels.Count!=baseline || commands.Items.Any(c=>c.Id.StartsWith("images."))) throw new Exception("Image registration leaked");
                }
                File.WriteAllText(output,Wire.Json(new {success=true,imageCycles=3,wasmWithoutUi=true}));
                docs.Set(null);
            }
            catch(Exception error) {File.WriteAllText(output,Wire.Json(new {success=false,error=error.ToString()}));host.Application.Service<DocumentWorkspace>("documents").Set(null);}
            finally {host.Window.Close();}
        };
        return host.Window;
    }
}
