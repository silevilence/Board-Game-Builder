param([string]$Output='artifacts/native-repro-check')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$target=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $target) { throw 'Reproducibility evidence output already exists.' }
foreach($pass in @('first','second')) {
    $native=Join-Path $target 'native/Bgb.Entry.Repro.exe'
    if(Test-Path -LiteralPath $native) {
        if(-not ([IO.Path]::GetFullPath($native)).StartsWith($target.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Native output escaped evidence directory.' }
        Remove-Item -LiteralPath $native
    }
    dotnet publish "$repo/src/Toolkit.Entry/Toolkit.Entry.csproj" -c Release -r win-x64 -p:AssemblyName=Bgb.Entry.Repro -p:OutputType=Exe -p:DebugSymbols=false -p:DebugType=None -p:NativeOutputPath="$target/native/" -o "$target/$pass"
    if($LASTEXITCODE -ne 0) { throw 'Native reproducibility publish failed.' }
}
$first=(Get-FileHash "$target/first/Bgb.Entry.Repro.exe").Hash
$second=(Get-FileHash "$target/second/Bgb.Entry.Repro.exe").Hash
@{success=($first -eq $second);first=$first;second=$second} | ConvertTo-Json | Set-Content "$target/result.json"
if($first -ne $second) { throw 'Native entry differs after relinking identical inputs.' }
