param(
    [Parameter(Mandatory)][string]$CordiSharpCandidate,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$evidence=[IO.Path]::GetFullPath($Output)
$cordi=(Resolve-Path -LiteralPath $CordiSharpCandidate).Path
if(Test-Path -LiteralPath $evidence){throw "Evidence directory already exists: $evidence"}
New-Item -ItemType Directory -Path $evidence | Out-Null
Push-Location $repo
try {
    $sources=Join-Path $evidence 'candidate-source'
    python "$PSScriptRoot/prepare.py" $sources
    if($LASTEXITCODE -ne 0){throw 'Candidate preparation failed'}
    dotnet --info | Set-Content (Join-Path $evidence 'environment.txt')
    git rev-parse HEAD | Set-Content (Join-Path $evidence 'source-head.txt')
    Get-FileHash -LiteralPath $cordi | ConvertTo-Json | Set-Content (Join-Path $evidence 'cordisharp-hash.json')
    foreach($mode in @('baseline','native')) {
        $arguments=@('publish',"$PSScriptRoot/Board.csproj",'-c','Release','-r','win-x64','--self-contained','true',
            '--artifacts-path',(Join-Path $evidence "$mode-build"),'-o',(Join-Path $evidence $mode))
        if($mode -eq 'native'){$arguments+=@("-p:BoardSources=$sources","-p:CordiSharpCandidate=$cordi")}
        else {$arguments+='-p:PublishAot=false'}
        & dotnet @arguments *> (Join-Path $evidence "$mode-build.log")
        $LASTEXITCODE | Set-Content (Join-Path $evidence "$mode-build.exit.txt")
        if($LASTEXITCODE -ne 0){throw "$mode build failed; evidence retained"}
        & (Join-Path $evidence "$mode/Board.AotProbe.exe") *> (Join-Path $evidence "$mode-run.log")
        $LASTEXITCODE | Set-Content (Join-Path $evidence "$mode-run.exit.txt")
        if($LASTEXITCODE -ne 0){throw "$mode execution failed; evidence retained"}
    }
    $baseline=Get-Content -LiteralPath (Join-Path $evidence 'baseline-run.log') -Raw
    $native=Get-Content -LiteralPath (Join-Path $evidence 'native-run.log') -Raw
    if($baseline -ne $native){throw 'Native result differs from production JIT baseline'}
    Get-ChildItem -LiteralPath (Join-Path $evidence 'native') -File | ForEach-Object {
        [pscustomobject]@{Name=$_.Name;Length=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
    } | ConvertTo-Json | Set-Content (Join-Path $evidence 'native-files.json')
    Write-Output 'PASS strict native and production JIT result equality; clean-machine proof is a separate check.'
} finally {Pop-Location}
