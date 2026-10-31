# Installs an already published unpackaged hub; never registers or changes a Store package.
param([string]$Source, [switch]$Start, [switch]$UnpackagedWorker, [string]$Receipt)
$ErrorActionPreference = 'Stop'

if (-not ('NexusLocalNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles;
public static class NexusLocalNative {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle h,StringBuilder b,uint n,uint f);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref uint n,StringBuilder b);
    public static bool Packaged() { uint n=0; int r=GetCurrentPackageFullName(ref n,null); if(r==15700)return false; if(r==122)return true; throw new System.ComponentModel.Win32Exception(r); }
    public static string Physical(string p) { using(var f=System.IO.File.Open(p,System.IO.FileMode.Open,System.IO.FileAccess.Read,System.IO.FileShare.ReadWrite|System.IO.FileShare.Delete)) {
        var b=new StringBuilder(32768); uint n=GetFinalPathNameByHandle(f.SafeFileHandle,b,(uint)b.Capacity,0);
        if(n==0||n>=b.Capacity)throw new System.ComponentModel.Win32Exception(); var resolved=b.ToString(); return resolved.StartsWith("\\\\?\\UNC\\",StringComparison.OrdinalIgnoreCase)?"\\\\"+resolved.Substring(8):resolved.Substring(4); } }
}
'@
}

function Get-NexusLocalPayload([string]$Directory) {
    $root = [IO.Path]::GetFullPath($Directory).TrimEnd('\')
    foreach ($name in 'NexusSystem.exe', 'NexusSystem.dll', 'Nexus.Core.dll', 'NexusSystem.pri', 'NexusSystem.deps.json', 'NexusSystem.runtimeconfig.json') {
        if (-not (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) { throw "Incomplete published app: missing $name" }
    }
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
    if ($files | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'A local publish must not contain links or reparse points.' }
    $entries = @($files | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($root, $_.FullName)
        [pscustomobject]@{ Path = $relative; Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $canonical = ($entries | ForEach-Object { $_.Path.Replace('\', '/') + ':' + $_.Hash }) -join "`n"
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical)))
    [pscustomobject]@{ Root = $root; Hash = $hash; Entries = $entries }
}

function Get-NexusLocalDestination([string]$LocalAppData, [string]$Version, [string]$Hash) {
    if ($Version -notmatch '^\d+\.\d+\.\d+\.0$' -or $Hash -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid local version or payload hash.' }
    if (-not [IO.Path]::IsPathFullyQualified($LocalAppData)) { throw 'LocalAppData must be an absolute directory.' }
    Join-Path ([IO.Path]::GetFullPath($LocalAppData)) "NexusApps\local\$Version\$Hash"
}

# Earlier local copies (other versions, other payloads, old publish folders) go once the new one is installed; a copy a
# running process still uses is kept for the next installation.
function Remove-NexusLocalStale {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([string]$Destination, [string[]]$Running = @())
    $local = Split-Path (Split-Path $Destination)
    if (-not $PSCmdlet.ShouldProcess($local, 'Remove earlier local NexusSystem copies')) { return }
    foreach ($folder in @(Get-ChildItem -LiteralPath $local -Directory -ErrorAction SilentlyContinue)) {
        # <version>\<payload hash>\ copies; an older layout put the files directly in <version>\.
        $layered = $folder.Name -match '^\d+\.\d+\.\d+\.0$' -and -not (Test-Path -LiteralPath (Join-Path $folder.FullName 'NexusSystem.exe'))
        $copies = if ($layered) { @(Get-ChildItem -LiteralPath $folder.FullName -Directory) } else { @($folder) }
        foreach ($copy in $copies) {
            if ($copy.FullName -eq $Destination) { continue }
            if ($Running | Where-Object { $_ -and $_.StartsWith($copy.FullName + '\', [StringComparison]::OrdinalIgnoreCase) }) { continue }
            Remove-Item -LiteralPath $copy.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($folder.FullName -ne (Split-Path $Destination) -and (Test-Path -LiteralPath $folder.FullName) -and
            -not (Get-ChildItem -LiteralPath $folder.FullName -Force)) { Remove-Item -LiteralPath $folder.FullName -Force }
    }
}

function Copy-NexusLocalPayload($Payload, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) {
        if ((Get-NexusLocalPayload $Destination).Hash -ne $Payload.Hash) { throw 'Existing immutable local release differs; nothing was overwritten.' }
        return
    }
    New-Item -ItemType Directory -Path $Destination | Out-Null
    foreach ($entry in $Payload.Entries) {
        $target = Join-Path $Destination $entry.Path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $Payload.Root $entry.Path) -Destination $target
    }
    if ((Get-NexusLocalPayload $Destination).Hash -ne $Payload.Hash) { throw 'Copied local release failed its payload hash check.' }
}

function Write-NexusLocalReceipt([string]$Path, $Result) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))) | Out-Null
    $temporaryReceipt = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $Result | ConvertTo-Json | Set-Content -LiteralPath $temporaryReceipt -Encoding utf8
    Move-Item -LiteralPath $temporaryReceipt -Destination $Path -Force
}

if ($MyInvocation.InvocationName -eq '.') { return }
if (-not $Source) { throw 'Supply -Source with the output folder of dotnet publish src/Nexus.App.' }
$physicalSource = [IO.Path]::GetDirectoryName([NexusLocalNative]::Physical((Join-Path $Source 'NexusSystem.exe')))
if ($physicalSource -match '\\WindowsApps\\') { throw 'A signed Store installation is not a local publish source.' }
if ([NexusLocalNative]::Packaged()) {
    if ($UnpackagedWorker) { throw 'The local install worker still has a package identity; refusing virtualized deployment.' }
    # The Task Scheduler starts an ordinary interactive-user process, outside an inherited Codex MSIX identity.
    $receiptRoot = Join-Path $env:USERPROFILE '.nexus\nexus-apps'
    [IO.Directory]::CreateDirectory($receiptRoot) | Out-Null
    $Receipt = Join-Path $receiptRoot ('local-install-' + [guid]::NewGuid().ToString('N') + '.json')
    $taskName = 'NexusSystem-LocalInstall-' + [guid]::NewGuid().ToString('N')
    $scriptPath = [NexusLocalNative]::Physical($PSCommandPath)
    $arguments = "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -File `"$scriptPath`" -Source `"$physicalSource`" -UnpackagedWorker -Receipt `"$Receipt`""
    if ($Start) { $arguments += ' -Start' }
    $action = New-ScheduledTaskAction -Execute (Get-Process -Id $PID).Path -Argument $arguments -WorkingDirectory $PSScriptRoot
    $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
    try {
        Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings | Out-Null
        Start-ScheduledTask -TaskName $taskName
        $deadline = (Get-Date).AddSeconds(55)
        while (-not (Test-Path -LiteralPath $Receipt) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
        if (-not (Test-Path -LiteralPath $Receipt)) { throw 'No unpackaged installation receipt; local deployment is not confirmed.' }
        $result = Get-Content -LiteralPath $Receipt -Raw | ConvertFrom-Json
        if (-not $result.Success) { throw "Unpackaged local install failed: $($result.Error)" }
        $result
    }
    finally {
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        # Delete only our unique registration; never send Stop to its running action or the opened hub.
        if ($task) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false }
    }
    exit
}

try {
    $payload = Get-NexusLocalPayload $physicalSource
    $version = (Get-Item -LiteralPath (Join-Path $physicalSource 'NexusSystem.exe')).VersionInfo.FileVersion
    $destination = Get-NexusLocalDestination $env:LOCALAPPDATA $version $payload.Hash
    Copy-NexusLocalPayload $payload $destination
    $executable = Join-Path $destination 'NexusSystem.exe'
    $physical = [NexusLocalNative]::Physical($executable)
    if (-not $physical.Equals($executable, [StringComparison]::OrdinalIgnoreCase)) { throw "Virtualized local executable refused: $physical" }
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'NexusSystem (local).lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable; $shortcut.WorkingDirectory = $destination; $shortcut.Arguments = ''
    $shortcut.Description = 'NexusSystem local - ' + $version; $shortcut.IconLocation = $executable + ',0'; $shortcut.Save()
    $physicalShortcut = [NexusLocalNative]::Physical($shortcutPath)
    if (-not $physicalShortcut.Equals($shortcutPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'The local shortcut was virtualized.' }
    Remove-NexusLocalStale $destination @(Get-CimInstance Win32_Process -Filter "Name='NexusSystem.exe'" | ForEach-Object ExecutablePath)
    $processId = $null
    if ($Start) {
        $process = Start-Process -FilePath $executable -WorkingDirectory $destination -ArgumentList '--lang fr' -PassThru -WindowStyle Normal
        $processId = $process.Id
    }
    $result = [pscustomobject]@{ Success = $true; Version = $version; PayloadHash = $payload.Hash; Executable = $executable;
        PhysicalExecutable = $physical; Shortcut = $shortcutPath; PhysicalShortcut = $physicalShortcut; Packaged = $false; ProcessId = $processId }
    if ($Receipt) { Write-NexusLocalReceipt $Receipt $result }
    $result
}
catch {
    if ($Receipt) { Write-NexusLocalReceipt $Receipt ([pscustomobject]@{ Success = $false; Error = $_.Exception.Message }) }
    throw
}
