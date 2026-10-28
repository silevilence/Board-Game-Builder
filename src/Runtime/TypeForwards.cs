using System.Runtime.CompilerServices;
using BoardGame.Runtime;

[assembly:InternalsVisibleTo("Engine.Tests")]

[assembly:TypeForwardedTo(typeof(RuntimeHost))]
[assembly:TypeForwardedTo(typeof(RuntimePluginDefinition))]
[assembly:TypeForwardedTo(typeof(RuntimePluginScope))]
[assembly:TypeForwardedTo(typeof(IAuthoritySession))]
[assembly:TypeForwardedTo(typeof(SessionFault))]

[assembly:TypeForwardedTo(typeof(InteractionIntent))]
