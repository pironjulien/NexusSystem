# Points both catalogs (Claude Code and Codex) at a product's plugin folder at one release tag, and sets the Claude
# entry's version. Run when a product releases (by hand or by its release workflow); the plugin files themselves live
# only in the product's repository.
param(
    [Parameter(Mandatory)][string]$Name,      # plugin name, e.g. nexus-android
    [Parameter(Mandatory)][string]$Product,   # repository, e.g. NexusAndroid
    [Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Tag,
    # The plugin folder in the product's repository. Left out: the folder each catalog entry already names (NexusKeypad's
    # is "connecteurs", its "plugin" folder being the Logi plugin), "plugin" for an entry that names none. A default of
    # "plugin" once repointed nexus-keypad at the Logi plugin (2026-11-08).
    [ValidatePattern('^[a-z]+$')][string]$Path
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

function Edit-Catalog([string]$Relative, [scriptblock]$Change) {
    $path = Join-Path $repository $Relative
    $market = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    $entry = @($market.plugins | Where-Object { $_.name -eq $Name })
    if ($entry.Count -ne 1) { return $false }
    & $Change $entry[0]
    [IO.File]::WriteAllText($path, (($market | ConvertTo-Json -Depth 32) -replace "`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
    return $true
}

# Built here: inside Edit-Catalog, $path names the catalog file (PowerShell variables are case-insensitive).
$dossierDemande = $Path
function Get-Source($entry) {
    $dossier = if ($dossierDemande) { $dossierDemande } elseif ($entry.source -is [System.Collections.IDictionary] -and $entry.source.path) { $entry.source.path } else { 'plugin' }
    [ordered]@{ source = 'git-subdir'; url = "https://github.com/pironjulien/$Product.git"; path = $dossier; ref = $Tag }
}
$claude = Edit-Catalog '.claude-plugin\marketplace.json' {
    param($entry)
    $entry.source = Get-Source $entry
    $entry.version = $Tag.Substring(1)
}
if (-not $claude) { throw "$Name is not in the Claude Code catalog" }
$codex = Edit-Catalog '.agents\plugins\marketplace.json' {
    param($entry)
    $entry.source = Get-Source $entry
}
"$Name -> $Product $Tag (Claude Code$(if ($codex) { ', Codex' }))"
