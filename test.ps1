# Full offline check of the marketplace and of the Nexus pack (exit code 0 when everything is fine).
# Offline: manifests (Claude and Codex), pins, skills, PowerShell lint (PSScriptAnalyzer), get-nexus.ps1 under
# Windows PowerShell 5.1 on throwaway profiles (-DryRun).
# -Full also checks each uv.lock against its pyproject.toml and starts every MCP server from PyPI exactly as its
# .mcp.json says, in a throwaway uv cache, then lists its tools.
param([switch]$Full)
$ErrorActionPreference = 'Stop'
$env:PYTHONDONTWRITEBYTECODE = '1'

python -B (Join-Path $PSScriptRoot 'tests\check_catalog.py')
if ($LASTEXITCODE) { exit $LASTEXITCODE }

if (-not (Get-Module -ListAvailable PSScriptAnalyzer)) {
    throw 'PSScriptAnalyzer is missing: Install-Module PSScriptAnalyzer -Scope CurrentUser'
}
$lint = Invoke-ScriptAnalyzer -Path $PSScriptRoot -Recurse -Settings (Join-Path $PSScriptRoot 'PSScriptAnalyzerSettings.psd1')
if ($lint) {
    $lint | Format-Table -AutoSize ScriptName, Line, RuleName, Message | Out-String -Width 200 | Write-Host
    exit 1
}
Write-Host 'OK lint: PowerShell scripts clean'

powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests\test_pack.ps1')
if ($LASTEXITCODE) { exit $LASTEXITCODE }

if ($Full) {
    $cache = Join-Path ([IO.Path]::GetTempPath()) "nexus-plugins-uv-$PID"
    $env:UV_CACHE_DIR = $cache
    try {
        foreach ($plugin in Get-ChildItem (Join-Path $PSScriptRoot 'plugins') -Directory) {
            uv lock --check --project $plugin.FullName --quiet
            if ($LASTEXITCODE) { throw "uv.lock out of date in $($plugin.Name)" }
        }
        python -B (Join-Path $PSScriptRoot 'tests\smoke_mcp.py')
        $code = $LASTEXITCODE
    } finally {
        Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item Env:UV_CACHE_DIR
    }
    if ($code) { exit $code }
}
Write-Host 'test.ps1: all checks passed'
exit 0
