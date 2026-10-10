param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$evidencePath = [IO.Path]::GetFullPath($Evidence)
New-Item -ItemType Directory -Force -Path $evidencePath | Out-Null
function Invoke-Cli([string[]]$Arguments) {
    $result = & "$rootPath/bgb.exe" @Arguments
    if ($LASTEXITCODE -ne 0) { throw "CLI failed: $Arguments $result" }
    $result | Out-File -LiteralPath "$evidencePath/cli.log" -Append -Encoding utf8
}
Invoke-Cli @('docs','entry')
Invoke-Cli @('operations','--project',"$rootPath/docs/author/example")
Invoke-Cli @('call','project.validate','--project',"$rootPath/docs/author/example",'--args','{}')
$export = Join-Path $evidencePath 'game'
$parameters = @{ target=$export } | ConvertTo-Json -Compress
Invoke-Cli @('call','player.export','--project',"$rootPath/docs/author/example",'--args',$parameters)
$start = [Diagnostics.ProcessStartInfo]::new("$export/Play.exe")
$start.UseShellExecute=$false
$start.CreateNoWindow=$true
$start.WorkingDirectory=$evidencePath
foreach ($argument in @('batch',"$export/game.bgm",'--count','3','--seed','42','--out',"$evidencePath/batch")) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
if (-not $process.WaitForExit(120000)) { $process.Kill($true); throw 'Player validation timed out.' }
if ($process.ExitCode -ne 0) { throw 'Exported Player validation failed.' }
foreach ($entry in @('bgb.dll','editor/Editor.dll','player/Player.dll','debug-player/Player.dll')) {
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $rootPath $entry)).ProductVersion.Split('+')[0]
    $expected = (Get-Content -LiteralPath "$rootPath/toolkit.json" -Raw | ConvertFrom-Json).version
    if ($actual -ne $expected) { throw "Version mismatch: $entry $actual != $expected" }
}
@{ success=$true; toolkit=$rootPath; version=$expected; export=$export } | ConvertTo-Json | Set-Content -LiteralPath "$evidencePath/result.json"
