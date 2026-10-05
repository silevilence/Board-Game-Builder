$ErrorActionPreference = 'Stop'
$allowed = @{
 'Kernel.Abstractions' = @(); 'Kernel' = @('Kernel.Abstractions'); 'Modules' = @('Kernel.Abstractions')
 'Lua' = @('Kernel.Abstractions'); 'Runtime' = @('Kernel','Modules','Lua')
 'View.Abstractions' = @('Kernel.Abstractions'); 'View.MonoGame' = @('View.Abstractions')
 'Player' = @('Runtime','View.MonoGame'); 'Editor' = @('Runtime','View.MonoGame')
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
}
Write-Output 'Project boundaries passed.'
