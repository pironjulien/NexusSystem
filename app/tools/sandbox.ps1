# Runs an exact MSIX in a clean, disposable Windows Sandbox. Only the report folder is writable from the guest.
# The existing host installation is never deployed, replaced or removed. Requires Windows Sandbox already enabled.
param(
    [Parameter(Mandatory)][string]$Msix,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedSha256,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.0$')][string]$Version,
    [switch]$Certification,
    [switch]$CertificationOnly,
    [switch]$TestSignInGuest,
    [string]$Report = (Join-Path $env:LOCALAPPDATA ('NexusApps\sandbox-' + [guid]::NewGuid().ToString('N'))),
    [ValidateRange(1, 120)][int]$TimeoutMinutes = 30
)
$ErrorActionPreference = 'Stop'
if ($CertificationOnly) { $Certification = $true }
$package = (Resolve-Path -LiteralPath $Msix).Path
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $ExpectedSha256) { throw 'The MSIX does not match ExpectedSha256.' }
. (Join-Path $PSScriptRoot 'wack.ps1') -Msix $package -Report $Report
$identity = Get-WackIdentity $package
Assert-WackCertificationMode -Path $package -CertificationOnly:$CertificationOnly -TestSignInGuest:$TestSignInGuest
if ($identity.Version -ne $Version) { throw 'The MSIX does not match Version.' }
$sandboxCli = Get-Command wsb.exe -ErrorAction SilentlyContinue
if ($sandboxCli) {
    $runningSandboxes = & $sandboxCli.Source list --raw
    if ($LASTEXITCODE) { throw 'The running Windows Sandbox inventory could not be read; nothing was launched.' }
    $sandboxOpen = @((($runningSandboxes -join "`n") | ConvertFrom-Json).WindowsSandboxEnvironments).Count -gt 0
} else { $sandboxOpen = @(Get-Process WindowsSandbox, WindowsSandboxClient -ErrorAction SilentlyContinue).Count -gt 0 }
if ($sandboxOpen) {
    throw 'Another Windows Sandbox is open; it was left untouched.'
}
$sandbox = Join-Path $env:SystemRoot 'System32\WindowsSandbox.exe'
if (-not (Test-Path -LiteralPath $sandbox -PathType Leaf)) { throw 'Windows Sandbox is not available.' }
$reportRoot = [IO.Path]::GetFullPath($Report)
if (Test-Path -LiteralPath $reportRoot) { throw 'The report directory already exists; choose a new path.' }
$inputDir = Join-Path $reportRoot 'input'
$outputDir = Join-Path $reportRoot 'output'
New-Item -ItemType Directory -Path $inputDir, $outputDir | Out-Null
Copy-Item -LiteralPath $package -Destination (Join-Path $inputDir 'package.msix')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sandbox-guest.ps1'), (Join-Path $PSScriptRoot 'wack.ps1') -Destination $inputDir
[pscustomobject]@{ Version = $Version; Sha256 = $ExpectedSha256.ToUpperInvariant(); Certification = [bool]$Certification; CertificationOnly = [bool]$CertificationOnly; TestSignInGuest = [bool]$TestSignInGuest } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $inputDir 'run.json') -Encoding UTF8
$inputXml = [Security.SecurityElement]::Escape($inputDir)
$outputXml = [Security.SecurityElement]::Escape($outputDir)
$kitMapping = ''
if ($Certification -or $TestSignInGuest) {
    $kit = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit'
    if (-not (Test-Path -LiteralPath (Join-Path $kit 'appcert.exe') -PathType Leaf)) { throw 'The Windows App Certification Kit is not installed.' }
    $kitXml = [Security.SecurityElement]::Escape($kit)
    $kitMapping = "<MappedFolder><HostFolder>$kitXml</HostFolder><SandboxFolder>C:\NexusWackKit</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>"
    if ($Certification) {
        $dependencies = Get-WackDependency
        $taefXml = [Security.SecurityElement]::Escape($dependencies.Taef)
        $kitMapping += "<MappedFolder><HostFolder>$taefXml</HostFolder><SandboxFolder>C:\NexusTaef</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>"
        $referencesXml = [Security.SecurityElement]::Escape($dependencies.References)
        $kitMapping += "<MappedFolder><HostFolder>$referencesXml</HostFolder><SandboxFolder>C:\NexusSdkReferences</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>"
    }
}
$configuration = @"
<Configuration>
  <Networking>Disable</Networking>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder><HostFolder>$inputXml</HostFolder><SandboxFolder>C:\NexusInput</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$outputXml</HostFolder><SandboxFolder>C:\NexusOutput</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
    $kitMapping
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\NexusInput\sandbox-guest.ps1</Command></LogonCommand>
</Configuration>
"@
$configurationPath = Join-Path $reportRoot 'run.wsb'
Set-Content -LiteralPath $configurationPath -Value $configuration -Encoding UTF8
$hostBefore = @(Get-AppxPackage -Name $identity.Name | ForEach-Object { $_.PackageFullName })
$process = Start-Process -FilePath $sandbox -ArgumentList ('"' + $configurationPath + '"') -WindowStyle Hidden -PassThru
$resultPath = Join-Path $outputDir 'result.json'
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
try {
    while (-not (Test-Path -LiteralPath $resultPath)) {
        if ((Get-Date) -gt $deadline) { throw "Sandbox timed out. Its isolated session and partial report were preserved: $reportRoot" }
        # Current Windows versions hand off from this launcher to the Sandbox client asynchronously.
        if ($process.HasExited -and $process.ExitCode -ne 0) {
            throw "Sandbox launcher failed ($($process.ExitCode)). See $reportRoot"
        }
        Start-Sleep -Seconds 1
    }
    $result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    if (-not $result.Success) { throw "Sandbox validation failed: $($result.Error) Report: $reportRoot" }
    if ($result.Sha256 -ne $ExpectedSha256 -or $result.Identity.Version -ne $Version) { throw 'The guest result does not match the requested package.' }
    Write-Host "Sandbox PASS: native smoke=$($result.NativeSmoke); certification=$($result.Certification). Report: $reportRoot"
    $result
} finally {
    $hostAfter = @(Get-AppxPackage -Name $identity.Name | ForEach-Object { $_.PackageFullName })
    if ([string]$hostBefore -ne [string]$hostAfter) { throw 'The host package list changed during the test; no host cleanup was attempted.' }
}
