# Smart App Control in the disposable Windows Sandbox that sandbox.ps1 starts. The Sandbox guest enforces it whatever
# the host's setting, and it trusts only signatures Microsoft vouches for: an unsigned Store package, test-signed in the
# guest, can never start there (0x800711C7, 2026-11-07). For such a package the guest turns it off for itself only,
# before installing, and the report says so; the host's policy is read before and after and must stay the same.

# Whether the MSIX carries a signature (AppxSignature.p7x).
function Test-NexusMsixSigned([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try { return $null -ne $archive.GetEntry('AppxSignature.p7x') } finally { $archive.Dispose() }
}

# Whether the guest turns Smart App Control off: asked explicitly, or implied by -Certification on an unsigned package.
function Test-NexusGuestSmartAppControlOff([bool]$Requested, [bool]$Certification, [bool]$Signed) {
    return $Requested -or ($Certification -and -not $Signed)
}

# The host's Smart App Control state (0 off, 1 on, 2 evaluation); $null when the value is absent.
function Get-NexusSmartAppControlState {
    (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -Name VerifiedAndReputablePolicyState -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState
}
