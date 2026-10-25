# Offline tests of get-nexus.ps1 under Windows PowerShell 5.1: detection of the AI apps, plan, -Only/-Skip, both ways
# of starting it (file, `irm | iex`), on throwaway profiles. Nothing is installed (-DryRun, or no AI app found).
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
        'NexusRelay    not public yet', 'NexusGmail    not public yet', 'NexusKeypad   soon',
        'Prerequisites, once: uv, Node.js LTS (for Codex)', 'Dry run: nothing installed.')

    $result = Invoke-Pack $all "& '$pack' -DryRun -Only windows,codex"
    Test-Case '-Only a product and an AI app' $result 0 @('NexusWindows  for Codex', 'Node.js LTS (for Codex)') @('NexusAndroid  for', 'NexusBrowser  for')

    $result = Invoke-Pack $all "& '$pack' -DryRun -Skip 'codex, browser'"
    Test-Case '-Skip as one comma separated string' $result 0 @('NexusWindows  for Claude Code, Claude Desktop, Antigravity') @('NexusBrowser  for', 'Node.js')

    $result = Invoke-Pack $all "& '$pack' -DryRun -Only keypad"
    Test-Case '-Only a product not public yet' $result 0 @('Nothing to install with these options.')

    $result = Invoke-Pack $all "& '$pack' -DryRun -Only nexuswindows"
    Test-Case 'unknown name refused' $result 2 @("Unknown name 'nexuswindows'")

    $desktop = Initialize-Profile 'desktop' @('AppData\Roaming\Claude')
    $result = Invoke-Pack $desktop "& '$pack' -DryRun"
    Test-Case 'Claude Desktop alone also means its Code tab' $result 0 @('AI apps found: Claude Code, Claude Desktop') @('Codex', 'Antigravity')

    $store = Initialize-Profile 'store' @('AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude')
    $result = Invoke-Pack $store "& '$pack' -DryRun"
    Test-Case 'Claude Desktop from the Microsoft Store' $result 0 @('AI apps found: Claude Code, Claude Desktop')

    $agy = Initialize-Profile 'agy' @('AppData\Local\Programs\Antigravity')
    $result = Invoke-Pack $agy "& '$pack' -DryRun"
    Test-Case 'Antigravity installed, never opened' $result 0 @('AI apps found: Antigravity', 'Prerequisites, once: uv') @('Node.js')

    # `irm | iex`: the script text run in the user's own session, which must stay open whatever happens.
    $empty = Initialize-Profile 'empty' @()
    $result = Invoke-Pack $empty "Get-Content -Raw -LiteralPath '$pack' | Invoke-Expression; 'session still open'"
    Test-Case 'irm | iex with no AI app: message, window kept' $result 0 @('No AI app found', 'session still open')

    $result = Invoke-Pack $all "& ([scriptblock]::Create((Get-Content -Raw -LiteralPath '$pack'))) -DryRun -Only android; 'session still open'"
    Test-Case 'scriptblock form with options' $result 0 @('NexusAndroid  for Claude Code', 'session still open') @('NexusWindows  for')
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
if ($failures) { Write-Host "test_pack.ps1: $failures failure(s)"; exit 1 }
Write-Host 'OK pack: detection, plan, options, file and iex forms'
exit 0
