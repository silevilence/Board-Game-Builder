# Windows PowerShell 5.1. Map ONLY the two exported games and this evidence directory.
param([string]$Evidence='C:\Evidence')
$ErrorActionPreference='Stop'
try {
    $environment=@{os=[Environment]::OSVersion.VersionString;dotnet=[bool](Get-Command dotnet -ErrorAction SilentlyContinue);runtime=(Test-Path 'C:\Program Files\dotnet');runtimeX86=(Test-Path 'C:\Program Files (x86)\dotnet')}
    $environment | ConvertTo-Json | Set-Content "$Evidence/environment.json"
    if ($environment.dotnet -or $environment.runtime -or $environment.runtimeX86) { throw 'A separate environment without SDK/Runtime is required.' }
    $results=@()
    foreach ($mode in @('Standalone','Slim')) {
        $source='C:\From'+$mode
        $destination='C:\Games\'+$mode
        if (Test-Path -LiteralPath $destination) { throw 'Expected a fresh game directory.' }
        New-Item -ItemType Directory -Path 'C:\Games' -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Recurse
        $manifest=Get-Content -LiteralPath "$destination/release.json" -Raw | ConvertFrom-Json
        foreach ($file in $manifest.files.PSObject.Properties) {
            if ((Get-FileHash -LiteralPath (Join-Path $destination $file.Name) -Algorithm SHA256).Hash -ne $file.Value) { throw "Export copy mismatch: $mode/$($file.Name)" }
        }
        foreach ($scenario in @('batch','gui')) {
            $report=Join-Path $Evidence ($mode+'-'+$scenario)
            $parameters=if ($scenario -eq 'batch') { @('batch',('"'+$destination+'\game.bgm"'),'--count','3','--seed','42','--out',('"'+$report+'"')) } else { @('gui',('"'+$destination+'\game.bgm"'),'--smoke',('"'+$report+'.json"')) }
            $process=Start-Process -FilePath "$destination/Play.exe" -ArgumentList $parameters -WorkingDirectory 'C:\' -WindowStyle Hidden -PassThru
            if (-not $process.WaitForExit(240000)) { $process.Kill(); throw "$mode/$scenario timed out" }
            if ($process.ExitCode -ne 0) { throw "$mode/$scenario failed: $($process.ExitCode)" }
            if ($scenario -eq 'gui' -and -not (Get-Content ($report+'.json') -Raw | ConvertFrom-Json).success) { throw "$mode GUI smoke failed" }
        }
        $results+=@{mode=$mode;success=$true;files=$manifest.files.PSObject.Properties.Name.Count}
    }
    @{success=$true;results=$results} | ConvertTo-Json -Depth 5 | Set-Content "$Evidence/result.json"
} catch {
    @{success=$false;error=($_ | Out-String)} | ConvertTo-Json | Set-Content "$Evidence/result.json"
    exit 1
}
