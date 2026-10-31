param([Parameter(Mandatory)][string]$Destination, [string]$SourceRevision = 'b10583e')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$target = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $target) { throw "Candidate source directory already exists: $target" }
New-Item -ItemType Directory -Path $target | Out-Null
# Pin the pre-adaptation baseline so this historical experiment remains repeatable.
$paths = git -C $repo ls-tree --name-only "$($SourceRevision):src/Application.Core"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the pinned source revision' }
foreach ($name in $paths | Where-Object { $_ -like '*.cs' }) {
    $source = git -C $repo show "$($SourceRevision):src/Application.Core/$name"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read baseline source: $name" }
    [IO.File]::WriteAllText((Join-Path $target $name), ($source -join "`n") + "`n")
}
function Replace-Source([string]$Name, [string]$Before, [string]$After) {
    $path = Join-Path $target $Name
    $source = (Get-Content -LiteralPath $path -Raw).Replace("`r`n", "`n")
    $Before = $Before.Replace("`r`n", "`n")
    if (-not $source.Contains($Before, [StringComparison]::Ordinal)) { throw "Production source changed: $Name / $Before" }
    $source.Replace($Before, $After, [StringComparison]::Ordinal) | Set-Content -LiteralPath $path -Encoding utf8
}
Replace-Source 'ContentJson.cs' 'using System.Text.Json.Serialization;' 'using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;'
$optionsBefore = "internal static readonly JsonSerializerOptions Options=new()`n    {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};"
Replace-Source 'ContentJson.cs' $optionsBefore 'internal static readonly JsonSerializerOptions Options=ContentJsonContext.Default.Options;
    private static JsonTypeInfo<T> TypeInfo<T>()=>(JsonTypeInfo<T>)(ContentJsonContext.Default.GetTypeInfo(typeof(T))
        ?? throw new NotSupportedException("Missing static content JSON contract: "+typeof(T).FullName));'
Replace-Source 'ContentJson.cs' 'json.RootElement.Deserialize<T>(Options)' 'json.RootElement.Deserialize(TypeInfo<T>())'
Replace-Source 'ContentJson.cs' 'JsonSerializer.Serialize(value,Options)' 'JsonSerializer.Serialize(value,TypeInfo<T>())'
Replace-Source 'ContentJson.cs' 'JsonSerializer.SerializeToElement(value,Options)' 'JsonSerializer.SerializeToElement(value,TypeInfo<T>())'
Replace-Source 'GameRuntime.cs' 'ContentJson.Canonical(new{identity,payload})' 'ContentJson.Canonical(new ContentFingerprintInput(identity,payload))'
Replace-Source 'GamePackage.cs' 'private sealed record PackageManifest' 'internal sealed record PackageManifest'
# Author-only metadata is opaque JSON; it cannot remain an arbitrary CLR object in a static codec.
Replace-Source 'PrecompiledPlayer.cs' 'string Instructions,object Versions)' 'string Instructions,System.Text.Json.JsonElement Versions)'
Replace-Source 'PrecompiledPlayer.cs' 'ContentJson.Write(content.Versions)' 'content.Versions.GetRawText()'
Copy-Item -LiteralPath "$PSScriptRoot/ContentJsonContext.cs.template" -Destination "$target/ContentJsonContext.cs"
Write-Output $target
