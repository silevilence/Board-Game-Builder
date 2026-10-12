# Run in a disposable Windows Sandbox. Inputs are original Release attachments and verified runtime ZIPs.
param([ValidateSet('standalone','slim')][string]$Mode='standalone',[string]$Evidence='C:\Evidence')
$ErrorActionPreference='Stop';$owned=@();$steps=@();$sequence=0
function Invoke-L([string[]]$Arguments,[int]$Expected=0) {
    $start=New-Object Diagnostics.ProcessStartInfo;$start.FileName="$script:application\bgb-launcher.exe";$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.Arguments=((@('--home',$script:settings)+$Arguments)|ForEach-Object {'"'+$_+'"'}) -join ' '
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.StandardOutputEncoding=[Text.Encoding]::UTF8;$start.StandardErrorEncoding=[Text.Encoding]::UTF8
    $process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    if(-not $process.WaitForExit(240000)) {$process.Kill();throw "Command timed out: $Arguments"}
    $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();$exit=$process.ExitCode;$process.Dispose()
    $script:sequence++;$out|Set-Content "$Evidence/command-$script:sequence.stdout" -Encoding utf8;$err|Set-Content "$Evidence/command-$script:sequence.stderr" -Encoding utf8
    $script:steps+=@{arguments=$Arguments;exit=$exit;output="command-$script:sequence.stdout"}
    if($exit -ne $Expected) {throw "Exit $exit for $Arguments : $out $err"}
    if($Expected -ne 0) {return $out+$err};$parsed=$out | ConvertFrom-Json;foreach($item in $parsed) {Write-Output $item}
}
function Call([string]$Operation,[hashtable]$Parameters=@{},[int]$Expected=0) {
    $path="$Evidence/args-$script:sequence.json";$Parameters|ConvertTo-Json -Depth 12|Set-Content $path -Encoding utf8
    return Invoke-L @('cli','--version',$script:version,'--project',$script:project,'--','call',$Operation,'--args-file',$path) $Expected
}
function Stop-Owned($item) {
    $process=Get-Process -Id $item.processId -ErrorAction SilentlyContinue
    if($process -and $process.StartTime.ToUniversalTime() -eq ([datetime]$item.startedAt).ToUniversalTime() -and $process.MainModule.FileName -eq $item.image) {& taskkill /PID $item.processId /T /F | Out-File "$Evidence/stopped.log" -Append}
}
try {
    if($env:USERNAME -ne 'WDAGUtilityAccount' -or (Test-Path 'C:\Program Files\dotnet') -or (Get-Command dotnet -ErrorAction SilentlyContinue)) {throw 'Fresh Sandbox required'}
    if($Mode -eq 'slim') {
        $verified=Get-Content 'C:\RuntimeDownloads\verified.json' -Raw | ConvertFrom-Json
        foreach($name in @('dotnet-runtime-10.0.12-win-x64.zip','windowsdesktop-runtime-10.0.12-win-x64.zip')) {
            $item=$verified | Where-Object local -eq $name
            if(-not $item -or (Get-FileHash "C:\RuntimeDownloads\$name" -Algorithm SHA512).Hash -ne $item.hash) {throw 'Runtime fixture hash mismatch'}
            Expand-Archive -LiteralPath "C:\RuntimeDownloads\$name" -DestinationPath 'C:\Program Files\dotnet' -Force
        }
        $sdks=@(& 'C:\Program Files\dotnet\dotnet.exe' --list-sdks);if($sdks.Count -ne 0) {throw 'SDK must be absent'}
        & 'C:\Program Files\dotnet\dotnet.exe' --list-runtimes | Set-Content "$Evidence/system-runtimes.txt"
    }
    New-Item -ItemType Directory -Force 'C:\作者 工具','C:\游戏 工程' | Out-Null
    Copy-Item -LiteralPath 'C:\LauncherSource' -Destination 'C:\作者 工具\Launcher' -Recurse
    $application='C:\作者 工具\Launcher';$settings='C:\作者 设置';$storage='C:\共享 引擎';$skills='C:\作者 技能'
    $null=Invoke-L @('storage',$storage)
    foreach($version in @('0.2.0','0.2.1')) {$zip="C:\Downloads\V$version\Board-Game-Builder-V$version-$Mode-win-x64.zip";$null=Invoke-L @('import',$zip,($zip+'.sha256'))}
    $physical=Invoke-L @('storage-report');$runtime=Invoke-L @('runtime','0.2.0')
    $results=@();$installed=@()
    foreach($version in @('0.2.0','0.2.1')) {
        $format=if($version -eq '0.2.0') {'module'} else {'state'};$project="C:\游戏 工程\$format"
        $null=Call "project.create.$format";$null=Invoke-L @('binding','--project',$project,'--set',$version);$null=Invoke-L @('default','0.2.1')
        $selection=Invoke-L @('resolve','--project',$project);if($selection.version -ne $version -or $selection.source -ne 'project') {throw 'Project binding changed'}
        $docs=Invoke-L @('cli','--project',$project,'--','docs','entry');if(-not $docs.version.StartsWith($version+'+')) {throw 'Documentation version mismatch'}
        $null=Call 'project.validate';$null=Call 'run.cpu' @{seed=42;count=3}
        $profile=(Invoke-L @('cli','--project',$project,'--','profile')).result.effective.profile
        foreach($plugin in $profile.plugins) {if($plugin.id -eq 'edit.disk') {$plugin.id='edit.gui'}}
        [IO.File]::WriteAllText("$project/author.profile.json",(@{defaultProfile='connected';profiles=@{connected=$profile}} | ConvertTo-Json -Depth 12),(New-Object Text.UTF8Encoding($false)))
        $launched=Invoke-L @('open','--project',$project);$owned+=,$launched;$workspaces=@()
        for($i=0;$i -lt 30;$i++) {$workspaces=@(Invoke-L @('cli','--project',$project,'--','workspaces'));if($workspaces.Count -eq 1) {break};Start-Sleep -Milliseconds 500}
        if($workspaces.Count -ne 1 -or $workspaces[0].engineVersion -ne $version) {throw 'GUI workspace version mismatch'}
        $before=[IO.File]::ReadAllText("$project/rules.lua");$draft=$before+"`n-- launcher GUI draft $version`n"
        $write=Call 'files.write' @{files=@{'rules.lua'=$draft}}
        if($write.result.saved -or ([IO.File]::ReadAllText("$project/rules.lua")) -ne $before) {throw 'GUI route implicitly saved draft'}
        $read=Call 'files.read' @{path='rules.lua'};if(-not $read.result.text.Contains("launcher GUI draft $version")) {throw 'GUI draft not read'}
        $null=Call 'project.validate';$null=Call 'project.pack' @{target="C:/game-$version.bgm"}
        if($Mode -eq 'slim' -and $version -eq '0.2.0') {
            $failure=Call 'player.export' @{target='C:/Missing Export';offline=$true} 1
            if($failure -notmatch 'Offline export dependencies are missing' -or (Test-Path 'C:\Missing Export')) {throw 'Missing export dependency not rejected'}
        }
        $export=@{target="C:/Game $version"};if($Mode -eq 'slim') {$export.offline=$true;$export.components='C:/Components'}
        $exported=Call 'player.export' $export
        if(([IO.File]::ReadAllText("$project/rules.lua")) -ne $before) {throw 'Export saved project'}
        $null=Call 'workspace.save';if(([IO.File]::ReadAllText("$project/rules.lua")) -ne $draft) {throw 'Explicit save failed'}
        $other=if($version -eq '0.2.0') {'0.2.1'} else {'0.2.0'}
        $mismatch=Invoke-L @('cli','--version',$other,'--project',$project,'--','call','project.validate','--instance',$workspaces[0].instance) 1
        if($mismatch -notmatch '版本不匹配') {throw 'Cross-version connection not refused'}
        $installed+=Invoke-L @('skill','install','--version',$version,'--target',$skills)
        Copy-Item -LiteralPath "C:\Game $version" -Destination "$Evidence/game-$version" -Recurse
        $results+=@{version=$version;format=$format;selection=$selection;docsVersion=$docs.version;workspace=$workspaces[0];write=$write;export=$exported}
        Stop-Owned $launched
    }
    foreach($skill in $installed) {
        $arguments=@($skill.installation.commandArguments)+@('docs','entry');$data=& $skill.installation.cli @arguments | ConvertFrom-Json
        if($LASTEXITCODE -ne 0 -or -not $data.version.StartsWith($skill.installation.version+'+')) {throw 'Pinned SKILL changed version'}
    }
    Remove-Item -LiteralPath (Join-Path $installed[0].target 'SKILL.md');$null=Invoke-L @('skill','repair','--version','0.2.0','--target',$skills)
    $null=Invoke-L @('skill','uninstall','--version','0.2.0','--target',$skills);$null=Invoke-L @('skill','status','--version','0.2.1','--target',$skills)
    $null=Invoke-L @('skill','uninstall','--version','0.2.1','--target',$skills)
    $version='0.2.0';$project='C:\游戏 工程\module'
    $null=Invoke-L @('binding','--project',$project,'--set','0.9.0');$missing=Invoke-L @('resolve','--project',$project);if($missing.state -ne 'missing') {throw 'Missing bound version silently changed'}
    $null=Invoke-L @('open','--project',$project) 1;$null=Invoke-L @('binding','--project',$project,'--set','0.2.0')
    # Remove the connected profile only in this disposable fixture, then verify invalid runtime format stays unchanged.
    Remove-Item -LiteralPath "$project/author.profile.json"
    $entry=Get-Content "$project/game.start.json" -Raw|ConvertFrom-Json;$entry.format=999;[IO.File]::WriteAllText("$project/game.start.json",($entry|ConvertTo-Json -Depth 12),(New-Object Text.UTF8Encoding($false)))
    $hash=(Get-FileHash "$project/game.start.json").Hash;$null=Call 'project.validate' @{} 1
    if((Get-FileHash "$project/game.start.json").Hash -ne $hash) {throw 'Invalid input was migrated'}
    $gui=Start-Process -FilePath "$application/Launcher.exe" -ArgumentList @('--home',('"'+$settings+'"'),'--verify-launcher',('"'+$Evidence+'\gui.json"')) -WindowStyle Hidden -PassThru
    if(-not $gui.WaitForExit(240000)) {$gui.Kill();throw 'GUI verification timeout'}
    if(-not (Get-Content "$Evidence/gui.json" -Raw|ConvertFrom-Json).success) {throw 'GUI verification failed'}
    if($Mode -eq 'slim') {
        $unknown="$storage/versions/0.2.0/.state/export-dependencies/author-notes.txt";[IO.File]::WriteAllText($unknown,'preserve user notes')
        $null=Invoke-L @('uninstall','0.2.0') 1;if([IO.File]::ReadAllText($unknown) -ne 'preserve user notes') {throw 'Unknown cache file was deleted'}
        Remove-Item -LiteralPath $unknown
    }
    $null=Invoke-L @('uninstall','0.2.0');$null=Invoke-L @('uninstall','0.2.1');$after=Invoke-L @('storage-report')
    if($after.physicalObjects -ne 0) {throw 'Export cache or product references prevented removal'}
    @{success=$true;mode=$Mode;network=$false;sdk=$false;physical=$physical;afterRemoval=$after;runtime=$runtime;projects=$results;skills=$installed;steps=$steps} | ConvertTo-Json -Depth 18 | Set-Content "$Evidence/result.json" -Encoding utf8
} catch {@{success=$false;mode=$Mode;error=($_|Out-String);steps=$steps}|ConvertTo-Json -Depth 12|Set-Content "$Evidence/result.json" -Encoding utf8;exit 1}
finally {foreach($item in $owned) {try {Stop-Owned $item} catch {$_|Out-File "$Evidence/cleanup-errors.log" -Append}}}
