# WACK ownership tests. No Appx command or certification executable is run: the commands below are local test doubles.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Local test doubles of Appx commands: they change nothing on the machine.')]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\tools\wack.ps1') -Msix 'unused-in-tests'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('nexus-wack-tests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$script:wackTest = $null

function Assert-Wack($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function New-WackTest([string]$Mode = '') {
    $script:wackTest = @{ Mode = $Mode; Packages = @(); Commands = [Collections.Generic.List[string]]::new();
        Removed = [Collections.Generic.List[string]]::new(); Registered = $null; Layout = $null }
}
function Get-AppxPackage {
    [CmdletBinding()] param([string]$Name)
    @($script:wackTest.Packages | Where-Object Name -eq $Name)
}
function Add-AppxPackage {
    [CmdletBinding()] param([string]$Register)
    $script:wackTest.Layout = Split-Path $Register
    $script:wackTest.Registered = [pscustomobject]@{ Name = 'Example.Certification'; Publisher = 'CN=Example'; Version = '1.2.3.0';
        PackageFullName = 'Example.Certification_1.2.3.0_x64__test'; InstallLocation = $script:wackTest.Layout }
    $script:wackTest.Packages = @($script:wackTest.Registered)
}
function Remove-AppxPackage {
    [CmdletBinding()] param([string]$Package)
    $script:wackTest.Removed.Add($Package)
    $script:wackTest.Packages = @($script:wackTest.Packages | Where-Object PackageFullName -ne $Package)
}
function Invoke-WackCommand([string[]]$Arguments) {
    $script:wackTest.Commands.Add($Arguments -join ' ')
    if ($Arguments[0] -eq 'reset' -and $script:wackTest.Mode -eq 'appears') {
        $script:wackTest.Packages = @($script:existingPackage)
    }
    if ($Arguments[0] -eq 'test') {
        if ($script:wackTest.Mode -eq 'fail') { throw 'expected certification failure' }
        if ($script:wackTest.Mode -eq 'replace') {
            $script:wackTest.Packages = @($script:existingPackage)
        }
        if ($script:wackTest.Mode -eq 'leftover') { $script:wackTest.Packages = @($script:existingPackage) }
    }
}
function Assert-WackFailure([scriptblock]$Run, [string]$Contains) {
    try { & $Run; throw 'Expected certification refusal.' }
    catch { if ($_.Exception.Message -notlike "*$Contains*") { throw } }
}

try {
    $sdk = Join-Path $testRoot 'sdk'
    $kit = Join-Path $sdk 'App Certification Kit'
    $taef = Join-Path $sdk 'Testing\Runtimes\TAEF'
    New-Item -ItemType Directory -Path $kit, $taef | Out-Null
    Assert-WackFailure { Get-WackDependency $sdk } 'Certification Kit is not installed'
    [IO.File]::WriteAllText((Join-Path $kit 'appcert.exe'), '')
    Assert-WackFailure { Get-WackDependency $sdk } 'TAEF runtime'
    [IO.File]::WriteAllText((Join-Path $taef 'TE.exe'), '')
    Assert-WackFailure { Get-WackDependency $sdk } 'SDK References metadata'
    $metadata = Join-Path $sdk 'References\10.0.26100.0\ExampleContract\1.0.0.0'
    New-Item -ItemType Directory -Path $metadata | Out-Null
    [IO.File]::WriteAllText((Join-Path $metadata 'Example.winmd'), '')
    $dependencies = Get-WackDependency $sdk
    Assert-Wack ($dependencies.Kit -eq $kit -and $dependencies.Taef -eq $taef -and $dependencies.References -eq (Join-Path $sdk 'References')) 'Certification dependency paths were not preserved.'
    Write-Host 'WACK: 4 toolkit, TAEF and SDK metadata dependency scenarios passed without executable launch.'
    $reportFixture = Join-Path $testRoot 'report.xml'
    foreach ($overall in 'PASS', 'FAIL') {
        [IO.File]::WriteAllText($reportFixture, ('<REPORT OVERALL_RESULT="' + $overall + '" />'))
        Assert-Wack ((Read-WackReport $reportFixture).OverallResult -eq $overall) 'WACK overall result was lost.'
    }
    [IO.File]::WriteAllText($reportFixture, '<REPORT />')
    Assert-WackFailure { Read-WackReport $reportFixture } 'no overall result'
    Write-Host 'WACK: 3 XML report scenarios passed (PASS, FAIL and missing result).'

    $msix = Join-Path $testRoot 'example.msix'
    $archive = [IO.Compression.ZipFile]::Open($msix, 'Create')
    try {
        $writer = [IO.StreamWriter]::new($archive.CreateEntry('AppxManifest.xml').Open())
        try { $writer.Write('<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Identity Name="Example.Certification" Publisher="CN=Example" Version="1.2.3.0" ProcessorArchitecture="x64" /></Package>') }
        finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
    Assert-WackFailure { Assert-WackCertificationMode -Path $msix -CertificationOnly } 'needs a signed MSIX'
    Assert-WackCertificationMode -Path $msix -CertificationOnly -TestSignInGuest
    Assert-WackCertificationMode -Path $msix
    Write-Host 'WACK: unsigned certification-only input refused before deployment; guest signing and native-smoke modes retained.'
    $script:existingPackage = [pscustomobject]@{ Name = 'Example.Certification'; Publisher = 'CN=Example'; Version = '1.2.3.0';
        PackageFullName = 'Example.Certification_1.2.3.0_x64__test'; InstallLocation = (Join-Path $testRoot 'existing-installation') }

    foreach ($registered in @($false, $true)) {
        New-WackTest
        $script:wackTest.Packages = @($script:existingPackage)
        $report = Join-Path $testRoot ('refused-' + $registered)
        Assert-WackFailure { Invoke-NexusCertification -Msix $msix -Report $report -Registered:$registered } 'already installed'
        Assert-Wack ($script:wackTest.Commands.Count -eq 0 -and $script:wackTest.Removed.Count -eq 0 -and -not (Test-Path $report)) 'Pre-existing installation was touched.'
    }

    New-WackTest
    Invoke-NexusCertification -Msix $msix -Report (Join-Path $testRoot 'package')
    Assert-Wack ($script:wackTest.Commands.Count -eq 2 -and $script:wackTest.Commands[1].Contains('-appxpackagepath') -and $script:wackTest.Removed.Count -eq 0) 'Package mode must leave deployment ownership to appcert.'

    New-WackTest
    Invoke-NexusCertification -Msix $msix -Report (Join-Path $testRoot 'registered') -Registered
    Assert-Wack ($script:wackTest.Removed.Count -eq 1 -and $script:wackTest.Removed[0] -eq $script:wackTest.Registered.PackageFullName -and -not (Test-Path $script:wackTest.Layout)) 'Clean registered test did not remove only its exact package and layout.'

    New-WackTest 'fail'
    Assert-WackFailure { Invoke-NexusCertification -Msix $msix -Report (Join-Path $testRoot 'failure') -Registered } 'expected certification failure'
    Assert-Wack ($script:wackTest.Removed.Count -eq 1 -and -not (Test-Path $script:wackTest.Layout)) 'Failed certification did not clean its own registration.'

    New-WackTest 'replace'
    Invoke-NexusCertification -Msix $msix -Report (Join-Path $testRoot 'replacement') -Registered -WarningAction SilentlyContinue
    Assert-Wack ($script:wackTest.Removed.Count -eq 0 -and $script:wackTest.Packages[0].InstallLocation -eq $script:existingPackage.InstallLocation) 'A replacement with the same full name was removed.'

    New-WackTest 'leftover'
    Invoke-NexusCertification -Msix $msix -Report (Join-Path $testRoot 'leftover') -WarningAction SilentlyContinue
    Assert-Wack ($script:wackTest.Removed.Count -eq 0 -and $script:wackTest.Packages.Count -eq 1) 'Package-mode leftovers must be reported, never removed by name.'

    New-WackTest 'appears'
    Assert-WackFailure { Invoke-NexusCertification -Msix $msix -Report (Join-Path $testRoot 'appeared') -Registered -WarningAction SilentlyContinue } 'appeared before registration'
    Assert-Wack ($null -eq $script:wackTest.Registered -and $script:wackTest.Removed.Count -eq 0) 'An installation appearing during preflight was replaced.'
    Write-Host 'WACK: 8 ownership and refusal scenarios passed without Appx or appcert execution.'
} finally {
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not ([IO.Path]::GetFullPath($testRoot)).StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $testRoot).LinkType) { throw 'Unexpected test directory; cleanup refused.' }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
