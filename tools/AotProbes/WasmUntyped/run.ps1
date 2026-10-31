param([string]$Output = 'artifacts/native-aot/wasm/untyped')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$destination = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $destination) { throw "Evidence directory already exists: $destination" }
New-Item -ItemType Directory -Path $destination | Out-Null
$production = Join-Path $repo 'src/Wasm.Execution/WasmExecutor.cs'
$source = Get-Content -LiteralPath $production -Raw
$marker = 'public sealed class WasmCall'
$offset = $source.IndexOf($marker, [StringComparison]::Ordinal)
if ($offset -lt 0 -or $source.IndexOf($marker, $offset + $marker.Length, [StringComparison]::Ordinal) -ge 0) {
    throw 'Production WasmCall boundary changed; inspect candidate generation before continuing'
}
$candidate = Join-Path $destination 'WasmExecutor.cs'
($source.Substring(0, $offset) + (Get-Content -LiteralPath "$PSScriptRoot/WasmCall.cs" -Raw)) |
    Set-Content -LiteralPath $candidate -Encoding utf8
$results = [ordered]@{ sdk = $null; productionSha256 = (Get-FileHash -LiteralPath $production -Algorithm SHA256).Hash; jit = $null; aot = $null }
Push-Location $repo
try {
    $results.sdk = & dotnet --version
    foreach ($mode in @('jit', 'aot')) {
        $aot = if ($mode -eq 'aot') { 'true' } else { 'false' }
        & dotnet publish "$PSScriptRoot/WasmUntyped.csproj" -c Release -r win-x64 --artifacts-path "$destination/build-$mode" -o "$destination/$mode" "-p:ProbeExecutor=$candidate" "-p:PublishAot=$aot" -p:TrimmerSingleWarn=false *> "$destination/$mode-build.log"
        $buildExit = $LASTEXITCODE
        $results[$mode] = [ordered]@{ buildExit = $buildExit; runExit = $null }
        if ($buildExit -eq 0) {
            & "$destination/$mode/Wasm.Untyped.AotProbe.exe" "$destination/$mode-result.json" *> "$destination/$mode-run.log"
            $results[$mode].runExit = $LASTEXITCODE
        }
    }
    if (Test-Path -LiteralPath "$destination/aot") {
        @(Get-ChildItem -LiteralPath "$destination/aot" -File | ForEach-Object {
            [ordered]@{ name = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        }) | ConvertTo-Json | Set-Content -LiteralPath "$destination/files.json" -Encoding utf8
    }
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$destination/summary.json" -Encoding utf8
    $results | ConvertTo-Json -Depth 5
    if ($results.jit.buildExit -ne 0 -or $results.jit.runExit -ne 0 -or $results.aot.buildExit -ne 0 -or $results.aot.runExit -ne 0) {
        throw 'Untyped candidate failed; inspect build and run evidence'
    }
}
finally { Pop-Location }
