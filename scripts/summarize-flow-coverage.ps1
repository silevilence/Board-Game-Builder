param([Parameter(Mandatory)][string]$InputFile, [Parameter(Mandatory)][string]$OutputFile, [string]$Markdown)
$ErrorActionPreference = 'Stop'
$root = (Split-Path $PSScriptRoot -Parent).Replace('\','/') + '/'
[xml]$coverage = Get-Content -LiteralPath $InputFile -Raw
$sources = @($coverage.coverage.packages.package.classes.class | Group-Object filename | ForEach-Object {
    $path = $_.Name.Replace('\','/')
    if ($path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { $path = $path.Substring($root.Length) }
    $lines = @($_.Group.lines.line | Group-Object number)
    $uncovered = @($lines | Where-Object { @($_.Group | Where-Object { [int]$_.hits -gt 0 }).Count -eq 0 } | ForEach-Object { [int]$_.Name } | Sort-Object)
    [ordered]@{ path = $path; covered = $lines.Count - $uncovered.Count; total = $lines.Count; uncoveredLines = $uncovered }
} | Sort-Object { $_.path })
$uniqueCovered = 0
$uniqueTotal = 0
foreach ($source in $sources) {
    $uniqueCovered += $source.covered
    $uniqueTotal += $source.total
}
$required = @('src/Kernel/FlowTransaction.cs','src/Kernel.Abstractions/FlowState.cs','src/Kernel/GameEngine.cs',
    'src/Kernel/Replay.cs','src/Runtime/Flow/FlowController.cs','src/Runtime/PlaySession.cs',
    'src/Runtime/GameRuntime.cs','src/Runtime/RuntimeComposition.cs','src/Authoring/RunOperations.cs',
    'src/Editor/SessionController.cs','src/Editor.Core/FlowEditorWork.cs')
foreach ($path in $required) { if ($path -notin $sources.path) { throw "Missing acceptance coverage source: $path" } }
$methods = @(
    foreach ($class in $coverage.coverage.packages.package.classes.class) {
        $path = $class.filename.Replace('\','/')
        if ($path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { $path = $path.Substring($root.Length) }
        foreach ($method in $class.methods.method) {
            $rate = [double]::Parse($method.'line-rate', [Globalization.CultureInfo]::InvariantCulture)
            if ($rate -ge .9) { continue }
            [ordered]@{ path=$path; method=$class.name+'.'+$method.name; signature=$method.signature;
                lineRate=$rate; line=($method.lines.line | Select-Object -First 1).number }
        }
    }
)
[ordered]@{
    coverageSha256 = (Get-FileHash -LiteralPath $InputFile).Hash.ToLowerInvariant()
    settingsSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'flow-coverage.config')).Hash.ToLowerInvariant()
    covered = [int]$coverage.coverage.'lines-covered'; total = [int]$coverage.coverage.'lines-valid'
    lineRate = [double]::Parse($coverage.coverage.'line-rate', [Globalization.CultureInfo]::InvariantCulture)
    sourceCounting = 'Distinct source line numbers per file; tool totals may count a line in multiple generated classes.'
    uniqueCovered = $uniqueCovered; uniqueTotal = $uniqueTotal
    selection = 'Only modules and source files matching the Include expressions in flow-coverage.config are measured.'
    explicitExcludes = @('src/Editor/FlowVerification.cs')
    otherHarnessesOutsideIncludes = @('src/Editor/RuntimeDocumentVerification.cs','src/Editor/ExportVerification.cs')
    sources = $sources
    methodsBelow90 = $methods
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputFile -Encoding utf8
if ($Markdown) {
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('# 流程设计验收覆盖率明细')
    $lines.Add('')
    $lines.Add('由 scripts/summarize-flow-coverage.ps1 从本轮 Cobertura 自动生成；不混用历史局部方法表。')
    $lines.Add('')
    $percent = (100 * [double]::Parse($coverage.coverage.'line-rate',[Globalization.CultureInfo]::InvariantCulture)).ToString('F2',[Globalization.CultureInfo]::InvariantCulture)
    $lines.Add(('本轮为 **{0}/{1} 行，{2}%**；采集范围、原始 XML、来源及命令见[验收记录](flow-editor-review.md)。仅测量配置正向 Include 匹配的模块与源码，测试驱动不计入生产分母。' -f $coverage.coverage.'lines-covered',$coverage.coverage.'lines-valid',$percent))
    $lines.Add(('上述为工具按类计数的总量，同一源码行可能出现在多个生成类中。逐文件按行号去重后合计 **{0}/{1} 行**；两个口径分别保留，不将去重合计冒充工具分母。' -f $uniqueCovered,$uniqueTotal))
    $lines.Add('')
    $lines.Add(('以下完整列出本轮行覆盖率低于 90% 的 **{0} 个方法条目**（含编译器生成的状态机与闭包）；历史 94.46% 与本轮范围不同，不能直接比较。' -f $methods.Count))
    $lines.Add('')
    $lines.Add('| 源码 | 方法（工具原名） | 行覆盖率 |')
    $lines.Add('| --- | --- | ---: |')
    foreach ($method in $methods) {
        $rate = (100*$method.lineRate).ToString('F2',[Globalization.CultureInfo]::InvariantCulture)
        $label=($method.method+$method.signature).Replace('|','\|')
        $lines.Add(('| [{0}](../../{0}#L{1}) | `{2}` | {3}% |' -f $method.path,$method.line,$label,$rate))
    }
    [IO.File]::WriteAllLines([IO.Path]::GetFullPath($Markdown),$lines)
}
