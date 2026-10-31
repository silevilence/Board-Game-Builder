param([string]$Output='artifacts/native-aot/gl-embedded')
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$destination=[IO.Path]::GetFullPath((Join-Path $repo $Output))
& (Join-Path $repo 'third_party/MonoGame/build-patched.ps1')
if(-not (Test-Path -LiteralPath $destination)){New-Item -ItemType Directory -Path $destination | Out-Null}
$publish=Join-Path $destination 'publish'
dotnet publish (Join-Path $PSScriptRoot 'GlEmbedded.csproj') -c Release -r win-x64 -o $publish --nologo *> (Join-Path $destination 'build.log')
$buildExit=$LASTEXITCODE
$buildExit | Set-Content -LiteralPath (Join-Path $destination 'build-exit.txt')
if($buildExit -ne 0){throw 'Strict embedded NativeAOT publish failed; no run was attempted'}
$evidence=Join-Path $destination 'host'
if(-not (Test-Path -LiteralPath $evidence)){New-Item -ItemType Directory -Path $evidence | Out-Null}
$process=Start-Process -FilePath (Join-Path $publish 'AotProbe.GlEmbedded.exe') -ArgumentList ('"'+$evidence+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'stdout.log') -RedirectStandardError (Join-Path $evidence 'stderr.log')
try {
    if(-not $process.WaitForExit(45000)){$process.Kill($true);$process.WaitForExit();throw 'Embedded probe exceeded 45 seconds and was stopped'}
    $process.ExitCode | Set-Content -LiteralPath (Join-Path $evidence 'exit-code.txt')
    if($process.ExitCode -ne 0){throw 'Embedded probe failed; see host/result.txt'}
} finally {$process.Dispose()}
Get-Content -LiteralPath (Join-Path $evidence 'result.txt')
