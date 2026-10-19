# Full offline check of the marketplace (exit code 0 when everything is fine).
# -Full also checks each uv.lock against its pyproject.toml and starts every MCP server from PyPI exactly as its
# .mcp.json says, in a throwaway uv cache, then lists its tools.
param([switch]$Full)
$ErrorActionPreference = 'Stop'
$env:PYTHONDONTWRITEBYTECODE = '1'

python -B (Join-Path $PSScriptRoot 'tests\check_catalog.py')
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
