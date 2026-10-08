param([string]$Release = 'artifacts/network-release', [string]$Output = ('artifacts/m4/release-check-'+[guid]::NewGuid()))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$releasePath = [IO.Path]::GetFullPath($Release)
$outputPath = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outputPath) { throw 'Choose a new evidence directory.' }
New-Item -ItemType Directory -Path $outputPath | Out-Null
$rsa = [Security.Cryptography.RSA]::Create(2048)
$request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
$certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1),[DateTimeOffset]::UtcNow.AddDays(2))
[IO.File]::WriteAllBytes("$outputPath/server.pfx",$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx))
$pin = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificate.RawData))
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$key = [guid]::NewGuid().ToString('N')
$url = "https://127.0.0.1:$port/"
@{module="$repo/samples/quick-cards";listen=$url;roomKey=$key;controllers=@{p1='human';p2='human'};seed=42;output='records';certificate='server.pfx'} | ConvertTo-Json -Depth 6 | Set-Content "$outputPath/server.json" -Encoding utf8NoBOM
$empty = New-Item -ItemType Directory "$outputPath/no-dotnet"
$environment = @{DOTNET_ROOT=$empty.FullName;DOTNET_ROOT_X64=$empty.FullName;DOTNET_MULTILEVEL_LOOKUP='0'}
$server = $null; $clients = @()
try {
    $server = Start-Process -FilePath "$releasePath/Server/Server.exe" -ArgumentList @("`"$outputPath/server.json`"") -Environment $environment -WindowStyle Hidden -PassThru -RedirectStandardOutput "$outputPath/server.stdout.txt" -RedirectStandardError "$outputPath/server.stderr.txt"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path "$outputPath/records/ready.json") -and !$server.HasExited -and $watch.Elapsed.TotalSeconds -lt 20) { Start-Sleep -Milliseconds 100 }
    if (!(Test-Path "$outputPath/records/ready.json")) { throw "Server startup failed; see $outputPath/server.stderr.txt" }
    foreach ($seat in @('p1','p2')) {
        @{module="$repo/samples/quick-cards";server=$url;roomKey=$key;seat=$seat;credentials="$seat.identity.json";recording="$seat.recording.json";auto=$true;certificateSha256=$pin} | ConvertTo-Json | Set-Content "$outputPath/$seat.json" -Encoding utf8NoBOM
        $clients += Start-Process -FilePath "$releasePath/NetworkClient/NetworkClient.exe" -ArgumentList @("`"$outputPath/$seat.json`"") -Environment $environment -WindowStyle Hidden -PassThru -RedirectStandardOutput "$outputPath/$seat.stdout.txt" -RedirectStandardError "$outputPath/$seat.stderr.txt"
    }
    foreach ($client in $clients) {
        if (!$client.WaitForExit(30000) -or $client.ExitCode -ne 0) { throw 'Client failed or timed out; inspect evidence logs.' }
    }
    foreach ($seat in @('p1','p2')) {
        $record = Get-Content "$outputPath/$seat.recording.json" -Raw | ConvertFrom-Json
        if (!$record.frames[-1].view.ended) { throw "$seat did not reach the end" }
    }
    @{success=$true;transport='https';clients=2;server=1;selfContained=$true;separateMachines=$false;dotnetRoot=$empty.FullName} | ConvertTo-Json | Set-Content "$outputPath/result.json"
    Write-Output "PASS network release: $outputPath"
} finally {
    foreach ($client in $clients) { if (!$client.HasExited) { $client.Kill($true); $client.WaitForExit() }; $client.Dispose() }
    if ($server) {
        New-Item -ItemType Directory -Force "$outputPath/records" | Out-Null
        Set-Content "$outputPath/records/stop.request" 'stop'
        if (!$server.WaitForExit(10000)) { $server.Kill($true); $server.WaitForExit() }
        $server.Dispose()
    }
    $certificate.Dispose(); $rsa.Dispose()
}
