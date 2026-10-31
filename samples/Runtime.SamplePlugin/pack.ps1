param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $destination) { throw "Output must be a new directory: $destination" }
dotnet build "$PSScriptRoot/Runtime.SamplePlugin.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Sample plugin build failed' }
New-Item -ItemType Directory -Path $destination | Out-Null
$assembly = Join-Path $destination 'Runtime.SamplePlugin.dll'
Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net10.0/Runtime.SamplePlugin.dll" -Destination $assembly
$manifest = [ordered]@{
    format=1; kind='runtime'; id='sample.counter'; version='1'; api=1
    entry=@{assembly='Runtime.SamplePlugin.dll'; type='Example.RuntimePlugin.Factory'; method='Create'}
    targets=@('win-x64')
    aot=@{compatible=$false; targets=@(); reason='Native publication of this package has not been verified.'}
    provides=@(@{name='sample.counter';api=1}); requires=@()
    execution=@{modes='All';scope='Instance';contributions=@(@{id='tick';phase='Update';before=$null;after=$null})}
    files=@(@{path='Runtime.SamplePlugin.dll';sha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant();role='implementation'})
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $destination 'runtime.plugin.json') -Encoding utf8
Write-Output $destination
