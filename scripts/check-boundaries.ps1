$ErrorActionPreference = 'Stop'
$allowed = @{
 'Kernel.Abstractions' = @(); 'Kernel' = @('Kernel.Abstractions'); 'Modules' = @('Kernel.Abstractions')
 'Lua' = @('Kernel.Abstractions'); 'Runtime' = @('Kernel','Modules','Lua')
 'Networking' = @('Runtime')
 'Server' = @('Networking'); 'NetworkClient' = @('Networking')
 'View.Abstractions' = @('Kernel.Abstractions'); 'View.MonoGame' = @('View.Abstractions')
 'Authoring' = @('Runtime'); 'Cli' = @('Authoring')
 'Launcher.Core' = @(); 'Launcher' = @('Launcher.Core'); 'Launcher.Cli' = @('Launcher.Core')
 'Player' = @('Runtime','View.MonoGame','Networking'); 'Editor.Core' = @('Runtime','Authoring'); 'Editor' = @('Editor.Core','Runtime','View.MonoGame')
}
Get-ChildItem "$PSScriptRoot/../src" -Filter *.csproj -Recurse | ForEach-Object {
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
      -not ($_.BaseName -eq 'Runtime' -and $package.Include -eq 'CordiSharp')) { throw 'Only the CordiSharp core may enter the shared runtime' }
 }
}
Write-Output 'Project boundaries passed.'
