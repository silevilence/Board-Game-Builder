param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$repo='C:/code/dotnet/Board-Game-Builder'
$target=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $target){throw 'Choose a new output directory'}
# Same self-contained components and validation as publish-toolkit, before optional ZIP/compact layouts.
& "$repo/scripts/publish-author.ps1" -Output $target
if($LASTEXITCODE -ne 0){throw 'Author/Player publish failed'}
foreach($component in @(@('Editor','editor'),@('Player','debug-player'))) {
  dotnet publish "$repo/src/$($component[0])/$($component[0]).csproj" -c Release -r win-x64 --self-contained true -p:EnableEditorDebug=true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "$target/$($component[1])"
  if($LASTEXITCODE -ne 0){throw 'Component publish failed'}
  Copy-Item -LiteralPath "$target/worker" -Destination "$target/$($component[1])/worker" -Recurse
}
$sourceCommit=git -C $repo rev-parse HEAD
@{api=1;version='0.2.1';platform='win-x64';commit=$sourceCommit;entries=@{editor='editor/Editor.exe';cli='bgb.exe';player='player/Player.exe';debugPlayer='debug-player/Player.exe';documentation='docs/author/entry.md'}} | ConvertTo-Json -Depth 6 | Set-Content "$target/toolkit.json" -Encoding utf8NoBOM
Copy-Item -LiteralPath "$repo/samples/quick-cards" -Destination "$target/docs/author/example" -Recurse
Copy-Item -LiteralPath "$repo/changelog.md" -Destination $target
Copy-Item -LiteralPath "$repo/docs/releases.md" -Destination "$target/README.md"
& "$repo/scripts/validate-toolkit.ps1" -Root $target -Evidence "$target.validation"
if($LASTEXITCODE -ne 0){throw 'Toolkit validation failed'}
$sourceFiles=@(git -C $repo ls-files; git -C $repo ls-files --others --exclude-standard) | Where-Object {$_ -match '^(src/|tests/|docs/author/|Directory.Build.props|BoardGameBuilder.slnx)'} | Sort-Object -Unique
@{baseCommit=$sourceCommit;dirty=@(git -C $repo status --porcelain).Count -gt 0;files=@($sourceFiles | ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash}})} | ConvertTo-Json -Depth 6 | Set-Content "$target.validation/source.json" -Encoding utf8NoBOM
Write-Output "Validated complete local distribution: $target"
