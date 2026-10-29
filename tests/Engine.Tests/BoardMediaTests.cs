using BoardGame.Authoring;
using BoardGame.Editor;
using System.Text.Json;

namespace Engine.Tests;
public static class BoardMediaTests
{
    public static void Run()
    {
        if(OperatingSystem.IsWindowsVersionAtLeast(6,1)) Windows().GetAwaiter().GetResult();
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static async Task Windows()
    {
        Check.That(!typeof(BoardTtsImport).Assembly.GetReferencedAssemblies().Any(a=>a.Name!.Contains("Editor") || a.Name.StartsWith("Avalonia")),"Author algorithms depend on UI");
        var root=Path.Combine(Path.GetTempPath(),"bgb-media-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var project=Path.Combine(root,"project");ModuleDocument.Create().Save(project);
            var diskBefore=File.ReadAllBytes(Path.Combine(project,"module.json"));
            using var documents=new DocumentWorkspace();documents.Set(ModuleDocument.Open(project));
            var author=new AuthorImportWorkspace(documents);
            documents.EditDraft("module.json",documents.Draft("module.json").Text.Replace("play","draft-stage"));
            var before=author.View();var preview=BoardTtsImport.Preview(author.Source(),"samples/tts-subset/save.json","shared");
            documents.Form("import",()=>"parameters");author.Apply("import",preview);
            var after=author.View();var content=BoardPrintSource.Load(after);
            Check.That(content.Initial.Phases.Contains("draft-stage") && content.Resources!.Images.Count==5,"Import ignored the file draft");
            Check.That(File.ReadAllBytes(Path.Combine(project,"module.json")).SequenceEqual(diskBefore),"Import silently saved");
            documents.Undo();Check.That(author.View().Source.Version==before.Source.Version,"Import Undo was not one workspace edit");
            documents.Redo();Check.That(author.View().Source.Version==after.Source.Version);
            var stale=BoardTtsImport.Preview(author.Source(),"samples/tts-subset/save.json","stale");
            documents.EditDraft("rules.lua",documents.Draft("rules.lua").Text+"\n-- change");
            documents.Form("import",()=>"parameters");Check.Throws(()=>author.Apply("import",stale),"重新预览");
            Check.That(!author.View().Paths.Any(p=>p.Contains("stale")),"Conflict partially applied files");
            documents.Set(ModuleDocument.Open(project));documents.Form("import",()=>"parameters");
            Check.Throws(()=>author.Apply("import",stale),"generation");

            // The standalone author host uses the exact same algorithms without an Editor instance.
            var profile=new EffectiveProfile("media","test",project,new(1,[new("edit.disk"),new("author.module"),new("author.tts"),new("author.print")]));
            await using(var host=await AuthorHost.Start(profile,AuthorHost.Builtins))
            {
                var receipt=(EditReceipt)await host.Invoke("board.import.tts",JsonSerializer.SerializeToElement(new{source=Path.GetFullPath("samples/tts-subset/save.json"),prefix="shared"}),"import");
                Check.That(receipt.State==EditState.Applied && receipt.Saved);
                var loaded=BoardGame.Runtime.GameRuntime.Load(project);var ids=loaded.Initial.Entities.Values.Where(e=>!e.IsContainer && e.Properties.GetValueOrDefault("viewTablecloth")!=1).Select(e=>e.Id).ToArray();
                var pdf=Path.Combine(root,"cards.pdf");
                await host.Invoke("board.print",JsonSerializer.SerializeToElement(new{target=pdf,entities=ids}),"print");
                Check.That(File.ReadAllBytes(pdf).Length>1000,"Standalone print did not emit PDF");
                var guiPlan=PrintLayout.Create(content,ids,new());var diskPlan=PrintLayout.Create(loaded,ids,new());
                Check.That(guiPlan.Pages.Count==diskPlan.Pages.Count && guiPlan.Pages.SelectMany(p=>p).Zip(diskPlan.Pages.SelectMany(p=>p)).All(pair=>pair.First.Image.SequenceEqual(pair.Second.Image)),"GUI and standalone imposition differ");
            }
            using var unsupported=new DocumentWorkspace(false);unsupported.Set(ModuleDocument.Open("samples/motion-lab"));
            Check.Throws(()=>new AuthorImportWorkspace(unsupported).Source(),"格式");
        }
        finally {Directory.Delete(root,true);}
    }
}
