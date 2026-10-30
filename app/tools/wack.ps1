# Windows App Certification Kit on a clean Windows profile (normally Windows Sandbox). An installed package with the
# same identity is refused before the kit can deploy, replace or remove it. Reports stay outside the source tree.
# Dot-source only to load the functions for isolated tests; running the script invokes certification.
param(
    [Parameter(Mandatory)][string]$Msix,
    [string]$Report = (Join-Path $env:LOCALAPPDATA 'NexusApps\wack'),
    [switch]$Registered
)
$ErrorActionPreference = 'Stop'

function Get-WackDependency([string]$SdkRoot = (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10')) {
    $kit = Join-Path $SdkRoot 'App Certification Kit'
    $taef = Join-Path $SdkRoot 'Testing\Runtimes\TAEF'
    $references = Join-Path $SdkRoot 'References'
    if (-not (Test-Path -LiteralPath (Join-Path $kit 'appcert.exe') -PathType Leaf)) { throw 'The Windows App Certification Kit is not installed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $taef 'TE.exe') -PathType Leaf)) { throw 'The Windows App Certification Kit needs its TAEF runtime (Testing\Runtimes\TAEF\TE.exe).' }
    if (-not (Test-Path -LiteralPath $references -PathType Container) -or -not (Get-ChildItem -LiteralPath $references -Recurse -Filter *.winmd -File | Select-Object -First 1)) { throw 'The Windows App Certification Kit needs the SDK References metadata.' }
    return [pscustomobject]@{ Kit = $kit; Taef = $taef; References = $references }
}

function Get-WackIdentity([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw 'The MSIX has no AppxManifest.xml.' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $manifest = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
        $identity = $manifest.Package.Identity
        if (-not $identity -or $identity.Name -notmatch '^[A-Za-z0-9.-]{3,50}$' -or -not $identity.Publisher -or -not $identity.Version) {
            throw 'The MSIX has no valid package identity.'
        }
        return [pscustomobject]@{ Name = [string]$identity.Name; Publisher = [string]$identity.Publisher; Version = [string]$identity.Version }
    } finally { $archive.Dispose() }
}

function Invoke-WackCommand([string[]]$Arguments) {
    $appcert = Join-Path (Get-WackDependency).Kit 'appcert.exe'
    Push-Location -LiteralPath (Split-Path $appcert)
    try {
        & $appcert @Arguments
        if ($LASTEXITCODE) { throw "appcert failed (exit $LASTEXITCODE)." }
    } finally { Pop-Location }
}

function Read-WackReport([string]$Path) {
    $document = [xml](Get-Content -Raw -LiteralPath $Path)
    $overall = $document.DocumentElement.GetAttribute('OVERALL_RESULT')
    if ($document.DocumentElement.Name -ne 'REPORT' -or -not $overall) { throw 'The WACK report has no overall result.' }
    return [pscustomobject]@{ OverallResult = $overall }
}

function Assert-WackCertificationMode([string]$Path, [switch]$CertificationOnly, [switch]$TestSignInGuest) {
    if (-not $CertificationOnly -or $TestSignInGuest) { return }
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        if (-not $archive.GetEntry('AppxSignature.p7x')) { throw 'CertificationOnly needs a signed MSIX or TestSignInGuest; an unsigned layout is not deployed in this mode.' }
    } finally { $archive.Dispose() }
}

function Invoke-NexusCertification([string]$Msix, [string]$Report, [switch]$Registered) {
    $packagePath = (Resolve-Path -LiteralPath $Msix -ErrorAction Stop).Path
    $identity = Get-WackIdentity $packagePath
    # Get-AppxPackage -Name takes patterns: the manifest name above is restricted to literal identity characters.
    # Refuse even another publisher with that name rather than guessing what the kit might replace.
    if (@(Get-AppxPackage -Name $identity.Name -ErrorAction Stop).Count) {
        throw "Package '$($identity.Name)' is already installed. Certification requires a clean Windows profile or Windows Sandbox; the existing installation was left untouched."
    }

    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $layout = Join-Path $temporaryRoot ('nexussystem-wack-' + [guid]::NewGuid().ToString('N'))
    $transcribing = $false
    try {
        New-Item -ItemType Directory -Force $Report | Out-Null
        Start-Transcript -Path (Join-Path $Report 'run.log') -Force | Out-Null
        $transcribing = $true
        $xml = Join-Path ([IO.Path]::GetFullPath($Report)) 'wack-report.xml'
        Invoke-WackCommand -Arguments @('reset')
        if ($Registered) {
            [IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $layout)
            Remove-Item -LiteralPath (Join-Path $layout 'AppxSignature.p7x'), (Join-Path $layout 'AppxBlockMap.xml'), (Join-Path $layout '[Content_Types].xml') -Force -ErrorAction SilentlyContinue
            # Check again immediately before registration: another installation may have appeared since preflight.
            if (@(Get-AppxPackage -Name $identity.Name -ErrorAction Stop).Count) { throw 'Package appeared before registration; nothing was registered.' }
            Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ErrorAction Stop
            $owned = @(Get-AppxPackage -Name $identity.Name -ErrorAction Stop | Where-Object {
                $_.Publisher -eq $identity.Publisher -and [string]$_.Version -eq $identity.Version -and
                [IO.Path]::GetFullPath($_.InstallLocation).TrimEnd('\') -eq $layout
            })
            if ($owned.Count -ne 1) { throw 'The package registered by this test could not be identified uniquely.' }
            Invoke-WackCommand -Arguments @('test', '-packagefullname', $owned[0].PackageFullName, '-reportoutputpath', $xml)
        } else {
            # appcert owns this deployment, including its removal. Never uninstall packages by name afterwards.
            Invoke-WackCommand -Arguments @('test', '-appxpackagepath', $packagePath, '-reportoutputpath', $xml)
        }
        Write-Host "Certification finished, report $xml"
    } finally {
        try {
            if ($Registered) {
                # Registration may have succeeded before throwing. Only a package still pointing at this run's unique
                # layout belongs to us; a later replacement is preserved, even if it has the same PackageFullName.
                $owned = @(Get-AppxPackage -Name $identity.Name -ErrorAction Stop | Where-Object {
                    $_.Publisher -eq $identity.Publisher -and [string]$_.Version -eq $identity.Version -and
                    $_.InstallLocation -and [IO.Path]::GetFullPath($_.InstallLocation).TrimEnd('\') -eq $layout
                })
                foreach ($package in $owned) { Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop }
            }
            $left = @(Get-AppxPackage -Name $identity.Name -ErrorAction Stop)
            if ($left.Count) { Write-Warning "Package remains installed; preserved: $($left.PackageFullName -join ', ')" }
            $layoutInUse = @($left | Where-Object { $_.InstallLocation -and [IO.Path]::GetFullPath($_.InstallLocation).TrimEnd('\') -eq $layout }).Count -gt 0
            if ((Test-Path -LiteralPath $layout) -and -not $layoutInUse) {
                if (-not ([IO.Path]::GetFullPath($layout)).StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $layout).LinkType) {
                    throw 'Unexpected certification layout; cleanup refused.'
                }
                Remove-Item -LiteralPath $layout -Recurse -Force
            }
            if ($layoutInUse) { Write-Warning "Certification layout remains registered and was preserved: $layout" }
        } finally {
            if ($transcribing) { Stop-Transcript | Out-Null }
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-NexusCertification -Msix $Msix -Report $Report -Registered:$Registered }
