param([string]$Output='artifacts/launcher')
$ErrorActionPreference='Stop'
$target=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $target) { throw 'Launcher output already exists.' }
foreach($project in @('Launcher','Launcher.Cli')) {
    dotnet publish "$PSScriptRoot/../src/$project/$project.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $target
    if($LASTEXITCODE -ne 0) { throw "Launcher publish failed: $project" }
}
Write-Output "Independent launcher: $target"
