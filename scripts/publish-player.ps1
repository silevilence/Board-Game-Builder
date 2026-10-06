param([string]$Output = 'artifacts/player-release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$target = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $target) { throw 'Release output already exists. Choose a new directory.' }
# This is the engine-maintainer build. Export never calls dotnet or a compiler.
dotnet publish "$repo/src/Player/Player.csproj" -c Release -r win-x64 --self-contained true -p:EnableEditorDebug=false -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $target
if ($LASTEXITCODE -ne 0) { throw 'Player publish failed' }
dotnet publish "$repo/src/Lua/Lua.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "$target/worker"
if ($LASTEXITCODE -ne 0) { throw 'Lua worker publish failed' }
# Avalonia 12 native runtime packages include optional PDBs even with DebugSymbols=false.
Get-ChildItem -LiteralPath $target -Filter *.pdb -File -Recurse | ForEach-Object {
    $symbol = [IO.Path]::GetFullPath($_.FullName)
    if (-not $symbol.StartsWith($target.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Symbol escaped release directory' }
    Remove-Item -LiteralPath $symbol
}
$files = [ordered]@{}
Get-ChildItem -LiteralPath $target -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($target,$_.FullName).Replace('\','/')
    if ($relative -match '(^|/)(Editor|CordiSharp)|\.pdb$|\.csproj$') { throw "Unexpected editor/development content: $relative" }
    $files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
@{ api=1; version='0.2.0'; platform='win-x64'; files=$files; guiEntry=$true; editorDebug=$false } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$target/release.json" -Encoding utf8NoBOM
Write-Output "Precompiled Player: $target"
