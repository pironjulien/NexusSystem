# Offline maintenance contracts. Functions are loaded from the actual release script; all mutation stays in a
# throwaway profile. No real task, network request, AI process, or installed product is touched.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidOverwritingBuiltInCmdlets', '', Justification='Isolated network and Task Scheduler stubs never escape this offline test process.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification='Task constructors are inert test stubs; no task or host setting is changed.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '', Justification='The actual pack functions loaded from their AST consume these script-scope variables.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '', Justification='Stubs retain the native command parameter contract while exercising only relevant arguments.')]
param()
$ErrorActionPreference='Stop'
$pack=Join-Path (Split-Path $PSScriptRoot) 'get-nexus.ps1'
$ast=[Management.Automation.Language.Parser]::ParseFile($pack,[ref]$null,[ref]$null)
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$true)) {. ([scriptblock]::Create($definition.Extent.Text))}
$original=@{}; foreach($key in @('USERPROFILE','LOCALAPPDATA','APPDATA','CODEX_HOME','NEXUS_PACK','NEXUS_PACK_TARGETS','NEXUS_MCP_TOKEN')){$original[$key]=[Environment]::GetEnvironmentVariable($key,'Process')}
$work=Join-Path ([IO.Path]::GetTempPath()) "nexus-maintenance-test-$PID"
$Marketplace='nexus-plugins'; $EmitEvents=$false; $script:NexusPackScript=$pack
$AiNames=[ordered]@{'claude-code'='Claude Code';'claude-desktop'='Claude Desktop';'codex'='Codex';'antigravity'='Antigravity'}
$windows=@{Key='windows';Name='NexusWindows';Repo='NexusWindows';Plugin='nexus-windows';Server='nexus-windows';Agy='nexus-windows';Targets=@('codex')}
$browser=@{Key='browser';Name='NexusBrowser';Repo='NexusBrowser';Plugin='nexus-browser';Server='nexus-browser';Agy='nexus-browser';Targets=@('codex')}
$Products=@($windows,$browser)
$Checks=@{'codex'=${function:Get-CodexState};'claude-code'=${function:Get-ClaudeCodeState};'claude-desktop'=${function:Get-ClaudeDesktopState};'antigravity'=${function:Get-AntigravityState}}
function Assert([bool]$Value,[string]$Message){if(-not $Value){throw $Message}}
function Profile([string]$Name,[string]$Config=''){
    $env:USERPROFILE=Join-Path $work $Name; $env:LOCALAPPDATA=Join-Path $env:USERPROFILE 'AppData\Local'; $env:APPDATA=Join-Path $env:USERPROFILE 'AppData\Roaming'; $env:CODEX_HOME=Join-Path $env:USERPROFILE '.codex'
    New-Item -ItemType Directory -Force $env:CODEX_HOME,$env:LOCALAPPDATA,$env:APPDATA | Out-Null
    [IO.File]::WriteAllText((Join-Path $env:CODEX_HOME 'config.toml'),$Config)
}
New-Item -ItemType Directory -Force $work | Out-Null
try{
    Profile 'disabled' "[plugins.`"nexus-windows@nexus-plugins`"]`nenabled=false"
    $before=[IO.File]::ReadAllText((Join-Path $env:CODEX_HOME 'config.toml'))
    Assert ((Get-NexusMaintenanceProfile $windows 'codex').state -eq 'disabled') 'Disabled plugin was not preserved.'
    Assert ([IO.File]::ReadAllText((Join-Path $env:CODEX_HOME 'config.toml')) -ceq $before) 'Inspection wrote the user configuration.'
    Profile 'custom' "[mcp_servers.nexus-windows]`ncommand='powershell.exe'`nargs=['custom.ps1']"
    Assert ((Get-NexusMaintenanceProfile $windows 'codex').route -eq 'custom') 'Custom direct route was treated as owned.'
    Profile 'other-marketplace' "[plugins.`"nexus-windows@personal`"]`nenabled=true"
    Assert ((Get-NexusMaintenanceProfile $windows 'codex').state -eq 'kept') 'Another marketplace was taken over.'
    Profile 'owned' "[plugins.`"nexus-windows@nexus-plugins`"]`nenabled=true"
    Assert ((Get-NexusMaintenanceProfile $windows 'codex').route -eq 'catalogue') 'Public active catalogue was missed.'
    Assert (-not(Test-NexusOwnedJsonRoute $browser ([pscustomobject]@{url='http://127.0.0.1:16182/mcp?agent=codex&custom=1'}) 'codex')) 'Custom HTTP route was taken over.'
    Assert (-not(Test-NexusOwnedJsonRoute $windows ([pscustomobject]@{command=(Join-Path $env:USERPROFILE '.nexus\nexus-windows\NexusWindows.exe');args=@('--mcp','--agent','codex');env=@{CUSTOM='yes'}}) 'codex')) 'Custom environment was taken over.'
    Write-Host 'OK maintenance preserves disabled, absent, custom and other-catalogue routes'

    # Status is read-only, stale receipt never current, corrupt timestamps do not crash status readers.
    $path=Get-NexusMaintenanceStatusPath
    Assert ((Get-NexusMaintenanceStatus).state -eq 'pending' -and -not(Test-Path -LiteralPath $path)) 'Absent status caused a write.'
    Write-NexusMaintenanceStatus @{schema=1;state='current';checked_at='invalid';products=@()}
    $bytes=[IO.File]::ReadAllText($path)
    Assert ((Get-NexusMaintenanceStatus).state -eq 'stale') 'Invalid timestamp was accepted as fresh.'
    Assert ([IO.File]::ReadAllText($path) -ceq $bytes) 'Status read rewrote a historical receipt.'
    Write-NexusMaintenanceStatus @{schema=1;state='failed';checked_at=[DateTimeOffset]::UtcNow.ToString('O');products=@()}
    Assert ((Get-NexusMaintenanceStatus).state -eq 'failed') 'A fresh ISO timestamp changed meaning under the current PowerShell culture.'
    Write-Host 'OK status separates stale historical receipts from live state'

    # Installation record is checked against bytes, not just its version label.
    $native=Join-Path $env:USERPROFILE ('.nexus\nexus-windows\native\'+('a'*64)); New-Item -ItemType Directory -Force $native|Out-Null
    $artifacts=@{}
    foreach($name in @('NexusWindows.exe','NexusWindows.dll','NexusWindows.deps.json','NexusWindows.runtimeconfig.json')){
        [IO.File]::WriteAllText((Join-Path $native $name),"fixture $name")
        $artifacts[$name]=(Get-FileHash -LiteralPath (Join-Path $native $name) -Algorithm SHA256).Hash
    }
    $record=@{Schema=2;Version='3.9.4';SourceRoot='release v3.9.4';NativeHash=('a'*64);NativeExecutable=(Join-Path $native 'NexusWindows.exe');NativeArtifacts=$artifacts}
    $record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $env:USERPROFILE '.nexus\nexus-windows\installation.json')
    Assert ((Get-NexusInstalledVersion $windows) -eq '3.9.4') 'Valid installation rejected.'
    [IO.File]::WriteAllText((Join-Path $native 'NexusWindows.dll'),'modified')
    Assert (-not(Get-NexusInstalledVersion $windows)) 'Modified assembly was reported as installed and verified.'
    Write-Host 'OK installed version requires matching manifest bytes'

    # Provenance is the installer's existing receipt. No checkout lookup, git pull or rebuild is needed to keep it.
    $windowsRecord=Join-Path $env:USERPROFILE '.nexus\nexus-windows\installation.json'
    Assert ((Get-NexusInstallationSource $windows) -eq 'release') 'Public Windows provenance was missed.'
    $record.SourceRoot=Join-Path $work 'moved-repository\NexusWindows'
    $record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $windowsRecord
    Assert ((Get-NexusInstallationSource $windows) -eq 'repository') 'Windows repository provenance was lost when its source directory was absent.'
    $record.SourceRoot='unrecognised provenance';$record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $windowsRecord
    Assert ((Get-NexusInstallationSource $windows) -eq 'unknown') 'Unproved Windows provenance was accepted as public.'
    $record.SourceRoot='release v3.9.4';$record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $windowsRecord
    $browserNative=Join-Path $env:LOCALAPPDATA ('NexusBrowser\versions\'+('b'*16));New-Item -ItemType Directory -Force $browserNative|Out-Null
    $browserCurrent=Join-Path $env:LOCALAPPDATA 'NexusBrowser\current.json'
    @{version='2.33.4';exe=(Join-Path $browserNative 'NexusBrowser.exe');fingerprint=('b'*64)}|ConvertTo-Json|Set-Content -LiteralPath $browserCurrent
    $browserRecord=Join-Path $browserNative 'installation.json'
    @{repository_root=(Join-Path $work 'moved-repository\NexusBrowser')}|ConvertTo-Json|Set-Content -LiteralPath $browserRecord
    Assert ((Get-NexusInstallationSource $browser) -eq 'repository') 'Browser repository provenance was lost when its source directory was absent.'
    $publicBrowser=Join-Path $env:LOCALAPPDATA 'NexusBrowser\app'
    @{repository_root=$publicBrowser}|ConvertTo-Json|Set-Content -LiteralPath $browserRecord
    Assert ((Get-NexusInstallationSource $browser) -eq 'release') 'Public Browser payload provenance was missed.'
    New-Item -ItemType Directory -Force $publicBrowser|Out-Null
    [IO.File]::WriteAllText((Join-Path $publicBrowser '.git'),'gitdir: a checkout marker')
    Assert ((Get-NexusInstallationSource $browser) -eq 'repository') 'A checkout in the public payload location was treated as a release.'
    Remove-Item -LiteralPath (Join-Path $publicBrowser '.git')
    Remove-Item -LiteralPath $browserRecord
    Assert ((Get-NexusInstallationSource $browser) -eq 'unknown') 'Missing Browser provenance was accepted as public.'
    Write-Host 'OK repository provenance survives moved sources; unknown origins never imply public ownership'

    # A receipt counts only for the live process birth, loaded assembly and immediate real host chain.
    $birth=[DateTime]::UtcNow
    $engine=Join-Path $native 'NexusWindows.exe';$assembly=Join-Path $native 'NexusWindows.dll'
    $receiptPath=Join-Path $env:USERPROFILE ".nexus\status\runtime\windows-codex-800-$($birth.Ticks)-session.json"
    New-Item -ItemType Directory -Force (Split-Path $receiptPath)|Out-Null
    $receipt=@{schema=1;product='windows';pid=800;agent='codex';state='idle';processStartUtc=$birth.ToString('O');processStartTicks=$birth.Ticks.ToString();executable=$engine;assembly=$assembly;assemblySha256=(Get-FileHash -LiteralPath $assembly).Hash.ToLowerInvariant();version='3.9.5'}
    $record.NativeArtifacts['NexusWindows.dll']=$receipt.assemblySha256
    $record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $env:USERPROFILE '.nexus\nexus-windows\installation.json')
    $receipt|ConvertTo-Json|Set-Content -LiteralPath $receiptPath
    $script:processFixture=@(
        [pscustomobject]@{ProcessId=800;ParentProcessId=801;Name='NexusWindows.exe';CommandLine="`"$engine`" --mcp --agent codex";ExecutablePath=$engine;CreationDate=$birth},
        [pscustomobject]@{ProcessId=801;ParentProcessId=802;Name='NexusWindows.exe';CommandLine='NexusWindows.exe --mcp --agent codex';ExecutablePath=(Join-Path $env:USERPROFILE '.nexus\nexus-windows\NexusWindows.exe');CreationDate=$birth},
        [pscustomobject]@{ProcessId=802;ParentProcessId=1;Name='codex.exe';CommandLine='codex.exe app-server';ExecutablePath='C:\signed\codex.exe';CreationDate=$birth}
    )
    function Get-CimInstance {param($ClassName,$Property,$ErrorAction) return $script:processFixture}
    $live=Get-NexusLoadedVersion $windows
    Assert ($live.versions -contains '3.9.5' -and $live.agents[0].agent -eq 'codex' -and -not $live.unknown) 'A valid host-owned runtime was missed.'
    $script:processFixture[0].CreationDate=$birth.AddSeconds(1)
    $live=Get-NexusLoadedVersion $windows
    Assert (-not $live.versions.Count -and $live.unknown) 'A receipt from a reused PID was accepted.'
    Assert ($live.agents.Count -eq 1 -and $null -eq $live.agents[0].version) 'An unattested connection disappeared from its AI profile verdict.'
    $script:processFixture[0].CreationDate=$birth
    $script:processFixture[1].Name='powershell.exe';$script:processFixture[1].ExecutablePath='C:\Windows\powershell.exe'
    $live=Get-NexusLoadedVersion $windows
    Assert (-not $live.versions.Count) 'A qualification client under an AI shell was counted as an AI connection.'
    $script:processFixture[1].Name='NexusWindows.exe';$script:processFixture[1].ExecutablePath=(Join-Path $env:USERPROFILE '.nexus\nexus-windows\NexusWindows.exe')
    [IO.File]::WriteAllText($assembly,'changed after receipt')
    $live=Get-NexusLoadedVersion $windows
    Assert (-not $live.versions.Count -and $live.unknown) 'Receipt with a different loaded assembly hash was accepted.'
    Remove-Item Function:Get-CimInstance
    Write-Host 'OK loaded runtime requires real host ancestry, PID birth and matching assembly bytes'

    # A checksum mismatch refuses the file, and never calls an installer.
    function Invoke-WebRequest {param($Uri,[switch]$UseBasicParsing,$OutFile,$TimeoutSec) [IO.File]::WriteAllText($OutFile,'wrong bytes')}
    $bad=Join-Path $work 'bad.ps1'; $refused=$false
    try{Save-VerifiedFile 'https://example.invalid/get.ps1' $bad ('0'*64)}catch{$refused=$true}
    Assert ($refused -and -not(Test-Path -LiteralPath $bad)) 'Checksum mismatch was not rejected.'
    Remove-Item Function:Invoke-WebRequest
    Write-Host 'OK bad checksum is refused before execution'

    # Static parameter support is checked on the verified file itself, never inferred only from a version number.
    $legacy=Join-Path $work 'legacy-installer.ps1';[IO.File]::WriteAllText($legacy,'param([switch]$Register)')
    $refused=$false;try{Assert-NexusUpdateContract $legacy @('UpdateOnly')}catch{$refused=$true}
    Assert $refused 'An installer without UpdateOnly was accepted.'
    [IO.File]::WriteAllText($legacy,'param([switch]$UpdateOnly,[switch]$RefreshExistingProfiles)')
    Assert-NexusUpdateContract $legacy @('UpdateOnly','RefreshExistingProfiles')
    $override=$env:NEXUS_WINDOWS_SOURCE
    try{$env:NEXUS_WINDOWS_SOURCE='C:\development';$refused=$false;try{Assert-NexusOfficialUpdateEnvironment 'windows'}catch{$refused=$true};Assert $refused 'A development source override was accepted.'}
    finally{if($null -eq $override){Remove-Item Env:NEXUS_WINDOWS_SOURCE -ErrorAction SilentlyContinue}else{$env:NEXUS_WINDOWS_SOURCE=$override}}
    Write-Host 'OK unsupported installer and development overrides are refused'

    # Task registration is mocked. The exact durable copy and one authority have logon + hourly triggers.
    function New-ScheduledTaskAction {param($Execute,$Argument) [pscustomobject]@{Execute=$Execute;Arguments=$Argument}}
    function New-ScheduledTaskTrigger {param([switch]$AtLogOn,[switch]$Once,$At,$RepetitionInterval,$User) [pscustomobject]@{Logon=$AtLogOn.IsPresent;Interval=$RepetitionInterval;User=$User}}
    function New-ScheduledTaskSettingsSet {param([switch]$Hidden,[switch]$StartWhenAvailable,$MultipleInstances,$ExecutionTimeLimit,$RestartCount,$RestartInterval,[switch]$AllowStartIfOnBatteries,[switch]$DontStopIfGoingOnBatteries) [pscustomobject]@{Hidden=$Hidden.IsPresent;Enabled=$true;MultipleInstances=$MultipleInstances;RestartCount=$RestartCount}}
    function New-ScheduledTaskPrincipal {param($UserId,$LogonType,$RunLevel) [pscustomobject]@{UserId=$UserId;RunLevel=$RunLevel}}
    function Get-ScheduledTask {param($TaskName,$ErrorAction) return $null}
    function Register-ScheduledTask {param($TaskName,$Action,$Trigger,$Settings,$Principal,$Description,[switch]$Force) $script:task=@{Name=$TaskName;Action=$Action;Triggers=@($Trigger);Settings=$Settings;Principal=$Principal}}
    $script:maintenanceStarts=0
    function Start-ScheduledTask {param($TaskName) Assert ($TaskName -eq 'NexusSystem Update') 'A bootstrap started another task.'; $script:maintenanceStarts++}
    Install-NexusMaintenance $pack
    Assert ($script:task.Name -eq 'NexusSystem Update' -and $script:task.Triggers.Count -eq 2 -and $script:task.Triggers[1].Interval.TotalHours -eq 1) 'Task authority/cadence differs.'
    Assert ($script:task.Triggers[0].User -eq $script:task.Principal.UserId) 'A per-user update task requested an all-users logon trigger.'
    Assert ($script:task.Action.Arguments -match '-WindowStyle Hidden.*-Maintain' -and $script:task.Settings.MultipleInstances -eq 'IgnoreNew' -and $script:task.Principal.RunLevel -eq 'Limited') 'Task is not hidden, serial and least-privileged.'
    Assert ((Get-FileHash -LiteralPath (Join-Path (Get-NexusMaintenanceRoot) 'get-nexus.ps1')).Hash -eq (Get-FileHash -LiteralPath $pack).Hash) 'Durable copy differs.'
    Assert ($script:maintenanceStarts -eq 0) 'Normal pack installation unexpectedly started maintenance.'
    Install-NexusMaintenance $pack -Start
    Assert ($script:maintenanceStarts -eq 1) 'An existing hub did not start the same maintenance worker.'
    function Get-ScheduledTask {param($TaskName,$ErrorAction) return [pscustomobject]@{Settings=[pscustomobject]@{Enabled=$false}}}
    Install-NexusMaintenance $pack -Start
    Assert ($script:task.Settings.Enabled -eq $false) 'Task disabled by its owner was re-enabled.'
    Assert ($script:maintenanceStarts -eq 1) 'A disabled maintenance task was started by bootstrap.'
    Write-Host 'OK durable single updater is hidden, hourly and resumes missed runs'

    # The explicit development choice survives future bootstrap calls without another task or public copy.
    $coordinatorFolder=Join-Path $work 'development NexusSystem';New-Item -ItemType Directory -Force $coordinatorFolder|Out-Null
    [IO.File]::WriteAllText((Join-Path $coordinatorFolder '.git'),'gitdir: local-worktree-metadata')
    $coordinator=Join-Path $coordinatorFolder 'get-nexus.ps1';Copy-Item -LiteralPath $pack -Destination $coordinator
    $choices=Join-Path $env:USERPROFILE '.nexus\nexussystem.json'
    @{developmentCoordinator=$coordinator}|ConvertTo-Json|Set-Content -LiteralPath $choices
    $durable=Join-Path (Get-NexusMaintenanceRoot) 'get-nexus.ps1';$publicBefore=[IO.File]::ReadAllText($durable)
    Install-NexusMaintenance $pack -Start
    Assert ($script:task.Action.Arguments -like ('*-File "'+$coordinator+'" -Maintain -MaintenanceCurrent') -and $script:task.Name -eq 'NexusSystem Update') 'Explicit development coordinator was not kept as the existing task action.'
    Assert (-not $script:task.Settings.Enabled -and $script:maintenanceStarts -eq 1 -and $script:task.Triggers[0].User -eq $script:task.Principal.UserId) 'Development bootstrap overrode task disablement, started it, or lost the per-user trigger.'
    Assert ([IO.File]::ReadAllText($durable) -ceq $publicBefore) 'Development bootstrap copied a public manager over the existing durable payload.'
    Remove-Item -LiteralPath (Join-Path $coordinatorFolder '.git')
    $previousArguments=$script:task.Action.Arguments;$refused=$false
    try{Install-NexusMaintenance $pack}catch{$refused=$true}
    Assert ($refused -and $script:task.Action.Arguments -ceq $previousArguments -and [IO.File]::ReadAllText($durable) -ceq $publicBefore) 'Invalid development selection fell back to the public manager or changed the task.'
    @{developmentCoordinator='relative\get-nexus.ps1'}|ConvertTo-Json|Set-Content -LiteralPath $choices
    $refused=$false;try{Install-NexusMaintenance $pack}catch{$refused=$true}
    Assert $refused 'Relative development coordinator was accepted.'
    @{autoUpdate=$false;developmentCoordinator=$coordinator}|ConvertTo-Json|Set-Content -LiteralPath $choices
    Install-NexusMaintenance $pack -Start
    Assert ($script:task.Action.Arguments -ceq $previousArguments -and $script:maintenanceStarts -eq 1) 'Automatic-update opt-out changed or started the development task.'
    Remove-Item -LiteralPath $choices
    Write-Host 'OK explicit repository coordinator survives bootstrap; invalid selection and update opt-out never fall back'

    # Real orchestration with inert release and product implementations. A new connection is never fabricated.
    function Get-NexusMaintenanceToken {return 'fixture credential'}
    Assert (Test-NexusOwnedJsonRoute $browser ([pscustomobject]@{serverUrl='http://127.0.0.1:16182/mcp?agent=antigravity';headers=[pscustomobject]@{Authorization='Bearer fixture credential'}}) 'antigravity') 'Route inspection did not use the same current user credential as maintenance.'
    function Find-AiApp {return ,@('codex','claude-code')}
    function Get-LatestTag([string]$Repo){if($script:networkFailure){throw 'network unavailable'};if($Repo -eq 'NexusSystem'){return 'v1.6.35'};if($Repo -eq 'NexusWindows'){return 'v3.9.5'}return 'v2.33.5'}
    function Assert-NexusUpdateContract([string]$File,[string[]]$Parameters){Assert ($Parameters -contains 'UpdateOnly') 'No update-only contract was checked.'}
    function Get-NexusPublishedInstaller([string]$Repo,[string]$Work,[string]$Name='get.ps1'){
        $script:downloads+= $Repo
        if($script:networkFailure){throw 'network unavailable'}
        $tag=if($Repo -eq 'NexusSystem'){'v1.6.36'}else{Get-LatestTag $Repo}
        return [pscustomobject]@{file=$pack;tag=$tag;version=$tag.Substring(1);sha256=(Get-FileHash -LiteralPath $pack).Hash}
    }
    function Get-NexusInstalledVersion($Product){if($Product.Key -eq 'windows'){return $script:installed};if($Product.Key -eq 'browser'){return $script:browserInstalled};return $null}
    function Invoke-ScriptFile([string]$What,[string]$File,[string[]]$Arguments=@()){
        Assert (($Arguments -join ',') -eq '-UpdateOnly') 'Maintenance used a broad installer.'
        Assert ($env:NEXUS_PACK_TARGETS -eq 'codex') 'Maintenance widened the active profile selection.'
        Assert ($env:NEXUS_MCP_TOKEN -ceq 'fixture credential') 'The installer did not inherit the same current credential used by route inspection.'
        if($script:installerThrows){throw 'fixture installer exception'}
        $script:installs++; if($script:installerFailure){return 1}; $script:installed='3.9.5'; return 0
    }
    function Invoke-NexusPluginMaintenance($Product,$Profiles){$script:pluginRefresh++;Assert ($Profiles.Count -eq 1 -and $Profiles[0].key -eq 'codex') 'Plugin refresh widened selection.'; $Profiles[0].pluginVersion=$script:pluginVersion}
    function Get-NexusLoadedVersion($Product){return [pscustomobject]@{versions=@($script:loaded);unknown=$false;agents=@([pscustomobject]@{agent='codex';version=$script:loaded})}}
    $script:pluginVersion='3.9.5';$script:installed='3.9.4';$script:browserInstalled=$null;$script:loaded='3.9.4';$script:installs=0;$script:pluginRefresh=0;$script:downloads=@();$script:networkFailure=$false;$script:installerFailure=$false;$script:installerThrows=$false
    $env:NEXUS_MCP_TOKEN='inherited-before-installation'
    Assert ((Invoke-NexusMaintenance -Current) -eq 2) 'Old loaded connection was falsely reported current.'
    Assert ($env:NEXUS_MCP_TOKEN -ceq 'inherited-before-installation') 'Successful maintenance did not restore its caller environment.'
    $status=Get-NexusMaintenanceStatus
    Assert ($status.products.Count -eq 1 -and $status.products[0].installedVersion -eq '3.9.5' -and $status.products[0].loadedVersions[0] -eq '3.9.4') 'Published/installed/loaded were conflated.'
    Assert ($script:installs -eq 1 -and $script:pluginRefresh -eq 1) 'Expected update did not run once.'
    Assert ($script:downloads -notcontains 'NexusSystem') 'Qualification tried to download/execute the older public manager.'
    Assert ((Invoke-NexusMaintenance -Current) -eq 2 -and $script:installs -eq 1) 'Retry reinstalled an already installed version.'
    $script:loaded='3.9.5'
    Assert ((Invoke-NexusMaintenance -Current) -eq 0) 'Attested matching runtime was not current.'
    $status=Get-NexusMaintenanceStatus
    Assert ($status.products[0].profiles[0].state -eq 'runtime-verified' -and $null -eq $status.products[0].profiles[0].guideLoaded) 'Runtime proof was promoted into guide-loading proof.'
    $savedStatus=[IO.File]::ReadAllText((Get-NexusMaintenanceStatusPath));$beforeDownloads=$script:downloads.Count
    Assert ((Invoke-NexusMaintenance) -eq 0 -and $script:downloads.Count -eq $beforeDownloads) 'Cooldown performed another network check.'
    Assert ([IO.File]::ReadAllText((Get-NexusMaintenanceStatusPath)) -ceq $savedStatus) 'Cooldown renewed an unobserved receipt.'
    $script:pluginVersion='3.9.4'
    Assert ((Invoke-NexusMaintenance -Current) -eq 2) 'An older catalogue plugin was reported distributed.'
    $lag=Get-NexusMaintenanceStatus
    Assert ($lag.products[0].profiles[0].reason -like '*distribution remains pending*') 'Catalogue distribution lag was not explained.'
    $script:pluginVersion='3.9.5'
    $choices=Join-Path $env:USERPROFILE '.nexus\nexussystem.json';[IO.File]::WriteAllText($choices,'{"skip":["windows","claude-code"]}')
    Assert ((Invoke-NexusMaintenance -Current) -eq 0 -and (Get-NexusMaintenanceStatus).products.Count -eq 0) 'Multiple local skip choices were ignored.'
    Remove-Item -LiteralPath $choices
    Write-Host 'OK update preserves occupied runtime, distinguishes versions and retries activation without reinstalling'

    $record.SourceRoot=Join-Path $work 'moved-repository\NexusWindows';$record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $windowsRecord
    $script:installed='3.9.4';$script:loaded='3.9.4'
    $beforeInstalls=$script:installs;$beforeRefresh=$script:pluginRefresh;$beforeDownloads=$script:downloads.Count
    $beforeRecord=[IO.File]::ReadAllText($windowsRecord)
    Assert ((Invoke-NexusMaintenance -Current) -eq 0) 'A local Windows engine was required to equal the public release.'
    $local=Get-NexusMaintenanceStatus
    Assert ($script:installs -eq $beforeInstalls -and $script:downloads.Count -eq $beforeDownloads -and $script:pluginRefresh -eq ($beforeRefresh+1)) 'A Windows repository build downloaded an installer or skipped public catalogue maintenance.'
    Assert ($local.products[0].state -eq 'kept' -and $local.products[0].publishedVersion -eq '3.9.5' -and $local.products[0].installedVersion -eq '3.9.4' -and $local.products[0].loadedVersions[0] -eq '3.9.4') 'Windows repository observation conflated published, installed or loaded versions.'
    Assert ([IO.File]::ReadAllText($windowsRecord) -ceq $beforeRecord) 'Maintenance rewrote the local Windows source receipt.'
    $record.SourceRoot='unknown';$record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $windowsRecord
    Assert ((Invoke-NexusMaintenance -Current) -eq 2 -and $script:installs -eq $beforeInstalls -and (Get-NexusMaintenanceStatus).products[0].message -like 'Installation provenance is unknown*') 'Unknown Windows provenance allowed a public replacement.'
    $record.SourceRoot='release v3.9.4';$record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $windowsRecord

    $Products=@($browser)
    [IO.File]::WriteAllText((Join-Path $env:CODEX_HOME 'config.toml'),"[plugins.`"nexus-browser@nexus-plugins`"]`nenabled=true")
    @{repository_root=(Join-Path $work 'moved-repository\NexusBrowser')}|ConvertTo-Json|Set-Content -LiteralPath $browserRecord
    $script:browserInstalled='2.33.4';$script:loaded='2.33.4';$script:pluginVersion='2.33.5'
    $beforeRefresh=$script:pluginRefresh;$beforeRecord=[IO.File]::ReadAllText($browserRecord)
    Assert ((Invoke-NexusMaintenance -Current) -eq 0) 'A local Browser engine was required to equal the public release.'
    $local=Get-NexusMaintenanceStatus
    Assert ($script:installs -eq $beforeInstalls -and $script:downloads.Count -eq $beforeDownloads -and $script:pluginRefresh -eq ($beforeRefresh+1) -and $local.products[0].state -eq 'kept') 'A Browser repository build was replaced or its public catalogue was skipped.'
    Assert ([IO.File]::ReadAllText($browserRecord) -ceq $beforeRecord) 'Maintenance rewrote the Browser source receipt.'
    $script:loaded='2.33.3'
    Assert ((Invoke-NexusMaintenance -Current) -eq 2 -and -not (Get-NexusMaintenanceStatus).products[0].retryable) 'A local Browser activation wait scheduled a public installer retry.'
    Assert ((Invoke-NexusMaintenance -Current) -eq 2 -and $script:installs -eq $beforeInstalls -and $script:downloads.Count -eq $beforeDownloads) 'A later local Browser cycle ran the public installer.'
    $Products=@($windows,$browser);$script:browserInstalled=$null;$script:pluginVersion='3.9.5';$script:loaded='3.9.5'
    [IO.File]::WriteAllText((Join-Path $env:CODEX_HOME 'config.toml'),"[plugins.`"nexus-windows@nexus-plugins`"]`nenabled=true")
    Write-Host 'OK repository engines are observed and preserved while their public catalogue plugins are refreshed'

    $lock=[IO.File]::Open((Join-Path (Get-NexusMaintenanceRoot) 'maintenance.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try{Assert ((Invoke-NexusMaintenance -Current) -eq 2) 'Concurrent maintenance was not excluded.'}finally{$lock.Dispose()}
    $script:networkFailure=$true
    Assert ((Invoke-NexusMaintenance -Current) -eq 1 -and (Get-NexusMaintenanceStatus).state -eq 'failed') 'Network error was swallowed.'
    $script:networkFailure=$false;$script:installed='3.9.4';$script:installerFailure=$true
    Assert ((Invoke-NexusMaintenance -Current) -eq 1) 'Installer failure was swallowed.'
    Assert ($env:NEXUS_MCP_TOKEN -ceq 'inherited-before-installation') 'Installer failure did not restore its caller environment.'
    Remove-Item Env:NEXUS_MCP_TOKEN
    $script:installerFailure=$false;$script:installerThrows=$true
    Assert ((Invoke-NexusMaintenance -Current) -eq 1 -and $null -eq [Environment]::GetEnvironmentVariable('NEXUS_MCP_TOKEN','Process')) 'Installer exception left a credential in an originally absent caller environment.'
    $script:installerThrows=$false
    $settingsPath=Join-Path $env:USERPROFILE '.nexus\nexussystem.json';[IO.File]::WriteAllText($settingsPath,'{"autoUpdate":false}')
    $beforeDownloads=$script:downloads.Count
    Assert ((Invoke-NexusMaintenance -Current) -eq 0 -and (Get-NexusMaintenanceStatus).state -eq 'disabled' -and $script:downloads.Count -eq $beforeDownloads) 'Automatic-update opt-out was not respected at run time.'
    Write-Host 'OK exclusion, network and installer failures remain explicit'
    Write-Host 'OK maintenance contract'
}finally{
    foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key],'Process')}
    # This test owns only its checked absolute temporary profile.
    if([IO.Path]::GetFullPath($work).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue}
}
