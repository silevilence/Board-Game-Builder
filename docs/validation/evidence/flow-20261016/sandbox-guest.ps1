$ErrorActionPreference = 'Stop'
try {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    $runtimeInstalled = (Test-Path 'C:\Program Files\dotnet') -or (Test-Path 'C:\Program Files (x86)\dotnet')
    if ($dotnetCommand -or $runtimeInstalled) { throw 'Guest contains a global dotnet installation' }
    $gameProcess = Start-Process -FilePath 'C:\Game\Play.exe' -ArgumentList 'gui','--smoke','C:\FlowCheck\player.json' -WindowStyle Hidden -PassThru
    if (-not $gameProcess.WaitForExit(180000)) { Stop-Process -Id $gameProcess.Id; throw 'Player timed out' }
    $result = Get-Content -LiteralPath 'C:\FlowCheck\player.json' -Raw | ConvertFrom-Json
    if (-not $result.success) { throw $result.error }
    @{ success = $true; globalDotnet = $false; games = $result.games; os = [Environment]::OSVersion.ToString() } | ConvertTo-Json | Set-Content 'C:\FlowCheck\result.json'
} catch {
    @{ success = $false; error = $_.ToString() } | ConvertTo-Json | Set-Content 'C:\FlowCheck\result.json'
}
