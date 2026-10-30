# Builds the probe, packs it as MSIX, signs it with a throwaway certificate, installs it, runs it, then reads from
# outside the package which of its writes are really visible (what Claude, Codex, Antigravity and Chrome would see).
# CI only: trusting the throwaway certificate changes the machine's certificate store, acceptable on a disposable
# runner, never on a real PC.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingConvertToSecureStringWithPlainText', '', Justification = 'Random one-time PFX password of a throwaway CI certificate, never a secret.')]
param()
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'run-probe.ps1 runs on a disposable CI runner only.' }
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$layout = Join-Path $dist 'layout'
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $layout 'Assets') | Out-Null

dotnet publish (Join-Path $root 'Probe.csproj') -c Release -o $layout --nologo -v q
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
Copy-Item (Join-Path $root 'Package\AppxManifest.xml') $layout
Add-Type -AssemblyName System.Drawing
foreach ($asset in @(@('StoreLogo', 50), @('Square150x150Logo', 150), @('Square44x44Logo', 44))) {
    $bitmap = New-Object System.Drawing.Bitmap $asset[1], $asset[1]
    [System.Drawing.Graphics]::FromImage($bitmap).Clear([System.Drawing.Color]::MediumPurple)
    $bitmap.Save((Join-Path $layout "Assets\$($asset[0]).png"), [System.Drawing.Imaging.ImageFormat]::Png)
}

$kit = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'x64\makeappx.exe') } |
    Sort-Object Name -Descending | Select-Object -First 1
$msix = Join-Path $dist 'NexusProbe.msix'
& (Join-Path $kit.FullName 'x64\makeappx.exe') pack /d $layout /p $msix /o | Out-Null
if ($LASTEXITCODE) { throw 'makeappx failed' }
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=NexusProbeTest' -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddDays(2)
$pfx = Join-Path $dist 'probe.pfx'
$password = ConvertTo-SecureString -String ([guid]::NewGuid().ToString()) -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $password | Out-Null
Import-PfxCertificate -FilePath $pfx -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $password | Out-Null
& (Join-Path $kit.FullName 'x64\signtool.exe') sign /fd SHA256 /sha1 $cert.Thumbprint /s My $msix | Out-Null
if ($LASTEXITCODE) { throw 'signtool failed' }
Add-AppxPackage -Path $msix

$local = $env:LOCALAPPDATA; $roaming = $env:APPDATA; $home_ = $env:USERPROFILE
New-Item -ItemType Directory -Force (Join-Path $local 'NexusProbe') | Out-Null; Set-Content (Join-Path $local 'NexusProbe\existing.txt') 'before'
$package = Get-AppxPackage -Name NexusProbe
Start-Process explorer.exe "shell:AppsFolder\$($package.PackageFamilyName)!Probe"
$done = Join-Path $home_ '.nexusprobe\done.txt'
$deadline = (Get-Date).AddMinutes(3)
while (-not (Test-Path $done) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
if (-not (Test-Path $done)) { throw 'The probe did not finish (no done.txt).' }
Write-Host "probe: $(Get-Content $done -Raw)"

$private = Join-Path $local "Packages\$($package.PackageFamilyName)\LocalCache"
$existing = Get-Content (Join-Path $local 'NexusProbe\existing.txt') -Raw
$rows = foreach ($tag in 'direct', 'child', 'breakaway') {
    $key = Get-ItemProperty 'HKCU:\Software\NexusProbe' -ErrorAction SilentlyContinue
    [PSCustomObject]@{
        writer                   = $tag
        'new LocalAppData file'  = Test-Path (Join-Path $local "NexusProbe\$tag.txt")
        'new RoamingAppData file' = Test-Path (Join-Path $roaming "NexusProbe\$tag.txt")
        'existing AppData file'  = $existing -match $tag
        'profile, outside AppData' = Test-Path (Join-Path $home_ ".nexusprobe\$tag.txt")
        'HKCU key'               = [bool]($key -and $key.$tag)
        'user PATH-like variable' = [Environment]::GetEnvironmentVariable("NEXUSPROBE_$($tag.ToUpperInvariant())", 'User') -eq $tag
        'redirected copy found'  = (Test-Path (Join-Path $private "Local\NexusProbe\$tag.txt")) -or (Test-Path (Join-Path $private "Roaming\NexusProbe\$tag.txt"))
    }
}
$rows | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
$rows | ConvertTo-Json | Set-Content (Join-Path $dist 'probe-results.json')
"### MSIX probe (visible from outside the package)`n" + ($rows | ConvertTo-Csv -NoTypeInformation | Out-String) | Add-Content $env:GITHUB_STEP_SUMMARY
