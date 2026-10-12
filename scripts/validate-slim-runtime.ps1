# Run only in a disposable Windows Sandbox. Runtime fixtures are official verified ZIPs, not system copies.
param([string]$InputDirectory='C:\Downloads', [string]$RuntimeDirectory='C:\RuntimeDownloads', [string]$Evidence='C:\Evidence')
$ErrorActionPreference='Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or (Test-Path 'C:\Program Files\dotnet') -or (Test-Path 'C:\RuntimeFixtures')) { throw 'A fresh disposable Windows Sandbox is required.' }
try {
    $verified=Get-Content -LiteralPath "$RuntimeDirectory/verified.json" -Raw | ConvertFrom-Json
    foreach ($item in $verified) {
        $file=Join-Path $RuntimeDirectory $item.local
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA512).Hash.ToLowerInvariant() -ne $item.hash.ToLowerInvariant()) { throw 'Runtime fixture integrity mismatch.' }
    }
    New-Item -ItemType Directory -Path 'C:\RuntimeFixtures' | Out-Null
    $zip=Get-ChildItem -LiteralPath $InputDirectory -Filter '*-slim-win-x64.zip' -File
    if (@($zip).Count -ne 1) { throw 'Expected one slim toolkit ZIP.' }
    $hash=(Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ((Get-Content -LiteralPath ($zip.FullName+'.sha256') -Raw).Trim() -ne "$hash  $($zip.Name)") { throw 'Slim ZIP checksum mismatch.' }
    Expand-Archive -LiteralPath $zip.FullName -DestinationPath 'C:\Probe Toolkit'
    $checks=@()
    function Probe([string]$Name,[string]$Executable,[string]$Expected,[bool]$Success=$false) {
        $start=New-Object Diagnostics.ProcessStartInfo
        $start.FileName=$Executable; $start.Arguments='docs entry'; $start.UseShellExecute=$false; $start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
        $process=[Diagnostics.Process]::Start($start)
        $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Runtime probe timed out: $Name" }
        $out=$stdout.GetAwaiter().GetResult(); $err=$stderr.GetAwaiter().GetResult()
        $out | Set-Content "$Evidence/$Name.stdout"; $err | Set-Content "$Evidence/$Name.stderr"
        $text=$out+$err
        if (($Success -and $process.ExitCode -ne 0) -or (-not $Success -and $process.ExitCode -eq 0) -or $text -notmatch $Expected) { throw "Unexpected runtime result $Name : $text" }
        $script:checks+=@{name=$Name;exit=$process.ExitCode;diagnostic=$text}
        $process.Dispose()
    }
    Probe 'missing-core' 'C:\Probe Toolkit\bgb.exe' 'compatible x64 .NET Runtime'
    Expand-Archive -LiteralPath "$RuntimeDirectory/dotnet-runtime-10.0.12-win-x86.zip" -DestinationPath 'C:\Program Files\dotnet'
    Probe 'wrong-architecture' 'C:\Probe Toolkit\bgb.exe' 'Wrong .NET Runtime architecture'
    Move-Item -LiteralPath 'C:\Program Files\dotnet' -Destination 'C:\RuntimeFixtures\x86'
    Expand-Archive -LiteralPath "$RuntimeDirectory/dotnet-runtime-10.0.11-win-x64.zip" -DestinationPath 'C:\Program Files\dotnet'
    Probe 'older-patch' 'C:\Probe Toolkit\bgb.exe' 'Microsoft.NETCore.App 10.0.12'
    Move-Item -LiteralPath 'C:\Program Files\dotnet' -Destination 'C:\RuntimeFixtures\old'
    Expand-Archive -LiteralPath "$RuntimeDirectory/dotnet-runtime-10.0.12-win-x64.zip" -DestinationPath 'C:\Program Files\dotnet'
    Probe 'compatible-core' 'C:\Probe Toolkit\bgb.exe' 'game.start.json' $true
    Probe 'missing-desktop' 'C:\Probe Toolkit\editor\Editor.exe' 'Microsoft.WindowsDesktop.App 10.0.12'
    Expand-Archive -LiteralPath "$RuntimeDirectory/windowsdesktop-runtime-10.0.12-win-x64.zip" -DestinationPath 'C:\Program Files\dotnet' -Force
    & 'C:\Program Files\dotnet\dotnet.exe' --list-runtimes | Set-Content "$Evidence/installed-runtimes.txt"
    $sdks=@(& 'C:\Program Files\dotnet\dotnet.exe' --list-sdks)
    if ($LASTEXITCODE -ne 0 -or $sdks.Count -ne 0 -or (Test-Path 'C:\Program Files\dotnet\sdk')) { throw 'SDK must be absent.' }
    ConvertTo-Json -InputObject $sdks | Set-Content "$Evidence/installed-sdks.json"
    @{success=$true;checks=$checks;zip=$zip.Name;sha256=$hash} | ConvertTo-Json -Depth 6 | Set-Content "$Evidence/runtime-checks.json"
    & "$PSScriptRoot/validate-clean-windows.ps1" -InputDirectory $InputDirectory -Mode slim -ArchiveName '*-slim-win-x64.zip' -Components "$InputDirectory/components" -Evidence "$Evidence/acceptance"
} catch {
    @{success=$false;error=($_ | Out-String);checks=$checks} | ConvertTo-Json -Depth 6 | Set-Content "$Evidence/runtime-checks.json"
    exit 1
}
