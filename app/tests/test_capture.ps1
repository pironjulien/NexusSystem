# Output guards and cleanup of owned fixtures only: no app launch, screenshots or real profile mutation.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\tools\capture.ps1')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$fixture = Join-Path $temporaryRoot ('nexus-capture-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$project = Join-Path $fixture 'project'
$default = Join-Path $project 'dist\captures'
$custom = Join-Path $fixture 'custom'
New-Item -ItemType Directory -Path $default, $custom | Out-Null
$script:checks = 0
function Assert-CaptureRefused([string]$Path, [string]$Reason) {
    try { Resolve-NexusCaptureOutput $Path $project | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $Reason) { throw }
        $script:checks++
        return
    }
    throw "Unsafe capture output was accepted: $Path"
}
try {
    Assert-CaptureRefused ([IO.Path]::GetPathRoot($fixture)) 'drive root'
    Assert-CaptureRefused $project 'project or profile'
    Assert-CaptureRefused $env:USERPROFILE 'project or profile'
    Assert-CaptureRefused $env:LOCALAPPDATA 'project or profile'
    $sentinel = Join-Path $custom 'sentinel.txt'
    Set-Content -LiteralPath $sentinel -Value 'preserve'
    Assert-CaptureRefused $custom 'needs its own'
    $marker = Join-Path $custom '.nexus-captures.json'
    [pscustomobject]@{ kind='NexusApps.capture'; version=1; path=$default } | ConvertTo-Json | Set-Content -LiteralPath $marker
    Assert-CaptureRefused $custom 'does not match'
    [pscustomobject]@{ kind='NexusApps.capture'; version=1; path=$custom } | ConvertTo-Json | Set-Content -LiteralPath $marker
    Assert-CaptureRefused $custom 'foreign content'
    if ((Get-Content -Raw -LiteralPath $sentinel).Trim() -ne 'preserve') { throw 'Foreign content was modified.' }
    Remove-Item -LiteralPath $sentinel
    Set-Content -LiteralPath (Join-Path $custom 'home-fr-dark.png') -Value 'image fixture'
    $approved = Resolve-NexusCaptureOutput $custom $project
    if ($approved.Pictures.Count -ne 1 -or $approved.Path -ne $custom) { throw 'A marked output was not recognized.' }
    $script:checks++
    $prepared = Initialize-NexusCaptureOutput $custom $project
    if ($prepared -ne $custom -or (Test-Path -LiteralPath (Join-Path $custom 'home-fr-dark.png')) -or
        -not (Test-Path -LiteralPath $marker)) { throw 'Owned image cleanup failed.' }
    $script:checks++
    Set-Content -LiteralPath (Join-Path $default 'home-en-light.png') -Value 'image fixture'
    if ((Resolve-NexusCaptureOutput $default $project).Pictures.Count -ne 1) { throw 'The default output was not recognized.' }
    $script:checks++
    $junction = Join-Path $fixture 'junction'
    New-Item -ItemType Junction -Path $junction -Target $custom | Out-Null
    Assert-CaptureRefused $junction 'symbolic link or junction'
    Write-Host "Capture output: $script:checks guard scenarios passed; no native capture or user data accessed."
} finally {
    if (-not ([IO.Path]::GetFullPath($fixture)).StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        (Get-Item -LiteralPath $fixture).LinkType) { throw 'Unexpected capture-test fixture; cleanup refused.' }
    # Windows PowerShell 5.1's Remove-Item can throw on junctions. Delete the owned link itself without recursion.
    if ($junction -and (Test-Path -LiteralPath $junction)) { [IO.Directory]::Delete($junction) }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
