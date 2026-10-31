param([switch]$Native)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repo
try {
    $run=Join-Path $repo ('artifacts/rendering-verification/'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $run | Out-Null
    foreach($backend in @('gl','dx')) {
        $gl=($backend -eq 'gl').ToString().ToLowerInvariant()
        $output=Join-Path $run $backend
        dotnet publish tests/Rendering.Games.Tests -c Release -p:UseDesktopGl=$gl --artifacts-path "$run/build-$backend" -o $output
        if($LASTEXITCODE){throw "$backend build failed"}
        $process=Start-Process "$output/Rendering.Games.Tests.exe" -ArgumentList ('"'+$run+'/'+$backend+'.json"') -PassThru -Wait -WindowStyle Hidden
        if($process.ExitCode -ne 0){throw "$backend verification failed"}
    }
    if($Native) {
        foreach($item in @(@('Rendering.DesktopGL.Tests','viewport'),@('Rendering.Games.Tests','native'))) {
            $project=$item[0];$name=$item[1];$output=Join-Path $run $name
            dotnet publish "tests/$project" -c Release -r win-x64 -p:PublishAot=true -p:UseStaticRuntime=true -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true --artifacts-path "$run/build-$name" -o $output
            if($LASTEXITCODE){throw "$name native publish failed"}
            $process=Start-Process "$output/$project.exe" -ArgumentList ('"'+$run+'/'+$name+'.json"') -PassThru -Wait -WindowStyle Hidden
            if($process.ExitCode -ne 0){throw "$name native verification failed"}
        }
    }
    $baseline=Get-Content "$run/gl.json" -Raw | ConvertFrom-Json
    foreach($path in Get-ChildItem $run -Filter '*.json') {
        $report=Get-Content $path.FullName -Raw | ConvertFrom-Json
        if(!$report.success){throw "Failed report: $path"}
        if($report.stateHash -and ($report.stateHash -ne $baseline.stateHash -or $report.recordingHash -ne $baseline.recordingHash)){throw "Gameplay differs: $path"}
    }
    Write-Output "Verified reports and binaries: $run"
} finally {Pop-Location}
