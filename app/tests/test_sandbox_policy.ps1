# sandbox.ps1's Smart App Control mode: turned off in the disposable guest only, for an unsigned package under
# -Certification or when asked, recorded in the report, and never on the host (2026-11-07: the guest's Smart App Control
# refused the test-signed Store package 1.0.18.0 with 0x800711C7).
$ErrorActionPreference = 'Stop'
$tools = Join-Path $PSScriptRoot '..\tools'
. (Join-Path $tools 'sandbox-policy.ps1')
function Assert-Policy([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }

Assert-Policy (Test-NexusGuestSmartAppControlOff $false $true $false) 'Certification of an unsigned package must turn it off in the guest.'
Assert-Policy (-not (Test-NexusGuestSmartAppControlOff $false $true $true)) 'A signed package keeps the guest policy.'
Assert-Policy (-not (Test-NexusGuestSmartAppControlOff $false $false $false)) 'Without -Certification nothing is turned off unless asked.'
Assert-Policy (Test-NexusGuestSmartAppControlOff $true $false $true) 'An explicit request turns it off.'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$dir = Join-Path ([IO.Path]::GetTempPath()) ('nexus-sac-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    foreach ($case in @(@{ Name = 'unsigned'; Entries = @('AppxManifest.xml') }, @{ Name = 'signed'; Entries = @('AppxManifest.xml', 'AppxSignature.p7x') })) {
        $path = Join-Path $dir ($case.Name + '.msix')
        $zip = [IO.Compression.ZipFile]::Open($path, 'Create')
        try { foreach ($entry in $case.Entries) { [void]$zip.CreateEntry($entry) } } finally { $zip.Dispose() }
        Assert-Policy ((Test-NexusMsixSigned $path) -eq ($case.Name -eq 'signed')) "Signature detection is wrong for the $($case.Name) package."
    }
} finally { Remove-Item -LiteralPath $dir -Recurse -Force }

# The host is only read: the guest alone writes the policy, after its identity check and only when run.json asks.
$guest = Get-Content -Raw (Join-Path $tools 'sandbox-guest.ps1')
$identityCheck = $guest.IndexOf("WDAGUtilityAccount') { throw")
$write = $guest.IndexOf('-Name VerifiedAndReputablePolicyState -Value 0')
Assert-Policy ($identityCheck -ge 0 -and $write -gt $identityCheck) 'The guest must check its Sandbox identity before changing the policy.'
Assert-Policy ($guest.LastIndexOf('if ($spec.DisableSmartAppControlInGuest)', $write) -gt $identityCheck) 'The guest changes the policy only when run.json asks.'
Assert-Policy ($guest.IndexOf('SmartAppControlInGuest = ') -gt $write) 'The guest must record the change in result.json.'
Assert-Policy ($guest.IndexOf("Write-NexusPhase 'register'") -gt $write) 'The policy must be off before the package is installed.'
foreach ($hostScript in 'sandbox.ps1', 'sandbox-policy.ps1') {
    Assert-Policy ((Get-Content -Raw (Join-Path $tools $hostScript)) -notmatch 'Set-ItemProperty|New-ItemProperty|CiTool') "$hostScript must never change the host policy."
}
$hostScriptText = Get-Content -Raw (Join-Path $tools 'sandbox.ps1')
Assert-Policy ($hostScriptText -match 'hostSmartAppControl -ne \[string\]\(Get-NexusSmartAppControlState\)') 'sandbox.ps1 must check the host policy is unchanged.'
'OK sandbox Smart App Control: guest only, unsigned under -Certification or asked, recorded, host unchanged'
