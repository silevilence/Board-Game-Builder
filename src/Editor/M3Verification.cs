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
                await host.Ready;await host.Application.Enable("author.runtime.board");await host.Application.Enable("author.module");var app=host.Application;
                async Task ReadyPanels()
                {
                    foreach(var panel in app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Select(t=>t.Content).OfType<FormatPanel>()) await panel.Ready;
                }
                async Task Enable(string id) {await app.Enable(id);await ReadyPanels();}
                async Task Reload(string id) {await app.Reload(id);await ReadyPanels();}
                var docs=app.Service<DocumentWorkspace>("documents");
                var panels=app.Service<PanelContributions>("panels");var commands=app.Service<EditorCommands>("commands");
                docs.Set(ModuleDocument.Open("samples/wasm-counter"));
                await Enable("author.extensions");await app.Disable("author.extensions");
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
                    await Enable("author.images");
                    docs.EditForm("图片导入",Wire.Json(new[] {source,"cards","2","2","false"}));
                    await Reload("author.images");
                    if(panels.Count!=baseline+1) throw new Exception("Image panel duplicated");
                    await commands.Execute("images.preview");
                    docs.EditForm("图片导入",Wire.Json(new[] {source,"changed","2","2","false"}));
                    await commands.Execute("images.apply");
                    if(docs.Current!.Snapshot().Resources!.Images.Count!=0 || !docs.HasPendingForms) throw new Exception("Stale image preview consumed newer fields");
                    docs.EditForm("图片导入",Wire.Json(new[] {source,"cards","2","2","false"}));
                    await commands.Execute("images.apply");
                    if(docs.Current!.Snapshot().Resources!.Images.Count!=4) throw new Exception("Image UI import failed");
                    if(cycle==2)
                    {
                        app.Service<WorkspaceShell>("workspace").Tabs.SelectedItem=app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,"author.images"));
                        await Task.Delay(150);
                        using var screenshot=new RenderTargetBitmap(new(1400,900));screenshot.Render(host.Window);screenshot.Save(output+".png",PngBitmapEncoderOptions.Default);
                    }
                    docs.Current.Undo();if(docs.Current.Snapshot().Resources!.Images.Count!=0) throw new Exception("Import Undo failed");
                    await app.Disable("author.images");
                    if(panels.Count!=baseline || commands.Items.Any(c=>c.Id.StartsWith("images."))) throw new Exception("Image registration leaked");
                }
                for(var cycle=0;cycle<3;cycle++)
                {
                    await Enable("author.tts");docs.EditForm("TTS 导入",Wire.Json(new[] {"samples/tts-subset/save.json","tts"}));
                    await Reload("author.tts");await commands.Execute("tts.preview");
                    docs.EditForm("TTS 导入",Wire.Json(new[] {"samples/tts-subset/save.json","changed"}));await commands.Execute("tts.apply");
                    if(docs.Current!.Snapshot().Resources!.Images.Count!=0 || !docs.HasPendingForms) throw new Exception("Stale TTS preview consumed newer fields");
                    docs.EditForm("TTS 导入",Wire.Json(new[] {"samples/tts-subset/save.json","tts"}));await commands.Execute("tts.apply");
                    if(docs.Current!.Snapshot().Resources!.Images.Count!=5) throw new Exception("TTS UI conversion failed");
                    docs.Current.Undo();await app.Disable("author.tts");
                    if(panels.Count!=baseline || commands.Items.Any(c=>c.Id.StartsWith("tts."))) throw new Exception("TTS contributions leaked");
                }
                TtsImport.Preview(docs.Current!,"samples/tts-subset/save.json","print").Apply(docs.Current!);
                var printIds=string.Join("\n",docs.Current!.Snapshot().Initial.Entities.Values.Where(e=>!e.IsContainer && e.Properties.GetValueOrDefault("viewTablecloth")!=1).Select(e=>e.Id));
                var pdfs=new List<string>();
                for(var cycle=0;cycle<3;cycle++)
                {
                    var pdf=output+"."+cycle+".pdf";await Enable("author.print");
                    docs.EditForm("打印拼版",Wire.Json(new[] {printIds,"63","88","10","3","long-edge",pdf}));
                    await Reload("author.print");await commands.Execute("print.preview");
                    if(cycle==0)
                    {
                        app.Service<WorkspaceShell>("workspace").Tabs.SelectedItem=app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,"author.print"));await Task.Delay(150);
                        using var screenshot=new RenderTargetBitmap(new(1400,900));screenshot.Render(host.Window);screenshot.Save(output+".print.png",PngBitmapEncoderOptions.Default);
                    }
                    await commands.Execute("print.export");if(!File.Exists(pdf)) throw new Exception("PDF UI export failed");pdfs.Add(pdf);
                    await app.Disable("author.print");if(panels.Count!=baseline || commands.Items.Any(c=>c.Id.StartsWith("print."))) throw new Exception("Print contribution leaked");
                }
                File.WriteAllText(output,Wire.Json(new {success=true,imageCycles=3,ttsCycles=3,printCycles=3,pdfs,wasmWithoutUi=true}));
                docs.Set(null);
            }
            catch(Exception error) {File.WriteAllText(output,Wire.Json(new {success=false,error=error.ToString()}));host.Application.Service<DocumentWorkspace>("documents").Set(null);}
            finally {host.Window.Close();}
        };
        return host.Window;
    }
}
