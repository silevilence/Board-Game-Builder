# No-script board NativeAOT prototype

This research probe runs production `BoardApplication`, Kernel, update scheduling,
formal `GameRuntime.Load` / `GamePackage.Pack`, and authority recording. It selects
no Lua, WASM, graphics, Editor, or author YAML implementation.

The default project references the production `BoardGame.Hosting` project. The
ordinary baseline uses `-p:PublishAot=false`. The isolated candidate uses copied
production sources, static JSON contracts, closed ordinal dictionary converters,
named fingerprint inputs, and runtime-only module primitives. It is deliberately
a single-assembly experiment and is **not** a proposed production dependency graph.

`prepare.py` creates a new source snapshot from pinned pre-adaptation commit
`b10583e`, applies the Core JSON candidate, and refuses to overwrite a directory.
It changes no production source or NuGet cache. `WireStatic.cs` is compiled only
for the candidate. Existing type forwarding declarations are omitted only because
their source and destination types are in the same experimental assembly.

Use the statically adapted CordiSharp candidate from the Core prototype:

```powershell
pwsh -NoProfile -File tools/AotProbes/Board/run.ps1 -CordiSharpCandidate artifacts/native-aot/core/build-patched-v4/bin/CordiSharp.Patched/release/CordiSharp.dll -Output artifacts/native-aot/board/new-run
```

The script requires a new output directory, publishes production JIT and strict
NativeAOT into distinct directories, runs both, and requires exact output equality.
It keeps all compiler diagnostics and fails on either compilation or runtime
failure. No warning suppression, full-assembly preservation, or dynamic loading
is used. It records the candidate CordiSharp DLL hash and native output manifest.

The executable needs no input or source files. It creates a temporary game fixture,
packs and reloads it, executes a seeded shuffle/draw/finish sequence twice, restores
recordings, resumes a checkpoint, rejects tampered recordings, and checks ordinal
dictionary semantics. It retains its small temporary game fixtures for inspection.
Run the resulting native executable in clean Windows separately; success on the
development host alone does not prove clean-environment compatibility.

See [the report](../../../docs/validation/native-aot-board-prototype.md) for actual
results, adaptation boundaries, and production integration requirements.
