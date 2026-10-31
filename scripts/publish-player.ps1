param([string]$Output = 'artifacts/player-release', [string]$HostProject='src/Player/Player.csproj',
    [string]$EntryPoint='Player.exe', [ValidateSet(2,3)][int]$PackageFormat=2, [bool]$IncludeLuaWorker=$true, [bool]$IncludeRuntimePlugins=$true,
    [bool]$IncludeDesktopGlBackend=$false)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$productVersion = ([xml](Get-Content -LiteralPath "$repo/Directory.Build.props" -Raw)).Project.PropertyGroup.Version
$target = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $target) { throw 'Release output already exists. Choose a new directory.' }
# This is the engine-maintainer build. Export never calls dotnet or a compiler.
dotnet publish (Join-Path $repo $HostProject) -c Release -r win-x64 --self-contained true -p:EnableEditorDebug=false -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $target
if ($LASTEXITCODE -ne 0) { throw 'Player publish failed' }
if($IncludeDesktopGlBackend) {
    dotnet run --project "$repo/tools/GraphicsBackendPack" -c Release -- "$target/backends/desktopgl"
    if($LASTEXITCODE -ne 0) {throw 'DesktopGL backend packaging failed'}
}
if($IncludeLuaWorker) {
    dotnet publish "$repo/src/Lua/Lua.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "$target/worker"
    if ($LASTEXITCODE -ne 0) { throw 'Lua worker publish failed' }
}
if($IncludeRuntimePlugins) {
    $contractProcess=Start-Process -FilePath (Join-Path $target $EntryPoint) -ArgumentList @('runtime-host',('"'+$target+'"')) -WindowStyle Hidden -PassThru -Wait
    if($contractProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $target 'runtime-host.json'))) {throw 'Player did not declare external runtime plugin support'}
}
# Avalonia 12 native runtime packages include optional PDBs even with DebugSymbols=false.
Get-ChildItem -LiteralPath $target -Filter *.pdb -File -Recurse | ForEach-Object {
    $symbol = [IO.Path]::GetFullPath($_.FullName)
    if (-not $symbol.StartsWith($target.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Symbol escaped release directory' }
    Remove-Item -LiteralPath $symbol
}
$files = [ordered]@{}
Get-ChildItem -LiteralPath $target -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($target,$_.FullName).Replace('\','/')
    if ($relative -match '(^|/)Editor|\.pdb$|\.csproj$' -or ($relative -match '(^|/)CordiSharp' -and $relative -ne 'CordiSharp.dll')) { throw "Unexpected editor/development content: $relative" }
    $files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
$catalogFile=Join-Path $env:TEMP ('bgb-release-catalog-'+[guid]::NewGuid()+'.json')
try {
    $catalogProcess=Start-Process -FilePath (Join-Path $target $EntryPoint) -ArgumentList @('runtime-catalog',$catalogFile) -WindowStyle Hidden -PassThru -Wait
    if($catalogProcess.ExitCode -ne 0) {throw 'Could not read published runtime catalog'}
    $runtimePlugins=Get-Content -LiteralPath $catalogFile -Raw | ConvertFrom-Json -AsHashtable
} finally {if(Test-Path -LiteralPath $catalogFile) {Remove-Item -LiteralPath $catalogFile}}
@{ api=1; version=$productVersion; platform='win-x64'; files=$files; guiEntry=$true; editorDebug=$false; runtimeApi=1; runtimePlugins=$runtimePlugins;entryPoint=$EntryPoint;packageFormat=$PackageFormat } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$target/release.json" -Encoding utf8NoBOM
Write-Output "Precompiled Player: $target"
