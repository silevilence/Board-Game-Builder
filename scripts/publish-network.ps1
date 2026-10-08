param([string]$Output = 'artifacts/network-release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$target = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $target) { throw 'Choose a new output directory.' }
foreach ($project in @('Server','NetworkClient')) {
    dotnet publish "$repo/src/$project/$project.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "$target/$project"
    if ($LASTEXITCODE -ne 0) { throw "$project publish failed" }
}
dotnet publish "$repo/src/Lua/Lua.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "$target/Server/worker"
if ($LASTEXITCODE -ne 0) { throw 'Lua worker publish failed' }
$files = [ordered]@{}
Get-ChildItem -LiteralPath $target -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($target,$_.FullName).Replace('\','/')
    if ($relative -match '(^|/)(Editor|MonoGame|Avalonia)' -or ($relative -match '(^|/)CordiSharp' -and $relative -notmatch '/CordiSharp.dll$')) { throw "Unexpected GUI/editor dependency: $relative" }
    $files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
@{ protocol=1; platform='win-x64'; files=$files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$target/release.json" -Encoding utf8NoBOM
Write-Output "Precompiled network tools: $target"
