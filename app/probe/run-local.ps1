# The MSIX probe on this PC (Windows developer mode on): the package registered from its folder (no certificate),
# started by Windows like any installed app (no window), its writes read from outside the package, then the package
# removed and every marker it wrote erased, the erasure checked. It writes only entries named NexusProbe: no AI app
# configuration, no Logitech, no Chrome. Refused without -Markers (the decision to write those markers is the user's).
param([switch]$Markers)
$ErrorActionPreference = 'Stop'
if (-not $Markers) { throw 'run-local.ps1 writes NexusProbe markers in this user profile: run it with -Markers once agreed.' }
$root = $PSScriptRoot
$layout = Join-Path ([IO.Path]::GetTempPath()) "nexus-probe-layout-$PID"
$local = $env:LOCALAPPDATA; $roaming = $env:APPDATA; $home_ = $env:USERPROFILE

function Clear-Marker {
    foreach ($path in (Join-Path $local 'NexusProbe'), (Join-Path $roaming 'NexusProbe'), (Join-Path $home_ '.nexusprobe')) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    if (Test-Path 'HKCU:\Software\NexusProbe') { Remove-Item 'HKCU:\Software\NexusProbe' -Recurse -Force }
    # The value itself removed from HKCU\Environment (an empty value would leave the variable there).
    foreach ($tag in 'DIRECT', 'CHILD', 'BREAKAWAY', 'SCRIPT', 'SCRIPTAWAY') { Remove-ItemProperty 'HKCU:\Environment' -Name "NEXUSPROBE_$tag" -ErrorAction SilentlyContinue }
    # The task the probe created from a child of the package (no trigger), deleted in the real Task Scheduler.
    if (Get-ScheduledTask -TaskPath '\' -TaskName 'NexusProbe' -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskPath '\' -TaskName 'NexusProbe' -Confirm:$false }
}

function Get-Leftover {
    $left = @()
    foreach ($path in (Join-Path $local 'NexusProbe'), (Join-Path $roaming 'NexusProbe'), (Join-Path $home_ '.nexusprobe')) {
        if (Test-Path -LiteralPath $path) { $left += $path }
    }
    if (Test-Path 'HKCU:\Software\NexusProbe') { $left += 'HKCU:\Software\NexusProbe' }
    # Read in the registry, not through the environment API (which shows an empty value and no value alike).
    $environment = Get-Item 'HKCU:\Environment'
    foreach ($tag in 'DIRECT', 'CHILD', 'BREAKAWAY', 'SCRIPT', 'SCRIPTAWAY') { if ($environment.GetValueNames() -contains "NEXUSPROBE_$tag") { $left += "HKCU\Environment\NEXUSPROBE_$tag" } }
    if (Get-ScheduledTask -TaskPath '\' -TaskName 'NexusProbe' -ErrorAction SilentlyContinue) { $left += 'scheduled task \NexusProbe' }
    if (Get-AppxPackage -Name NexusProbe) { $left += 'package NexusProbe' }
    $store = @(Get-ChildItem (Join-Path $local 'Packages') -Directory -Filter 'NexusProbe_*' -ErrorAction SilentlyContinue)
    if ($store.Count) { $left += $store.FullName }  # the package's private store (its redirected writes)
    return , $left
}

Clear-Marker
try {
    New-Item -ItemType Directory -Force (Join-Path $layout 'Assets') | Out-Null
    dotnet publish (Join-Path $root 'Probe.csproj') -c Release -o $layout --nologo -v q
    if ($LASTEXITCODE) { throw 'dotnet publish failed' }
    Copy-Item (Join-Path $root 'Package\AppxManifest.xml') $layout
    Add-Type -AssemblyName System.Drawing
    foreach ($asset in @(@('StoreLogo', 50), @('Square150x150Logo', 150), @('Square44x44Logo', 44))) {
        $bitmap = New-Object System.Drawing.Bitmap $asset[1], $asset[1]
        [System.Drawing.Graphics]::FromImage($bitmap).Clear([System.Drawing.Color]::Gray)
        $bitmap.Save((Join-Path $layout "Assets\$($asset[0]).png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    # An existing file under AppData (the case of a configuration the app edits): created before the package runs.
    New-Item -ItemType Directory -Force (Join-Path $local 'NexusProbe') | Out-Null
    Set-Content (Join-Path $local 'NexusProbe\existing.txt') 'before'
    $package = Get-AppxPackage -Name NexusProbe
    Start-Process explorer.exe "shell:AppsFolder\$($package.PackageFamilyName)!Probe"
    $done = Join-Path $home_ '.nexusprobe\done.txt'
    $deadline = (Get-Date).AddMinutes(2)
    while (-not (Test-Path $done) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (-not (Test-Path $done)) { throw 'The probe did not finish.' }
    # The task run by the Task Scheduler writes ~\.nexusprobe\task-ran.txt (its action is the probe with --task-ran).
    $ran = Join-Path $home_ '.nexusprobe\task-ran.txt'
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path $ran) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    $task = Get-ScheduledTask -TaskPath '\' -TaskName 'NexusProbe' -ErrorAction SilentlyContinue
    if ($task) {
        $info = $task | Get-ScheduledTaskInfo
        Write-Host "scheduled task seen from outside: \NexusProbe, state $($task.State), triggers $(@($task.Triggers | Where-Object { $_ }).Count), action $($task.Actions[0].Execute) $($task.Actions[0].Arguments), last run $($info.LastRunTime), result 0x$('{0:X}' -f $info.LastTaskResult)"
    } else { Write-Host 'scheduled task seen from outside: none' }
    Write-Host "task created (child): $(Get-Content (Join-Path $home_ '.nexusprobe\task-created.txt') -Raw -ErrorAction SilentlyContinue)"
    Write-Host "task ran (task-ran.txt): $(Test-Path $ran)"
    $private = Join-Path $local "Packages\$($package.PackageFamilyName)\LocalCache"
    $existing = Get-Content (Join-Path $local 'NexusProbe\existing.txt') -Raw
    $key = Get-ItemProperty 'HKCU:\Software\NexusProbe' -ErrorAction SilentlyContinue
    $rows = foreach ($tag in 'direct', 'child', 'breakaway', 'script', 'scriptaway') {
        [PSCustomObject]@{
            writer           = $tag
            newLocalAppData  = Test-Path (Join-Path $local "NexusProbe\$tag.txt")
            newRoamingData   = Test-Path (Join-Path $roaming "NexusProbe\$tag.txt")
            existingAppData  = $existing -match $tag
            profileOutside   = Test-Path (Join-Path $home_ ".nexusprobe\$tag.txt")
            hkcu             = [bool]($key -and $key.$tag)
            hkcuRegExe       = [bool]($key -and $key."$tag-reg")
            userVariable     = [Environment]::GetEnvironmentVariable("NEXUSPROBE_$($tag.ToUpperInvariant())", 'User') -eq $tag
            redirectedCopy   = (Test-Path (Join-Path $private "Local\NexusProbe\$tag.txt")) -or (Test-Path (Join-Path $private "Roaming\NexusProbe\$tag.txt"))
        }
    }
    Write-Host "probe: $(Get-Content $done -Raw) at $(Get-Date -Format HH:mm:ss.fff)"
    Write-Host "existing.txt seen from outside: [$($existing -replace "`r?`n", ' | ')]"
    Write-Host 'package private store:'
    Get-ChildItem $private -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $($_.FullName.Substring($private.Length)) = $((Get-Content $_.FullName -Raw) -replace "`r?`n", ' | ')" }
    $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    $rows | ConvertTo-Json | Set-Content (Join-Path ([IO.Path]::GetTempPath()) 'nexus-probe-results.json')
} finally {
    Get-AppxPackage -Name NexusProbe | Remove-AppxPackage
    Start-Sleep -Seconds 3  # anything the package started is finished before the markers are erased
    Clear-Marker
    # A file the package modified under AppData comes back into the real folder a moment after the package is removed
    # (seen on 25/10/2026): erased again until it stays gone.
    for ($i = 0; $i -lt 10; $i++) { Start-Sleep -Seconds 1; Clear-Marker }
    Remove-Item -LiteralPath $layout -Recurse -Force -ErrorAction SilentlyContinue
    $left = Get-Leftover
    if ($left.Count) { Write-Host "LEFT BEHIND: $($left -join ', ')" } else { Write-Host 'Clean: package removed, every NexusProbe marker erased.' }
}
