# Development channel of the plugin catalog: the same nexus-plugins catalog, but each plugin whose product repository
# is checked out next to this one is read from that checkout's plugin folder, so a change is tested at once (Claude
# Code reads a local marketplace live, at /reload-plugins or the next session; Codex after a marketplace upgrade).
# Products not checked out keep their published release. The catalog is written to ~\.nexus\dev-catalog with one
# directory junction per local plugin; nothing in the repositories changes. -Register also points Claude Code and
# Codex at it (the published GitHub catalog stays the default everywhere else).
param(
    [string]$Root = (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent),
    [string]$Destination = (Join-Path $HOME '.nexus\dev-catalog'),
    [switch]$Register
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

function Read-Json([string]$Path) { Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable }
function Write-Json([string]$Path, $Value) {
    New-Item -ItemType Directory -Force (Split-Path $Path -Parent) | Out-Null
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 32) -replace "`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
}

# The local checkout of a git-subdir source (pironjulien/<Product>, folder plugin), or $null.
function Get-LocalPlugin([string]$Checkouts, $Source) {
    if ($Source -isnot [Collections.IDictionary] -or $Source.source -ne 'git-subdir') { return $null }
    if ($Source.url -notmatch '(?:^|github\.com/)pironjulien/(Nexus[A-Za-z]+?)(?:\.git)?$') { return $null }
    $folder = Join-Path (Join-Path $Checkouts $Matches[1]) $Source.path
    if (Test-Path -LiteralPath (Join-Path $folder '.claude-plugin\plugin.json')) { return $folder }
    return $null
}

$plugins = Join-Path $Destination 'plugins'
if (Test-Path -LiteralPath $plugins) {
    # Junctions only: removing one never touches the checkout it points to.
    Get-ChildItem -LiteralPath $plugins -Force | Where-Object { $_.LinkType -eq 'Junction' } | ForEach-Object { $_.Delete() }
}
New-Item -ItemType Directory -Force $plugins | Out-Null

$local = @{}
foreach ($catalog in '.claude-plugin\marketplace.json', '.agents\plugins\marketplace.json') {
    $market = Read-Json (Join-Path $repository $catalog)
    foreach ($entry in $market.plugins) {
        $folder = Get-LocalPlugin $Root $entry.source
        if (-not $folder) { continue }
        if (-not $local.ContainsKey($entry.name)) {
            New-Item -ItemType Junction -Path (Join-Path $plugins $entry.name) -Target $folder | Out-Null
            $local[$entry.name] = $folder
        }
        $entry.source = if ($catalog -like '.claude-plugin*') { "./plugins/$($entry.name)" } else { @{ source = 'local'; path = "./plugins/$($entry.name)" } }
        if ($catalog -like '.claude-plugin*') {
            $entry.version = (Read-Json (Join-Path $folder '.claude-plugin\plugin.json')).version
        }
    }
    Write-Json (Join-Path $Destination $catalog) $market
}

if ($Register) {
    # Claude Code: the same marketplace name, from this folder (enabled plugins keep their keys).
    $settingsFile = Join-Path $HOME '.claude\settings.json'
    $settings = Read-Json $settingsFile
    if (-not $settings.extraKnownMarketplaces) { $settings.extraKnownMarketplaces = @{} }
    $settings.extraKnownMarketplaces['nexus-plugins'] = @{ source = @{ source = 'directory'; path = $Destination } }
    Copy-Item -LiteralPath $settingsFile "$settingsFile.dev-catalog.bak" -Force
    Write-Json $settingsFile $settings
    # Codex: its own command, which records a local marketplace in config.toml.
    $codex = Get-Command codex -ErrorAction SilentlyContinue
    if ($codex) {
        & $codex.Source plugin marketplace remove nexus-plugins 2>$null | Out-Null
        & $codex.Source plugin marketplace add $Destination
        if ($LASTEXITCODE) { throw 'codex plugin marketplace add failed' }
    } else { Write-Warning 'Codex CLI not found: Codex keeps the published catalog.' }
}

foreach ($name in $local.Keys | Sort-Object) { "{0,-14} local  {1}" -f $name, $local[$name] }
