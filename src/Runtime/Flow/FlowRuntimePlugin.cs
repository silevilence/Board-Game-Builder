using BoardGame.Kernel;

namespace BoardGame.Runtime;

/// <summary>Installs flow rules and privileged progression without a game session or driver.</summary>
public static class FlowRuntimePlugin
{
    public static RuntimePluginDefinition Create(Func<LoadedModule> module,CancellationToken lifetime=default,bool restored=false)
        =>FlowController.Plugin(module,lifetime,restored);
}
