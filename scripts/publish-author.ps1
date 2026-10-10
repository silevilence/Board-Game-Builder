param([string]$Output = 'artifacts/author-release', [string]$PlayerRelease)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$target = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $target) { throw 'Author output already exists; choose a new directory.' }
$staging = $target + '.staging-' + [guid]::NewGuid()
dotnet publish "$repo/src/Cli/Cli.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $staging
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed; staging retained for diagnosis.' }
dotnet publish "$repo/src/Lua/Lua.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "$staging/worker"
if ($LASTEXITCODE -ne 0) { throw 'Lua worker publish failed; staging retained for diagnosis.' }
if ($PlayerRelease) { Copy-Item -LiteralPath ([IO.Path]::GetFullPath($PlayerRelease)) -Destination "$staging/player" -Recurse }
else { & "$repo/scripts/publish-player.ps1" -Output "$staging/player" }
if ($LASTEXITCODE -ne 0) { throw 'Player preparation failed.' }
& "$staging/bgb.exe" docs entry | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Published CLI documentation check failed.' }
Move-Item -LiteralPath $staging -Destination $target
Write-Output "Self-contained author CLI: $target"
