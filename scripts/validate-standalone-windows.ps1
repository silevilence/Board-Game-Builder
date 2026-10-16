# Run in a second fresh Windows Sandbox with only the exported game mapped at C:\Input.
param([string]$InputDirectory='C:\Input', [string]$Evidence='C:\Evidence', [string]$ExpectedManifest, [switch]$Shutdown)
$ErrorActionPreference='Stop'
$exitCode=0
try {
    New-Item -ItemType Directory -Force -Path $Evidence | Out-Null
    $environment=@{os=[Environment]::OSVersion.VersionString;dotnet=[bool](Get-Command dotnet -ErrorAction SilentlyContinue);runtime=(Test-Path 'C:\Program Files\dotnet\shared');runtimeX86=(Test-Path 'C:\Program Files (x86)\dotnet\shared')}
    $environment | ConvertTo-Json | Set-Content "$Evidence/environment.json"
    if ($environment.dotnet -or $environment.runtime -or $environment.runtimeX86) { throw 'A clean environment is required.' }
    if (Test-Path C:\Game) { throw 'Use a new sandbox without an existing game directory.' }
    Copy-Item -LiteralPath $InputDirectory -Destination C:\Game -Recurse
    if ((Test-Path C:\Game\bgb.exe) -or (Get-ChildItem C:\Game -Filter 'Editor*' -Recurse)) { throw 'Author tools leaked into the standalone game.' }
    $verified=0;$manifestHash=$null
    if ($ExpectedManifest) {
        $expected=Get-Content -LiteralPath $ExpectedManifest -Raw | ConvertFrom-Json
        foreach ($file in $expected.files.PSObject.Properties) {
            $path=Join-Path 'C:\Game' $file.Name
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.Value) { throw "Export hash mismatch: $($file.Name)" }
            $verified++
        }
        if (@(Get-ChildItem C:\Game -File -Recurse).Count -ne $verified) { throw 'Unexpected files in exported game' }
        $manifestHash=(Get-FileHash -LiteralPath $ExpectedManifest -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    function RunGame([string]$Arguments,[string]$Name) {
        $process=Start-Process C:\Game\Play.exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput "$Evidence/$Name.stdout" -RedirectStandardError "$Evidence/$Name.stderr"
        $null=$process.Handle
        if (-not $process.WaitForExit(180000)) { Stop-Process -Id $process.Id;throw "Standalone $Name timed out" }
        if ($process.ExitCode -ne 0) { throw "Standalone $Name failed: $($process.ExitCode)" }
        return $process.ExitCode
    }
    $batchExit=RunGame "batch C:\Game\game.bgm --seed 71 --count 3 --out $Evidence/batch" 'batch'
    $guiExit=RunGame "gui C:\Game\game.bgm --smoke $Evidence/gui.json" 'gui'
    $gui=Get-Content "$Evidence/gui.json" -Raw | ConvertFrom-Json
    if (-not $gui.success) { throw 'Standalone graphical games failed.' }
    @{success=$true;filesVerified=$verified;manifestSha256=$manifestHash;batchExitCode=$batchExit;guiExitCode=$guiExit;games=$gui.games;
      packageSha256=(Get-FileHash C:\Game\game.bgm -Algorithm SHA256).Hash.ToLowerInvariant()} | ConvertTo-Json | Set-Content "$Evidence/result.json"
} catch {
    @{success=$false;error=($_ | Out-String)} | ConvertTo-Json | Set-Content "$Evidence/result.json"
    $exitCode=1
}
if ($Shutdown) { Start-Process -FilePath shutdown.exe -ArgumentList '/s /t 0' -WindowStyle Hidden }
exit $exitCode
