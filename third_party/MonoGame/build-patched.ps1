param(
    [string]$SourceRoot='.scratch/native-aot-gl/MonoGame',
    [string]$Output='artifacts/native-aot/monogame'
)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source=[IO.Path]::GetFullPath((Join-Path $repo $SourceRoot))
$destination=[IO.Path]::GetFullPath((Join-Path $repo $Output))
$commit='f34200720b558125964273fd3e7ab44cde0b0429'
$patch=Join-Path $PSScriptRoot 'desktopgl-explicit-context.patch'
$expectedPatch='46A421B691E8CC3E130BA0FC89608AACE4C8586D83AE0E51E1EAC4CD9D86A516'
if((Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash -ne $expectedPatch){throw 'MonoGame patch hash changed; review and update its pinned identity.'}
if(-not (Test-Path -LiteralPath $source)) {
    git clone --filter=blob:none --no-checkout https://github.com/MonoGame/MonoGame.git $source
    if($LASTEXITCODE -ne 0){throw 'MonoGame source acquisition failed'}
    git -C $source checkout --detach $commit
    if($LASTEXITCODE -ne 0){throw 'MonoGame pinned checkout failed'}
}
if((git -C $source rev-parse HEAD).Trim() -ne $commit){throw 'MonoGame source revision does not match the pinned upstream commit'}
$status=@(git -C $source status --porcelain)
if($status.Count -eq 0) {
    git -C $source apply --check $patch
    if($LASTEXITCODE -ne 0){throw 'MonoGame patch does not apply to pinned source'}
    git -C $source apply $patch
    if($LASTEXITCODE -ne 0){throw 'MonoGame patch application failed'}
} else {
    if(@($status | Where-Object {$_ -notmatch '^ M MonoGame.Framework/'}).Count -gt 0){throw 'Unexpected MonoGame source modifications; use an isolated clean source checkout'}
    $actual=[IO.Path]::GetTempFileName()
    try {
        git -C $source diff --binary "--output=$actual"
        if($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash -ne $expectedPatch){throw 'Existing MonoGame modifications differ from the pinned patch'}
    } finally {Remove-Item -LiteralPath $actual -ErrorAction Stop}
}
git -C $source submodule update --init ThirdParty/StbImageSharp ThirdParty/StbImageWriteSharp
if($LASTEXITCODE -ne 0){throw 'Pinned MonoGame image decoder submodules could not be restored'}
if(-not (Test-Path -LiteralPath $destination)){New-Item -ItemType Directory -Path $destination | Out-Null}
$log=Join-Path $destination 'source-build.log'
dotnet build (Join-Path $source 'MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj') -c Release -t:Rebuild -p:Version=3.8.4.1 -p:GenerateDocumentationFile=false --nologo *> $log
if($LASTEXITCODE -ne 0){throw "Patched MonoGame build failed; see $log"}
$assembly=Join-Path $destination 'MonoGame.Framework.dll'
Copy-Item -LiteralPath (Join-Path $source 'Artifacts/MonoGame.Framework/DesktopGL/Release/MonoGame.Framework.dll') -Destination $assembly
@{
    upstream='https://github.com/MonoGame/MonoGame';commit=$commit;packageVersion='3.8.4.1';patchSha256=$expectedPatch
    sdk=(dotnet --version);assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
    submodules=@(git -C $source submodule status ThirdParty/StbImageSharp ThirdParty/StbImageWriteSharp)
    scope='DesktopGL explicit SDL windows, one graphics thread; actual consumed APIs require strict NativeAOT publication and runtime verification'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'build-manifest.json') -Encoding utf8
Write-Output $assembly
