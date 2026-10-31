# Windows PowerShell 5.1. Run only inside a fresh Windows Sandbox.
param([string]$InputDirectory='C:\AotInput',[string]$Evidence='C:\AotEvidence')
$ErrorActionPreference='Stop'
$nativeEnvironment=@{os=[Environment]::OSVersion.VersionString;dotnet=[bool](Get-Command dotnet -ErrorAction SilentlyContinue);runtime=(Test-Path 'C:\Program Files\dotnet\shared');runtimeX86=(Test-Path 'C:\Program Files (x86)\dotnet\shared')}
if($nativeEnvironment.dotnet -or $nativeEnvironment.runtime -or $nativeEnvironment.runtimeX86){throw 'Requires clean Windows without SDK/system .NET.'}
if(-not (Test-Path -LiteralPath "$InputDirectory/probe-input.marker")){throw 'Missing isolated probe input.'}
New-Item -ItemType Directory -Path $Evidence -Force | Out-Null
$nativeEnvironment | ConvertTo-Json | Set-Content -LiteralPath "$Evidence/environment.json"
$results=@()
function Run-Probe([string]$Name,[string]$Exe,[string]$Arguments) {
    $start=@{FilePath=$Exe;WindowStyle='Hidden';PassThru=$true;RedirectStandardOutput="$Evidence/$Name.stdout.log";RedirectStandardError="$Evidence/$Name.stderr.log"}
    if($Arguments){$start.ArgumentList=$Arguments}
    $child=Start-Process @start
    $null=$child.Handle
    if(-not $child.WaitForExit(60000)){$child.Kill();$child.WaitForExit();throw "$Name timed out"}
    $result=@{name=$Name;exitCode=$child.ExitCode};$child.Dispose();return $result
}
try {
    $results+=Run-Probe 'gl' "$InputDirectory/gl/AotProbe.Gl.exe" "$Evidence/gl"
    $results+=Run-Probe 'wasm' "$InputDirectory/wasm/Wasm.AotProbe.exe" "$Evidence/wasm.json"
    $results+=Run-Probe 'lua-direct' "$InputDirectory/lua/LuaProbe.exe" ''
    # Native worker accepts one unframed JSON request in this mode. This proves the
    # actual process start/protocol failure separately from the direct executor.
    '{"source":"return {run=function() return true end}","path":"clean.lua","entry":"run","input":"{}","seed":7,"api":2}' | Set-Content "$Evidence/lua-request.json" -Encoding ascii
    $worker=Start-Process -FilePath "$InputDirectory/lua-worker/Lua.exe" -WindowStyle Hidden -PassThru -RedirectStandardInput "$Evidence/lua-request.json" -RedirectStandardOutput "$Evidence/lua-worker.stdout.log" -RedirectStandardError "$Evidence/lua-worker.stderr.log"
    $null=$worker.Handle
    if(-not $worker.WaitForExit(30000)){$worker.Kill();$worker.WaitForExit();throw 'Lua worker timed out'}
    $results+=@{name='lua-worker';exitCode=$worker.ExitCode}
    $worker.Dispose()
    @{complete=$true;results=$results} | ConvertTo-Json -Depth 6 | Set-Content "$Evidence/result.json"
} catch {
    @{complete=$false;results=$results;error=($_ | Out-String)} | ConvertTo-Json -Depth 6 | Set-Content "$Evidence/result.json"
}
