# sandbox.ps1's watch: a Sandbox that is gone stops the wait with its reason instead of waiting for a result that can
# no longer come (2026-11-07: connection lost before the guest's logon, the script waited until stopped by hand).
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\tools\sandbox-watch.ps1')
function Assert-Watch([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$t0 = [datetime]'2026-11-07T23:00:00'

$w = Get-NexusSandboxWatch $t0
Assert-Watch ($null -eq (Get-NexusSandboxStopReason $w $false $t0.AddMinutes(1) '')) 'A Sandbox still starting must be awaited.'
$stop = Get-NexusSandboxStopReason $w $false $t0.AddMinutes(5) ''
Assert-Watch ($stop -like '*did not start within 5 min*') "A Sandbox that never starts must stop the wait: $stop"

$w = Get-NexusSandboxWatch $t0
Assert-Watch ($null -eq (Get-NexusSandboxStopReason $w $true $t0.AddSeconds(30) '')) 'A running Sandbox must be awaited.'
Assert-Watch ($null -eq (Get-NexusSandboxStopReason $w $false $t0.AddMinutes(3) '')) 'A Sandbox missing for a moment must be awaited (grace).'
Assert-Watch ($null -eq (Get-NexusSandboxStopReason $w $true $t0.AddMinutes(3).AddSeconds(5) '')) 'A Sandbox back within the grace must be awaited.'
Assert-Watch ($null -eq (Get-NexusSandboxStopReason $w $false $t0.AddMinutes(4) '')) 'The grace restarts after the Sandbox came back.'
$stop = Get-NexusSandboxStopReason $w $false $t0.AddMinutes(4).AddSeconds(20) ''
Assert-Watch ($stop -like '*closed before the guest wrote its result*never started its script*') "A Sandbox gone before the guest's logon must say so: $stop"

$w = Get-NexusSandboxWatch $t0
$null = Get-NexusSandboxStopReason $w $true $t0.AddMinutes(1) 'certification'
$null = Get-NexusSandboxStopReason $w $false $t0.AddMinutes(2) 'certification'
$stop = Get-NexusSandboxStopReason $w $false $t0.AddMinutes(2).AddSeconds(15) 'certification'
Assert-Watch ($stop -like '*last guest phase: certification*') "A Sandbox gone during the guest's work must name its last phase: $stop"

# The running check never invents a verdict from an unreadable list.
$fake = Join-Path ([IO.Path]::GetTempPath()) ('nexus-wsb-' + [guid]::NewGuid().ToString('N') + '.cmd')
try {
    Set-Content -LiteralPath $fake -Value '@echo {"WindowsSandboxEnvironments":[]}' -Encoding ascii
    Assert-Watch (-not (Test-NexusSandboxRunning $fake)) 'An empty environment list means no Sandbox.'
    Set-Content -LiteralPath $fake -Value '@echo {"WindowsSandboxEnvironments":[{"Id":"x"}]}' -Encoding ascii
    Assert-Watch (Test-NexusSandboxRunning $fake) 'A listed environment means a running Sandbox.'
    Set-Content -LiteralPath $fake -Value '@echo not json' -Encoding ascii
    Assert-Watch (Test-NexusSandboxRunning $fake) 'An unreadable list must count as running.'
    Set-Content -LiteralPath $fake -Value '@exit /b 1' -Encoding ascii
    Assert-Watch (Test-NexusSandboxRunning $fake) 'A failed list must count as running.'
} finally { Remove-Item -LiteralPath $fake -Force -ErrorAction SilentlyContinue }
'OK sandbox watch: never started, gone before logon, gone during work, grace, unreadable list'
