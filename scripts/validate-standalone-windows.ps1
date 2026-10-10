# Run in a second fresh Windows Sandbox with only the exported game mapped at C:\Input.
param([string]$InputDirectory='C:\Input', [string]$Evidence='C:\Evidence')
$ErrorActionPreference='Stop'
try {
    $environment=@{os=[Environment]::OSVersion.VersionString;dotnet=[bool](Get-Command dotnet -ErrorAction SilentlyContinue);runtime=(Test-Path 'C:\Program Files\dotnet\shared');runtimeX86=(Test-Path 'C:\Program Files (x86)\dotnet\shared')}
    $environment | ConvertTo-Json | Set-Content "$Evidence/environment.json"
    if ($environment.dotnet -or $environment.runtime -or $environment.runtimeX86) { throw 'A clean environment is required.' }
    if (Test-Path C:\Game) { throw 'Use a new sandbox without an existing game directory.' }
    Copy-Item -LiteralPath $InputDirectory -Destination C:\Game -Recurse
    if ((Test-Path C:\Game\bgb.exe) -or (Get-ChildItem C:\Game -Filter 'Editor*' -Recurse)) { throw 'Author tools leaked into the standalone game.' }
    $batch=Start-Process C:\Game\Play.exe -ArgumentList "batch C:\Game\game.bgm --seed 71 --count 3 --out $Evidence/batch" -WindowStyle Hidden -PassThru -Wait
    if ($batch.ExitCode -ne 0) { throw 'Standalone batch failed.' }
    $gui=Start-Process C:\Game\Play.exe -ArgumentList "gui C:\Game\game.bgm --smoke $Evidence/gui.json" -WindowStyle Hidden -PassThru -Wait
    if ($gui.ExitCode -ne 0 -or -not (Get-Content "$Evidence/gui.json" -Raw | ConvertFrom-Json).success) { throw 'Standalone graphical games failed.' }
    @{success=$true} | ConvertTo-Json | Set-Content "$Evidence/result.json"
} catch {
    @{success=$false;error=($_ | Out-String)} | ConvertTo-Json | Set-Content "$Evidence/result.json"
    exit 1
}
exit 0
