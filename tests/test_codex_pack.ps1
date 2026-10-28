# Offline contract tests: the real pack functions, isolated profiles and inert installers. No download, no service
# launch, no modification of the real Codex profile. Exercises ordering, failure, user choices and exact migration.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '', Justification = 'The pack functions loaded from their AST read these exact script-scope configuration variables.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidOverwritingBuiltInCmdlets', '', Justification = 'Isolated scope mocks for signed-engine discovery; no mock escapes this test process.')]
param([switch]$LivePluginInstall)
$ErrorActionPreference = 'Stop'
$pack = Join-Path (Split-Path $PSScriptRoot) 'get-nexus.ps1'
$work = Join-Path ([IO.Path]::GetTempPath()) "nexus-codex-pack-test-$PID"
$originalProfile = $env:USERPROFILE; $originalLocal = $env:LOCALAPPDATA; $originalAppData = $env:APPDATA
$originalCodex = $env:CODEX_HOME; $originalPackTargets = $env:NEXUS_PACK_TARGETS
$ast = [Management.Automation.Language.Parser]::ParseFile($pack, [ref]$null, [ref]$null)
foreach ($definition in $ast.FindAll({ param($a) $a -is [Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
$script:realCli = if ($LivePluginInstall) { Get-CodexCli } else { $null }
$Marketplace = 'nexus-plugins'; $EmitEvents = $false; $Ok = 'ok'; $No = 'failed'; $Raw = 'https://test.invalid'
$AiNames = [ordered]@{ 'claude-code' = 'Claude Code'; 'codex' = 'Codex' }
$Products = @(
    @{Key='windows'; Name='NexusWindows'; Plugin='nexus-windows'; Server='nexus-windows'; Targets=@('codex')},
    @{Key='android'; Name='NexusAndroid'; Plugin='nexus-android'; Server='nexus-android'; Targets=@('codex')},
    @{Key='browser'; Name='NexusBrowser'; Plugin='nexus-browser'; Server='nexus-browser'; Targets=@('codex')},
    @{Key='relay'; Name='NexusRelay'; Plugin='nexus-relay'; Server='nexus-relay'; Daemon=$true; Targets=@('codex')}
)
$Checks = @{ 'codex' = ${function:Get-CodexState}; 'claude-code' = { return 'missing' } }
$OneRunForAll = @('android', 'browser', 'relay'); $HonorsTargets = $OneRunForAll
$script:operations = @(); $script:failPlugin = ''; $script:omitGuide = ''; $script:installerFailure = ''
function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Initialize-Test([string]$Name, [string]$Config = '') {
    $root = Join-Path $work $Name
    New-Item -ItemType Directory -Force (Join-Path $root '.codex'), (Join-Path $root 'AppData\Local'), (Join-Path $root 'AppData\Roaming') | Out-Null
    $env:USERPROFILE = $root; $env:LOCALAPPDATA = Join-Path $root 'AppData\Local'; $env:APPDATA = Join-Path $root 'AppData\Roaming'
    $env:CODEX_HOME = Join-Path $root '.codex'
    [IO.File]::WriteAllText((Join-Path $env:CODEX_HOME 'config.toml'), $Config)
    $script:operations = @(); $script:failPlugin = ''; $script:omitGuide = ''; $script:installerFailure = ''
}
function Read-Config { return [IO.File]::ReadAllText((Join-Path $env:CODEX_HOME 'config.toml')) }
function Add-Config([string]$Text) { [IO.File]::AppendAllText((Join-Path $env:CODEX_HOME 'config.toml'), "`n$Text`n") }
function Install-Fixture([string]$Name) {
    Add-Config "[plugins.`"$Name@nexus-plugins`"]`nenabled = true"
    $root = Join-Path $env:CODEX_HOME "plugins\cache\nexus-plugins\$Name\1.0.0"
    New-Item -ItemType Directory -Force (Join-Path $root '.codex-plugin'), (Join-Path $root "skills\$Name") | Out-Null
    Set-Content -LiteralPath (Join-Path $root '.codex-plugin\plugin.json') -Value ('{"name":"'+$Name+'","skills":"./skills/"}')
    Set-Content -LiteralPath (Join-Path $root '.mcp.json') -Value '{}'
    if ($script:omitGuide -ne $Name) { Set-Content -LiteralPath (Join-Path $root "skills\$Name\SKILL.md") -Value "# $Name guide" }
}
function Find-AiApp { return , @('codex') }
function Sync-SessionPath { param([switch]$Registry) [void]$Registry }
function Get-Uvx { return 'uvx.exe' }
function Get-NodeMajor { return 24 }
function Test-Git { return $true }
function Get-CodexCli { return 'signed-codex.exe' }
function Install-CodexMarketplace([string]$Cli, [string]$Catalog) { $script:operations += "catalog:$Cli|$Catalog"; return 0 }
function Install-CodexPlugin([string]$Cli, [string]$Name) {
    $script:operations += "plugin:$Name"
    if ($script:failPlugin -eq $Name) { return 1 }
    Install-Fixture $Name
    return 0
}
function Invoke-Console([string]$File, [string[]]$Arguments) {
    $script:operations += "runtime:android|$env:NEXUS_PACK_TARGETS"
    if (($env:NEXUS_PACK_TARGETS -split ',') -contains 'codex' -and (Get-CodexPluginState (Read-Config) 'nexus-android') -ne 'enabled') {
        Add-Config "[mcp_servers.nexus-android]`ncommand = `"uvx`"`nargs = [`"nexus-android`"]"
    }
    $global:LASTEXITCODE = 0
}
function Invoke-Installer([string]$What, [string]$Url, [string]$Work) {
    $name = if ($What -eq 'NexusBrowser') { 'browser' } elseif ($What -eq 'NexusRelay') { 'relay' } else { throw "Unexpected installer $What" }
    $script:operations += "runtime:$name|$env:NEXUS_PACK_TARGETS"
    if ($script:installerFailure -eq $name) { return 1 }
    if (($env:NEXUS_PACK_TARGETS -split ',') -contains 'codex') {
        # Browser's older installer does not detect an installed plugin and writes a duplicate direct MCP table.
        if ($name -eq 'browser') {
            $python = (Join-Path $env:USERPROFILE '.nexus\nexus-browser\venv\Scripts\python.exe') | ConvertTo-Json -Compress
            $server = (Join-Path $env:LOCALAPPDATA 'NexusBrowser\app\client\mcp_server.py') | ConvertTo-Json -Compress
            Add-Config "[mcp_servers.nexus-browser]`ncommand = $python`nargs = [`"-B`", $server]`n[mcp_servers.nexus-browser.env]`nNEXUS_BROWSER_AGENT = `"Codex`""
        } elseif ((Get-CodexPluginState (Read-Config) 'nexus-relay') -ne 'enabled') {
            Add-Config "[mcp_servers.nexus-relay]`nurl = `"http://127.0.0.1:16181/mcp?agent=codex`"`nbearer_token_env_var = `"NEXUS_MCP_TOKEN`""
        }
    }
    return 0
}
New-Item -ItemType Directory -Force $work | Out-Null
try {
    Initialize-Test 'fresh'
    $code = Invoke-NexusPack @() @() $false $true 'test/local-catalog'
    Assert-True ($code -eq 0) 'Fresh installation failed.'
    foreach ($name in 'nexus-windows', 'nexus-android', 'nexus-browser', 'nexus-relay') {
        Assert-True (Test-CodexPluginInstalled $name) "$name guide not installed."
        Assert-True (@(Get-CodexServerTable (Read-Config) $name).Count -eq 0) "$name duplicate direct server remains."
    }
    Assert-True (($script:operations -join ';') -match '^catalog:.*;plugin:nexus-windows;plugin:nexus-android;plugin:nexus-browser;plugin:nexus-relay;runtime:android\|codex') 'Plugins must precede runtime installation.'
    Write-Host 'OK Codex: all four plugins include guides; runtime duplicates removed'

    Initialize-Test 'failed-plugin'
    $script:failPlugin = 'nexus-android'
    $code = Invoke-NexusPack @('android') @() $false $true 'test/local-catalog'
    Assert-True ($code -eq 1) 'A failed plugin must remain a failure even if its runtime succeeds.'
    Assert-True (@(Get-CodexServerTable (Read-Config) 'nexus-android').Count -eq 1) 'Fallback direct declaration must survive failed plugin install.'
    Write-Host 'OK Codex: plugin failure is visible, existing/fallback MCP kept'

    Initialize-Test 'missing-guide'
    $script:omitGuide = 'nexus-browser'
    $code = Invoke-NexusPack @('browser') @() $false $true 'test/local-catalog'
    Assert-True ($code -eq 1) 'Missing guide must fail the complete installation.'
    Assert-True (@(Get-CodexServerTable (Read-Config) 'nexus-browser').Count -eq 2) 'Missing guide must keep the direct server and env table.'
    Write-Host 'OK Codex: a missing installed guide is never reported as complete'

    $disabled = "[plugins.`"nexus-android@nexus-plugins`"]`nenabled = false`n[mcp_servers.nexus-android]`ncommand = `"uvx`"`nargs = [`"nexus-android`"]"
    Initialize-Test 'disabled-plugin' $disabled
    $code = Invoke-NexusPack @('android') @() $false $true 'test/local-catalog'
    Assert-True ($code -eq 0 -and (Read-Config) -ceq $disabled) 'A disabled plugin must remain byte-identical even with a direct MCP.'
    Assert-True (-not @($script:operations | Where-Object { $_ -match '^plugin:' }).Count) 'Disabled product must not be installed.'
    Write-Host 'OK Codex: disabled plugin takes precedence and remains unchanged'

    $preservedForms = @(
        @('literal-disabled-plugin', "[ plugins . 'nexus-android@nexus-plugins' ] # kept`n'enabled' = false`n", 'android'),
        @('escaped-disabled-plugin', ('[plugins."nexus\u002dandroid@nexus-plugins"]' + "`nenabled = false`n"), 'android'),
        @('long-escaped-disabled-plugin', ('[plugins."nexus\U0000002Dandroid@nexus-plugins"]' + "`nenabled = false`n"), 'android'),
        @('literal-disabled-server', "[ mcp_servers . 'nexus-android' ]`n'enabled' = false`ncommand = 'uvx'`nargs = ['nexus-android']`n", 'android'),
        @('literal-custom-server', "[ mcp_servers . 'nexus-browser' ]`ncommand = 'docker'`nargs = ['run','custom-browser']`n", 'browser'),
        @('inline-custom-server', "[mcp_servers]`n'nexus-browser' = { command = 'docker', args = ['run','custom-browser'] }`n", 'browser'),
        @('dotted-custom-server', "mcp_servers . 'nexus-browser' . command = 'docker'`nmcp_servers . 'nexus-browser' . args = ['run','custom-browser']`n", 'browser'),
        @('inline-plugin', "[plugins]`n'nexus-android@nexus-plugins' = { enabled = false }`n", 'android'),
        @('root-inline-plugins', "plugins = { 'nexus-android@nexus-plugins' = { enabled = false } }`n", 'android'),
        @('dotted-disabled-plugin', "plugins . 'nexus-android@nexus-plugins' . enabled = false`n", 'android'),
        @('literal-other-catalog', "[ plugins . 'nexus-windows@personal' ]`nenabled = true`n", 'windows')
    )
    foreach ($case_ in $preservedForms) {
        Initialize-Test $case_[0] $case_[1]
        $code = Invoke-NexusPack @($case_[2]) @() $false $true 'test/local-catalog'
        Assert-True ($code -eq 0 -and (Read-Config) -ceq $case_[1]) "$($case_[0]): valid TOML user choices must remain byte-identical."
        Assert-True (-not $script:operations.Count) "$($case_[0]): preserved declarations must cause no plugin or runtime installation."
    }
    Write-Host 'OK Codex: literal/escaped keys, spaced dots, dotted/inline declarations and disabled choices preserved'

    $embeddedHeaders = @'
instructions = """
[plugins."nexus-android@nexus-plugins"]
enabled = false
[mcp_servers.nexus-browser]
command = "docker"
"""
other = [
  "[plugins.'nexus-android@nexus-plugins']",
  "enabled = false",
]
'@
    Assert-True ((Get-CodexPluginState $embeddedHeaders 'nexus-android') -eq 'missing') 'Headers inside strings or arrays are not plugin declarations.'
    Assert-True (@(Get-CodexServerTable $embeddedHeaders 'nexus-browser').Count -eq 0) 'Headers inside multiline strings are not removable MCP tables.'
    $literalHeaders = "instructions = '''`n[plugins.`"nexus-android@nexus-plugins`"]`nenabled = false`n'''`n"
    Assert-True ((Get-CodexPluginState $literalHeaders 'nexus-android') -eq 'missing') 'Headers inside multiline literal strings are not plugin declarations.'
    $escapedLiteral = '[plugins."nexus\\u002dandroid@nexus-plugins"]' + "`nenabled = false`n"
    Assert-True ((Get-CodexPluginState $escapedLiteral 'nexus-android') -eq 'missing') 'An escaped backslash is not a Unicode key escape.'
    Write-Host 'OK Codex: headers embedded in multiline strings/arrays never become editable configuration'

    $custom = "[mcp_servers.nexus-browser]`ncommand = `"docker`"`nargs = [`"run`", `"my-browser`"]`n[other]`nvalue = `"kept`""
    Initialize-Test 'custom' $custom
    $code = Invoke-NexusPack @('browser') @() $false $true 'test/local-catalog'
    Assert-True ($code -eq 0 -and (Read-Config) -ceq $custom) 'Custom server must remain byte-identical.'
    Assert-True (-not @($script:operations | Where-Object { $_ -match '^plugin:' }).Count) 'Custom server must not gain a second plugin.'
    Write-Host 'OK Codex: custom MCP preserved, no second server'

    $existing = "[plugins.`"nexus-windows@personal`"]`nenabled = true`n"
    Initialize-Test 'other-catalog' $existing
    $code = Invoke-NexusPack @('windows') @() $false $true 'test/local-catalog'
    Assert-True ($code -eq 0 -and (Read-Config) -ceq $existing) 'Existing plugin from another catalog must be kept.'
    Assert-True (-not $script:operations.Count) 'Existing plugin must not cause another catalog or plugin installation.'
    Write-Host 'OK Codex: existing personal/team plugin preserved without duplicate'

    Initialize-Test 'exact-signatures'
    $android = "[mcp_servers.`"nexus-android`"]`ncommand = `"C:\\Tools\\uvx.exe`"`nargs = [`"--python`", `"3.14`", `"nexus-android==0.20.3`"]`nstartup_timeout_sec = 60`n"
    Assert-True (Test-CodexOwnedServer $android 'nexus-android') 'Official pinned Android signature not recognized.'
    Assert-True (-not (Test-CodexOwnedServer ($android+'cwd = "C:\\Custom"') 'nexus-android')) 'Custom cwd must prevent removal.'
    Assert-True (-not (Test-CodexOwnedServer ($android.Replace('nexus-android==0.20.3','--from nexus-android-dev')) 'nexus-android')) 'Custom package must prevent removal.'
    Assert-True (-not (Test-CodexOwnedServer "[mcp_servers.nexus-relay]`nurl = `"http://127.0.0.1:9999/mcp?agent=codex`"`nbearer_token_env_var = `"NEXUS_MCP_TOKEN`"`n" 'nexus-relay')) 'Nonstandard relay port must prevent removal.'
    Assert-True (-not (Test-CodexOwnedServer ($android+"[mcp_servers.nexus-android.env]`nMY_SETTING = `"yes`"`n") 'nexus-android')) 'Custom env must prevent removal.'
    Write-Host 'OK Codex: only exact official command/args/environment/URL signatures migrate'

    $spacedOwned = "[ mcp_servers . 'nexus-android' ] # original`ncommand = 'uvx'`nargs = ['--python','3.14','nexus-android']`n[other]`ntext = 'kept'`n[ plugins . 'nexus-android@nexus-plugins' ]`nenabled = true`n"
    $prefix = $embeddedHeaders + "`n"
    Initialize-Test 'spaced-owned' ($prefix + $spacedOwned)
    Assert-True (Test-CodexOwnedServer (Read-Config) 'nexus-android') 'Owned declaration with literal keys and spaced dots must be recognized.'
    Remove-CodexOwnedServer 'nexus-android'
    Assert-True ((Read-Config) -ceq ($prefix + "[other]`ntext = 'kept'`n[ plugins . 'nexus-android@nexus-plugins' ]`nenabled = true`n")) 'Surgical removal must preserve neighboring tables and header-like text exactly.'
    Write-Host 'OK Codex: only the recognized owned table is removed, neighboring bytes preserved'

    Initialize-Test 'signed-desktop'
    & {
        # Inert files and simulated signatures exercise discovery without executing either candidate.
        . ([scriptblock]::Create(($ast.FindAll({ param($a) $a -is [Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq 'Get-CodexCli' }, $true) | Select-Object -First 1).Extent.Text))
        function Get-Command([string]$Name) {
            if ($Name -eq 'codex') { return @() }
            return Microsoft.PowerShell.Core\Get-Command $Name -ErrorAction SilentlyContinue
        }
        function Get-Process { return @() }
        function Get-AppxPackage { return @() }
        function Get-AuthenticodeSignature([string]$LiteralPath) {
            $good = $LiteralPath -eq $script:trustedEngine -and -not $script:invalidSignature
            return [pscustomobject]@{ Status = $(if ($good) { 'Valid' } else { 'NotSigned' }); SignerCertificate = [pscustomobject]@{Subject='CN="OpenAI OpCo, LLC", O="OpenAI OpCo, LLC", C=US'} }
        }
        $bin = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
        foreach ($id in 'trusted', 'unsigned') { New-Item -ItemType Directory -Force (Join-Path $bin $id) | Out-Null; Set-Content -LiteralPath (Join-Path $bin "$id\codex.exe") -Value 'inert' }
        $script:trustedEngine = Join-Path $bin 'trusted\codex.exe'; $script:invalidSignature = $false
        Assert-True ((Get-CodexCli) -eq $script:trustedEngine) 'Desktop-only signed engine must be discovered without a PATH CLI.'
        $script:invalidSignature = $true
        Assert-True ($null -eq (Get-CodexCli)) 'Unsigned desktop engines must never be selected.'
    }
    Write-Host 'OK Codex: desktop-only OpenAI-signed engine found, unsigned engine refused'

    if ($LivePluginInstall) {
        Assert-True ([bool]$script:realCli) 'Live local plugin installation requires the installed signed Codex engine.'
        Initialize-Test 'real-plugin-cache'
        & {
            foreach ($name in 'Install-CodexMarketplace', 'Install-CodexPlugin') {
                . ([scriptblock]::Create(($ast.FindAll({ param($a) $a -is [Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq $name }, $true) | Select-Object -First 1).Extent.Text))
            }
            function Get-CodexCli { return $script:realCli }
            $code = Invoke-NexusPack @() @() $false $true (Split-Path $PSScriptRoot)
            Assert-True ($code -eq 0) 'The real signed CLI failed to install the local catalog plugins.'
            foreach ($name in 'nexus-windows', 'nexus-android', 'nexus-browser', 'nexus-relay') {
                Assert-True (Test-CodexPluginInstalled $name) "Real installed $name cache is missing its guide."
                Assert-True (@(Get-CodexServerTable (Read-Config) $name).Count -eq 0) "Real installed $name retains a duplicate."
            }
        }
        Write-Host 'OK Codex: real signed CLI installed all four packages and guides in an isolated cache (product runtimes mocked)'
    }
} finally {
    $env:USERPROFILE = $originalProfile; $env:LOCALAPPDATA = $originalLocal; $env:APPDATA = $originalAppData
    if ($null -eq $originalCodex) { Remove-Item Env:CODEX_HOME -ErrorAction SilentlyContinue } else { $env:CODEX_HOME = $originalCodex }
    if ($null -eq $originalPackTargets) { Remove-Item Env:NEXUS_PACK_TARGETS -ErrorAction SilentlyContinue } else { $env:NEXUS_PACK_TARGETS = $originalPackTargets }
    $resolvedWork = [IO.Path]::GetFullPath($work)
    if (-not $resolvedWork.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Test cleanup escaped the temporary folder.' }
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host 'OK Codex public pack contract'
exit 0
