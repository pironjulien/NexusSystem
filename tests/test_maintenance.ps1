# Offline contracts of get-nexus.ps1's maintenance part: it installs the native engine of its own release, checked,
# registers the task that starts it directly, hands over from the PowerShell worker of releases up to 1.6.54, and
# reads the receipt. Functions are loaded from the actual release script; all mutation stays in a throwaway profile.
# No real task, network request, engine run or installed product is touched. The engine's own decisions are tested
# in app/tests/Nexus.Core.Tests (MaintainerTests).
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidOverwritingBuiltInCmdlets', '', Justification='Isolated network, process and Task Scheduler stubs never escape this offline test process.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification='Task constructors are inert test stubs; no task or host setting is changed.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '', Justification='The actual pack functions loaded from their AST consume these script-scope variables.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '', Justification='Stubs retain the native command parameter contract while exercising only relevant arguments.')]
param()
$ErrorActionPreference='Stop'
$pack=Join-Path (Split-Path $PSScriptRoot) 'get-nexus.ps1'
$ast=[Management.Automation.Language.Parser]::ParseFile($pack,[ref]$null,[ref]$null)
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$true)) {. ([scriptblock]::Create($definition.Extent.Text))}
$NexusMaintenanceVersion=([regex]::Match([IO.File]::ReadAllText($pack),"\`$NexusMaintenanceVersion = '(\d+\.\d+\.\d+)'")).Groups[1].Value
$original=@{}; foreach($key in @('USERPROFILE','LOCALAPPDATA','APPDATA','PROCESSOR_ARCHITECTURE','PROCESSOR_ARCHITEW6432')){$original[$key]=[Environment]::GetEnvironmentVariable($key,'Process')}
$work=Join-Path ([IO.Path]::GetTempPath()) "nexus-maintenance-test-$PID"
$EmitEvents=$false; $script:NexusPackScript=$pack
function Assert([bool]$Value,[string]$Message){if(-not $Value){throw $Message}}
New-Item -ItemType Directory -Force $work | Out-Null
try{
    $env:USERPROFILE=Join-Path $work 'profile'; $env:LOCALAPPDATA=Join-Path $env:USERPROFILE 'AppData\Local'; $env:APPDATA=Join-Path $env:USERPROFILE 'AppData\Roaming'
    New-Item -ItemType Directory -Force $env:LOCALAPPDATA,$env:APPDATA | Out-Null

    # The receipt is read-only, a stale receipt is never current, corrupt timestamps do not crash status readers.
    $path=Get-NexusMaintenanceStatusPath
    Assert ((Get-NexusMaintenanceStatus).state -eq 'pending' -and -not(Test-Path -LiteralPath $path)) 'Absent status caused a write.'
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path,'{"schema":1,"state":"current","checked_at":"invalid","products":[]}')
    $bytes=[IO.File]::ReadAllText($path)
    Assert ((Get-NexusMaintenanceStatus).state -eq 'stale') 'Invalid timestamp was accepted as fresh.'
    Assert ([IO.File]::ReadAllText($path) -ceq $bytes) 'Status read rewrote a historical receipt.'
    [IO.File]::WriteAllText($path,"{`"schema`":1,`"state`":`"failed`",`"checked_at`":`"$([DateTimeOffset]::UtcNow.ToString('O'))`",`"products`":[]}")
    Assert ((Get-NexusMaintenanceStatus).state -eq 'failed') 'A fresh ISO timestamp changed meaning under the current PowerShell culture.'
    Write-Host 'OK status separates stale historical receipts from live state'

    # The engine of this very release, for this PC's architecture, checked before it replaces anything.
    $env:PROCESSOR_ARCHITEW6432=$null; $env:PROCESSOR_ARCHITECTURE='AMD64'
    Assert ((Get-NexusMaintenanceEngineName) -eq 'NexusMaintenance-x64.exe') 'x64 engine name.'
    $env:PROCESSOR_ARCHITEW6432='ARM64'
    Assert ((Get-NexusMaintenanceEngineName) -eq 'NexusMaintenance-arm64.exe') 'An emulated PowerShell on ARM64 chose the x64 engine.'
    $env:PROCESSOR_ARCHITEW6432=$null
    $script:engineBytes='engine fixture'
    $script:requests=@()
    # gh, when installed on this PC, is never run on fixtures: its attestation step is recorded instead.
    $script:provenance=0
    function Invoke-Native {param($What,$Command) $script:provenance++; return 0}
    function Invoke-WebRequest {param($Uri,[switch]$UseBasicParsing,$OutFile,$TimeoutSec)
        $script:requests+=[string]$Uri
        if($OutFile){[IO.File]::WriteAllText($OutFile,$script:engineBytes);return}
        $hash=[BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes('engine fixture'))).Replace('-','').ToLowerInvariant()
        return [pscustomobject]@{Content="$hash  NexusMaintenance-x64.exe`n$('0'*64)  get-nexus.ps1`n"}
    }
    $engine=Join-Path (Get-NexusMaintenanceRoot) 'NexusMaintenance.exe'
    Save-NexusMaintenanceEngine $engine
    Assert ([IO.File]::ReadAllText($engine) -ceq 'engine fixture') 'The checked engine was not installed.'
    Assert (@($script:requests | Where-Object {$_ -like "*/releases/download/v$NexusMaintenanceVersion/*"}).Count -eq $script:requests.Count) 'Another release than this file''s own was used.'
    $before=$script:requests.Count
    Save-NexusMaintenanceEngine $engine
    Assert ($script:requests.Count -eq $before+1) 'An already installed engine was downloaded again.'
    Remove-Item -LiteralPath $engine
    $script:engineBytes='tampered'; $refused=$false
    try{Save-NexusMaintenanceEngine $engine}catch{$refused=$true}
    Assert ($refused -and -not(Test-Path -LiteralPath $engine) -and -not @(Get-ChildItem -LiteralPath (Split-Path $engine) -Filter '*.tmp').Count) 'A checksum mismatch left an engine behind.'
    $script:engineBytes='engine fixture'
    Write-Host 'OK the engine is this release''s own, checked, and replaced only when it differs'

    # Task registration is mocked: the engine is the action itself (no console host, no PowerShell, no policy flag).
    function New-ScheduledTaskAction {param($Execute,$Argument) [pscustomobject]@{Execute=$Execute;Arguments=$Argument}}
    function New-ScheduledTaskTrigger {param([switch]$AtLogOn,[switch]$Once,$At,$RepetitionInterval,$User) [pscustomobject]@{Logon=$AtLogOn.IsPresent;Interval=$RepetitionInterval;User=$User}}
    function New-ScheduledTaskSettingsSet {param([switch]$Hidden,[switch]$StartWhenAvailable,$MultipleInstances,$ExecutionTimeLimit,$RestartCount,$RestartInterval,[switch]$AllowStartIfOnBatteries,[switch]$DontStopIfGoingOnBatteries) [pscustomobject]@{Hidden=$Hidden.IsPresent;Enabled=$true;MultipleInstances=$MultipleInstances;RestartCount=$RestartCount}}
    function New-ScheduledTaskPrincipal {param($UserId,$LogonType,$RunLevel) [pscustomobject]@{UserId=$UserId;RunLevel=$RunLevel}}
    function Get-ScheduledTask {param($TaskName,$ErrorAction) return $null}
    function Register-ScheduledTask {param($TaskName,$Action,$Trigger,$Settings,$Principal,$Description,[switch]$Force) $script:task=@{Name=$TaskName;Action=$Action;Triggers=@($Trigger);Settings=$Settings;Principal=$Principal}; $script:registrations++}
    $script:maintenanceStarts=0; $script:registrations=0
    function Start-ScheduledTask {param($TaskName) Assert ($TaskName -eq 'NexusSystem Update') 'A bootstrap started another task.'; $script:maintenanceStarts++}
    $legacy=Join-Path (Get-NexusMaintenanceRoot) 'get-nexus.ps1'; [IO.File]::WriteAllText($legacy,'# PowerShell worker of 1.6.54')
    Install-NexusMaintenance
    Assert ($script:task.Name -eq 'NexusSystem Update' -and $script:task.Triggers.Count -eq 2 -and $script:task.Triggers[1].Interval.TotalHours -eq 1) 'Task authority/cadence differs.'
    Assert ($script:task.Triggers[0].User -eq $script:task.Principal.UserId) 'A per-user update task requested an all-users logon trigger.'
    Assert ($script:task.Action.Execute -eq $engine -and -not $script:task.Action.Arguments -and $script:task.Settings.MultipleInstances -eq 'IgnoreNew' -and $script:task.Settings.Hidden -and $script:task.Principal.RunLevel -eq 'Limited') 'The task does not start the engine directly, hidden, serial and least-privileged.'
    Assert (-not (Test-Path -LiteralPath $legacy)) 'The PowerShell worker of 1.6.54 stayed beside the engine.'
    Assert ($script:maintenanceStarts -eq 0) 'Normal pack installation unexpectedly started maintenance.'
    Install-NexusMaintenance -Start
    Assert ($script:maintenanceStarts -eq 1) 'An existing hub did not start the same maintenance engine.'
    function Get-ScheduledTask {param($TaskName,$ErrorAction) return [pscustomobject]@{Settings=[pscustomobject]@{Enabled=$false}}}
    Install-NexusMaintenance -Start
    Assert ($script:task.Settings.Enabled -eq $false) 'Task disabled by its owner was re-enabled.'
    Assert ($script:maintenanceStarts -eq 1) 'A disabled maintenance task was started by bootstrap.'
    Write-Host 'OK the single updater is the engine itself: hidden, hourly, resumes missed runs, respects a disabled task'

    # The author's workstation: an engine built from the repository, qualified with --current, never replaced.
    $development=Join-Path $work 'development'; New-Item -ItemType Directory -Force $development | Out-Null
    $built=Join-Path $development 'NexusMaintenance.exe'; [IO.File]::WriteAllText($built,'local build')
    $choices=Join-Path $env:USERPROFILE '.nexus\nexussystem.json'
    @{developmentCoordinator=$built}|ConvertTo-Json|Set-Content -LiteralPath $choices
    $publicBefore=[IO.File]::ReadAllText($engine); $before=$script:requests.Count
    Install-NexusMaintenance -Start
    Assert ($script:task.Action.Execute -eq $built -and $script:task.Action.Arguments -eq '--current') 'The development engine was not the task action.'
    Assert ($script:requests.Count -eq $before -and [IO.File]::ReadAllText($engine) -ceq $publicBefore) 'Development registration downloaded or replaced a public engine.'
    Assert (-not $script:task.Settings.Enabled -and $script:maintenanceStarts -eq 1) 'Development registration overrode task disablement or started it.'
    $previousAction=$script:task.Action
    foreach($invalid in @('relative\NexusMaintenance.exe',(Join-Path $development 'get-nexus.ps1'),(Join-Path $development 'missing\NexusMaintenance.exe'))){
        @{developmentCoordinator=$invalid}|ConvertTo-Json|Set-Content -LiteralPath $choices
        $refused=$false;try{Install-NexusMaintenance}catch{$refused=$true}
        Assert ($refused -and $script:task.Action -eq $previousAction -and $script:requests.Count -eq $before) "Invalid development selection '$invalid' fell back to the public engine or changed the task."
    }
    @{autoUpdate=$false;developmentCoordinator=$built}|ConvertTo-Json|Set-Content -LiteralPath $choices
    $registeredBefore=$script:registrations
    Install-NexusMaintenance -Start
    Assert ($script:registrations -eq $registeredBefore -and $script:maintenanceStarts -eq 1) 'Automatic-update opt-out registered or started a task.'
    Write-Host 'OK explicit repository engine survives registration; invalid selection and update opt-out never fall back'

    # -Maintain hands over: this release's engine runs the cycle as current, then the task is pointed at it.
    Remove-Item -LiteralPath $choices
    function Get-ScheduledTask {param($TaskName,$ErrorAction) return $null}
    $script:runs=@()
    function Start-Process {param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru)
        Assert ($Wait -and $PassThru) 'The engine verdict was not awaited.'
        $script:runs+=[pscustomobject]@{File=$FilePath;Arguments=@($ArgumentList);RegisteredBefore=$script:registrations}
        return [pscustomobject]@{ExitCode=2}
    }
    $registeredBefore=$script:registrations
    Assert ((Invoke-NexusMaintenance) -eq 2) 'The engine exit code was lost.'
    Assert ($script:runs.Count -eq 1 -and $script:runs[0].File -eq $engine -and ($script:runs[0].Arguments -join ' ') -eq '--current') 'The hand-over did not run this release''s engine as current.'
    Assert ($script:runs[0].RegisteredBefore -eq $registeredBefore -and $script:registrations -eq $registeredBefore+1 -and $script:task.Action.Execute -eq $engine) 'The task was not pointed at the engine after its run.'
    Assert ($script:maintenanceStarts -eq 1) 'The hand-over started a second run through the task.'
    $null=Invoke-NexusMaintenance -Force
    Assert (($script:runs[1].Arguments -join ' ') -eq '--current --force') '-ForceMaintenance did not reach the engine.'
    [IO.File]::WriteAllText($choices,'{"autoUpdate":false}')
    Assert ((Invoke-NexusMaintenance) -eq 0 -and $script:runs.Count -eq 2) 'Automatic-update opt-out ran the engine.'
    Remove-Item -LiteralPath $choices
    Write-Host 'OK -Maintain hands over to the engine, then registers it'

    # -MaintenanceFrom (the pack check): the engine comes from that folder, checked, and the task names the release
    # being checked (local catalogue and engine folder), never the published one.
    $Catalog=Join-Path $work 'catalog with space'; $MaintenanceFrom=Join-Path $work 'engines'
    New-Item -ItemType Directory -Force $Catalog,$MaintenanceFrom | Out-Null
    [IO.File]::WriteAllText((Join-Path $MaintenanceFrom 'NexusMaintenance-x64.exe'),'engine fixture')
    $hash=(Get-FileHash -LiteralPath (Join-Path $MaintenanceFrom 'NexusMaintenance-x64.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $MaintenanceFrom 'SHA256SUMS.txt'),"$hash  NexusMaintenance-x64.exe`n")
    Remove-Item -LiteralPath $engine; $before=$script:requests.Count
    Install-NexusMaintenance
    Assert ($script:requests.Count -eq $before -and [IO.File]::ReadAllText($engine) -ceq 'engine fixture') 'The test engine was not taken, checked, from -MaintenanceFrom.'
    Assert ($script:task.Action.Execute -eq $engine -and $script:task.Action.Arguments -ceq "--test-catalog `"$Catalog`" --test-engines `"$MaintenanceFrom`"") 'The task does not name the release being checked.'
    $MaintenanceFrom=''; $Catalog='pironjulien/NexusSystem'
    Install-NexusMaintenance
    Assert (-not $script:task.Action.Arguments) 'A public installation kept the test release in its task.'
    Write-Host 'OK -MaintenanceFrom registers the release being checked, by its folders'

    # The hand-over contract the 1.6.54 worker checks on a published file before running it.
    $names=@($ast.ParamBlock.Parameters | ForEach-Object {$_.Name.VariablePath.UserPath})
    Assert ($names -contains 'Maintain' -and $names -contains 'MaintenanceCurrent' -and $ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-NexusMaintenance'},$true)) 'The 1.6.54 worker would refuse to hand over to this file.'
    Write-Host 'OK maintenance contract'
}finally{
    foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key],'Process')}
    # This test owns only its checked absolute temporary profile.
    if([IO.Path]::GetFullPath($work).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue}
}
