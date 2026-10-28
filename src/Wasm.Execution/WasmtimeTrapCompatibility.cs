using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using Wasmtime;

namespace BoardGame.Wasm;

// Wasmtime 48.0.2 contains the native option but predates its managed binding.
// Replace this shim with Config.WithSignalsBasedTraps(false) when upgrading to
// a release containing https://github.com/bytecodealliance/wasmtime-dotnet/pull/390.
internal static class WasmtimeTrapCompatibility
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicProperties,typeof(Config))]
    internal static void UseExplicitChecks(Config config)
    {
        var property=typeof(Config).GetProperty("NativeHandle",BindingFlags.Instance|BindingFlags.NonPublic);
        if(property?.GetValue(config) is not SafeHandle handle || handle.IsInvalid || handle.IsClosed)
            throw new NotSupportedException("The pinned Wasmtime binding no longer exposes its configuration handle; update the explicit-trap compatibility binding.");
        // SafeHandle marshalling holds the owner alive throughout the native call.
        // No fallback: signal-based trap recovery can terminate a CET-enabled host.
        SetSignalsBasedTraps(handle,false);
    }

    [DllImport("wasmtime",EntryPoint="wasmtime_config_signals_based_traps_set",CallingConvention=CallingConvention.Cdecl)]
    private static extern void SetSignalsBasedTraps(SafeHandle config,[MarshalAs(UnmanagedType.I1)] bool enabled);
}
