# tools/release-notes.ps1: the Store's release notes come from the versioned sections (CHANGELOG.md in English,
# store/release-notes.fr.md in French), fit Partner Center's limit, and a missing section stops the package.
# 2026-11-08: app 1.0.19.0 went to the Store with the notes of 1.0.18.0, typed by hand.
$ErrorActionPreference = 'Stop'
$app = Split-Path $PSScriptRoot
$tool = Join-Path $app 'tools\release-notes.ps1'
function Assert-Note([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-Failure([scriptblock]$Block) { try { & $Block | Out-Null; return $null } catch { return $_.Exception.Message } }

$dir = Join-Path ([IO.Path]::GetTempPath()) ('nexus-notes-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $en = Join-Path $dir 'CHANGELOG.md'
    $fr = Join-Path $dir 'release-notes.fr.md'
    [IO.File]::WriteAllText($en, @'
# Changelog

## [Unreleased]

## [app 2.0.1.0] - 2026-12-01

### Changed
- The newest **change**, with `code` and a [link](https://example.com)
  continued on a second line.

### Fixed
- A fix.

## [1.9.0] - 2026-11-30

### Fixed
- A catalog entry that must not appear.

## [app 2.0.0.0] - 2026-11-20

### Changed
- The previous version.
'@)
    [IO.File]::WriteAllText($fr, @'
# Notes

## [app 2.0.1.0] - 2026-12-01

### Modifie
- Le dernier **changement**, avec `code` et un [lien](https://example.com)
  suivi d'une deuxieme ligne.

### Corrige
- Une correction.
'@)
    $out = Join-Path $dir 'out'

    # Extraction: the version's section only, as plain text, the same shape in both languages.
    $files = & $tool -Version 2.0.1.0 -Out $out -Changelog $en -French $fr
    Assert-Note (@($files).Count -eq 2) 'Two files expected (en, fr).'
    $textEn = [IO.File]::ReadAllText((Join-Path $out 'release-notes.en.txt'))
    $textFr = [IO.File]::ReadAllText((Join-Path $out 'release-notes.fr.txt'))
    $b = [char]0x2022
    Assert-Note ($textEn -eq "Changed`r`n$b The newest change, with code and a link continued on a second line.`r`n`r`nFixed`r`n$b A fix.") "Wrong English notes:`n$textEn"
    Assert-Note ($textFr -eq "Modifie`r`n$b Le dernier changement, avec code et un lien suivi d'une deuxieme ligne.`r`n`r`nCorrige`r`n$b Une correction.") "Wrong French notes:`n$textFr"
    Assert-Note ($textEn -notmatch 'previous version|catalog entry') 'Another section leaked into the notes.'

    # A missing section, in either language, fails and writes nothing.
    $missing = Join-Path $dir 'missing'
    $failure = Get-Failure { & $tool -Version 2.0.2.0 -Out $missing -Changelog $en -French $fr }
    Assert-Note ($failure -match "no '## \[app 2\.0\.2\.0\]' section") "A missing English section must fail: $failure"
    $failure = Get-Failure { & $tool -Version 2.0.0.0 -Out $missing -Changelog $en -French $fr }
    Assert-Note ($failure -match '\(fr, ') "A missing French section must fail: $failure"
    Assert-Note (-not (Test-Path -LiteralPath $missing)) 'Nothing may be written when a section is missing.'
    $failure = Get-Failure { & $tool -Version 2.0.1.0 -Out $missing -Changelog $en -French (Join-Path $dir 'none.md') }
    Assert-Note ($failure -match 'does not exist') "A missing French file must fail: $failure"

    # A stale translation (an entry fewer) fails.
    $short = Join-Path $dir 'short.fr.md'
    [IO.File]::WriteAllText($short, "## [app 2.0.1.0]`n`n### Modifie`n- Le dernier changement.`n")
    $failure = Get-Failure { & $tool -Version 2.0.1.0 -Out $missing -Changelog $en -French $short }
    Assert-Note ($failure -match '2 entries in English and 1 in French') "A translation with other entries must fail: $failure"

    # An empty section fails.
    $empty = Join-Path $dir 'empty.md'
    [IO.File]::WriteAllText($empty, "## [app 2.0.1.0]`n`n### Changed`n`n## [app 2.0.0.0]`n")
    $failure = Get-Failure { & $tool -Version 2.0.1.0 -Out $missing -Changelog $empty -French $fr }
    Assert-Note ($failure -match 'no entry') "An empty section must fail: $failure"

    # Partner Center's limit: 1,500 characters, Windows line breaks counted as two.
    $long = Join-Path $dir 'long.md'
    $longFr = Join-Path $dir 'long.fr.md'
    $fill = 'x' * 600
    [IO.File]::WriteAllText($long, "## [app 2.0.1.0]`n`n### Changed`n- $fill`n- $fill`n")
    [IO.File]::WriteAllText($longFr, "## [app 2.0.1.0]`n`n### Modifie`n- $fill`n- $fill`n")
    $files = & $tool -Version 2.0.1.0 -Out $out -Changelog $long -French $longFr
    Assert-Note ([IO.File]::ReadAllText((Join-Path $out 'release-notes.en.txt')).Length -le 1500) 'Notes within the limit must pass.'
    [IO.File]::WriteAllText($long, "## [app 2.0.1.0]`n`n### Changed`n- $fill`n- $fill`n- $('x' * 300)`n")
    [IO.File]::WriteAllText($longFr, "## [app 2.0.1.0]`n`n### Modifie`n- $fill`n- $fill`n- x`n")
    $failure = Get-Failure { & $tool -Version 2.0.1.0 -Out $missing -Changelog $long -French $longFr }
    Assert-Note ($failure -match 'Partner Center accepts 1500') "Notes over the limit must fail: $failure"

    # The repository itself: the app's current version has both notes, within the limit.
    $version = ([xml](Get-Content -Raw (Join-Path $app 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $current = Join-Path $dir 'current'
    & $tool -Version $version -Out $current | Out-Null
    foreach ($code in 'en', 'fr') {
        $text = [IO.File]::ReadAllText((Join-Path $current "release-notes.$code.txt"))
        Assert-Note ($text.Length -gt 0 -and $text.Length -le 1500) "app $version ($code): $($text.Length) characters."
    }

    # package.ps1 -Store writes them next to the package, before building.
    $package = Get-Content -Raw (Join-Path $app 'tools\package.ps1')
    $call = $package.IndexOf("'release-notes.ps1') -Version `$Version -Out `$outDir")
    Assert-Note ($call -ge 0 -and $call -lt $package.IndexOf('dotnet publish')) 'package.ps1 -Store must write the release notes before building.'
} finally { Remove-Item -LiteralPath $dir -Recurse -Force }
"OK release notes: versioned sections, plain text, $version in English and French, limit and missing sections refused"
