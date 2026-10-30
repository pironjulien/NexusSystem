# Invoked only by sandbox.ps1 inside a disposable Windows Sandbox; never run on the host.
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Sandbox guest identity required.' }
function Write-NexusPhase([string]$Phase) {
    [pscustomobject]@{Phase=$Phase;Time=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content C:\NexusOutput\phase.json -Encoding UTF8
    Write-Output $Phase
}
function Get-NexusPayloadDigest([string]$Path, [string]$Evidence) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    $lines = [Collections.Generic.List[string]]::new()
    $entries = @()
    try {
        foreach ($entry in @($archive.Entries | Sort-Object FullName)) {
            $stream = $entry.Open()
            try { $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $stream.Dispose() }
            $signatureMetadata = $entry.FullName -in 'AppxSignature.p7x','AppxBlockMap.xml','[Content_Types].xml','AppxMetadata/CodeIntegrity.cat'
            $entries += [pscustomobject]@{ Name=$entry.FullName; Length=$entry.Length; Sha256=$digest; SignatureMetadata=$signatureMetadata }
            if ($signatureMetadata) { continue }
            $lines.Add($entry.FullName + ':' + $digest)
        }
        $entries | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $Evidence -Encoding UTF8
        return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace('-','')
    } finally { $archive.Dispose(); $sha.Dispose() }
}
Start-Transcript -Path C:\NexusOutput\guest.log -Force | Out-Null
$result = [ordered]@{ Success=$false; Started=(Get-Date).ToString('o'); User=$env:USERNAME; OS=[string](Get-CimInstance Win32_OperatingSystem).BuildNumber }
$layout = Join-Path $env:TEMP ('nexussystem-smoke-' + [guid]::NewGuid().ToString('N'))
$identity = $null
$ownedRegistration = $null
try {
    Write-NexusPhase 'verify'
    $spec = Get-Content -Raw C:\NexusInput\run.json | ConvertFrom-Json
    $result.ApplicationControl = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -Name VerifiedAndReputablePolicyState -ErrorAction SilentlyContinue |
        Select-Object VerifiedAndReputablePolicyState
    $msix = 'C:\NexusInput\package.msix'
    $hash = (Get-FileHash -LiteralPath $msix -Algorithm SHA256).Hash
    if ($hash -ne $spec.Sha256) { throw 'Package hash changed.' }
    $result.Sha256 = $hash
    . C:\NexusInput\wack.ps1 -Msix $msix
    $identity = Get-WackIdentity $msix
    $result.Identity = $identity
    if ($identity.Version -ne $spec.Version) { throw 'Unexpected package version.' }
    if (@(Get-AppxPackage -Name $identity.Name).Count) { throw 'Guest is not a clean profile.' }
    if (@(Get-Process NexusSystem -ErrorAction SilentlyContinue).Count) { throw 'Unexpected existing process.' }
    if ($spec.TestSignInGuest) {
        # This is development trust in the disposable guest only. The original input and its hash stay unchanged.
        # No PFX is exported, no host certificate store is mapped, and no application-control policy is changed.
        Write-NexusPhase 'test-sign'
        $unsignedPackage = $msix
        $result.OriginalPayloadSha256 = Get-NexusPayloadDigest $unsignedPackage C:\NexusOutput\original-entries.json
        $msix = Join-Path $env:TEMP 'nexussystem-test-signed.msix'
        Copy-Item -LiteralPath $unsignedPackage -Destination $msix
        $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $identity.Publisher `
            -CertStoreLocation Cert:\CurrentUser\My -HashAlgorithm SHA256 -KeyAlgorithm RSA -KeyLength 3072 `
            -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddDays(1)
        $publicCertificate = Join-Path $env:TEMP 'nexussystem-test.cer'
        Export-Certificate -Cert $certificate -FilePath $publicCertificate | Out-Null
        Import-Certificate -FilePath $publicCertificate -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        Import-Certificate -FilePath $publicCertificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        & C:\NexusWackKit\signtool.exe sign /fd SHA256 /sha1 $certificate.Thumbprint $msix
        if ($LASTEXITCODE) { throw 'Guest development package signing failed.' }
        & C:\NexusWackKit\signtool.exe verify /pa /v $msix
        if ($LASTEXITCODE) { throw 'Guest development package signature verification failed.' }
        $result.TestSignedSha256 = (Get-FileHash -LiteralPath $msix -Algorithm SHA256).Hash
        $result.TestSignedPayloadSha256 = Get-NexusPayloadDigest $msix C:\NexusOutput\test-signed-entries.json
        if ($result.TestSignedPayloadSha256 -ne $result.OriginalPayloadSha256) { throw 'Signing changed the application payload.' }
        $result.DevelopmentSigner = $certificate.Thumbprint
    }
    Write-NexusPhase 'extract'
    $archive = [IO.Compression.ZipFile]::OpenRead($msix)
    try {
        $signedPackage = $null -ne $archive.GetEntry('AppxSignature.p7x')
        foreach ($runtime in 'coreclr.dll','hostfxr.dll','Microsoft.UI.Xaml.dll') {
            if (-not @($archive.Entries | Where-Object { $_.Name -eq $runtime }).Count) { throw "Self-contained runtime missing: $runtime" }
        }
    } finally { $archive.Dispose() }
    $result.Deployment = if ($signedPackage) { 'signed MSIX' } else { 'unsigned development layout' }
    if ($spec.CertificationOnly -and -not $signedPackage) { throw 'CertificationOnly refuses unsigned layouts; use a signed MSIX or TestSignInGuest.' }
    if (-not $spec.CertificationOnly) {
    Write-NexusPhase 'register'
    if ($signedPackage) {
        # Preserve the signature and block map so Windows evaluates the same trust chain as a real installation.
        Add-AppxPackage -Path $msix
    } else {
        New-Item -ItemType Directory -Path $layout | Out-Null
        [IO.Compression.ZipFile]::ExtractToDirectory($msix, $layout)
        Remove-Item -LiteralPath (Join-Path $layout 'AppxBlockMap.xml'), (Join-Path $layout '[Content_Types].xml') -Force -ErrorAction SilentlyContinue
        New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Force | Out-Null
        New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense -Value 1 -PropertyType DWord -Force | Out-Null
        Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    }
    $owned = @(Get-AppxPackage -Name $identity.Name | Where-Object {
        $_.Publisher -eq $identity.Publisher -and [string]$_.Version -eq $identity.Version -and ($signedPackage -or $_.InstallLocation -eq $layout)
    })
    if ($owned.Count -ne 1) { throw 'Exact registration not found.' }
    $ownedRegistration = $owned[0]
    $aumid = $owned[0].PackageFamilyName + '!App'
    Write-NexusPhase 'launch'
    # Use the activation contract's actual app PID. Explorer is only a shell launcher and its process does not prove
    # whether the packaged application started (or why activation failed).
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface INexusActivationManager {
    [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
}
[ComImport, Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c")]
class NexusActivationManager {}
public static class NexusActivate {
    public static uint App(string id) {
        var manager = (INexusActivationManager)new NexusActivationManager();
        uint pid;
        int hr = manager.ActivateApplication(id, "", 2, out pid);
        Marshal.ThrowExceptionForHR(hr);
        return pid;
    }
}
'@
    $activatedProcessId = [NexusActivate]::App($aumid)
    $result.ActivatedProcessId = $activatedProcessId
    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 250
        $app = Get-Process -Id $activatedProcessId -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }
    } until ($app -or (Get-Date) -gt $deadline)
    if (-not $app) { throw 'Packaged app did not open a native window.' }
    $result.ProcessId = $app.Id
    $result.WindowTitle = $app.MainWindowTitle
    $result.FileVersion = $app.MainModule.FileVersionInfo.FileVersion
    if ($result.FileVersion -ne $spec.Version) { throw 'Executable version differs from manifest.' }
    Write-NexusPhase 'uia'
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $window = [Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
    $elements = $window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
    $names = @($elements | ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -Unique)
    $result.UiNames = $names
    foreach ($product in 'NexusWindows','NexusAndroid','NexusBrowser','NexusKeypad','NexusRelay','NexusAGYmail') {
        if ($product -notin $names) { throw "Product missing from real UI: $product" }
    }
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    $bounds = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object Drawing.Bitmap $bounds.Width,$bounds.Height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($bounds.Location,[Drawing.Point]::Empty,$bounds.Size); $bitmap.Save('C:\NexusOutput\fresh-home.png',[Drawing.Imaging.ImageFormat]::Png) } finally { $graphics.Dispose(); $bitmap.Dispose() }
    Write-NexusPhase 'close'
    # Keep a process handle before exit so Get-Process can still report its real exit code afterwards.
    [void]$app.Handle
    [void]$app.CloseMainWindow()
    if (-not $app.WaitForExit(10000)) { $app.Kill(); $app.WaitForExit(); throw 'App did not close cleanly.' }
    $result.CloseExitCode = $app.ExitCode
    if ($null -eq $result.CloseExitCode -or $result.CloseExitCode -ne 0) { throw 'App exit code was unavailable or indicated an error.' }
    $appLogs = @(Get-ChildItem "$env:LOCALAPPDATA\Packages\$($owned[0].PackageFamilyName)\LocalCache\Local\NexusSystem\app.log" -ErrorAction SilentlyContinue)
    if (-not $appLogs.Count) { throw 'App startup log was not written.' }
    foreach ($log in $appLogs) {
        Copy-Item -LiteralPath $log.FullName -Destination C:\NexusOutput\app.log
        if ((Get-Content -Raw -LiteralPath $log.FullName) -match 'unhandled|unobserved') { throw 'App log contains a runtime failure.' }
    }
    Remove-AppxPackage -Package $owned[0].PackageFullName
    if (@(Get-AppxPackage -Name $identity.Name).Count) { throw 'Smoke registration was not removed.' }
    $result.NativeSmoke = 'PASS'
    } else { $result.NativeSmoke = 'not run (certification only)' }
    if ($spec.Certification) {
        Write-NexusPhase 'certification'
        $kit = "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit"
        New-Item -ItemType Directory -Path (Split-Path $kit) -Force | Out-Null
        Copy-Item -LiteralPath C:\NexusWackKit -Destination $kit -Recurse
        $taef = Join-Path (Split-Path $kit) 'Testing\Runtimes\TAEF'
        New-Item -ItemType Directory -Path (Split-Path $taef) -Force | Out-Null
        Copy-Item -LiteralPath C:\NexusTaef -Destination $taef -Recurse
        Copy-Item -LiteralPath C:\NexusSdkReferences -Destination (Join-Path (Split-Path $kit) 'References') -Recurse
        Get-WackDependency | ConvertTo-Json | Set-Content C:\NexusOutput\wack-dependencies.json -Encoding UTF8
        # The SDK installer normally registers this analyzer; merely copying its signed binaries does not.
        $registration = Start-Process -FilePath "$env:WINDIR\System32\regsvr32.exe" -ArgumentList @('/s', ('"' + (Join-Path $kit 'binaryinfo.dll') + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($registration.ExitCode -ne 0) { throw "WACK binary analyzer registration failed in the disposable guest (exit $($registration.ExitCode))." }
        $analyzer = Get-ItemProperty -LiteralPath 'Registry::HKEY_CLASSES_ROOT\CLSID\{65B4D3E5-5DAA-48F3-AAFC-43EEA6215244}\InprocServer32' -ErrorAction Stop
        if ($analyzer.'(default)' -ne (Join-Path $kit 'binaryinfo.dll')) { throw 'Unexpected WACK binary analyzer registration.' }
        Invoke-NexusCertification -Msix $msix -Report C:\NexusOutput\wack -Registered:(-not $signedPackage)
        $result.Certification = (Read-WackReport C:\NexusOutput\wack\wack-report.xml).OverallResult
        if ($result.Certification -ne 'PASS') { throw 'WACK did not pass.' }
        if (@(Get-AppxPackage -Name $identity.Name).Count) { throw 'Certification registration remains.' }
    }
    $result.Success = $true
} catch {
    $result.Error = $_ | Out-String
    $result.Error | Write-Output
    foreach ($logName in 'Application','Microsoft-Windows-AppModel-Runtime/Admin','Microsoft-Windows-TWinUI/Operational','Microsoft-Windows-CodeIntegrity/Operational') {
        try {
            $events = Get-WinEvent -FilterHashtable @{ LogName=$logName; StartTime=[datetime]$result.Started } -ErrorAction Stop |
                Select-Object TimeCreated,ProviderName,Id,LevelDisplayName,Message
            $events | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ('C:\NexusOutput\events-' + ($logName -replace '[/\\]','_') + '.json') -Encoding UTF8
        } catch { Write-Output "No diagnostic events from $logName : $_" }
    }
    if ($identity) {
        Get-ChildItem "$env:LOCALAPPDATA\Packages" -Recurse -Filter app.log -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like '*NexusSystem*' } |
            Select-Object -First 1 | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination C:\NexusOutput\app.log }
    }
} finally {
    try {
        if ($identity -and $ownedRegistration) {
            foreach ($left in @(Get-AppxPackage -Name $identity.Name | Where-Object {
                $_.PackageFullName -eq $ownedRegistration.PackageFullName -and $_.InstallLocation -eq $ownedRegistration.InstallLocation
            })) { Remove-AppxPackage -Package $left.PackageFullName }
        }
    } catch { $result.Success = $false; $result.CleanupError = $_ | Out-String }
    finally {
        $result.Finished = (Get-Date).ToString('o')
        $result | ConvertTo-Json -Depth 6 | Set-Content C:\NexusOutput\result.json.tmp -Encoding UTF8
        Move-Item -LiteralPath C:\NexusOutput\result.json.tmp -Destination C:\NexusOutput\result.json
        Stop-Transcript | Out-Null
        shutdown.exe /s /t 0
    }
}

