# Maps only exported game directories and a writable evidence folder into a fresh offline Sandbox.
param([string]$Evidence='C:\Evidence')
$ErrorActionPreference='Stop';$checks=@()
try {
    if($env:USERNAME -ne 'WDAGUtilityAccount' -or (Get-Command dotnet -ErrorAction SilentlyContinue) -or (Test-Path 'C:\Program Files\dotnet') -or (Test-Path 'C:\LauncherSource') -or (Test-Path 'C:\Downloads')) {throw 'Isolated player-only Sandbox required'}
    New-Item -ItemType Directory -Force 'C:\独立 游戏' | Out-Null
    foreach($mode in @('standalone','slim')) {foreach($version in @('0.2.0','0.2.1')) {
        $name="$mode-$version";$game="C:\独立 游戏\$name";Copy-Item -LiteralPath "C:\Games\$name" -Destination $game -Recurse
        if(Get-ChildItem $game -Recurse -File | Where-Object Name -in @('Editor.exe','bgb.exe','bgb-launcher.exe','Launcher.exe','toolkit.json')) {throw 'Author tools unexpectedly present'}
        foreach($entry in @('Play.exe','Player.exe')) {
            $bytes=[IO.File]::ReadAllBytes("$game/$entry");$pe=[BitConverter]::ToInt32($bytes,60)
            if([BitConverter]::ToUInt16($bytes,$pe+24+68) -ne 2) {throw "Game entry must use the GUI subsystem: $entry"}
        }
        $batch="$Evidence\batch-$name";$report="$Evidence\gui-$name.json"
        foreach($arguments in @(
            @('batch',('"'+$game+'\game.bgm"'),'--count','3','--seed','42','--out',('"'+$batch+'"')),
            @('gui',('"'+$game+'\game.bgm"'),'--smoke',('"'+$report+'"')))) {
            $process=Start-Process -FilePath "$game/Play.exe" -ArgumentList $arguments -WorkingDirectory 'C:\' -WindowStyle Hidden -PassThru
            if(-not $process.WaitForExit(180000)) {$process.Kill();throw "Player timed out: $name"}
            if($process.ExitCode -ne 0) {throw "Player failed: $name"}
        }
        if(-not (Get-Content $report -Raw|ConvertFrom-Json).success) {throw "Player GUI failed: $name"}
        $checks+=@{mode=$mode;version=$version;gameSha256=(Get-FileHash "$game/game.bgm").Hash;gui=$report;batch=$batch}
    }}
    @{success=$true;systemRuntime=$false;authors=$false;sharedStore=$false;network=$false;checks=$checks}|ConvertTo-Json -Depth 8|Set-Content "$Evidence/result.json" -Encoding utf8
} catch {@{success=$false;error=($_|Out-String);checks=$checks}|ConvertTo-Json -Depth 8|Set-Content "$Evidence/result.json" -Encoding utf8;exit 1}
