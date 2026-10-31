param([string]$Output = 'artifacts/native-aot/wasm/repro')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$destination = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $destination) { throw "Evidence directory already exists: $destination" }
New-Item -ItemType Directory -Path $destination | Out-Null
$project = Join-Path $PSScriptRoot 'Wasm.csproj'
$results = [ordered]@{ sdk = (& dotnet --version); strictPublishExit = $null; jit = $null; diagnosticAot = $null }
function Build-Probe([string]$Name, [string[]]$Options) {
    & dotnet publish $project -c Release -r win-x64 --artifacts-path "$destination/build-$Name" -o "$destination/$Name" @Options *> "$destination/$Name-build.log"
    return $LASTEXITCODE
}
Push-Location $repo
try {
    $jitBuild = Build-Probe 'jit' @('-p:PublishAot=false')
    if ($jitBuild -ne 0) { throw "JIT baseline failed ($jitBuild); inspect jit-build.log" }
    & "$destination/jit/Wasm.AotProbe.exe" "$destination/jit-result.json" *> "$destination/jit-run.log"
    $results.jit = [ordered]@{ exit = $LASTEXITCODE; report = 'jit-result.json' }
    $results.strictPublishExit = Build-Probe 'strict-aot' @('-p:TrimmerSingleWarn=false')
    # Research only: keep all diagnostics visible and collect runtime evidence even when strict publication fails.
    $diagnosticBuild = Build-Probe 'diagnostic-aot' @('-p:TreatWarningsAsErrors=false', '-p:TrimmerSingleWarn=false')
    if ($diagnosticBuild -ne 0) { throw "Diagnostic native compilation failed ($diagnosticBuild); inspect diagnostic-aot-build.log" }
    & "$destination/diagnostic-aot/Wasm.AotProbe.exe" "$destination/aot-result.json" *> "$destination/aot-run.log"
    $results.diagnosticAot = [ordered]@{ exit = $LASTEXITCODE; report = 'aot-result.json' }
    Get-ChildItem -LiteralPath "$destination/diagnostic-aot" -File | ForEach-Object {
        [ordered]@{ name = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    } | ConvertTo-Json | Set-Content -LiteralPath "$destination/files.json" -Encoding utf8
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$destination/summary.json" -Encoding utf8
    $results | ConvertTo-Json -Depth 5
    if ($results.jit.exit -ne 0 -or $results.diagnosticAot.exit -ne 0) { throw 'Runtime probe failed; inspect result files' }
    if ($results.strictPublishExit -ne 0) { Write-Warning 'Strict NativeAOT publication is blocked. Diagnostic runtime success does not mark this dependency supported.' }
}
finally { Pop-Location }
