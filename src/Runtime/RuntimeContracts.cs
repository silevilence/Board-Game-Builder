using BoardGame.Kernel;

namespace BoardGame.Runtime;

public interface IDefinitionLoader
{
    GameDefinition Load(string directory);
    IReadOnlyList<string> ContentFiles(string directory);
    string Source { get; }
}
