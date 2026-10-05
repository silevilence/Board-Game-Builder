param([Parameter(Mandatory)][string]$Export, [string]$Evidence='artifacts/sandbox-evidence')
$ErrorActionPreference='Stop'
$game=(Resolve-Path -LiteralPath $Export).Path
$output=[IO.Path]::GetFullPath($Evidence)
New-Item -ItemType Directory -Force -Path $output | Out-Null
# Prepare only: launching this configuration remains an explicit host operation.
@'
$ErrorActionPreference='Stop'
try {
  @{ os=[Environment]::OSVersion.VersionString; dotnet=(Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source); editorInstalled=(Test-Path 'C:\Program Files\Board Game Builder') } | ConvertTo-Json | Set-Content C:\Evidence\environment.json
  $process=Start-Process C:\Game\Player.exe -ArgumentList 'gui C:\Game\game.bgm --smoke C:\Evidence\games.json' -PassThru -Wait
  $process.ExitCode | Set-Content C:\Evidence\exit-code.txt
} catch { $_ | Out-String | Set-Content C:\Evidence\error.txt }
'@ | Set-Content -LiteralPath "$output/verify.ps1" -Encoding utf8
$escapedGame=[System.Security.SecurityElement]::Escape($game)
$escapedOutput=[System.Security.SecurityElement]::Escape($output)
@"
<Configuration>
 <Networking>Disable</Networking>
 <MappedFolders>
  <MappedFolder><HostFolder>$escapedGame</HostFolder><SandboxFolder>C:\Game</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
  <MappedFolder><HostFolder>$escapedOutput</HostFolder><SandboxFolder>C:\Evidence</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
 </MappedFolders>
 <LogonCommand><Command>powershell.exe -NoProfile -File C:\Evidence\verify.ps1</Command></LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath "$output/verify.wsb" -Encoding utf8
Write-Output "$output/verify.wsb"
