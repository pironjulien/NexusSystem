# The Store's release notes ("What's new in this version" in Partner Center) for one app version, in English and in
# French, written as release-notes.en.txt and release-notes.fr.txt next to the package (package.ps1 -Store calls it
# before building). English: the `## [app <version>]` section of CHANGELOG.md. French: the same section of
# app/store/release-notes.fr.md, its versioned translation. Nothing is written at submission time: a missing or empty
# section, a translation that does not have the same entries, or a text over Partner Center's limit stops here.
# On 2026-11-08, app 1.0.19.0 went to the Store with the notes of 1.0.18.0, typed by hand in Partner Center.
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Out,
    [string]$Changelog = (Join-Path $PSScriptRoot '..\..\CHANGELOG.md'),
    [string]$French = (Join-Path $PSScriptRoot '..\store\release-notes.fr.md')
)
$ErrorActionPreference = 'Stop'
# Partner Center's limit for "What's new in this version", counted with Windows line breaks (two characters each).
$limit = 1500

# The lines of the `## [app <version>]` section (without its heading), or an error naming the file.
function Get-NexusAppSection([string]$Path, [string]$Version) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Release notes: $Path does not exist." }
    $lines = [IO.File]::ReadAllLines($Path)
    $heading = '^## \[app ' + [regex]::Escape($Version) + '\](\s|$)'
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $heading) { $start = $i; break } }
    if ($start -lt 0) { throw "Release notes: no '## [app $Version]' section in $Path." }
    $section = @()
    for ($i = $start + 1; $i -lt $lines.Count -and $lines[$i] -notmatch '^## '; $i++) { $section += $lines[$i] }
    return , $section
}

# Markdown section to plain text: each group heading on its own line, each entry one line starting with a bullet,
# code, emphasis and link markup removed.
function ConvertTo-NexusPlainNote([string[]]$Section) {
    $blocks = [Collections.Generic.List[object]]::new()
    $entry = $null
    foreach ($line in $Section) {
        if ($line -match '^### (.+)$') {
            $entry = $null; $blocks.Add(@{ Heading = $Matches[1].Trim() })
        } elseif ($line -match '^- (.+)$') {
            $entry = [Text.StringBuilder]::new($Matches[1].Trim()); $blocks.Add(@{ Entry = $entry })
        } elseif ($line -match '^\s+(\S.*)$' -and $entry) {
            [void]$entry.Append(' ').Append($Matches[1].Trim())
        } elseif ($line.Trim()) {
            throw "Release notes: unexpected line in the section: '$line' (entries are '- ' bullets under '### ' groups)."
        }
    }
    $entries = @($blocks | Where-Object { $_.Entry })
    if (-not $entries) { throw 'Release notes: the section has no entry.' }
    $out = [Collections.Generic.List[string]]::new()
    foreach ($block in $blocks) {
        if ($block.Heading) {
            if ($out.Count) { $out.Add('') }
            $out.Add($block.Heading)
        } else {
            $text = $block.Entry.ToString()
            $text = $text -replace '\[([^\]]+)\]\([^)]*\)', '$1' -replace '\*\*([^*]+)\*\*', '$1' -replace '`', ''
            $out.Add([char]0x2022 + ' ' + $text)
        }
    }
    return [pscustomobject]@{ Text = ($out -join "`r`n"); Entries = $entries.Count }
}

$notes = [ordered]@{}
foreach ($language in @(@{ Code = 'en'; Path = $Changelog }, @{ Code = 'fr'; Path = $French })) {
    $path = [IO.Path]::GetFullPath($language.Path)
    try { $note = ConvertTo-NexusPlainNote (Get-NexusAppSection $path $Version) }
    catch { throw "$($_.Exception.Message) ($($language.Code), $path)" }
    if ($note.Text.Length -gt $limit) {
        throw "Release notes ($($language.Code)): $($note.Text.Length) characters for app $Version, Partner Center accepts $limit. Shorten the section in $path."
    }
    $notes[$language.Code] = $note
}
if ($notes.en.Entries -ne $notes.fr.Entries) {
    throw "Release notes: app $Version has $($notes.en.Entries) entries in English and $($notes.fr.Entries) in French; update the translation in $([IO.Path]::GetFullPath($French))."
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
foreach ($code in $notes.Keys) {
    $file = Join-Path ([IO.Path]::GetFullPath($Out)) "release-notes.$code.txt"
    # Exactly the text to paste: no final line break, which Partner Center would keep.
    [IO.File]::WriteAllText($file, $notes[$code].Text, $utf8)
    $file
}
