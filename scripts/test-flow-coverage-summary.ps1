$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('bgb-coverage-summary-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    # Eleven covered lines plus a generated class repeating line 1 and missing line 2.
    # The repeated line remains covered after merging; tool totals and unique totals differ.
    $paths = @('src/Kernel/FlowTransaction.cs','src/Kernel.Abstractions/FlowState.cs','src/Kernel/GameEngine.cs',
        'src/Kernel/Replay.cs','src/Runtime/Flow/FlowController.cs','src/Runtime/PlaySession.cs',
        'src/Runtime/GameRuntime.cs','src/Runtime/RuntimeComposition.cs','src/Authoring/RunOperations.cs',
        'src/Editor/SessionController.cs','src/Editor.Core/FlowEditorWork.cs')
    $classes = foreach ($path in $paths) {
        '<class name="Original" filename="' + $path + '"><methods><method name="Run" signature="()" line-rate="1"><lines><line number="1" hits="1" /></lines></method></methods><lines><line number="1" hits="1" /></lines></class>'
    }
    $classes += '<class name="Generated" filename="src/Kernel/FlowTransaction.cs"><methods><method name="MoveNext" signature="()" line-rate="0"><lines><line number="1" hits="0" /><line number="2" hits="0" /></lines></method></methods><lines><line number="1" hits="0" /><line number="2" hits="0" /></lines></class>'
    $inputFile = Join-Path $temporary 'input.xml'
    $outputFile = Join-Path $temporary 'summary.json'
    $markdown = Join-Path $temporary 'summary.md'
    ('<coverage lines-covered="11" lines-valid="13" line-rate="0.846153846"><packages><package><classes>' + ($classes -join '') + '</classes></package></packages></coverage>') | Set-Content -LiteralPath $inputFile
    & (Join-Path $PSScriptRoot 'summarize-flow-coverage.ps1') -InputFile $inputFile -OutputFile $outputFile -Markdown $markdown
    $summary = Get-Content -LiteralPath $outputFile -Raw | ConvertFrom-Json
    $text = Get-Content -LiteralPath $markdown -Raw
    if ($summary.covered -ne 11 -or $summary.total -ne 13 -or $summary.uniqueCovered -ne 11 -or $summary.uniqueTotal -ne 12) { throw 'Tool and unique line counts were not preserved' }
    if (-not $text.Contains('**11/13 行，84.62%**') -or -not $text.Contains('**11/12 行**') -or $text.Contains('**/ 行**')) { throw 'Markdown omitted or changed line counts' }
    $first = $summary.sources | Where-Object path -eq 'src/Kernel/FlowTransaction.cs'
    if ($first.covered -ne 1 -or $first.total -ne 2 -or @($first.uncoveredLines).Count -ne 1 -or $first.uncoveredLines[0] -ne 2 -or $summary.methodsBelow90.Count -ne 1) { throw 'Generated class lines were not merged correctly' }
    'PASS flow-coverage-summary (tool totals, unique totals, Markdown and generated-class overlap)'
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'bgb-coverage-summary-*') { throw 'Unexpected test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
