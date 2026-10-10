# Windows PowerShell 5.1 compatible. Run inside a fresh Windows Sandbox, never on a development host.
param([string]$InputDirectory='C:\Evidence\downloads', [string]$Evidence='C:\Evidence\acceptance')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $Evidence | Out-Null
try {
    $environment=@{ os=[Environment]::OSVersion.VersionString; dotnet=[bool](Get-Command dotnet -ErrorAction SilentlyContinue); runtime=(Test-Path 'C:\Program Files\dotnet\shared'); runtimeX86=(Test-Path 'C:\Program Files (x86)\dotnet\shared') }
    $environment | ConvertTo-Json | Set-Content "$Evidence/environment.json"
    if ($environment.dotnet -or $environment.runtime -or $environment.runtimeX86) { throw 'A clean environment without SDK/runtime is required.' }
    $zip=Get-ChildItem -LiteralPath $InputDirectory -Filter '*.zip' -File
    if (@($zip).Count -ne 1) { throw 'Expected exactly one downloaded release ZIP.' }
    $expected=(Get-Content -LiteralPath ($zip.FullName+'.sha256') -Raw).Trim()
    $hash=(Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne "$hash  $($zip.Name)") { throw 'Downloaded ZIP checksum mismatch.' }
    $install='C:\发行 验证\Initial Toolkit'
    Expand-Archive -LiteralPath $zip.FullName -DestinationPath $install
    $manifest=Get-Content -LiteralPath "$install/toolkit.json" -Raw | ConvertFrom-Json
    function Invoke-ReleaseCli([string[]]$Arguments) {
        & "$script:install/bgb.exe" @Arguments | Out-File "$Evidence/cli.log" -Append -Encoding utf8
        if ($LASTEXITCODE -ne 0) { throw "CLI failed: $Arguments" }
    }
    function Invoke-ReleaseOperation([string]$Operation,[string]$Project,[hashtable]$Parameters=@{}) {
        $paramsFile=Join-Path $Evidence 'parameters.json'
        $Parameters | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $paramsFile -Encoding utf8
        Invoke-ReleaseCli @('call',$Operation,'--project',$Project,'--args-file',$paramsFile)
    }
    function Invoke-ReleaseGui([string]$Executable,[string[]]$Arguments,[string]$Report) {
        $p=Start-Process -FilePath $Executable -ArgumentList $Arguments -WorkingDirectory 'C:\' -WindowStyle Hidden -PassThru
        if (-not $p.WaitForExit(240000)) { $p.Kill(); throw "GUI timed out: $Executable" }
        if ($p.ExitCode -ne 0) { throw "GUI failed: $Executable exit $($p.ExitCode)" }
        if ($Report) {
            $result=Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
            if (-not $result.success) { throw "GUI checks failed: $Report" }
        }
    }
    Invoke-ReleaseCli @('docs','entry')
    Invoke-ReleaseCli @('skill','install','--target','C:\Author Skills','--method','copy')
    Invoke-ReleaseGui "$install/editor/Editor.exe" @('--verify-toolkit','C:\Evidence\acceptance\before') "$Evidence/before/editor.json"
    $moved='C:\发行 验证\Moved Toolkit'
    $source=[IO.Path]::GetFullPath($install);$destination=[IO.Path]::GetFullPath($moved)
    if (-not $source.StartsWith('C:\发行 验证\') -or -not $destination.StartsWith('C:\发行 验证\')) { throw 'Invalid move paths.' }
    Move-Item -LiteralPath $source -Destination $destination
    $install=$moved
    Invoke-ReleaseCli @('skill','repair','--target','C:\Author Skills')
    Invoke-ReleaseCli @('skill','status','--target','C:\Author Skills')
    Invoke-ReleaseCli @('skill','uninstall','--target','C:\Author Skills')
    Invoke-ReleaseCli @('skill','install','--target','C:\Linked Skills','--method','junction')
    Invoke-ReleaseCli @('skill','status','--target','C:\Linked Skills')
    Invoke-ReleaseCli @('skill','uninstall','--target','C:\Linked Skills')
    Invoke-ReleaseGui "$install/editor/Editor.exe" @('--verify-toolkit','C:\Evidence\acceptance\after') "$Evidence/after/editor.json"
    foreach ($format in @('module','state')) {
        $project="C:\Games\$format Project"
        Invoke-ReleaseOperation "project.create.$format" $project
        Invoke-ReleaseOperation 'project.validate' $project
        Invoke-ReleaseOperation 'run.cpu' $project @{seed=17;count=3}
        Invoke-ReleaseOperation 'project.pack' $project @{target="C:/Games/$format.bgm"}
    }
    Invoke-ReleaseOperation 'player.export' 'C:\Games\module Project' @{target='C:/Standalone Game'}
    Invoke-ReleaseGui 'C:\Standalone Game\Play.exe' @('batch','"C:\Standalone Game\game.bgm"','--count','3','--seed','42','--out','C:\Evidence\acceptance\batch') ''
    Invoke-ReleaseGui 'C:\Standalone Game\Play.exe' @('gui','"C:\Standalone Game\game.bgm"','--smoke','C:\Evidence\acceptance\player-gui.json') "$Evidence/player-gui.json"
    Copy-Item -LiteralPath 'C:\Standalone Game' -Destination "$Evidence/standalone" -Recurse
    @{success=$true;version=$manifest.version;commit=$manifest.commit;zip=$zip.Name;sha256=$hash} | ConvertTo-Json | Set-Content "$Evidence/result.json"
} catch {
    @{success=$false;error=($_ | Out-String)} | ConvertTo-Json | Set-Content "$Evidence/result.json"
    exit 1
}
exit 0
