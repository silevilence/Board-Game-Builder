# Windows PowerShell 5.1. Run only in a disposable Windows Sandbox with no system .NET.
param([string]$LauncherDirectory='C:\LauncherSource',[string]$Downloads='C:\Downloads',[string]$Components='C:\Components',[string]$LegacyDirectory='C:\LegacyDownloads',[string]$Evidence='C:\Evidence')
$ErrorActionPreference='Stop'
$owned=@();$steps=@();$sequence=0
function Stop-Owned($item) {
    $process=Get-Process -Id $item.processId -ErrorAction SilentlyContinue
    if($process -and $process.StartTime.ToUniversalTime() -eq ([datetime]$item.startedAt).ToUniversalTime() -and $process.MainModule.FileName -eq $item.image) {
        & taskkill.exe /PID $item.processId /T /F | Out-File "$Evidence/stopped-processes.log" -Append
        if($LASTEXITCODE -ne 0) {throw 'Cannot stop owned validation process.'}
    }
}
function Invoke-Launcher([string[]]$Arguments,[int]$Expected=0) {
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName="$script:application\bgb-launcher.exe"; $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.Arguments=((@('--home',$script:settings)+$Arguments) | ForEach-Object {'"'+$_+'"'}) -join ' '
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $start.StandardOutputEncoding=[Text.Encoding]::UTF8;$start.StandardErrorEncoding=[Text.Encoding]::UTF8
    $process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    if(-not $process.WaitForExit(240000)) {$process.Kill();throw "Launcher command timed out: $Arguments"}
    $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();$exit=$process.ExitCode;$process.Dispose()
    $script:sequence++;$out | Set-Content "$Evidence/command-$script:sequence.stdout" -Encoding utf8;$err | Set-Content "$Evidence/command-$script:sequence.stderr" -Encoding utf8
    $script:steps+=@{arguments=$Arguments;exit=$exit;output="command-$script:sequence.stdout"}
    if($exit -ne $Expected) {throw "Unexpected exit $exit for $Arguments : $err"}
    return @{stdout=$out;stderr=$err;exit=$exit}
}
function Wait-Editor([string]$Version) {
    for($attempt=0;$attempt -lt 60;$attempt++) {
        $result=Invoke-Launcher @('processes',$Version)
        $processes=@($result.stdout | ConvertFrom-Json)
        foreach($item in $processes) {
            if($item.image -like '*\.payload\editor\Editor.exe') {
                $process=Get-Process -Id $item.processId -ErrorAction SilentlyContinue
                if($process -and $process.MainWindowHandle -ne 0) {return}
            }
        }
        Start-Sleep -Seconds 1
    }
    throw "Editor did not show its actual window: $Version"
}
try {
    if($env:USERNAME -ne 'WDAGUtilityAccount' -or (Get-Command dotnet -ErrorAction SilentlyContinue) -or (Test-Path 'C:\Program Files\dotnet')) {throw 'Fresh Sandbox without SDK/Runtime is required.'}
    New-Item -ItemType Directory -Force 'C:\应用 程序','C:\工程 保留',$Evidence | Out-Null
    Copy-Item -LiteralPath $LauncherDirectory -Destination 'C:\应用 程序\启动器' -Recurse
    $application='C:\应用 程序\启动器';$settings='C:\作者 设置';$storage='C:\引擎 存储'
    Set-Content 'C:\工程 保留\marker.txt' 'user project survives'
    $null=Invoke-Launcher @('storage',$storage)
    foreach($version in @('V0.2.0','V0.2.1')) {
        $zip="$Downloads\$version\Board-Game-Builder-$version-standalone-win-x64.zip"
        $null=Invoke-Launcher @('import',$zip,($zip+'.sha256'))
        $result=Invoke-Launcher @('run',$version,'--','docs','entry')
        if(-not ($result.stdout | ConvertFrom-Json).version.StartsWith($version.Substring(1))) {throw 'CLI selected a different version.'}
    }
    $before=(Invoke-Launcher @('storage-report')).stdout | ConvertFrom-Json
    if($before.physicalObjects -ne 437 -or $before.coreRuntimeFiles -ne 1 -or $before.desktopRuntimeFiles -ne 1) {throw 'Shared physical identity mismatch.'}
    (Invoke-Launcher @('runtime','V0.2.0')).stdout | Set-Content "$Evidence/private-runtime.json" -Encoding utf8
    $a=(Invoke-Launcher @('launch','V0.2.0')).stdout | ConvertFrom-Json;$owned+=,$a;Wait-Editor 'V0.2.0'
    $b=(Invoke-Launcher @('launch','V0.2.1')).stdout | ConvertFrom-Json;$owned+=,$b;Wait-Editor 'V0.2.1'
    $null=Invoke-Launcher @('default','V0.2.1')
    $null=Invoke-Launcher @('uninstall','V0.2.0') 1
    Stop-Owned $a
    $first=Get-Content "$storage/versions/0.2.0/toolkit.json" -Raw | ConvertFrom-Json
    $second=Get-Content "$storage/versions/0.2.1/toolkit.json" -Raw | ConvertFrom-Json
    $hashes=@{};foreach($file in $second.files) {$hashes[$file.sha256]=$true}
    $shared=$first.files | Where-Object {$_.path -like '*.md' -and $hashes.ContainsKey($_.sha256)} | Select-Object -First 1
    if(-not $shared) {throw 'Expected shared documentation fixture.'}
    $object="$storage/objects/$($shared.sha256)";[IO.File]::SetAttributes($object,[IO.FileAttributes]::Normal);[IO.File]::WriteAllText($object,'controlled corruption')
    $null=Invoke-Launcher @('repair','V0.2.0','--components',$Components) 1
    Stop-Owned $b
    $repair=(Invoke-Launcher @('repair','V0.2.0','--components',$Components)).stdout | ConvertFrom-Json
    if($repair.versions.Count -ne 2 -or $repair.replacedObjects -ne 1) {throw 'Shared repair did not cover both versions.'}
    $b=(Invoke-Launcher @('launch','V0.2.1')).stdout | ConvertFrom-Json;$owned+=,$b;Wait-Editor 'V0.2.1'
    $null=Invoke-Launcher @('uninstall','V0.2.0')
    $one=(Invoke-Launcher @('storage-report')).stdout | ConvertFrom-Json
    if($one.versions -ne 1 -or $one.coreRuntimeFiles -ne 1) {throw 'Live second version lost its references.'}
    $null=Invoke-Launcher @('run','V0.2.1','--','docs','entry')
    $zip="$Downloads\V0.2.0\Board-Game-Builder-V0.2.0-standalone-win-x64.zip"
    $null=Invoke-Launcher @('import',$zip,($zip+'.sha256'))
    & "$storage/versions/0.2.0/bgb.exe" docs entry | Set-Content "$Evidence/direct-cli.json" -Encoding utf8
    if($LASTEXITCODE -ne 0) {throw 'Direct installed entry failed.'}
    $gui=New-Object Diagnostics.ProcessStartInfo;$gui.FileName="$application\Launcher.exe";$gui.UseShellExecute=$false
    $gui.Arguments="--home `"$settings`" --verify-launcher `"$Evidence\launcher-gui.json`""
    $process=[Diagnostics.Process]::Start($gui);if(-not $process.WaitForExit(240000)) {$process.Kill();throw 'Launcher GUI timed out.'}
    $report=Get-Content "$Evidence/launcher-gui.json" -Raw | ConvertFrom-Json
    if(-not $report.success -or $report.installations -ne 2) {throw 'Restarted GUI did not restore the installations.'}
    Stop-Owned $b
    $null=Invoke-Launcher @('uninstall','V0.2.1')
    if(((Invoke-Launcher @('settings')).stdout | ConvertFrom-Json).defaultVersion) {throw 'Default must require explicit selection after removal.'}
    $null=Invoke-Launcher @('uninstall','V0.2.0')
    $last=(Invoke-Launcher @('storage-report')).stdout | ConvertFrom-Json
    if($last.physicalObjects -ne 0) {throw 'Last reference was not collected.'}
    $legacy="$LegacyDirectory/Board-Game-Builder-V0.1.0-win-x64.zip"
    $null=Invoke-Launcher @('import',$legacy,($legacy+'.sha256'))
    $null=Invoke-Launcher @('run','V0.1.0','--','docs','entry')
    $null=Invoke-Launcher @('uninstall','V0.1.0')
    if((Get-Content 'C:\工程 保留\marker.txt') -ne 'user project survives') {throw 'User project was modified.'}
    @{success=$true;systemRuntime=$false;network=$false;before=$before;afterOne=$one;afterLast=$last;repair=$repair;legacy=$true;steps=$steps} | ConvertTo-Json -Depth 10 | Set-Content "$Evidence/result.json" -Encoding utf8
} catch {
    @{success=$false;error=($_|Out-String);steps=$steps} | ConvertTo-Json -Depth 8 | Set-Content "$Evidence/result.json" -Encoding utf8
    exit 1
} finally {foreach($item in $owned) {try {Stop-Owned $item} catch {$_ | Out-File "$Evidence/cleanup-errors.log" -Append}}}
