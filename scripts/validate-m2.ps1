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
dotnet run --project tests/Engine.Tests -c Release --no-build -- m2-gate
if($LASTEXITCODE -ne 0) {throw 'Two-mode seed gate failed'}
foreach($sample in @('quick-cards','hex-skirmish')) {
  dotnet run --project src/Player -c Release --no-build -- batch "samples/$sample" --seed 1 --count 20 --out "$root/$sample-seeds"
  if($LASTEXITCODE -ne 0) {throw "Seed gate failed: $sample"}
  $start=[Diagnostics.ProcessStartInfo]::new("$repo/src/Editor/bin/Release/net10.0-windows/Editor.exe")
  $start.UseShellExecute=$false
  $start.CreateNoWindow=$true
  foreach($argument in @('--verify',"$repo/samples/$sample","$root/$sample-workflow","$root/player")) {$start.ArgumentList.Add($argument)}
  $process=[Diagnostics.Process]::Start($start)
  if(-not $process.WaitForExit(180000)) {$process.Kill($true);throw "Editor verification timed out: $sample"}
  $result=Get-Content -Raw -LiteralPath "$root/$sample-workflow/result.json" | ConvertFrom-Json
  if(-not $result.success) {throw $result.error}
}
Write-Output "Local M2 verification complete: $root"
Write-Output 'Clean Windows verification remains separate; see docs/validation/m2-review.md.'
