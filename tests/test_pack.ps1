# Offline tests of get-nexus.ps1 under Windows PowerShell 5.1: detection of the AI apps, plan, -Only/-Skip, both ways
# of starting it as a file, on throwaway profiles. Nothing is installed (-DryRun, or no AI app found).
$ErrorActionPreference = 'Stop'
$pack = Join-Path (Split-Path $PSScriptRoot) 'get-nexus.ps1'
$work = Join-Path ([IO.Path]::GetTempPath()) "nexus-pack-test-$PID"
$failures = 0

function Initialize-Profile([string]$Name, [string[]]$Folders) {
    $root = Join-Path $work $Name
    foreach ($folder in @('AppData\Roaming', 'AppData\Local') + $Folders) { New-Item -ItemType Directory -Force (Join-Path $root $folder) | Out-Null }
    return $root
}

function Invoke-Pack([string]$ProfileRoot, [string]$Invocation) {
    # A child Windows PowerShell with this profile and a PATH without any AI command line.
    $script = Join-Path $work "run-$([guid]::NewGuid().ToString('N')).ps1"
    $lines = @(
        "`$env:USERPROFILE = '$ProfileRoot'",
        "`$env:APPDATA = '$ProfileRoot\AppData\Roaming'",
        "`$env:LOCALAPPDATA = '$ProfileRoot\AppData\Local'",
        "Remove-Item Env:CODEX_HOME -ErrorAction SilentlyContinue",
        "`$env:PATH = '$env:SystemRoot\System32;$env:SystemRoot;$env:SystemRoot\System32\WindowsPowerShell\v1.0'",
        $Invocation,
        "exit `$LASTEXITCODE"
    )
    [IO.File]::WriteAllText($script, ($lines -join "`r`n"), (New-Object Text.UTF8Encoding $true))
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script *>&1 | Out-String
    return @{ Output = $output; Code = $LASTEXITCODE }
}

function Test-Case([string]$Name, $Result, [int]$Code, [string[]]$Has, [string[]]$HasNot = @()) {
    $problems = @()
    if ($Result.Code -ne $Code) { $problems += "exit code $($Result.Code), expected $Code" }
    foreach ($text in $Has) { if (-not $Result.Output.Contains($text)) { $problems += "missing: $text" } }
    foreach ($text in $HasNot) { if ($Result.Output.Contains($text)) { $problems += "unexpected: $text" } }
    if ($problems) {
        $script:failures++
        Write-Host "FAIL $Name`n  $($problems -join "`n  ")`n--- output ---`n$($Result.Output)"
    } else {
        Write-Host "OK $Name"
    }
}

New-Item -ItemType Directory -Force $work | Out-Null
try {
    $all = Initialize-Profile 'all' @('.claude', '.codex', '.gemini\antigravity', 'AppData\Roaming\Claude')
    $result = Invoke-Pack $all "& '$pack' -DryRun"
    Test-Case 'all four AI apps, every product' $result 0 @(
        'AI apps found: Claude Code, Claude Desktop, Codex, Antigravity',
        'NexusWindows  for Claude Code, Claude Desktop, Codex, Antigravity',
        'NexusAndroid  for Claude Code, Claude Desktop, Codex, Antigravity',
        'NexusBrowser  for Claude Code, Claude Desktop, Codex, Antigravity',
        'NexusRelay    for Claude Code, Codex, Antigravity', 'NexusAGYmail  for Antigravity',
        'NexusKeypad   needs Logi Options+',
        'Prerequisites, once: Git (for Claude Code and Codex)', 'Dry run: nothing installed.') @('Node.js', ' uv')

    $result = Invoke-Pack $all "& '$pack' -DryRun -Only windows,codex"
    Test-Case '-Only a product and an AI app' $result 0 @('NexusWindows  for Codex', 'Prerequisites, once: Git (for Codex)') @('Node.js', 'NexusAndroid  for', 'NexusBrowser  for', 'Git (for Claude Code)')
    # Git (Claude Code clones the catalog with it) only when Claude Code gets a plugin and git is missing.
    $result = Invoke-Pack $all "& '$pack' -DryRun -Only claude-code -Json"
    Test-Case 'Git for Claude Code when missing' $result 0 @('Prerequisites, once: Git (for Claude Code)', '"git":true')
    $result = Invoke-Pack $all "`$env:PATH += ';$(Split-Path (Get-Command git.exe).Source)'; & '$pack' -DryRun -Only claude-code -Json"
    Test-Case 'no Git step when git is there' $result 0 @('Prerequisites, once: none', '"git":false') @('Git (for Claude Code)')

    # The installers that configure every AI app found in one run take the selection (NEXUS_PACK_TARGETS).
    $result = Invoke-Pack $all "& '$pack' -DryRun -Only browser,codex -Json"
    Test-Case 'NexusBrowser takes the selection' $result 0 @('NexusBrowser  for Codex', '"products":["browser"]') @('not run')
    $result = Invoke-Pack $all "& '$pack' -DryRun -Only android,codex -Json"
    Test-Case 'one-run installer that takes the selection' $result 0 @('NexusAndroid  for Codex', '"products":["android"]') @('not run')
    $result = Invoke-Pack $all "& '$pack' -DryRun -Only relay -Skip antigravity -Json"
    Test-Case 'NexusRelay takes the selection too' $result 0 @('NexusRelay    for Claude Code, Codex', '"products":["relay"]') @('not run')
    # Its Claude Code plugin comes from the catalog, but its installer is still told Claude Code: the host stays
    # Claude on a new PC (Codex review of 28/10: without it, Codex became the host).
    Test-Case 'NexusRelay installer told Claude Code' $result 0 @('"relay":["claude-code","codex"]')

    # Native engines: each product's installer is told every chosen AI app, the plugin ones included (it sees the
    # plugins and adds no second server), since the plugins reach the engine or daemon it installs.
    $result = Invoke-Pack $all "& '$pack' -DryRun -Only windows,android,browser,agymail -Json"
    Test-Case 'native installers told every chosen AI app' $result 0 @(
        '"windows":["claude-code","claude-desktop","codex","antigravity"]', '"android":["claude-code","claude-desktop","codex","antigravity"]',
        '"browser":["claude-code","claude-desktop","codex","antigravity"]', '"agymail":["antigravity"]') @('not run')
    # This PC's own choices, outside the code: ~\.nexus\nexussystem.json adds its names to -Skip.
    $local = Initialize-Profile 'local-skip' @('.claude', '.codex', '.gemini\antigravity', 'AppData\Roaming\Claude', '.nexus')
    [IO.File]::WriteAllText((Join-Path $local '.nexus\nexussystem.json'), '{"skip": ["claude-desktop", "nonsense"]}')
    $result = Invoke-Pack $local "& '$pack' -DryRun -Only windows"
    Test-Case 'nexussystem.json skips Claude Desktop' $result 0 @('NexusWindows  for Claude Code, Codex, Antigravity', "unknown name 'nonsense' ignored") @('NexusWindows  for Claude Code, Claude Desktop')
    [IO.File]::WriteAllText((Join-Path $local '.nexus\nexussystem.json'), '{ not json')
    $result = Invoke-Pack $local "& '$pack' -DryRun -Only windows"
    Test-Case 'unreadable nexussystem.json ignored' $result 0 @('is not readable JSON: ignored', 'NexusWindows  for Claude Code, Claude Desktop, Codex, Antigravity')
    # A product turned off by the user in an AI app stays off there; the others are installed.
    $off = Initialize-Profile 'off' @('.claude', '.codex')
    [IO.File]::WriteAllText((Join-Path $off '.codex\config.toml'), "[mcp_servers.nexus-windows]`ncommand = `"npx`"`nenabled = false`n`n[mcp_servers.nexus-browser]`ncommand = `"nexus-browser.exe`"`nenabled = false`n")
    $result = Invoke-Pack $off "& '$pack' -DryRun -Only windows,browser -Json"
    Test-Case 'turned off by the user: kept' $result 0 @(
        'NexusWindows  Codex: turned off by you, left as it is', 'NexusBrowser  Codex: turned off by you, left as it is',
        '"products":["windows","browser"]') @('not run')

    # The real state of a declaration (the functions taken from the script).
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($pack, [ref]$null, [ref]$null)
    $functions = ($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                @('Read-Json', 'Test-CommandName', 'Get-JsonServerState', 'Get-TomlTable', 'Get-CodexState', 'Get-CodexHome', 'Read-CodexTomlKeyPath', 'Get-CodexTomlTable', 'Get-CodexPluginState', 'Get-CodexServerTable', 'Read-CodexTomlString', 'Get-ClaudeCodeState', 'Get-AntigravityState') -contains $a.Name }, $true) |
            ForEach-Object { $_.Extent.Text }) -join "`n"
    $states = Initialize-Profile 'states' @('.codex', '.claude')
    [IO.File]::WriteAllText((Join-Path $states '.codex\config.toml'), (@(
                '[mcp_servers.nexus-windows]', 'command = "C:\\nowhere\\node.exe"', '',
                '[mcp_servers.nexus-android]', 'command = "powershell.exe"', '[mcp_servers.nexus-android.env]', 'X = "1"', '',
                '[mcp_servers.nexus-relay]', 'url = "http://127.0.0.1:16181/mcp"', '',
                '[plugins."nexus-browser@nexus-plugins"]', 'enabled = false') -join "`n"))
    [IO.File]::WriteAllText((Join-Path $states '.claude\settings.json'), '{"enabledPlugins": {"nexus-windows@nexus-plugins": false, "nexus-android@nexus-plugins": true}}')
    # No plugin (its route failed): the product's own installer declared it in ~\.claude.json.
    [IO.File]::WriteAllText((Join-Path $states '.claude.json'), '{"mcpServers": {"nexus-browser": {"command": "powershell.exe"}, "nexus-relay": {"command": "C:\\nowhere\\NexusRelay.exe"}}}')
    foreach ($plugin in 'nexus-computer', 'nexus-android') { New-Item -ItemType Directory -Force (Join-Path $states ".gemini\config\plugins\$plugin") | Out-Null; Set-Content (Join-Path $states ".gemini\config\plugins\$plugin\plugin.json") '{}' }
    [IO.File]::WriteAllText((Join-Path $states '.gemini\config\config.json'), '{"plugins": {"nexus-computer": {"enabled": false}, "nexus-android": {"enabled": true}}}')
    $probe = "`$Marketplace = 'nexus-plugins'`n$functions`n" +
        "'codex=' + ((@('nexus-windows', 'nexus-android', 'nexus-relay', 'nexus-browser', 'nexus-agymail') | ForEach-Object { Get-CodexState @{ Server = `$_ } }) -join ',')`n" +
        "'claude=' + ((@('nexus-windows', 'nexus-android', 'nexus-browser', 'nexus-relay', 'nexus-agymail') | ForEach-Object { Get-ClaudeCodeState @{ Plugin = `$_; Server = `$_ } }) -join ',')`n" +
        "'agy=' + ((@('nexus-computer', 'nexus-android', 'nexus-browser') | ForEach-Object { Get-AntigravityState @{ Agy = `$_ } }) -join ',')"
    $result = Invoke-Pack $states $probe
    Test-Case 'declarations: ok, disabled, invalid, missing' $result 0 @(
        'codex=invalid,ok,ok,disabled,missing', 'claude=disabled,ok,ok,invalid,missing', 'agy=disabled,ok,missing')

    foreach ($case_ in @(
        @('current', @('nexus-windows'), '{}', 'ok'),
        @('legacy', @('nexus-computer'), '{}', 'ok'),
        @('current-disabled', @('nexus-windows'), '{"plugins":{"nexus-windows":{"enabled":false}}}', 'disabled'),
        @('legacy-disabled', @('nexus-computer'), '{"plugins":{"nexus-computer":{"enabled":false}}}', 'disabled'),
        @('legacy-disabled-after-move', @('nexus-windows'), '{"plugins":{"nexus-computer":{"enabled":false}}}', 'disabled'),
        @('disabled-without-folder', @(), '{"plugins":{"nexus-windows":{"enabled":false}}}', 'disabled'),
        @('absent', @(), '{}', 'missing')
    )) {
        $agyProfile = Initialize-Profile "agy-$($case_[0])" @('.gemini\config\plugins')
        foreach ($name in $case_[1]) {
            $pluginFolder = Join-Path $agyProfile ".gemini\config\plugins\$name"
            New-Item -ItemType Directory -Force $pluginFolder | Out-Null
            [IO.File]::WriteAllText((Join-Path $pluginFolder 'plugin.json'), '{}')
        }
        [IO.File]::WriteAllText((Join-Path $agyProfile '.gemini\config\config.json'), $case_[2])
        $probe = "$functions`n'windows=' + (Get-AntigravityState @{ Agy = 'nexus-windows'; AgyLegacy = @('nexus-computer') })"
        $result = Invoke-Pack $agyProfile $probe
        Test-Case "Antigravity Windows identity: $($case_[0])" $result 0 @("windows=$($case_[3])")
    }

    $result = Invoke-Pack $all "& '$pack' -DryRun -Skip 'codex, browser'"
    Test-Case '-Skip as one comma separated string' $result 0 @('NexusWindows  for Claude Code, Claude Desktop, Antigravity') @('NexusBrowser  for', 'Node.js')

    $result = Invoke-Pack $all "& '$pack' -DryRun -Only keypad"
    Test-Case 'NexusKeypad without Logi Options+' $result 0 @('NexusKeypad   needs Logi Options+', 'Nothing to install with these options.')

    $logi = Initialize-Profile 'logi' @('.claude', 'AppData\Local\Logi\LogiPluginService')
    $result = Invoke-Pack $logi "& '$pack' -DryRun -Only keypad"
    Test-Case 'NexusKeypad with Logi Options+' $result 0 @('NexusKeypad   for your Logi Options+')

    $codexOnly = Initialize-Profile 'codex' @('.codex')
    $result = Invoke-Pack $codexOnly "& '$pack' -DryRun"
    Test-Case 'NexusRelay needs two AI apps, NexusAGYmail Antigravity' $result 0 @(
        'NexusWindows  for Codex', 'NexusRelay    needs two of Claude Code, Codex, Antigravity', 'NexusAGYmail  for Antigravity only')
    # Without Claude Code: Codex hosts the relay (NexusRelay 1.4.0).
    $codexAgy = Initialize-Profile 'codex-agy' @('.codex', '.gemini\antigravity')
    $result = Invoke-Pack $codexAgy "& '$pack' -DryRun -Only relay"
    Test-Case 'NexusRelay without Claude Code' $result 0 @('NexusRelay    for Codex, Antigravity')

    $result = Invoke-Pack $all "& '$pack' -DryRun -Only nexuswindows"
    Test-Case 'unknown name refused' $result 2 @("Unknown name 'nexuswindows'")

    $desktop = Initialize-Profile 'desktop' @('AppData\Roaming\Claude')
    $result = Invoke-Pack $desktop "& '$pack' -DryRun"
    Test-Case 'Claude Desktop alone also means its Code tab' $result 0 @('AI apps found: Claude Code, Claude Desktop', 'NexusWindows  for Claude Code, Claude Desktop', 'NexusRelay    needs two of') @('AI apps found: Claude Code, Claude Desktop, Codex')

    $store = Initialize-Profile 'store' @('AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude')
    $result = Invoke-Pack $store "& '$pack' -DryRun"
    Test-Case 'Claude Desktop from the Microsoft Store' $result 0 @('AI apps found: Claude Code, Claude Desktop')

    $moved = Initialize-Profile 'moved' @('AppData\Local\Packages\AnthropicPBC.Claude_fnn82j28hfe8t\LocalCache\Roaming\Claude',
        'AppData\Local\Claude-Data\anthropicpbc.claude_fnn82j28hfe8t')
    New-Item -ItemType File (Join-Path $moved 'AppData\Local\Packages\AnthropicPBC.Claude_fnn82j28hfe8t\LocalCache\Roaming\Claude\.user-data-left.anthropicpbc.claude_fnn82j28hfe8t') | Out-Null
    $result = Invoke-Pack $moved "& '$pack' -DryRun"
    Test-Case 'Claude Desktop from the Store, data in Claude-Data' $result 0 @('AI apps found: Claude Code, Claude Desktop')

    $left = Initialize-Profile 'left' @('AppData\Roaming\Claude')
    New-Item -ItemType File (Join-Path $left 'AppData\Roaming\Claude\.user-data-left.anthropicpbc.claude_fnn82j28hfe8t') | Out-Null
    $result = Invoke-Pack $left "& '$pack' -DryRun"
    Test-Case 'a folder Claude left is not Claude Desktop' $result 1 @('No AI app found') @('AI apps found')

    $agy = Initialize-Profile 'agy' @('AppData\Local\Programs\Antigravity')
    $result = Invoke-Pack $agy "& '$pack' -DryRun"
    Test-Case 'Antigravity installed, never opened' $result 0 @('AI apps found: Antigravity', 'Prerequisites, once: none') @('Node.js', 'Git (')

    $result = Invoke-Pack $all "& '$pack' -DryRun -Json -Only windows,codex"
    Test-Case '-Json events for the Nexus apps' $result 0 @(
        '"type":"found"', '"ais":["claude-code","claude-desktop","codex","antigravity"]', '"type":"plan"',
        '"products":["windows"]', '"ais":["codex"]', '"type":"done"', '"code":0')

    $empty = Initialize-Profile 'empty' @()
    # Product installers are files of their release, checked against its SHA256SUMS.txt before running (-File).
    $sums = Join-Path $work 'sums.txt'
    [IO.File]::WriteAllText($sums, ("{0}  get.ps1`n{1}  other.zip`n" -f ('a' * 64), ('b' * 64)))
    $probe = "$functions`n" + (([System.Management.Automation.Language.Parser]::ParseFile($pack, [ref]$null, [ref]$null)).FindAll({ param($a)
                $a -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq 'Get-ExpectedSha256' }, $true) | Select-Object -First 1).Extent.Text +
        "`n`$m = [IO.File]::ReadAllText('$sums')`n'ok=' + (Get-ExpectedSha256 `$m 'get.ps1')`n" +
        "try { Get-ExpectedSha256 (`$m + `$m) 'get.ps1' } catch { 'twice=refused' }`n" +
        "try { Get-ExpectedSha256 'not a checksum' 'get.ps1' } catch { 'malformed=refused' }`n" +
        "try { Get-ExpectedSha256 `$m 'missing.ps1' } catch { 'absent=refused' }"
    $result = Invoke-Pack $empty $probe
    Test-Case 'installer checksums: exact line, duplicates, malformed and absent refused' $result 0 @("ok=$('a' * 64)", 'twice=refused', 'malformed=refused', 'absent=refused')
    # NexusKeypad counts as installed from its state file or any of its editions (Test-Device, taken from the script).
    $device = ([System.Management.Automation.Language.Parser]::ParseFile($pack, [ref]$null, [ref]$null)).FindAll({
            param($ast) $ast -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $ast.Name -eq 'Test-Device' }, $true) | Select-Object -First 1
    foreach ($case in @(@('none', $null), @('NexusKeypad', 'AppData\Local\Logi\LogiPluginService\Plugins\NexusKeypad'),
            @('TrinitePad', 'AppData\Local\Logi\LogiPluginService\Plugins\TrinitePad'), @('AiAgentsPc', 'AppData\Local\Logi\LogiPluginService\Plugins\AiAgentsPc'),
            @('PcDashboard', 'AppData\Local\Logi\LogiPluginService\Plugins\PcDashboard'), @('keypad.json', '.nexus\status'))) {
        $root = Initialize-Profile "keypad-$($case[0])" @($(if ($case[1]) { $case[1] }))
        if ($case[0] -eq 'keypad.json') { Set-Content (Join-Path $root '.nexus\status\keypad.json') '{"product":"keypad"}' }
        $result = Invoke-Pack $root "$($device.Extent.Text); 'installed=' + (Test-Device `$null)"
        $expected = if ($case[0] -eq 'none') { 'installed=False' } else { 'installed=True' }
        Test-Case "NexusKeypad detected: $($case[0])" $result 0 @($expected)
    }

} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
if ($failures) { Write-Host "test_pack.ps1: $failures failure(s)"; exit 1 }
Write-Host 'OK pack: detection, plan, options, checked installer files'
exit 0
