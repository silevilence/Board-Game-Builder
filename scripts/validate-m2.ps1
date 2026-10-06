param([string]$Output=('artifacts/m2-validation-'+(Get-Date -Format yyyyMMdd-HHmmss)))
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
$root=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $root) {throw 'Choose a new validation output directory'}
dotnet build BoardGameBuilder.slnx -c Release --nologo
if($LASTEXITCODE -ne 0) {throw 'Build failed'}
& "$PSScriptRoot/check-boundaries.ps1"
dotnet run --project tests/Engine.Tests -c Release --no-build
if($LASTEXITCODE -ne 0) {throw 'Tests failed'}
& "$PSScriptRoot/publish-player.ps1" -Output "$root/player"
$previousRelease=$env:BGB_TEST_RELEASE
try {
  $env:BGB_TEST_RELEASE="$root/player"
  dotnet run --project tests/Engine.Tests -c Release --no-build -- export
  if($LASTEXITCODE -ne 0) {throw 'Export tests failed'}
} finally {$env:BGB_TEST_RELEASE=$previousRelease}
dotnet run --project tests/Engine.Tests -c Release --no-build -- m2-gate
if($LASTEXITCODE -ne 0) {throw 'Two-mode seed gate failed'}
foreach($sample in @('quick-cards','hex-skirmish')) {
  dotnet run --project src/Player -c Release --no-build -- batch "samples/$sample" --seed 1 --count 20 --out "$root/$sample-seeds"
  if($LASTEXITCODE -ne 0) {throw "Seed gate failed: $sample"}
  foreach($probe in @('session','export')) {
    $start=[Diagnostics.ProcessStartInfo]::new("$repo/src/Editor/bin/Release/net10.0-windows/Editor.exe")
    $start.UseShellExecute=$false
    $start.CreateNoWindow=$true
    $resultPath=if($probe -eq 'session') {"$root/$sample-session.json"} else {"$root/$sample-export/result.json"}
    $probeOutput=if($probe -eq 'session') {$resultPath} else {"$root/$sample-export"}
    foreach($argument in @("--verify-$probe","$repo/samples/$sample",$probeOutput)) {$start.ArgumentList.Add($argument)}
    if($probe -eq 'export') {$start.ArgumentList.Add("$root/player")}
    $process=[Diagnostics.Process]::Start($start)
    if(-not $process.WaitForExit(300000)) {$process.Kill($true);throw "Editor verification timed out: $sample / $probe"}
    $result=Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    if(-not $result.success) {throw ($result.errors -join "`n")}
  }
}

$start=[Diagnostics.ProcessStartInfo]::new("$repo/src/Editor/bin/Release/net10.0-windows/Editor.exe")
$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.ArgumentList.Add('--verify-foundation');$start.ArgumentList.Add("$root/foundation.json")
$process=[Diagnostics.Process]::Start($start)
if(-not $process.WaitForExit(120000)) {$process.Kill($true);throw 'Foundation verification timed out'}
if(-not (Get-Content -Raw "$root/foundation.json" | ConvertFrom-Json).success) {throw 'Foundation verification failed'}

Write-Output "Local M2/M2.5 verification complete: $root"
Write-Output 'Clean Windows verification remains separate; see docs/validation/m25-integration.md.'
