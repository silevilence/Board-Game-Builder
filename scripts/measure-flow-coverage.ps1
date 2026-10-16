param([string]$Output='artifacts/flow-coverage-run', [string]$CoverageTool, [string]$Release)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (Test-Path -LiteralPath $Output) { throw 'Choose a new coverage output directory' }
    if (-not $CoverageTool) {
        $command=Get-Command Microsoft.CodeCoverage.Console.exe -ErrorAction SilentlyContinue
        if ($command) { $CoverageTool=$command.Source }
        else {
            $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
            if (Test-Path -LiteralPath $vswhere) {
                $CoverageTool=& $vswhere -latest -products '*' -find 'Common7/IDE/Extensions/Microsoft/CodeCoverage.Console/Microsoft.CodeCoverage.Console.exe' | Select-Object -First 1
            }
        }
    }
    if (-not $CoverageTool -or -not (Test-Path -LiteralPath $CoverageTool -PathType Leaf)) {
        throw 'Install Visual Studio Code Coverage tools or pass -CoverageTool with the full Microsoft.CodeCoverage.Console.exe path.'
    }
    New-Item -ItemType Directory -Path $Output | Out-Null
    $Output=(Resolve-Path -LiteralPath $Output).Path
    $tool=Get-Item -LiteralPath $CoverageTool
    [ordered]@{ path=$tool.FullName;version=$tool.VersionInfo.FileVersion;sha256=(Get-FileHash -LiteralPath $tool.FullName).Hash.ToLowerInvariant() } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'coverage-tool.json') -Encoding utf8
    dotnet build BoardGameBuilder.slnx -c Release --nologo 2>&1 | Tee-Object -FilePath (Join-Path $Output 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $arguments=@('collect','-s','scripts/flow-coverage.config','-f','cobertura','-o',(Join-Path $Output 'coverage.xml'),
        'pwsh','-NoProfile','-File','scripts/validate-flow.ps1','-SkipBuild','-Ui','-Output',$Output)
    if ($Release) { $arguments+=@('-Release',$Release) }
    & $CoverageTool @arguments 2>&1 | Tee-Object -FilePath (Join-Path $Output 'collection.log')
    if ($LASTEXITCODE -ne 0) { throw 'Coverage run failed' }
    $result=Get-Content -LiteralPath (Join-Path $Output 'result.json') -Raw | ConvertFrom-Json
    if (-not $result.success) { throw 'Validation failed' }
    & (Join-Path $PSScriptRoot 'summarize-flow-coverage.ps1') -InputFile (Join-Path $Output 'coverage.xml') -OutputFile (Join-Path $Output 'coverage-summary.json')
}
finally { Pop-Location }
