param([string]$Output='artifacts/native-aot/lua')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$evidence=[IO.Path]::GetFullPath((Join-Path $repo $Output))
if(Test-Path -LiteralPath $evidence) { throw "Choose a new evidence directory: $evidence" }
New-Item -ItemType Directory -Path $evidence | Out-Null
Push-Location $repo
try {
    dotnet --info | Set-Content (Join-Path $evidence 'dotnet-info.txt')
    git rev-parse HEAD | Set-Content (Join-Path $evidence 'source-head.txt')
    foreach($kind in @('worker','probe')) {
        $project=if($kind -eq 'worker') {'src/Lua/Lua.csproj'} else {'tools/AotProbes/Lua/LuaProbe.csproj'}
        foreach($mode in @('jit','native')) {
            $name="$kind-$mode"
            $buildArgs=@('publish',$project,'-c','Release','-r','win-x64','--self-contained','true',
                '--artifacts-path',(Join-Path $evidence "$name-build"),'-o',(Join-Path $evidence $name))
            if($mode -eq 'native') {
                # Diagnostic builds only: retain every warning, without treating
                # code generation success as a supported publication result.
                $buildArgs+=@('-p:PublishAot=true','-p:TreatWarningsAsErrors=false',
                    '-p:IlcTreatWarningsAsErrors=false','-p:TrimmerSingleWarn=false')
            }
            & dotnet @buildArgs *> (Join-Path $evidence "$name-build.log")
            $LASTEXITCODE | Set-Content (Join-Path $evidence "$name-build.exit.txt")
            if($LASTEXITCODE -ne 0) {throw "Build failed: $name; inspect retained log"}
        }
    }
    foreach($mode in @('jit','native')) {
        & (Join-Path $evidence "probe-$mode/LuaProbe.exe") *> (Join-Path $evidence "probe-$mode-run.log")
        $LASTEXITCODE | Set-Content (Join-Path $evidence "probe-$mode-run.exit.txt")
        & (Join-Path $evidence 'probe-jit/LuaProbe.exe') (Join-Path $evidence "worker-$mode/Lua.exe") *> (Join-Path $evidence "worker-$mode-run.log")
        $LASTEXITCODE | Set-Content (Join-Path $evidence "worker-$mode-run.exit.txt")
    }
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $evidence 'worker-native/Lua.exe'))
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $child=[Diagnostics.Process]::Start($start)
    try {
        $errors=$child.StandardError.ReadToEndAsync(); $outputText=$child.StandardOutput.ReadToEndAsync()
        $child.StandardInput.Write('{"source":"return {run=function(x) return true end}","path":"probe.lua","entry":"run","input":"{}","seed":7,"api":2}')
        $child.StandardInput.Close()
        if(-not $child.WaitForExit(20000)) {$child.Kill($true);$child.WaitForExit()}
        $errors.Result | Set-Content (Join-Path $evidence 'worker-native-stderr.log')
        $outputText.Result | Set-Content (Join-Path $evidence 'worker-native-stdout.log')
        $child.ExitCode | Set-Content (Join-Path $evidence 'worker-native-raw.exit.txt')
    } finally { $child.Dispose() }
    Get-ChildItem -LiteralPath (Join-Path $evidence 'worker-native'),(Join-Path $evidence 'probe-native') -File -Recurse |
        ForEach-Object { [pscustomobject]@{Path=[IO.Path]::GetRelativePath($evidence,$_.FullName);Length=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} } |
        ConvertTo-Json | Set-Content (Join-Path $evidence 'native-files.json')
    Write-Output "Evidence: $evidence. Nonzero probe exit codes are incompatibility evidence, not success."
    foreach($name in @('probe-jit','worker-jit','probe-native','worker-native')) {
        if([int](Get-Content (Join-Path $evidence "$name-run.exit.txt")) -ne 0) {
            throw "Lua compatibility probe failed: $name. Inspect retained evidence; this is not a supported AOT publication."
        }
    }
} finally { Pop-Location }
