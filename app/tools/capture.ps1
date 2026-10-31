# Pictures of every page in French and English, dark and light, on the made-up demo profile (Initialize-NexusDemoProfile):
# never the real user's data. The app's --capture mode shows its window away from the desktop, without activation,
# and this script checks that it never becomes the foreground window.
param([string]$Out = (Join-Path $PSScriptRoot '..\dist\captures'), [string]$Size = '1366x860')
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
            if ($entry.PSIsContainer -or $entry.LinkType -or $entry.Name -notmatch '^(?:home|updates|(?:windows|android|browser|keypad|relay|agymail|indesign|wpp)(?:-here)?)-(?:fr|en)-(?:dark|light)\.png$') {
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
function Initialize-NexusDemoProfile([string]$Root) {
    # A made-up user profile for the apps' pictures (milestone reports, Store listing): the four AI apps, the products
    # declared as the native installers and this catalog leave them, a day of journal lines on everyday apps. Nothing
    # comes from a real PC; the same pictures every time (seeded per product).
    $utf8 = New-Object Text.UTF8Encoding $false
    function Write-DemoFile([string]$Relative, [string]$Text) {
        $file = Join-Path $Root $Relative
        New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
        [IO.File]::WriteAllText($file, $Text, $utf8)
    }
    Write-DemoFile '.claude\settings.json' (@{ enabledPlugins = [ordered]@{
        'nexus-windows@nexus-plugins' = $true; 'nexus-android@nexus-plugins' = $true; 'nexus-browser@nexus-plugins' = $true
        'nexus-relay@nexus-plugins' = $true } } | ConvertTo-Json -Compress)
    # Claude Desktop as the Store app leaves it: NexusWindows and NexusBrowser added as .mcpb extensions, NexusAndroid
    # not yet (its page shows the extension step).
    Write-DemoFile 'AppData\Roaming\Claude\claude_desktop_config.json' '{"mcpServers":{}}'
    foreach ($name in 'nexus-windows-claude', 'nexus-browser') {
        Write-DemoFile "AppData\Roaming\Claude\Claude Extensions\local.mcpb.julien-piron.$name\manifest.json" (@{ name = $name } | ConvertTo-Json -Compress)
    }
    Write-DemoFile '.codex\config.toml' ((@('nexus-windows', 'nexus-android', 'nexus-browser', 'nexus-relay') |
        ForEach-Object { "[plugins.`"$_@nexus-plugins`"]`nenabled = true`n" }) -join "`n")
    foreach ($plugin in 'nexus-windows', 'nexus-android', 'nexus-browser', 'nexus-relay', 'nexus-agymail') {
        Write-DemoFile ".gemini\config\plugins\$plugin\plugin.json" '{}'
    }
    New-Item -ItemType Directory -Force (Join-Path $Root '.gemini\antigravity'), (Join-Path $Root 'AppData\Local\Logi\LogiPluginService\Plugins\NexusKeypad') | Out-Null
    # The state each product writes about its own version (~\.nexus\status), as the released versions do.
    $states = [ordered]@{
        'windows-claude' = @('3.8.0', '3.8.0', 'NexusWindows 3.8.0 is up to date'); 'windows-codex' = @('3.8.0', '3.8.0', 'NexusWindows 3.8.0 is up to date')
        'windows-antigravity' = @('3.8.0', '3.8.0', 'NexusWindows 3.8.0 is up to date')
        'android' = @('0.21.0', '0.22.0', 'Version 0.22.0 is ready: it is used at the next start of your AI app.')
        'browser' = @('2.32.0', '2.32.0', $null); 'relay' = @('1.9.0', '1.9.0', 'NexusRelay 1.9.0 is up to date'); 'agymail' = @('0.13.0', '0.13.0', $null)
    }
    foreach ($name in $states.Keys) {
        $state = $states[$name]
        Write-DemoFile ".nexus\status\$name.json" ([ordered]@{ product = $name; version = $state[0]; latest = $state[1]; state = 'idle'
            checked_at = '2026-10-26T08:00:00Z'; message = $state[2] } | ConvertTo-Json -Compress)
    }
    $journals = [ordered]@{
        windows = @(40, @('notepad.exe|type', 'notepad.exe|key', 'mspaint.exe|click', 'explorer.exe|look', 'calculatorapp.exe|click', 'excel.exe|set', 'explorer.exe|launch'))
        android = @(12, @('com.android.settings|tap', 'com.google.android.calculator|tap', 'com.android.chrome|type', 'com.google.android.apps.maps|look', 'com.android.settings|scroll'))
        browser = @(20, @('example.com|navigate', 'wikipedia.org|navigate', 'wikipedia.org|click', 'example.com|fill'))
        relay = @(8, @('codex|send', 'antigravity|send', 'codex|wait_reply', 'codex|wake'))
    }
    $now = [DateTime]::UtcNow
    foreach ($product in $journals.Keys) {
        $count, $actions = $journals[$product]
        $random = New-Object Random ([int]([Text.Encoding]::UTF8.GetBytes($product) | Measure-Object -Sum).Sum)
        foreach ($agent in 'claude', 'codex', 'antigravity') {
            $lines = for ($i = 0; $i -lt $count; $i++) {
                $app, $action = $actions[$random.Next($actions.Count)] -split '\|'
                $roll = $random.Next(100)
                $issue = if ($roll -lt 80) { [string][char]0x2713 } elseif ($roll -lt 88) { '=' } elseif ($roll -lt 96) { '~' } else { [string][char]0x2717 }
                [ordered]@{ v = 1; ts = $now.AddMinutes(-$random.Next(1, 601)).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ"); agent = $agent; outil = 'demo'
                    appel = $null; app = $app; cadre = $null; action = $action; technique = $null; issue = $issue; ms = $random.Next(40, 901)
                    premier_plan = $false; erreur = $(if ($issue -eq [string][char]0x2717) { 'element not found' } else { $null }) } | ConvertTo-Json -Compress
            }
            Write-DemoFile "AppData\Local\NexusJournal\$product\$agent\$($now.ToString('yyyy-MM')).jsonl" (($lines -join "`n") + "`n")
        }
    }
    Write-Host "demo profile in $Root"
}# Dot-source only for read-only output validation; the native captures start only when the script is executed.
if ($MyInvocation.InvocationName -eq '.') { return }
$output = Resolve-NexusCaptureOutput $Out (Split-Path $PSScriptRoot)
$Out = $output.Path
$architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
$architecture = if ($architecture -eq 'ARM64') { 'ARM64' } else { 'x64' }
$exe = Get-ChildItem (Join-Path $HOME ".nexus\build\NexusSystem\Nexus.App\bin\$architecture") -Recurse -Filter NexusSystem.exe |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw 'Build src\Nexus.App first (output under ~\.nexus\build\NexusSystem).' }
$demo = Join-Path ([IO.Path]::GetTempPath()) ('nexus-demo-' + [guid]::NewGuid().ToString('N'))
Initialize-NexusDemoProfile $demo
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
        $process = Start-Process $exe.FullName -ArgumentList "--capture `"$Out`" --size $Size --lang $lang --theme $theme" -PassThru
        for ($i = 0; $i -lt 360 -and -not $process.HasExited; $i++) {
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
