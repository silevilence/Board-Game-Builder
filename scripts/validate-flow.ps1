param([string]$Output = 'artifacts/flow-review-current', [switch]$SkipBuild, [switch]$Ui, [switch]$UiOnly, [string]$Release)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    New-Item -ItemType Directory -Force -Path $Output | Out-Null
    $Output = (Resolve-Path -LiteralPath $Output).Path
    $inventory = @(git ls-files --cached --others --exclude-standard -- src tests samples scripts docs)
    if ($LASTEXITCODE -ne 0) { throw 'Git source inventory failed' }
    $sourceFiles = @($inventory | Where-Object {
        $_ -notmatch '(^|/)(bin|obj)/|^docs/validation/evidence/' -and
        ($_ -match '\.(cs|csproj|ps1|config|md|props|targets)$' -or $_ -match '^samples/flow-(cards|minimal)/')
    }) + @('BoardGameBuilder.slnx','Directory.Build.props','Directory.Build.targets','global.json','.gitattributes','ROADMAP.md')
    $hashes = [ordered]@{}
    $snapshot = Join-Path $Output 'source-snapshot'
    if (Test-Path -LiteralPath $snapshot) { throw 'Use a fresh source snapshot directory' }
    foreach ($file in $sourceFiles) {
        $hashes[$file.Replace('\','/')] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        $destination = Join-Path $snapshot $file
        New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
        Copy-Item -LiteralPath $file -Destination $destination
    }
    & (Join-Path $PSScriptRoot 'test-flow-coverage-summary.ps1') | Tee-Object -FilePath (Join-Path $Output 'summary-test.log')
    if (-not $SkipBuild) {
        dotnet build BoardGameBuilder.slnx -c Release --nologo 2>&1 | Tee-Object -FilePath (Join-Path $Output 'build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    }
    $groups = @('cordi','cordi-dispatch','debug-play','documents','packages','replay','cpu',
        'author-operations','author-validation','author-workspace','author-external','author-runs',
        'startup','runtime-recording','runtime-entry','runtime-host',
        'flow-editor-lifetime','flow-editor-model','flow-editor-canvas','flow-editor-properties',
        'flow-editor-validation','flow-editor-playback','flow-editor-conflicts',
        'flow-contract','flow-validation','flow-runtime','flow-systems','flow-players',
        'flow-protection','flow-restoration','flow-delivery','flow-review')
    if ($UiOnly) { $groups = @() }
    else {
        dotnet tests/Engine.Tests/bin/Release/net10.0/Engine.Tests.dll @groups 2>&1 | Tee-Object -FilePath (Join-Path $Output 'tests.log')
        if ($LASTEXITCODE -ne 0) { throw 'Flow regression failed' }
    }
    & (Join-Path $PSScriptRoot 'check-boundaries.ps1') | Tee-Object -FilePath (Join-Path $Output 'boundaries.log')
    $probes = @()
    if ($Ui -or $UiOnly) {
        foreach ($probe in @(@('--verify-flow','ui.json'), @('--verify-runtime-documents','formats.json'))) {
            $resultPath = Join-Path $Output $probe[1]
            if (Test-Path -LiteralPath $resultPath) { throw "Use a fresh output directory: $resultPath" }
            $process = Start-Process -FilePath 'src/Editor/bin/Release/net10.0-windows/Editor.exe' -ArgumentList $probe[0],('"' + $resultPath + '"') -WindowStyle Hidden -PassThru
            $null = $process.Handle
            if (-not $process.WaitForExit(180000)) { Stop-Process -Id $process.Id; throw "Probe timed out: $($probe[0])" }
            if ($process.ExitCode -ne 0) { throw "Probe failed or exit code unavailable: $($probe[0]); exit code $($process.ExitCode)" }
            $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            if (-not $result.success) { throw "Probe unsuccessful: $($result.error)" }
            $probes += [ordered]@{ probe = $probe[0]; exitCode = $process.ExitCode; success = [bool]$result.success }
        }
    }
    if ($Release) {
        $exportOutput = Join-Path $Output 'export-check'
        if (Test-Path -LiteralPath $exportOutput) { throw 'Use a fresh export output directory' }
        $exportProject = if ($Ui -or $UiOnly) { Join-Path $Output 'ui.json.project' } else { 'samples/flow-cards' }
        $arguments = @('--verify-export',('"' + $exportProject + '"'),('"' + $exportOutput + '"'),('"' + (Resolve-Path -LiteralPath $Release).Path + '"'))
        $process = Start-Process -FilePath 'src/Editor/bin/Release/net10.0-windows/Editor.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru
        $null = $process.Handle
        if (-not $process.WaitForExit(180000)) { Stop-Process -Id $process.Id; throw 'Export probe timed out' }
        if ($process.ExitCode -ne 0) { throw "Export probe failed or exit code unavailable: $($process.ExitCode)" }
        $result = Get-Content -LiteralPath (Join-Path $exportOutput 'result.json') -Raw | ConvertFrom-Json
        if (-not $result.success) { throw "Export checks failed: $($result.errors)" }
        $probes += [ordered]@{ probe='--verify-export';exitCode=$process.ExitCode;success=[bool]$result.success }
    }
    foreach ($file in $sourceFiles) {
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$file.Replace('\','/')]) { throw "Validation changed an input file: $file" }
    }
    [ordered]@{ date = [DateTimeOffset]::Now.ToString('o'); baseCommit = (git rev-parse HEAD); workingTree = @(git status --short);
        success = $true; groups = $groups; ui = [bool]($Ui -or $UiOnly); probes = $probes; exportProject = $exportProject; sourceUnchanged = $true; dotnet = (dotnet --version);
        sourceScope = 'Git-visible cs/csproj/ps1/config/md/props/targets under src, tests, samples, scripts and docs; all flow-cards/flow-minimal inputs; listed root build/roadmap/attributes files. Excludes bin/obj and historical evidence.'; sourceSha256 = $hashes
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'result.json') -Encoding utf8
}
finally { Pop-Location }
