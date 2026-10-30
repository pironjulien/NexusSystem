# Starts the hub quietly (never in the foreground) on a throwaway user profile, for real tests driven in the background
# (nexus-windows): USERPROFILE, APPDATA, LOCALAPPDATA and TEMP there, the system PATH plus Node.js, no access
# token of the real user. -Fresh empties the profile first and creates the folders of the four AI apps.
param([Parameter(Mandatory)][string]$ProfileRoot, [switch]$Fresh, [string]$Lang = 'fr', [switch]$AllowRealProfile)
$ErrorActionPreference = 'Stop'
# Tests never touch the real user's environment: a profile inside the real one, or the real one, needs the flag.
$real = [IO.Path]::GetFullPath([Environment]::GetFolderPath('UserProfile')).TrimEnd('\')
$target = [IO.Path]::GetFullPath($ProfileRoot).TrimEnd('\')
$temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$inTemp = $target.StartsWith($temp + '\', [StringComparison]::OrdinalIgnoreCase)
if (-not $AllowRealProfile -and -not $inTemp -and ($target -eq $real -or $target.StartsWith($real + '\', [StringComparison]::OrdinalIgnoreCase))) {
    throw "Refused: $ProfileRoot is the real user profile or inside it (use a folder under $temp, or -AllowRealProfile on purpose)."
}
if ($Fresh) {
    if (Test-Path $ProfileRoot) { Remove-Item $ProfileRoot -Recurse -Force }
    foreach ($d in '.codex', '.gemini\antigravity', 'AppData\Roaming\Claude', 'AppData\Local\Temp') { New-Item -ItemType Directory -Force (Join-Path $ProfileRoot $d) | Out-Null }
}
$architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
$architecture = if ($architecture -eq 'ARM64') { 'ARM64' } else { 'x64' }
$exe = Get-ChildItem (Join-Path $HOME ".nexus\build\NexusSystem\Nexus.App\bin\$architecture") -Recurse -Filter NexusSystem.exe |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw 'Build src\Nexus.App first (output under ~\.nexus\build\NexusSystem).' }
$start = New-Object Diagnostics.ProcessStartInfo $exe.FullName, "--quiet --lang $Lang"
$start.UseShellExecute = $false
$env_ = $start.Environment
$env_['USERPROFILE'] = $ProfileRoot; $env_['HOMEPATH'] = $ProfileRoot.Substring(2); $env_['HOMEDRIVE'] = $ProfileRoot.Substring(0, 2)
$env_['APPDATA'] = "$ProfileRoot\AppData\Roaming"; $env_['LOCALAPPDATA'] = "$ProfileRoot\AppData\Local"
$env_['TEMP'] = "$ProfileRoot\AppData\Local\Temp"; $env_['TMP'] = $env_['TEMP']
foreach ($k in 'CODEX_HOME', 'NEXUS_JOURNAL_HOME', 'NEXUS_MCP_TOKEN') { [void]$env_.Remove($k) }
$env_['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot;$env:SystemRoot\System32\WindowsPowerShell\v1.0;$env:SystemRoot\System32\Wbem;C:\Program Files\nodejs"
$process = [Diagnostics.Process]::Start($start)
for ($i = 0; $i -lt 40 -and $process.MainWindowHandle -eq [IntPtr]::Zero -and -not $process.HasExited; $i++) {
    Start-Sleep -Milliseconds 250
    $process.Refresh()
}
if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) { throw "NexusSystem did not open its native test window (pid $($process.Id))." }
"pid $($process.Id) hwnd $($process.MainWindowHandle)"
