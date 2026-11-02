param([Parameter(Mandatory)][string]$Tag, [string]$Output = 'artifacts/toolkit', [string]$Archives = 'artifacts/release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
python "$PSScriptRoot/release.py" check --tag $Tag
if ($LASTEXITCODE -ne 0) { throw 'Release metadata validation failed.' }
$target = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $target) { throw 'Toolkit output already exists.' }
if (Test-Path -LiteralPath ($target+'.slim')) { throw 'Slim toolkit output already exists.' }
$staging = $target + '.staging-' + [guid]::NewGuid()
# Build pinned dependencies before any consumer; use isolated static outputs so
# the ordinary Editor/CLI keep the normal CordiSharp implementation.
& "$repo/third_party/MonoGame/build-patched.ps1"
& "$repo/third_party/CordiSharp/build-static.ps1"
& "$PSScriptRoot/publish-author.ps1" -Output $staging -IncludeDesktopGlBackend $true
foreach ($component in @(@('Editor','editor','true'), @('Player','debug-player','true'))) {
    dotnet publish "$repo/src/$($component[0])/$($component[0]).csproj" -c Release -r win-x64 --self-contained true -p:EnableEditorDebug=$($component[2]) -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "$staging/$($component[1])"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($component[0])" }
    Copy-Item -LiteralPath "$staging/worker" -Destination "$staging/$($component[1])/worker" -Recurse
    Copy-Item -LiteralPath "$staging/player/backends" -Destination "$staging/$($component[1])/backends" -Recurse
}
$nativeBuild = $staging + '.native-build'
dotnet build "$repo/tools/NativeSdk/NativeSdk.csproj" -c Release -p:UseStaticRuntime=true --artifacts-path $nativeBuild
if ($LASTEXITCODE -ne 0) { throw 'Static runtime SDK build failed.' }
dotnet "$nativeBuild/bin/NativeSdk/release/NativeSdk.dll" pack "$staging/native-sdk"
if ($LASTEXITCODE -ne 0) { throw 'Native runtime SDK packaging failed.' }
Copy-Item -LiteralPath "$staging/native-sdk" -Destination "$staging/editor/native-sdk" -Recurse
$commit = git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source commit.' }
@{ api=1; version=$Tag.Substring(1); platform='win-x64'; commit=$commit; entries=@{
    editor='editor/Editor.exe'; cli='bgb.exe'; player='player/Player.exe'; debugPlayer='debug-player/Player.exe'; documentation='docs/author/entry.md'
} } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$staging/toolkit.json" -Encoding utf8NoBOM
Copy-Item -LiteralPath "$repo/changelog.md" -Destination $staging
Copy-Item -LiteralPath "$repo/docs/releases.md" -Destination "$staging/README.md"
Copy-Item -LiteralPath "$repo/samples/quick-cards" -Destination "$staging/docs/author/example" -Recurse
& "$PSScriptRoot/validate-toolkit.ps1" -Root $staging -Evidence "$staging.validation"
# Remove optional symbols before component identities are assigned, including export sources.
Get-ChildItem -LiteralPath $staging -Filter *.pdb -File -Recurse | ForEach-Object {
    $symbol = [IO.Path]::GetFullPath($_.FullName)
    if (-not $symbol.StartsWith($staging.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Symbol escaped staging directory' }
    Remove-Item -LiteralPath $symbol
}
python "$PSScriptRoot/toolkit_manifest.py" generate --root $staging --components "$Archives/components"
if ($LASTEXITCODE -ne 0) { throw 'Dependency manifest generation failed.' }
& "$PSScriptRoot/publish-toolkit-entries.ps1"
$slim = $staging + '.slim'
python "$PSScriptRoot/slim_toolkit.py" --source $staging --output $slim
if ($LASTEXITCODE -ne 0) { throw 'Slim variant preparation failed.' }
python "$PSScriptRoot/toolkit_manifest.py" generate --root $slim --components "$Archives/components"
if ($LASTEXITCODE -ne 0) { throw 'Slim dependency manifest generation failed.' }
foreach ($variant in @(@($staging,$target), @($slim,($target+'.slim')))) {
    $compact = $variant[0] + '.compact'
    python "$PSScriptRoot/compact_toolkit.py" prepare --source $variant[0] --root $compact --entries 'artifacts/toolkit-entries'
    if ($LASTEXITCODE -ne 0) { throw 'Compact layout preparation failed.' }
    python "$PSScriptRoot/toolkit_manifest.py" generate --root $compact --components "$Archives/components"
    if ($LASTEXITCODE -ne 0) { throw 'Compact manifest generation failed.' }
    python "$PSScriptRoot/compact_toolkit.py" compact --root $compact
    if ($LASTEXITCODE -ne 0) { throw 'Content compaction failed.' }
    & "$PSScriptRoot/validate-toolkit.ps1" -Root $compact -Evidence "$compact.validation" -Components "$Archives/components"
    python "$PSScriptRoot/release.py" archive --tag $Tag --root $compact --output $Archives --component-format
    if ($LASTEXITCODE -ne 0) { throw 'Archive validation failed.' }
    Move-Item -LiteralPath $compact -Destination $variant[1]
    Write-Output "Validated toolkit: $($variant[1])"
}
