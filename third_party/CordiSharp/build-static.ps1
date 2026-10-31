param([string]$Archive='.scratch/native-aot-cordisharp/source.zip', [string]$Output='artifacts/native-aot/cordisharp')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourceArchive=[IO.Path]::GetFullPath((Join-Path $repo $Archive))
$destination=[IO.Path]::GetFullPath((Join-Path $repo $Output))
$commit='7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f'
$archiveHash='917DF179E460C799FDD0F083071223EE622E4CD16E3036BF1108130314D35A27'
$patchHash='B07DAB43C89A82D854BBCF967CC3CEA6B6CF3C559E07AEA5D4D6A7425C21618C'
$generatorHash='A316E096C39C90C95EC9C3728D1EECB7DD514462A46B6A4B4D7267E1B7AEBC55'
if((Get-FileHash -LiteralPath "$PSScriptRoot/prepare-static.py").Hash -ne $generatorHash -or
   (Get-FileHash -LiteralPath "$PSScriptRoot/static-runtime.patch").Hash -ne $patchHash){throw 'CordiSharp adaptation changed; review its pinned identity first.'}
if(-not (Test-Path -LiteralPath $sourceArchive)) {
    New-Item -ItemType Directory -Path (Split-Path $sourceArchive -Parent) -Force | Out-Null
    Invoke-WebRequest "https://codeload.github.com/oniemiddle/CordiSharp/zip/$commit" -OutFile $sourceArchive
}
if((Get-FileHash -LiteralPath $sourceArchive).Hash -ne $archiveHash){throw 'CordiSharp source archive differs from the pinned upstream bytes.'}
$work=Join-Path $repo ('.scratch/cordisharp-static-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $sourceArchive -DestinationPath "$work/upstream"
python "$PSScriptRoot/prepare-static.py" "$work/upstream/CordiSharp-$commit" "$work/patched"
if($LASTEXITCODE -ne 0){throw 'CordiSharp static adaptation failed'}
if((Get-FileHash -LiteralPath "$work/patched/CordiSharp.patch").Hash -ne $patchHash){throw 'Generated adaptation differs from reviewed patch.'}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
dotnet build "$work/patched/CordiSharp.Patched.csproj" -c Release -o "$work/bin" --nologo *> "$destination/build.log"
if($LASTEXITCODE -ne 0){throw "CordiSharp build failed: $destination/build.log"}
Copy-Item -LiteralPath "$work/bin/CordiSharp.dll" -Destination "$destination/CordiSharp.dll"
Copy-Item -LiteralPath "$PSScriptRoot/LICENSE.txt" -Destination "$destination/CordiSharp.LICENSE.txt"
@{upstream='https://github.com/oniemiddle/CordiSharp';commit=$commit;packageVersion='0.2.0';
  archiveSha256=$archiveHash;patchSha256=$patchHash;generatorSha256=$generatorHash;
  sdk=(dotnet --version);assemblySha256=(Get-FileHash -LiteralPath "$destination/CordiSharp.dll").Hash;
  scope='Static runtime callbacks only; arbitrary reflection-based plugins remain JIT-only.'} |
  ConvertTo-Json | Set-Content -LiteralPath "$destination/build-manifest.json" -Encoding utf8
Write-Output "$destination/CordiSharp.dll"
