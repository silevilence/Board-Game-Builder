$ErrorActionPreference = 'Stop'
$allowed = @{
 'Application.Abstractions' = @(); 'Application.Core' = @('Application.Abstractions')
 'Application.Debug' = @('Application.Core')
 'Runtime.Plugins' = @('Application.Core')
 'Resources.Abstractions' = @(); 'Resources.Core' = @('Resources.Abstractions','Application.Core')
 'Kernel.Abstractions' = @('Application.Abstractions','Resources.Abstractions')
 'Kernel' = @('Kernel.Abstractions'); 'Modules' = @('Kernel.Abstractions','Modules.Runtime')
 'Modules.Runtime' = @('Kernel.Abstractions')
 'Lua.Execution' = @('Application.Abstractions'); 'Wasm.Execution' = @()
 'Lua' = @('Kernel.Abstractions','Lua.Execution')
 'BoardGame.Core' = @('Kernel','Application.Core')
 'BoardGame.Session' = @('BoardGame.Core','BoardGame.Recording')
 'BoardGame.Recording' = @('BoardGame.Core','Modules.Runtime')
 'BoardGame.Lua' = @('BoardGame.Core','Lua')
 'BoardGame.Wasm' = @('BoardGame.Core','Modules','Wasm.Execution')
 'BoardGame.Hosting' = @('BoardGame.Session','Resources.Core')
 'BoardGame.Interaction' = @('Application.Core','View.Abstractions')
 'BoardGame.Presentation' = @('BoardGame.Core','View.Abstractions','Rendering.Core')
 'Runtime' = @('BoardGame.Session','BoardGame.Interaction','Application.Core','Resources.Core','BoardGame.Core','BoardGame.Wasm','Kernel','Modules','BoardGame.Lua')
 'Networking' = @('Runtime')
 'Server' = @('Networking'); 'NetworkClient' = @('Networking')
 'Presentation.Core' = @(); 'Presentation.Avalonia' = @('Presentation.Core','Resources.Abstractions')
 'Rendering.Abstractions' = @('Resources.Abstractions'); 'Rendering.Core' = @('Rendering.Abstractions','Application.Core')
 'Rendering.Avalonia' = @('Rendering.Core'); 'Rendering.WindowsDX' = @('Rendering.Core')
 'View.Abstractions' = @('Presentation.Core','Kernel.Abstractions')
 'View.MonoGame' = @('Presentation.Avalonia','BoardGame.Interaction','View.Abstractions','BoardGame.Presentation','Rendering.WindowsDX','Rendering.Avalonia')
 'Authoring' = @('Runtime'); 'Cli' = @('Authoring','Sample.Motion.Authoring')
 'Launcher.Core' = @(); 'Launcher' = @('Launcher.Core'); 'Launcher.Cli' = @('Launcher.Core')
 'Player.Hosting' = @('Rendering.Avalonia','Application.Debug')
 'Player.Board' = @('Player.Hosting','Runtime','Networking','View.MonoGame')
 'Player' = @('Player.Board','Sample.Motion'); 'Toolkit.Entry' = @()
 'Editor.Core' = @('Runtime','Authoring')
 'Editor' = @('Editor.Core','Runtime','Application.Debug','Sample.Motion','View.MonoGame')
}
Get-ChildItem "$PSScriptRoot/../src" -Filter *.csproj -Recurse | ForEach-Object {
 if (-not $allowed.ContainsKey($_.BaseName)) { throw "Project has no declared boundary: $($_.BaseName)" }
 [xml]$project = Get-Content $_.FullName
 foreach ($reference in $project.SelectNodes('//ProjectReference')) {
  $name = [IO.Path]::GetFileNameWithoutExtension($reference.Include)
  if ($name -notin $allowed[$_.BaseName]) { throw "Forbidden dependency: $($_.BaseName) -> $name" }
 }
 if ($_.BaseName -in @('Kernel','Kernel.Abstractions')) {
  if ($project.SelectNodes('//PackageReference').Count) { throw 'Kernel must remain dependency free' }
 }
 foreach ($package in $project.SelectNodes('//PackageReference')) {
  if ($package.Include -like 'CordiSharp*' -and $_.BaseName -notin @('Editor','Editor.Core') -and
      -not ($_.BaseName -in @('Application.Core','Runtime') -and $package.Include -eq 'CordiSharp')) { throw 'Only the CordiSharp core may enter the shared runtime' }
 }
}
Write-Output 'Project boundaries passed.'
