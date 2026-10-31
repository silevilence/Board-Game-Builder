"""Create an isolated candidate; production sources are never changed."""
from pathlib import Path
import re
import shutil
import sys

repo = Path(__file__).resolve().parents[3]
target = Path(sys.argv[1]).resolve()
if target.exists():
    raise SystemExit(f"Candidate already exists: {target}")
projects = ["Application.Abstractions", "Application.Core", "Resources.Abstractions", "Resources.Core",
            "Kernel.Abstractions", "Kernel", "BoardGame.Core", "BoardGame.Session", "BoardGame.Recording", "BoardGame.Hosting"]
for name in projects:
    (target / name).mkdir(parents=True)
    for file in (repo / "src" / name).glob("*.cs"):
        shutil.copyfile(file, target / name / file.name)
(target / "Modules").mkdir()
shutil.copyfile(repo / "src/Modules/ModuleSnapshot.cs", target / "Modules/ModuleSnapshot.cs")
# Extract only the exact data contract and hash primitive needed at runtime.
# Historical package parsing and author YAML remain in their original sources.
module = (repo / "src/Modules/ModulePackage.cs").read_text(encoding="utf-8-sig")
contract = module[module.index("public sealed record RuntimeDependency"):module.index("public sealed record PackageManifest")]
hash_line = next(line for line in module.splitlines() if "public static string Hash(byte[]" in line)
(target / "Modules/RuntimeModulePrimitives.cs").write_text(
    "using System.Security.Cryptography;\nusing System.Text.Json.Serialization;\nnamespace BoardGame.Modules;\n"
    + contract + "\npublic static class ModulePackage {\n" + hash_line + "\n}\n", encoding="utf-8")

def replace(file, before, after):
    path = target / file
    source = path.read_text(encoding="utf-8-sig")
    if before not in source:
        raise SystemExit(f"Source changed: {file}: {before}")
    path.write_text(source.replace(before, after), encoding="utf-8")

model = target / "Kernel.Abstractions/Model.cs"
source = model.read_text(encoding="utf-8-sig")
model.write_text(source[:source.index("public static class Wire")] + source[source.index("public sealed record Seat"):], encoding="utf-8")
replace("Kernel.Abstractions/FlowState.cs", "new {GameContext,NodeContext}", "new FlowContextInput(GameContext,NodeContext)")
replace("Kernel/Replay.cs", "new { fingerprint,seed,initial }", "new ReplayHeaderInput(fingerprint,seed,initial)")
replace("Kernel/Replay.cs", "new { submission,effects,stateHash,previousHash }", "new ReplayEntryInput(submission,effects,stateHash,previousHash)")
replace("Modules/ModuleSnapshot.cs", "new {\n        id=module.Id,version=module.Version,state=module.Initial,rules=module.Rules,cpu=module.Cpu,\n        resources=module.ResourceHashes ?? new SortedDictionary<string,string>(StringComparer.Ordinal),runtime=module.RuntimeHashes }",
        "new ModuleFingerprintInput(module.Id,module.Version,module.Initial,module.Rules,module.Cpu,module.ResourceHashes ?? new SortedDictionary<string,string>(StringComparer.Ordinal),module.RuntimeHashes)")
replace("BoardGame.Recording/SessionRecording.cs", 'new {recording.Log,recording.Switches,recording.Versions,Hash=""}', 'new LegacyRecordingInput(recording.Log,recording.Switches,recording.Versions,"")')
replace("BoardGame.Hosting/BoardApplication.cs", "private sealed record Payload", "internal sealed record Payload")
for name in ["BoardGame.Hosting/BoardApplication.cs", "BoardGame.Core/RuntimeDefinition.cs", "BoardGame.Core/BoardStateDocument.cs"]:
    path = target / name
    source = path.read_text(encoding="utf-8")
    source = re.sub(r"\.Deserialize<(.+?)>\(Wire.Options\)", r".Deserialize(Wire.Type<\1>())", source)
    source = source.replace("JsonSerializer.SerializeToElement(", "Wire.Element(").replace(",Wire.Options)", ")")
    path.write_text(source, encoding="utf-8")
replace("BoardGame.Session/BoardUpdate.cs", "JsonSerializer.Deserialize<BoardUpdateRequest>(input.Data.Span,Wire.Options)", "JsonSerializer.Deserialize(input.Data.Span,Wire.Type<BoardUpdateRequest>())")
replace("BoardGame.Core/BoardRenderContract.cs", "JsonSerializer.SerializeToElement(new{})", 'Wire.Empty()')
print(target)
