using BoardGame.Runtime;

namespace BoardGame.Authoring;

public static class BoardApplicationAuthor
{
    public static AuthorPlugin Plugin=>new("author.runtime.board-application","1",1,[],["author.runtimes","author.runtime-plugins"],scope=>
        scope.Own(scope.Service<AuthorRuntimes>("author.runtimes").Register(new ApplicationAuthorRuntime("board.application",BoardApplicationPlugins.Catalog(),
            scope.Service<BoardGame.Plugins.PluginStore>("author.runtime-plugins")))),"Explicit board application validation and publication")
        {Includes=[new("author.runtime.plugins")]};
}
