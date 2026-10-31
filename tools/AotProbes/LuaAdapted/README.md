# Lua strict AOT adaptation experiment

This is an isolated research project, not a supported runtime or alternative
engine plugin. It currently **fails strict NativeAOT compilation** because the
pinned MoonSharp 2.0.0 assembly contains unresolved trim/dynamic-code analysis
errors. Do not publish its managed build as an AOT result.

The experiment changes only the following surfaces:

- Source-generated JSON for `LuaRequest`, `LuaResponse`, and `LuaCallShape`,
  preserving camelCase, rejected unknown fields, and required API identity.
- A closed writer for the already validated Lua result data graph.
- Exact registration roots for the seven HardSandbox core module types, without
  retaining the MoonSharp assembly as a whole.
- An explicit limited platform whose AOT indication uses
  `RuntimeFeature.IsDynamicCodeSupported`.

`LuaExecution.cs` is an experimental copy of the production implementation; only
sandbox initialization and the final JSON writer differ. `LuaProcessSession`,
`LuaProtocol`, and `SeededRandom` are linked directly from production. The test
entry point runs the same real sample scripts as the baseline probe. `--worker`
uses the executable itself as an explicitly selected standalone worker; the
worker protocol is adapted from `src/Lua/Program.cs` to use the generated JSON
contract rather than Kernel `Wire`.

From the repository root:

```powershell
dotnet publish tools/AotProbes/LuaAdapted/LuaAdapted.csproj -c Release -r win-x64 --artifacts-path artifacts/native-aot/lua-adapted/build -o artifacts/native-aot/lua-adapted/native
```

This command must fail until the third-party diagnostics are resolved. It keeps
the repository's warnings-as-errors policy, explicitly requires strict IL
analysis, and has no warning suppressions or whole-assembly preservation.

For semantic comparison only, explicitly opt into an ordinary managed build:

```powershell
dotnet publish tools/AotProbes/LuaAdapted/LuaAdapted.csproj -c Release -r win-x64 -p:PublishAot=false --self-contained true --artifacts-path artifacts/native-aot/lua-adapted/jit-build -o artifacts/native-aot/lua-adapted/jit
artifacts/native-aot/lua-adapted/jit/LuaAdapted.exe
artifacts/native-aot/lua-adapted/jit/LuaAdapted.exe --worker
```

The managed comparison passes the six script cases, deterministic repeats,
instruction budget, and pre-cancelled worker request. It does not establish native
worker memory limits, active cancellation, timeout, or lifecycle correctness.
See [the evidence report](../../../docs/validation/native-aot-lua-adapted.md).
