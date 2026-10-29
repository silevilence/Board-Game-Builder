# Run only inside a fresh Windows Sandbox. Author and Player use separate fresh guests.
param([ValidateSet('Author','Player')][string]$Stage, [string]$InputDirectory='C:\Input', [string]$Evidence='C:\Evidence')
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class VisibleConsoles {
    private delegate bool Callback(IntPtr window,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder text,int count);
    public static long[] Read() {
        var result=new List<long>();EnumWindows((window,parameter)=>{
            var name=new StringBuilder(256);GetClassName(window,name,name.Capacity);
            if(IsWindowVisible(window) && name.ToString()=="ConsoleWindowClass")result.Add(window.ToInt64());return true;
        },IntPtr.Zero);return result.ToArray();
    }
}
'@
$checks=New-Object System.Collections.Generic.List[string]
function Check([bool]$Condition,[string]$Name) {if(-not $Condition){throw $Name};$checks.Add($Name)}
function Gui([string]$Exe,[string[]]$Arguments,[string]$Report) {
    $quoted=@($Arguments | ForEach-Object {'"'+$_.Replace('"','\"')+'"'})
    $consoles=@([VisibleConsoles]::Read());$extraConsoles=New-Object System.Collections.Generic.List[long]
    $process=Start-Process -FilePath $Exe -ArgumentList $quoted -WorkingDirectory C:\ -WindowStyle Hidden -PassThru
    $null=$process.Handle
    $deadline=[DateTime]::UtcNow.AddMinutes(4)
    while(-not $process.WaitForExit(50)) {
        foreach($window in [VisibleConsoles]::Read()){if($consoles -notcontains $window){$extraConsoles.Add($window)}}
        if([DateTime]::UtcNow -gt $deadline){Stop-Process -Id $process.Id;throw "GUI timed out: $Exe"}
    }
    Check ($extraConsoles.Count -eq 0) "No new visible console window: $Exe"
    Check ($process.ExitCode -eq 0) "GUI exit: $Exe"
    $result=Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
    Check $result.success "GUI report: $Report"
}
try {
    $environment=@{os=[Environment]::OSVersion.VersionString;dotnet=[bool](Get-Command dotnet -ErrorAction SilentlyContinue);runtime=(Test-Path 'C:\Program Files\dotnet\shared');runtimeX86=(Test-Path 'C:\Program Files (x86)\dotnet\shared')}
    $environment | ConvertTo-Json | Set-Content -LiteralPath "$Evidence/environment.json" -Encoding utf8
    Check (-not $environment.dotnet -and -not $environment.runtime -and -not $environment.runtimeX86) 'No SDK or system .NET'
    $work='C:\Application Check';New-Item -ItemType Directory -Path $work | Out-Null
    if($Stage -eq 'Author') {
        Copy-Item -LiteralPath "$InputDirectory/toolkit" -Destination "$work/Author Kit" -Recurse
        Copy-Item -LiteralPath "$InputDirectory/board" -Destination "$work/Board Project" -Recurse
        Copy-Item -LiteralPath "$InputDirectory/motion" -Destination "$work/Motion Project" -Recurse
        $cli="$work/Author Kit/bgb.exe"
        function Call([string]$Operation,[string]$Project,[hashtable]$Arguments) {
            $parameters="$work/parameters.json";$Arguments | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $parameters -Encoding utf8
            & $cli call $Operation --project $Project --args-file $parameters | Out-File "$Evidence/cli.log" -Append -Encoding utf8
            Check ($LASTEXITCODE -eq 0) "CLI $Operation $Project"
        }
        Gui "$work/Author Kit/editor/Editor.exe" @('--verify-toolkit',"$work/Editor Checks") "$work/Editor Checks/editor.json"
        Copy-Item -LiteralPath "$work/Editor Checks/editor.json" -Destination "$Evidence/editor.json"
        New-Item -ItemType Directory -Path "$Evidence/products" | Out-Null
        foreach($kind in @('Board','Motion')) {
            $project="$work/$kind Project"
            Call 'project.validate' $project @{}
            Call 'project.pack' $project @{target="$Evidence/$kind.bgm"}
            $release=if($kind -eq 'Board'){"$work/Author Kit/player"}else{"$InputDirectory/minimal-player"}
            Call 'player.export' $project @{target="$Evidence/products/$kind";player=$release;offline=$true}
        }
        Check (-not (Get-ChildItem -LiteralPath "$Evidence/products/Motion" -File -Recurse | Where-Object {$_.Name -match '^(BoardGame\.|Kernel|Runtime\.dll|Modules\.dll|Lua|MoonSharp|Wasmtime)'})) 'Nonboard product excludes board/script runtimes'
    } else {
        Check (-not (Test-Path "$InputDirectory/toolkit")) 'No author installation mapped'
        $initial=Join-Path $work ('Initial '+[char]0x4E2D+[char]0x6587)
        $moved=Join-Path $work ('Moved '+[char]0x4E2D+[char]0x6587)
        New-Item -ItemType Directory -Path $initial | Out-Null
        Copy-Item -LiteralPath "$InputDirectory/Board" -Destination "$initial/Board" -Recurse
        Copy-Item -LiteralPath "$InputDirectory/Motion" -Destination "$initial/Motion" -Recurse
        $from=[IO.Path]::GetFullPath($initial);$to=[IO.Path]::GetFullPath($moved)
        Check ($from.StartsWith($work+'\') -and $to.StartsWith($work+'\')) 'Verified move stays inside validation workspace'
        Move-Item -LiteralPath $from -Destination $to
        Check (-not (Test-Path $initial)) 'Original installation location absent'
        Check (-not (Get-ChildItem -LiteralPath $moved -File -Recurse | Where-Object {$_.Name -eq 'bgb.exe' -or $_.Name -like 'Editor*'})) 'No author tools in player products'
        Gui "$moved/Board/Play.exe" @('gui',"$moved/Board/game.bgm",'--smoke',"$Evidence/board-gui.json") "$Evidence/board-gui.json"
        Gui "$moved/Motion/Play.exe" @('--verify-standalone',"$Evidence/motion-gui.json") "$Evidence/motion-gui.json"
    }
    @{success=$true;stage=$Stage;checks=@($checks);environment=$environment} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$Evidence/result.json" -Encoding utf8
} catch {
    @{success=$false;stage=$Stage;checks=@($checks);error=($_ | Out-String)} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$Evidence/result.json" -Encoding utf8
} finally {
    Start-Process -FilePath shutdown.exe -ArgumentList '/s /t 0' -WindowStyle Hidden
}
