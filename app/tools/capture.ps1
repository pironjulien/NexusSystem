# Pictures of every page in French and English, dark and light, on the made-up demo profile (tools/demo_profile.py):
# never the real user's data. The app's --capture mode shows its window away from the desktop, without activation,
# and this script checks that it never becomes the foreground window.
param([string]$Out = (Join-Path $PSScriptRoot '..\dist\captures'), [string]$Size = '1366x860', [switch]$Frames)
$ErrorActionPreference = 'Stop'
function Resolve-NexusCaptureOutput([string]$Path, [string]$ProjectRoot) {
    $target = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($target -eq [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Path)).TrimEnd('\')) {
        throw 'Capture output cannot be a drive root.'
    }
    $project = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
    $default = [IO.Path]::GetFullPath((Join-Path $project 'dist\captures')).TrimEnd('\')
    $protected = @([IO.Path]::GetPathRoot($target), $project, $env:USERPROFILE, $env:APPDATA, $env:LOCALAPPDATA,
                   [IO.Path]::GetTempPath(), $env:OneDrive, [Environment]::GetFolderPath('MyDocuments'))
    foreach ($directory in $protected | Where-Object { $_ }) {
        if ($target -eq [IO.Path]::GetFullPath($directory).TrimEnd('\')) { throw 'Capture output cannot be a drive, project or profile directory.' }
    }
    for ($ancestor = $target; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if ((Test-Path -LiteralPath $ancestor) -and (Get-Item -LiteralPath $ancestor -Force).LinkType) {
            throw 'Capture output cannot traverse a symbolic link or junction.'
        }
    }
    if (Test-Path -LiteralPath (Join-Path $target '.git')) { throw 'Capture output cannot be a repository root.' }
    $marker = Join-Path $target '.nexus-captures.json'
    $pictures = @()
    if (Test-Path -LiteralPath $target) {
        if (-not (Test-Path -LiteralPath $target -PathType Container)) { throw 'Capture output must be a directory.' }
        if ($target -ne $default) {
            if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Item -LiteralPath $marker -Force).LinkType) {
                throw 'An existing custom output needs its own .nexus-captures.json marker.'
            }
            $ownership = Get-Content -Raw -LiteralPath $marker | ConvertFrom-Json
            if ($ownership.kind -ne 'NexusApps.capture' -or $ownership.version -ne 1 -or $ownership.path -ne $target) {
                throw 'Capture output ownership marker does not match this directory.'
            }
        }
        foreach ($entry in Get-ChildItem -LiteralPath $target -Force) {
            if ($entry.Name -eq '.nexus-captures.json' -and -not $entry.PSIsContainer -and -not $entry.LinkType) { continue }
            if ($entry.PSIsContainer -or $entry.LinkType -or $entry.Name -notmatch '^(?:(?:home(?:-result)?|windows|android|browser|keypad|relay|agymail)-(?:fr|en)-(?:dark|light)|frame-(?:fr|en)-(?:dark|light)-\d{2})\.png$') {
                throw 'Capture output contains foreign content; nothing was removed.'
            }
            $pictures += $entry.FullName
        }
    }
    [pscustomobject]@{ Path = $target; Marker = $marker; Pictures = $pictures }
}
function Initialize-NexusCaptureOutput([string]$Path, [string]$ProjectRoot) {
    $approved = Resolve-NexusCaptureOutput $Path $ProjectRoot
    New-Item -ItemType Directory -Path $approved.Path -Force | Out-Null
    foreach ($picture in $approved.Pictures) { Remove-Item -LiteralPath $picture -Force }
    [pscustomobject]@{ kind = 'NexusApps.capture'; version = 1; path = $approved.Path } |
        ConvertTo-Json | Set-Content -LiteralPath $approved.Marker -Encoding UTF8
    $approved.Path
}
# Dot-source only for read-only output validation; the native captures start only when the script is executed.
if ($MyInvocation.InvocationName -eq '.') { return }
$output = Resolve-NexusCaptureOutput $Out (Split-Path $PSScriptRoot)
$Out = $output.Path
$architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
$architecture = if ($architecture -eq 'ARM64') { 'ARM64' } else { 'x64' }
$exe = Get-ChildItem (Join-Path $env:LOCALAPPDATA "NexusApps\build\Nexus.App\bin\$architecture") -Recurse -Filter NexusSystem.exe |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw 'Build src\Nexus.App first (output under LocalAppData\NexusApps\build).' }
$demo = Join-Path ([IO.Path]::GetTempPath()) ('nexus-demo-' + [guid]::NewGuid().ToString('N'))
python -B (Join-Path $PSScriptRoot 'demo_profile.py') $demo
if ($LASTEXITCODE) { throw 'The demonstration profile could not be prepared.' }
$Out = Initialize-NexusCaptureOutput $Out (Split-Path $PSScriptRoot)
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);' -Name Foreground -Namespace NexusCapture
$changed = $false  # true when a NexusSystem window was ever the foreground one (the user may switch windows meanwhile)
$saved = @{ USERPROFILE = $env:USERPROFILE; APPDATA = $env:APPDATA; LOCALAPPDATA = $env:LOCALAPPDATA;
            CODEX_HOME = $env:CODEX_HOME; NEXUS_JOURNAL_HOME = $env:NEXUS_JOURNAL_HOME }
try {
    $env:USERPROFILE = $demo; $env:APPDATA = "$demo\AppData\Roaming"; $env:LOCALAPPDATA = "$demo\AppData\Local"
    $env:CODEX_HOME = "$demo\.codex"; $env:NEXUS_JOURNAL_HOME = "$demo\AppData\Local\NexusJournal"
    foreach ($run in @('fr dark', 'en dark', 'fr light', 'en light')) {
        $lang, $theme = $run -split ' '
        $process = Start-Process $exe.FullName -ArgumentList "--capture `"$Out`" --size $Size $(if ($Frames) { '--frames' }) --lang $lang --theme $theme" -PassThru
        for ($i = 0; $i -lt 120 -and -not $process.HasExited; $i++) {
            Start-Sleep -Milliseconds 250
            $owner = 0
            [void][NexusCapture.Foreground]::GetWindowThreadProcessId([NexusCapture.Foreground]::GetForegroundWindow(), [ref]$owner)
            if ($owner -eq $process.Id) { $changed = $true }
        }
        if (-not $process.HasExited) { $process.Kill(); throw "capture $run did not finish" }
        if ($process.ExitCode) { throw "capture $run failed ($($process.ExitCode))" }
    }
} finally {
    foreach ($name in $saved.Keys) { Set-Item "Env:$name" $saved[$name] }
    Remove-Item $demo -Recurse -Force -ErrorAction SilentlyContinue
}
if ($changed) { throw 'A NexusSystem window took the foreground during the capture.' }
Write-Host "$((Get-ChildItem -LiteralPath $Out -Filter *.png).Count) pictures in $Out, foreground never taken"
