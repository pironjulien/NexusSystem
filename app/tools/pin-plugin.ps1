# Points both catalogs (Claude Code and Codex) at a product's plugin folder at one release tag, and sets the Claude
# entry's version. Run when a product releases (by hand or by its release workflow); the plugin files themselves live
# only in the product's repository.
param(
    [Parameter(Mandatory)][string]$Name,      # plugin name, e.g. nexus-android
    [Parameter(Mandatory)][string]$Product,   # repository, e.g. NexusAndroid
    [Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Tag
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

$claude = Edit-Catalog '.claude-plugin\marketplace.json' {
    param($entry)
    $entry.source = [ordered]@{ source = 'git-subdir'; url = "pironjulien/$Product"; path = 'plugin'; ref = $Tag }
    $entry.version = $Tag.Substring(1)
}
if (-not $claude) { throw "$Name is not in the Claude Code catalog" }
$codex = Edit-Catalog '.agents\plugins\marketplace.json' {
    param($entry)
    $entry.source = [ordered]@{ source = 'git-subdir'; url = "https://github.com/pironjulien/$Product.git"; path = 'plugin'; ref = $Tag }
}
"$Name -> $Product $Tag (Claude Code$(if ($codex) { ', Codex' }))"
