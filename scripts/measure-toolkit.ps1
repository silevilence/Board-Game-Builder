# Windows PowerShell 5.1 compatible; measures actual NTFS identities and allocated bytes.
param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Report, [switch]$AllowDuplicates)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ToolkitFileIdentity {
    [StructLayout(LayoutKind.Sequential)]
    public struct Info {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool GetFileInformationByHandle(IntPtr handle, out Info info);
    [StructLayout(LayoutKind.Sequential)]
    public struct StandardInfo {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending, Directory;
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool GetFileInformationByHandleEx(IntPtr handle, int kind, out StandardInfo info, uint size);
}
'@
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$manifest = Get-Content -LiteralPath (Join-Path $rootPath 'toolkit.json') -Raw | ConvertFrom-Json
if (-not $AllowDuplicates -and $manifest.layout -ne 'hardlinks-v1') { throw 'Expected hardlinks-v1 layout.' }
$identities = @{}
$contents = @{}
$runtimeIds = @{}
$desktopIds = @{}
[long]$logical = 0
[long]$allocated = 0
[long]$unique = 0
[long]$physical = 0
foreach ($file in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $rootPath $file.path))
    if (-not $path.StartsWith($rootPath.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe manifest path.' }
    $stream = [IO.File]::OpenRead($path)
    try {
        $info = New-Object ToolkitFileIdentity+Info
        if (-not [ToolkitFileIdentity]::GetFileInformationByHandle($stream.SafeFileHandle.DangerousGetHandle(),[ref]$info)) { throw "Cannot inspect $path" }
        $id = "$($info.Volume):$($info.IndexHigh):$($info.IndexLow)"
        if (-not $identities.ContainsKey($id)) {
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
            if ($hash -ne $file.sha256 -or $stream.Length -ne $file.size) { throw "Integrity mismatch: $path" }
            $standard = New-Object ToolkitFileIdentity+StandardInfo
            if (-not [ToolkitFileIdentity]::GetFileInformationByHandleEx($stream.SafeFileHandle.DangerousGetHandle(),1,[ref]$standard,24)) { throw "Cannot measure allocation: $path" }
            $allocated += $standard.AllocationSize
            $physical += $stream.Length
            $identities[$id] = @{sha256=$hash;size=$stream.Length}
        }
        if ($identities[$id].sha256 -ne $file.sha256) { throw 'Conflicting physical content.' }
        if (-not $contents.ContainsKey($file.sha256)) { $unique += $file.size }
        elseif (-not $AllowDuplicates -and $contents[$file.sha256] -ne $id) { throw "Duplicate physical data: $($file.path)" }
        $contents[$file.sha256] = $id
        $logical += $file.size
        if (($file.path.Split('/')[-1]) -eq 'coreclr.dll') { $runtimeIds[$id] = $file.sha256 }
        if (($file.path.Split('/')[-1]) -eq 'System.Windows.Forms.dll') { $desktopIds[$id] = $file.sha256 }
    } finally { $stream.Dispose() }
}
@{ success=$true; logicalFiles=$manifest.files.Count; physicalFiles=$identities.Count; logicalBytes=$logical;
   allocatedContentBytes=$allocated; uniqueContentBytes=$unique; physicalContentBytes=$physical;
   coreRuntimePhysicalFiles=$runtimeIds.Count; coreRuntimeIdentities=$runtimeIds; desktopRuntimePhysicalFiles=$desktopIds.Count;
   scope='Manifest product files; excludes toolkit.json, directory metadata and writable .state' } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Report -Encoding utf8
