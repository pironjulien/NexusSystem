# Watch of the Windows Sandbox that sandbox.ps1 starts: keep waiting for the guest's result, or stop with the reason.
# On 2026-11-07 the Sandbox's connection was lost before the guest's user logon (Hyper-V: "WaitForUserLogon" never
# completed, the VM was terminated 3 min later); sandbox.ps1 kept waiting for a result that could no longer come.
function Get-NexusSandboxWatch([datetime]$Started) {
    [pscustomobject]@{ Started = $Started; Seen = $null; Missing = $null; StartMinutes = 5; GraceSeconds = 15 }
}

# $Running: whether a Sandbox environment runs now. $Phase: the guest's last phase (phase.json), empty when it never
# started its script. Returns $null to keep waiting, or the reason to stop.
function Get-NexusSandboxStopReason($Watch, [bool]$Running, [datetime]$Now, [string]$Phase) {
    if ($Running) { $Watch.Seen = $Now; $Watch.Missing = $null; return $null }
    if (-not $Watch.Seen) {
        if (($Now - $Watch.Started).TotalMinutes -ge $Watch.StartMinutes) { return "Windows Sandbox did not start within $($Watch.StartMinutes) min." }
        return $null
    }
    if (-not $Watch.Missing) { $Watch.Missing = $Now }
    if (($Now - $Watch.Missing).TotalSeconds -lt $Watch.GraceSeconds) { return $null }
    $where = if ($Phase) { "last guest phase: $Phase" } else { 'the guest never started its script: no user logon in the Sandbox (window closed or connection lost)' }
    return "Windows Sandbox closed before the guest wrote its result ($where)."
}

# Whether a Sandbox environment runs: wsb.exe's list when present, else the Sandbox client process. An unreadable list
# counts as running (never a false verdict); the overall timeout still applies.
function Test-NexusSandboxRunning($Cli) {
    if ($Cli) {
        $raw = & $Cli list --raw 2>$null
        if ($LASTEXITCODE) { return $true }
        try { return @((($raw -join "`n") | ConvertFrom-Json).WindowsSandboxEnvironments).Count -gt 0 } catch { return $true }
    }
    return @(Get-Process WindowsSandboxClient, WindowsSandboxRemoteSession -ErrorAction SilentlyContinue).Count -gt 0
}
