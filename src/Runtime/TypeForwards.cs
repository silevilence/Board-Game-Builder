using System.Runtime.CompilerServices;
using BoardGame.Runtime;

[assembly:InternalsVisibleTo("Engine.Tests")]

[assembly:TypeForwardedTo(typeof(RuntimeHost))]
[assembly:TypeForwardedTo(typeof(RuntimePluginDefinition))]
[assembly:TypeForwardedTo(typeof(RuntimePluginScope))]
[assembly:TypeForwardedTo(typeof(IAuthoritySession))]
[assembly:TypeForwardedTo(typeof(SessionFault))]

[assembly:TypeForwardedTo(typeof(InteractionIntent))]

[assembly:TypeForwardedTo(typeof(IRuleProvider))]
[assembly:TypeForwardedTo(typeof(ICpuDecisionProvider))]
[assembly:TypeForwardedTo(typeof(IEffectProvider))]
[assembly:TypeForwardedTo(typeof(WasmPlugins))]

[assembly:TypeForwardedTo(typeof(SessionRecording))]
[assembly:TypeForwardedTo(typeof(ScriptSwitch))]
[assembly:TypeForwardedTo(typeof(IRecordingProvider))]
[assembly:TypeForwardedTo(typeof(IGameSession))]
[assembly:TypeForwardedTo(typeof(BoardUpdateRequest))]
[assembly:TypeForwardedTo(typeof(BoardUpdateResult))]
[assembly:TypeForwardedTo(typeof(IBoardUpdateState))]
