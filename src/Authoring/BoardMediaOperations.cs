using System.Runtime.Versioning;
using System.Text.Json;

namespace BoardGame.Authoring;

// Separate author plugins share the same import and imposition algorithms as the GUI.
public static class BoardMediaOperations
{
    private static AuthorOperationInfo ImportInfo(string kind)=>new("board.import."+kind,"author."+kind,
        "Import a board image/deck or TTS subset from the selected project snapshot; never execute source scripts",
        new(){["source"]=new("string",true,"External image or TTS JSON file"),["prefix"]=new("string",true,"New object/resource prefix"),
            ["rows"]=new("integer",false,"Image rows, default 1"),["columns"]=new("integer",false,"Image columns, default 1"),
            ["tablecloth"]=new("boolean",false,"Import one image as a tablecloth")},
        new(){["type"]="object"},["board.author-model"],["project"]);
    private static AuthorOperationInfo PrintInfo=>new("board.print","author.print","Export the selected board cards to a new PDF using the current project snapshot",
        new(){["target"]=new("string",true,"New PDF outside the project"),["entities"]=new("array",true,"Card IDs"),["options"]=new("object",false,"PrintOptions in millimetres")},
        new(){["type"]="object"},["Board runtime content","Windows"],["artifact"]);
    public static AuthorPlugin Images=>ImportPlugin("images");
    public static AuthorPlugin Tts=>ImportPlugin("tts");
    private static AuthorPlugin ImportPlugin(string kind)
    {
        var info=ImportInfo(kind);
        return new(info.Plugin,"1",1,[],["author.operations"],scope=>scope.Register(new(info,async call=>
        {
            if(!OperatingSystem.IsWindowsVersionAtLeast(6,1))throw new PlatformNotSupportedException("Board image import requires Windows");
            var format=call.Formats.Resolve(call.View) ?? throw new AuthorOperationException("format.unavailable","Enable the matching author format");
            var source=new BoardImportSource(call.View,format);
            var preview=kind=="images"?BoardImageImport.Preview(source,call.String("source"),call.String("prefix"),call.Integer("rows",1),call.Integer("columns",1),
                call.Arguments.TryGetProperty("tablecloth",out var cloth) && cloth.GetBoolean()):BoardTtsImport.Preview(source,call.String("source"),call.String("prefix"));
            var changes=preview.Changes(call.RequestId);call.Cancellation.ThrowIfCancellationRequested();
            return await call.Mode.Apply(changes,call.Cancellation);
        })),"Board "+kind+" import") {Operations=[info]};
    }
    public static AuthorPlugin Print=>new("author.print","1",1,[],["author.operations"],scope=>scope.Register(new(PrintInfo,call=>
    {
        if(!OperatingSystem.IsWindowsVersionAtLeast(6,1))throw new PlatformNotSupportedException("Board printing requires Windows");
        var target=Path.GetFullPath(call.String("target"));var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(call.View.Source.Identity.Directory));
        if(target.Equals(root,StringComparison.OrdinalIgnoreCase) || target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new AuthorOperationException("output.project","Choose a PDF outside the source project",target);
        var ids=call.Arguments.GetProperty("entities").EnumerateArray().Select(v=>v.GetString() ?? throw new InvalidDataException("Card ID is required")).ToArray();
        var options=call.Arguments.TryGetProperty("options",out var value)?value.Deserialize<PrintOptions>(AuthorProfiles.Json) ?? new():new PrintOptions();
        var plan=PrintLayout.Create(BoardPrintSource.Load(call.View),ids,options);
        call.Cancellation.ThrowIfCancellationRequested();PrintLayout.Export(plan,target);
        return Task.FromResult<object>(new {call.View.Source,target,kind="print-pdf",pages=plan.Pages.Count});
    })),"Board print imposition") {Operations=[PrintInfo]};
}
