param([Parameter(Mandatory)][string]$ExportDirectory, [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$SourceEvidence)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (Test-Path -LiteralPath $Output) { throw 'Choose a fresh Sandbox evidence directory' }
    if (Get-Process WindowsSandbox,WindowsSandboxRemoteSession -ErrorAction SilentlyContinue) { throw 'A Sandbox is already open; preserve it and run this check after it closes.' }
    $export=(Resolve-Path -LiteralPath $ExportDirectory).Path
    New-Item -ItemType Directory -Path $Output | Out-Null
    $Output=(Resolve-Path -LiteralPath $Output).Path
    $snapshot=Join-Path $Output 'scripts'
    New-Item -ItemType Directory -Path $snapshot | Out-Null
    Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $snapshot 'validate-flow-sandbox.ps1')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'validate-standalone-windows.ps1') -Destination (Join-Path $snapshot 'validate-standalone-windows.ps1')
    $files=[ordered]@{}
    Get-ChildItem -LiteralPath $export -File -Recurse | Sort-Object FullName | ForEach-Object {
        $files[[IO.Path]::GetRelativePath($export,$_.FullName).Replace('\','/')]=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()
    }
    [ordered]@{ format=1; sourceEvidenceSha256=(Get-FileHash -LiteralPath $SourceEvidence).Hash.ToLowerInvariant();
        launcherScriptSha256=(Get-FileHash -LiteralPath (Join-Path $snapshot 'validate-flow-sandbox.ps1')).Hash.ToLowerInvariant();
        guestScriptSha256=(Get-FileHash -LiteralPath (Join-Path $snapshot 'validate-standalone-windows.ps1')).Hash.ToLowerInvariant(); files=$files } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'export-manifest.json') -Encoding utf8
    $exportXml=[Security.SecurityElement]::Escape($export)
    $outputXml=[Security.SecurityElement]::Escape($Output)
    $scriptsXml=[Security.SecurityElement]::Escape($snapshot)
    $configuration=@"
<Configuration>
  <Networking>Disable</Networking>
  <MappedFolders>
    <MappedFolder><HostFolder>$exportXml</HostFolder><SandboxFolder>C:\Input</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$outputXml</HostFolder><SandboxFolder>C:\Evidence</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$scriptsXml</HostFolder><SandboxFolder>C:\Tools</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Tools\validate-standalone-windows.ps1 -ExpectedManifest C:\Evidence\export-manifest.json -Shutdown</Command></LogonCommand>
</Configuration>
"@
    $configPath=Join-Path $Output 'verify.wsb'
    [IO.File]::WriteAllText($configPath,$configuration)
    $process=Start-Process WindowsSandbox.exe -ArgumentList ('"'+$configPath+'"') -WindowStyle Hidden -PassThru
    # Cold Sandbox startup plus copying a self-contained runtime can take several minutes.
    $deadline=[DateTime]::UtcNow.AddMinutes(10)
    $resultPath=Join-Path $Output 'result.json'
    while (-not (Test-Path -LiteralPath $resultPath)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Sandbox result timed out; inspect the task-created Sandbox (launcher PID $($process.Id))." }
        Start-Sleep -Seconds 2
    }
    $result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if (-not $result.success) { throw "Sandbox failed: $($result.error)" }
    if ($result.manifestSha256 -ne (Get-FileHash (Join-Path $Output 'export-manifest.json')).Hash.ToLowerInvariant() -or $result.filesVerified -ne $files.Count) { throw 'Guest did not verify the exported bytes' }
    [ordered]@{ success=$true; manifestSha256=$result.manifestSha256; launcherScriptSha256=(Get-FileHash -LiteralPath (Join-Path $snapshot 'validate-flow-sandbox.ps1')).Hash.ToLowerInvariant(); timeoutMinutes=10 } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'host.json') -Encoding utf8
    $result | ConvertTo-Json
}
finally { Pop-Location }
