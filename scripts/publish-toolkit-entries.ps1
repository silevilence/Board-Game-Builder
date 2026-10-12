param([string]$Output = 'artifacts/toolkit-entries')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
foreach ($role in @('cli', 'editor', 'player', 'debugPlayer')) {
    $subsystem = if ($role -eq 'cli') { 'Exe' } else { 'WinExe' }
    dotnet publish "$repo/src/Toolkit.Entry/Toolkit.Entry.csproj" -c Release -r win-x64 -p:AssemblyName="Bgb.Entry.$role" -p:OutputType=$subsystem -p:DebugSymbols=false -p:DebugType=None -o "$Output/$role"
    if ($LASTEXITCODE -ne 0) { throw "Native bootstrap publish failed: $role" }
}
