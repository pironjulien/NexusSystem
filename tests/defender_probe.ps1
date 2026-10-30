# CI only (a disposable runner): does Microsoft Defender block the Nexus pack the way a beginner starts it?
# Defender's real-time protection is turned on for the runner, then each way of starting runs in its own process and
# the detections it raised are read back (Get-MpThreatDetection). Never on a real PC: it changes Defender's settings.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'defender_probe.ps1 runs on a disposable CI runner only.' }
$pack = 'https://raw.githubusercontent.com/pironjulien/NexusSystem/main/get-nexus.ps1'
$work = Join-Path $env:RUNNER_TEMP 'defender-probe'
New-Item -ItemType Directory -Force $work | Out-Null

function Show-State([string]$When) {
    $s = Get-MpComputerStatus
    Write-Host "$When : AV $($s.AntivirusEnabled), real time $($s.RealTimeProtectionEnabled), behavior $($s.BehaviorMonitorEnabled), IOAV $($s.IoavProtectionEnabled), engine $($s.AMEngineVersion), signatures $($s.AntivirusSignatureVersion)"
}

Show-State 'before'
try {
    Set-MpPreference -DisableRealtimeMonitoring $false -DisableBehaviorMonitoring $false -DisableIOAVProtection $false `
        -DisableScriptScanning $false -MAPSReporting Advanced -SubmitSamplesConsent SendSafeSamples
} catch { Write-Host "Set-MpPreference: $($_.Exception.Message)" }
Start-Sleep -Seconds 5
Show-State 'after'

# A fresh profile per case, so that each one really installs: the AI apps found by their folders.
function Initialize-CaseProfile([string]$Name) {
    $root = Join-Path $work $Name
    foreach ($d in '.codex', '.gemini\antigravity', 'AppData\Roaming\Claude', 'AppData\Local') { New-Item -ItemType Directory -Force (Join-Path $root $d) | Out-Null }
    return $root
}

$cases = [ordered]@{
    # What a beginner does: types (pastes) the line into an open PowerShell window. Emulated by feeding it to a
    # PowerShell that reads its commands from the keyboard input (-Command -): the process command line has no URL.
    'paste-irm-iex'        = @{ Args = '-NoProfile -Command -'; Input = "irm $pack | iex" }
    # The same, with the -DryRun options form.
    'paste-scriptblock'    = @{ Args = '-NoProfile -Command -'; Input = "& ([scriptblock]::Create((irm $pack))) -DryRun" }
    # What our automated tests did on the PC (flagged there): the URL on the PowerShell command line.
    'commandline-irm-iex'  = @{ Args = "-NoProfile -Command `"& ([scriptblock]::Create((irm $pack))) -DryRun`""; Input = $null }
    # The pack downloaded to a file, then run from it (the recommended alternative).
    'file'                 = @{ Args = $null; Input = $null; File = $true }
}

$rows = @()
foreach ($name in $cases.Keys) {
    $case = $cases[$name]
    $root = Initialize-CaseProfile $name
    $since = Get-Date
    $start = New-Object Diagnostics.ProcessStartInfo 'powershell.exe'
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    if ($case.File) {
        $file = Join-Path $work 'get-nexus.ps1'
        Invoke-WebRequest -UseBasicParsing $pack -OutFile $file
        $start.Arguments = "-NoProfile -ExecutionPolicy RemoteSigned -File `"$file`" -DryRun"
    } else {
        $start.Arguments = $case.Args
    }
    $start.Environment['USERPROFILE'] = $root
    $start.Environment['APPDATA'] = "$root\AppData\Roaming"
    $start.Environment['LOCALAPPDATA'] = "$root\AppData\Local"
    $process = [Diagnostics.Process]::Start($start)
    if ($case.Input) { $process.StandardInput.WriteLine($case.Input) }
    $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEndAsync()
    $errors = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(900000)) { $process.Kill() }
    Start-Sleep -Seconds 15  # detections are recorded a moment after the event
    $found = @(Get-MpThreatDetection -ErrorAction SilentlyContinue | Where-Object { $_.InitialDetectionTime -ge $since.AddSeconds(-2) })
    $threats = @($found | ForEach-Object { (Get-MpThreat -ThreatID $_.ThreatID -ErrorAction SilentlyContinue).ThreatName } | Select-Object -Unique)
    $text = $output.Result + $errors.Result
    $rows += [PSCustomObject]@{
        case     = $name
        exit     = $process.ExitCode
        blocked  = [bool]$found.Count
        threats  = ($threats -join ', ')
        finished = $text -match 'Summary|Dry run: nothing installed|everything''s installed'
    }
    Write-Host "== $name : exit $($process.ExitCode), detections $($found.Count) $($threats -join ', ')"
    Write-Host ($text.Substring([Math]::Max(0, $text.Length - 1500)))
}
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
"### Microsoft Defender probe`n`n| case | exit | blocked | threats | finished |`n| --- | --- | --- | --- | --- |`n" +
    (($rows | ForEach-Object { "| $($_.case) | $($_.exit) | $($_.blocked) | $($_.threats) | $($_.finished) |" }) -join "`n") |
    Add-Content $env:GITHUB_STEP_SUMMARY
