# Nexus pack: the whole Nexus ecosystem on a Windows 10/11 PC, for the AI apps found on it. The NexusSystem app of the
# Microsoft Store does it for you. By hand, always as a FILE of a NexusSystem release, checked, then run with -File
# (never run in memory from the network: antivirus heuristics rightly flag that):
#   $r = 'https://github.com/pironjulien/NexusSystem/releases/latest/download'
#   Invoke-WebRequest "$r/get-nexus.ps1" -OutFile get-nexus.ps1; Invoke-WebRequest "$r/SHA256SUMS.txt" -OutFile SHA256SUMS.txt
#   (Get-FileHash get-nexus.ps1).Hash   # the get-nexus.ps1 line of SHA256SUMS.txt
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\get-nexus.ps1 -Only windows,browser
# This file only starts the work: it downloads NexusMaintenance.exe, the native engine published with this same release
# (x64 or ARM64), checks it against the release's SHA256SUMS.txt and, when the official gh tool is signed in, its build
# provenance, then runs it in this console. The engine does everything else (NexusMaintenance.exe --install): it finds
# Claude Code, Claude Desktop, Codex and Antigravity, shows the plan, gives Claude Code and Codex the plugins of this
# catalog, runs each product's own official installer (the get.ps1 of its latest release, checked the same way),
# reports what each AI app declares, and registers the hourly "NexusSystem Update" task that starts that same engine.
# Windows PowerShell 5.1 or PowerShell 7, no administrator rights. Run it again at any time: only what is missing is done.
#   -Only / -Skip  product names (windows, android, browser, relay, agymail, indesign, keypad; free only when named) or AI names (claude-code,
#                  claude-desktop, codex, antigravity), comma separated
#   -DryRun        show the plan and do nothing
#   -NoGuide       do not open Chrome on its extensions page at the end (the guide is printed instead)
#   -Catalog       marketplace given to Claude Code and Codex (default pironjulien/NexusSystem; a local folder for tests)
#   -MaintenanceFrom  tests only, with a local -Catalog: a folder holding this version's engine and its SHA256SUMS.txt,
#                  built before the release exists (the release workflow's pack check); checked the same way. The task
#                  then starts it with --test-catalog <Catalog> --test-engines <folder>
#   This PC's own choices: %USERPROFILE%\.nexus\nexussystem.json, {"skip": ["claude-desktop"]}, adds its names to -Skip
#   -Json          also print one "NEXUS_PACK_EVENT {json}" line per event (found, plan, step, cell, done)
#   -Maintain      run the maintenance engine of this release now, then make it the hourly one; -ForceMaintenance skips
#                  its hourly pause (-MaintenanceCurrent: the hand-over contract of releases up to 1.6.54)
#   -MaintenanceStatus  read the last check as JSON without changing files or contacting GitHub
#   -InstallMaintenance  establish the same updater for an already installed Store hub, without reinstalling products
param(
    [string[]]$Only = @(),
    [string[]]$Skip = @(),
    [switch]$DryRun,
    [switch]$NoGuide,
    [string]$Catalog = 'pironjulien/NexusSystem',
    [string]$MaintenanceFrom = '',
    [switch]$Json,
    [switch]$Maintain,
    [switch]$MaintenanceStatus,
    [switch]$MaintenanceCurrent,
    [switch]$ForceMaintenance,
    [switch]$InstallMaintenance
)

$ErrorActionPreference = 'Stop'
# A Windows PowerShell child of PowerShell 7 can inherit Core modules before its own modules. Keep all user
# locations, but put this host's built-in modules first so Get-FileHash and other cmdlets load their Desktop build.
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    $builtinModules = Join-Path $PSHOME 'Modules'
    $modulePaths = @($env:PSModulePath -split ';' | Where-Object { $_ -and $_ -ine $builtinModules })
    $env:PSModulePath = (@($builtinModules) + $modulePaths) -join ';'
}
$ProgressPreference = 'SilentlyContinue'  # Invoke-WebRequest is many times slower with its progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$NexusMaintenanceVersion = '1.8.0'

function Get-NexusMaintenanceEngineName {
    # The engine of this PC's architecture: arm64 on an ARM64 Windows, also from an emulated x64 PowerShell.
    if ($env:PROCESSOR_ARCHITEW6432 -eq 'ARM64' -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { return 'NexusMaintenance-arm64.exe' }
    return 'NexusMaintenance-x64.exe'
}

function Get-ExpectedSha256([string]$Manifest, [string]$Name) {
    # The line for $Name in a SHA256SUMS.txt; a malformed or ambiguous manifest is refused.
    $expected = $null
    foreach ($line in ($Manifest -split '\r?\n' | Where-Object { $_.Trim() })) {
        if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(\S+)\s*$') { throw 'Malformed checksum file.' }
        if ($Matches[2] -ceq $Name) {
            if ($expected) { throw "$Name is listed twice in the checksum file." }
            $expected = $Matches[1].ToLowerInvariant()
        }
    }
    if (-not $expected) { throw "$Name is absent from the checksum file." }
    return $expected
}

function Save-NexusEngine([string]$Folder) {
    # The engine of THIS file's own release (v$NexusMaintenanceVersion), checked against that release's SHA256SUMS.txt
    # and, when the official gh tool is signed in, its build provenance; with -MaintenanceFrom, that folder's own.
    $tag = "v$NexusMaintenanceVersion"
    $name = Get-NexusMaintenanceEngineName
    $file = Join-Path $Folder $name
    if ($MaintenanceFrom) {
        $manifest = [IO.File]::ReadAllText((Join-Path $MaintenanceFrom 'SHA256SUMS.txt'))
        Copy-Item -LiteralPath (Join-Path $MaintenanceFrom $name) -Destination $file
    } else {
        $base = "https://github.com/pironjulien/NexusSystem/releases/download/$tag"
        $response = Invoke-WebRequest -UseBasicParsing "$base/SHA256SUMS.txt" -TimeoutSec 30
        $manifest = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
        Invoke-WebRequest -UseBasicParsing "$base/$name" -OutFile $file -TimeoutSec 300
    }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine (Get-ExpectedSha256 $manifest $name)) {
        throw "SHA-256 mismatch for $name`: it is not the published engine."
    }
    $gh = Get-Command gh.exe -ErrorAction SilentlyContinue
    if ($gh -and -not $MaintenanceFrom) {
        # gh needs a sign-in to read attestations, otherwise it exits with code 4: the check then cannot run, as without
        # gh. The SHA-256 from the release's SHA256SUMS.txt is already verified.
        $saved = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        try {
            $output = & $gh.Source attestation verify $file --repo 'pironjulien/NexusSystem' --source-ref "refs/tags/$tag" 2>&1
            $code = $LASTEXITCODE
        } finally { $ErrorActionPreference = $saved }
        if ($code -and $code -ne 4) { $output | Out-Host; throw "Build provenance failed for NexusSystem $tag (gh exit code $code)." }
    }
    return $file
}

function Invoke-NexusEngine([string[]]$Arguments) {
    # The checked engine in this console (its colors and the installers' output kept), from a folder of its own,
    # removed afterwards; its exit code. Only the engine is awaited, never the services the installers start.
    $folder = Join-Path ([IO.Path]::GetTempPath()) "nexus-engine-$PID-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    try {
        $engine = Save-NexusEngine $folder
        $line = ($Arguments | ForEach-Object { if ($_ -match '[\s"]' -or -not $_) { '"' + ($_ -replace '\\(?=\\*$)', '\\' -replace '"', '\"') + '"' } else { $_ } }) -join ' '
        $process = Start-Process -FilePath $engine -ArgumentList $line -NoNewWindow -PassThru
        $null = $process.Handle  # keeps the exit code readable once it has exited (Windows PowerShell 5.1)
        $process.WaitForExit()
        return $process.ExitCode
    } finally {
        Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-NexusMaintenance([switch]$Force) {
    # -Maintain: the hand-over from the PowerShell worker of releases up to 1.6.54 (it runs the newly published file
    # with -Maintain -MaintenanceCurrent), and a manual run.
    $arguments = @('--maintain')
    if ($Force) { $arguments += '--force' }
    return Invoke-NexusEngine $arguments
}

if ($MaintenanceCurrent -and -not $Maintain) { throw '-MaintenanceCurrent is an option of -Maintain.' }
if ($MaintenanceFrom -and ($Maintain -or $InstallMaintenance -or -not (Test-Path -LiteralPath $Catalog -PathType Container) -or
        -not (Test-Path -LiteralPath (Join-Path $MaintenanceFrom 'SHA256SUMS.txt') -PathType Leaf))) {
    throw '-MaintenanceFrom is a test option: an installation from a local -Catalog, with a folder holding SHA256SUMS.txt.'
}
if ($MaintenanceStatus) {
    # Read only, no download: the engine the task starts reads its own receipt.
    $task = Get-ScheduledTask -TaskName 'NexusSystem Update' -ErrorAction SilentlyContinue
    $engine = @($task.Actions | ForEach-Object { $_.Execute }) | Where-Object { $_ -and (Split-Path $_ -Leaf) -ieq 'NexusMaintenance.exe' -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
    if ($engine) { & $engine --status | Write-Output; exit $LASTEXITCODE }
    '{"schema": 1, "state": "pending", "checked_at": null, "latest": null, "catalogue": null, "products": [], "message": "Maintenance has not completed a check."}'
    exit 0
}
if ($InstallMaintenance) {
    if ($DryRun) { exit 0 }
    exit (Invoke-NexusEngine @('--install-maintenance', '--start'))
}
if ($Maintain) {
    if ($DryRun -or $Only.Count -or $Skip.Count -or $Catalog -ne 'pironjulien/NexusSystem') {
        throw 'Maintenance uses installed products and local choices only; DryRun/Only/Skip/Catalog are installation options.'
    }
    exit (Invoke-NexusMaintenance -Force:$ForceMaintenance)
}
$arguments = @('--install')
if ($Only.Count) { $arguments += '--only', ($Only -join ',') }
if ($Skip.Count) { $arguments += '--skip', ($Skip -join ',') }
if ($DryRun) { $arguments += '--dry-run' }
if ($NoGuide) { $arguments += '--no-guide' }
if ($Json) { $arguments += '--json' }
if ($Catalog -ne 'pironjulien/NexusSystem') {
    $arguments += '--catalog', $(if (Test-Path -LiteralPath $Catalog -PathType Container) { (Resolve-Path -LiteralPath $Catalog).ProviderPath } else { $Catalog })
}
if ($MaintenanceFrom) { $arguments += '--maintenance-from', (Resolve-Path -LiteralPath $MaintenanceFrom).ProviderPath }
$code = Invoke-NexusEngine $arguments
if ($PSCommandPath) {
    # Started from Explorer (right click > Run with PowerShell): the window would close at once, before the summary
    # and the steps left to the user could be read.
    $parent = (Get-CimInstance Win32_Process -Filter "ProcessId = $PID" -ErrorAction SilentlyContinue).ParentProcessId
    if ($parent -and (Get-Process -Id $parent -ErrorAction SilentlyContinue).ProcessName -eq 'explorer') {
        Read-Host 'Press Enter to close this window' | Out-Null
    }
}
exit $code
