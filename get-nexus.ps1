# Nexus pack: the whole Nexus ecosystem on a Windows 10/11 PC, for the AI apps found on it, in one line:
#   irm https://raw.githubusercontent.com/pironjulien/NexusSystem/main/get-nexus.ps1 | iex
# With options:
#   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/pironjulien/NexusSystem/main/get-nexus.ps1))) -Only windows,browser
# Windows PowerShell 5.1 or PowerShell 7, no administrator rights (Git for Claude Code and Node.js for Codex, when
# missing, ask Windows once).
# It finds Claude Code, Claude Desktop, Codex and Antigravity, shows what it will install and for whom, installs the
# shared prerequisites once (uv; Git through winget when Claude Code is there and git is missing; Node.js LTS through
# winget when Codex is there), then runs each product's own
# official installer, unchanged: nothing of their logic is copied here. Claude Code gets the plugins of this
# catalog. Then it checks what each AI app now declares and prints a summary and the steps left to a human.
# Run it again at any time: every installer only does what is missing.
#   -Only / -Skip  product names (windows, android, browser, relay, agymail, keypad) or AI names (claude-code,
#                  claude-desktop, codex, antigravity), comma separated
#   -DryRun        show the plan and do nothing
#   -NoGuide       do not open Chrome on its extensions page at the end (the guide is printed instead)
#   -Catalog       marketplace given to Claude Code (default pironjulien/NexusSystem; a local folder for tests)
#   -Json          also print one "NEXUS_PACK_EVENT {json}" line per event (found, plan, step, cell, done), for the
#                  Nexus apps that run this same script and show its progress
param(
    [string[]]$Only = @(),
    [string[]]$Skip = @(),
    [switch]$DryRun,
    [switch]$NoGuide,
    [string]$Catalog = 'pironjulien/NexusSystem',
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
$EmitEvents = $Json.IsPresent
$ProgressPreference = 'SilentlyContinue'  # Invoke-WebRequest is many times slower with its progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$Ok = [string][char]0x2713
$No = [string][char]0x2717
$Raw = 'https://raw.githubusercontent.com/pironjulien'
$Marketplace = 'nexus-plugins'

# AI apps, in display order. Claude Desktop also carries Claude Code (its Code tab reads the same ~\.claude).
$AiNames = [ordered]@{
    'claude-code'    = 'Claude Code'
    'claude-desktop' = 'Claude Desktop'
    'codex'          = 'Codex'
    'antigravity'    = 'Antigravity'
}

# Products. Each AI app is served by exactly one route: the catalog plugin (Claude Code, Plugin) or the product
# installer. Targets: the AI apps a product serves (an empty list: a device, the MX Creative Keypad).
$AllAi = @('claude-code', 'claude-desktop', 'codex', 'antigravity')
$Products = @(
    @{ Key = 'windows'; Name = 'NexusWindows'; Plugin = 'nexus-windows'; Agy = 'nexus-computer'; Server = 'nexus-windows'
       Targets = $AllAi },
    @{ Key = 'android'; Name = 'NexusAndroid'; Plugin = 'nexus-android'; Agy = 'nexus-android'; Server = 'nexus-android'
       Targets = $AllAi },
    @{ Key = 'browser'; Name = 'NexusBrowser'; Plugin = 'nexus-browser'; Agy = 'nexus-browser'; Server = 'nexus-browser'
       Targets = $AllAi },
    # Two AI apps talking: one host opens the exchanges (NexusRelay 1.4.0: the first of Claude Code, Codex,
    # Antigravity among those configured), the others answer. Claude Desktop's chat is not a relay client.
    # Claude Code gets its plugin from this catalog (1.6.26); the daemon, Codex and Antigravity come from its installer,
    # which sees the plugin and adds no second server. Daemon: the installer still serves Claude Code (it is told every
    # chosen client, so the host stays Claude when present, and a daemon that failed marks Claude Code failed too).
    @{ Key = 'relay'; Name = 'NexusRelay'; Plugin = 'nexus-relay'; Daemon = $true; Agy = 'nexus-relay'; Server = 'nexus-relay'
       Targets = @('claude-code', 'codex', 'antigravity'); MinAis = 2 },
    # Claude and Codex have their own official Gmail connector.
    @{ Key = 'agymail'; Name = 'NexusAGYmail'; Agy = 'nexus-agymail'; Server = 'nexus-agymail'; Targets = @('antigravity') },
    @{ Key = 'keypad'; Name = 'NexusKeypad'; Targets = @(); Device = 'Logi Options+' }
)

function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

function Send-Event([hashtable]$Data) {
    if ($EmitEvents) { Write-Host ('NEXUS_PACK_EVENT ' + (New-Object PSObject -Property $Data | ConvertTo-Json -Compress -Depth 5)) }
}

function Test-LogiService {
    # The folder of the Logitech plugin service (Logi Options+, opened once), where NexusKeypad goes.
    return Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService')
}

function Get-ClaudeCli {
    $cli = Get-Command claude -ErrorAction SilentlyContinue
    if ($cli) { return $cli.Source }
    $local = Join-Path $env:USERPROFILE '.local\bin\claude.exe'
    if (Test-Path -LiteralPath $local) { return $local }
    return $null
}

function Get-DesktopFolder {
    # Where Claude Desktop reads claude_desktop_config.json: the classic install (%APPDATA%\Claude), the Store (MSIX)
    # one redirected into its package, and the newer Store versions' %LOCALAPPDATA%\Claude-Data\<package>. A folder
    # whose data moved away holds only a ".user-data-left.*" marker: Claude no longer reads it.
    $folders = @()
    $candidates = @(Join-Path $env:APPDATA 'Claude')
    $packages = Join-Path $env:LOCALAPPDATA 'Packages'
    if (Test-Path -LiteralPath $packages) {
        foreach ($package in Get-ChildItem -LiteralPath $packages -Directory -Filter '*Claude_*' -ErrorAction SilentlyContinue) {
            $candidates += Join-Path $package.FullName 'LocalCache\Roaming\Claude'
        }
    }
    $data = Join-Path $env:LOCALAPPDATA 'Claude-Data'
    if (Test-Path -LiteralPath $data) {
        $candidates += @(Get-ChildItem -LiteralPath $data -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    }
    foreach ($folder in $candidates) {
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) { continue }
        if (Get-ChildItem -LiteralPath $folder -Force -Filter '.user-data-left.*' -ErrorAction SilentlyContinue) { continue }
        $folders += $folder
    }
    return , $folders
}

function Find-AiApp {
    $found = [ordered]@{}
    $desktop = Get-DesktopFolder
    if ($desktop.Count) { $found['claude-desktop'] = $true }
    if ((Get-ClaudeCli) -or (Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.claude')) -or $desktop.Count) {
        $found['claude-code'] = $true
    }
    if ((Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.codex')) -or (Get-Command codex -ErrorAction SilentlyContinue)) {
        $found['codex'] = $true
    }
    $gemini = Join-Path $env:USERPROFILE '.gemini'
    if ((Test-Path -LiteralPath (Join-Path $gemini 'antigravity')) -or (Test-Path -LiteralPath (Join-Path $gemini 'config')) -or
        (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Programs\Antigravity'))) {
        $found['antigravity'] = $true
    }
    $ordered = @()
    foreach ($key in $AiNames.Keys) { if ($found.Contains($key)) { $ordered += $key } }
    return , $ordered
}

function Split-NameList([string[]]$Names) {
    $out = @()
    foreach ($name in $Names) { foreach ($part in ($name -split '[,\s]+')) { if ($part) { $out += $part.ToLowerInvariant() } } }
    return , $out
}

function Get-Product([string]$Key) { return $Products | Where-Object { $_.Key -eq $Key } | Select-Object -First 1 }

function Test-Wanted([string]$Key, [string[]]$Universe, [string[]]$OnlyList, [string[]]$SkipList) {
    $scoped = @($OnlyList | Where-Object { $Universe -contains $_ })
    if ($scoped.Count -and $scoped -notcontains $Key) { return $false }
    return $SkipList -notcontains $Key
}

function Read-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $text = [IO.File]::ReadAllText($Path)
    if (-not $text.Trim()) { return $null }
    return $text | ConvertFrom-Json
}

function Write-JsonWithBackup([string]$Path, $Value) {
    if (Test-Path -LiteralPath $Path) {
        $backup = Join-Path $env:USERPROFILE ".nexus\pack\backups\$(Split-Path $Path -Leaf).$(Get-Date -Format yyyyMMdd-HHmmss)"
        New-Item -ItemType Directory -Force -Path (Split-Path $backup) | Out-Null
        Copy-Item -LiteralPath $Path -Destination $backup
    } else {
        New-Item -ItemType Directory -Force -Path (Split-Path $Path) | Out-Null
    }
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 100), (New-Object Text.UTF8Encoding $false))
}

function Write-Property($Object, [string]$Name, $Value) {
    if ($Object.PSObject.Properties[$Name]) { $Object.$Name = $Value } else { $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value }
}

function Invoke-Console([string]$File, [string[]]$Arguments) {
    # A program in this same console (its colors and progress kept, nothing captured); sets $LASTEXITCODE.
    $line = ($Arguments | ForEach-Object { if ($_ -match '[\s"]' -or -not $_) { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $process = Start-Process -FilePath $File -ArgumentList $line -NoNewWindow -PassThru
    $null = $process.Handle  # keeps the exit code readable once it has exited (Windows PowerShell 5.1)
    $process.WaitForExit()
    $global:LASTEXITCODE = $process.ExitCode
}

function Invoke-Native([string]$What, [scriptblock]$Command) {
    # Output goes to the console; only the exit code is kept. Windows PowerShell 5.1 turns a native program's
    # stderr into errors under 'Stop', so 'Continue' while it runs.
    Say ''
    Say "== $What" Cyan
    Send-Event @{ type = 'step'; name = $What; state = 'start' }
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $global:LASTEXITCODE = 0
        & $Command | Out-Host
        $code = $LASTEXITCODE
    } catch {
        Say "$_" Red
        $code = 1
    } finally {
        $ErrorActionPreference = $saved
    }
    if ($code) { Say "$What failed (exit code $code)." Red }
    Send-Event @{ type = 'step'; name = $What; state = 'end'; code = [int]$code }
    return [int]$code
}

function Invoke-Installer([string]$What, [string]$Url, [string]$Work) {
    $file = Join-Path $Work ("$([guid]::NewGuid().ToString('N').Substring(0, 8))-" + [IO.Path]::GetFileName(([Uri]$Url).AbsolutePath))
    try {
        Invoke-WebRequest -UseBasicParsing $Url -OutFile $file
    } catch {
        Say ''
        Say "== $What" Cyan
        Say "Download failed: $Url ($($_.Exception.Message))" Red
        return 1
    }
    $shell = (Get-Process -Id $PID).Path  # the same PowerShell that runs the pack
    return Invoke-Native $What { Invoke-Console $shell @('-NoProfile', '-ExecutionPolicy', 'RemoteSigned', '-File', $file) }
}

function Sync-SessionPath([switch]$Registry) {
    # uv's folder first (where its installer puts it). -Registry: a program just installed (Node.js) is on the
    # registry PATH, which this window only gets this way.
    $known = @($env:PATH -split ';' | Where-Object { $_ })
    if ($Registry) {
        foreach ($scope in 'Machine', 'User') {
            foreach ($entry in ([Environment]::GetEnvironmentVariable('Path', $scope) -split ';')) {
                if ($entry -and $known -notcontains $entry) { $env:PATH = "$env:PATH;$entry"; $known += $entry }
            }
        }
    }
    $uvBin = Join-Path $env:USERPROFILE '.local\bin'
    if ($known -notcontains $uvBin) { $env:PATH = "$uvBin;$env:PATH" }
}

function Get-Uvx {
    # Its installer's folder first: PowerShell keeps a failed lookup of a program installed meanwhile.
    $local = Join-Path $env:USERPROFILE '.local\bin\uvx.exe'
    if (Test-Path -LiteralPath $local) { return $local }
    $uvx = Get-Command uvx -ErrorAction SilentlyContinue
    if ($uvx) { return $uvx.Source }
    return $null
}

function Get-NodeMajor {
    $node = Get-Command node.exe -ErrorAction SilentlyContinue
    if (-not $node) { return 0 }
    $version = & $node.Source --version
    if ($version -match '^v(\d+)\.') { return [int]$Matches[1] }
    return 0
}

function Test-Git { return [bool](Get-Command git.exe -ErrorAction SilentlyContinue) }

function Get-CodexCli {
    # The desktop ships the same signed engine as the CLI, without necessarily putting it on PATH.
    # Never download another Codex or execute an arbitrary executable found beside the desktop app.
    $candidates = @()
    foreach ($command in @(Get-Command codex -All -ErrorAction SilentlyContinue)) {
        if (-not $command.Source) { continue }
        if ([IO.Path]::GetFileName($command.Source) -ieq 'codex.exe') { $candidates += $command.Source }
        $package = Join-Path (Split-Path $command.Source) 'node_modules\@openai\codex'
        if (Test-Path -LiteralPath $package -PathType Container) {
            $candidates += @(Get-ChildItem -LiteralPath $package -Recurse -Filter codex.exe -File | ForEach-Object { $_.FullName })
        }
    }
    $candidates += @(Get-Process -Name codex -ErrorAction SilentlyContinue | Where-Object { $_.Path } | ForEach-Object {
        $engine = Join-Path (Split-Path $_.Path) 'resources\codex.exe'
        if (Test-Path -LiteralPath $engine) { $engine } else { $_.Path }
    })
    $desktop = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
    if (Test-Path -LiteralPath $desktop -PathType Container) {
        $candidates += @(Get-ChildItem -LiteralPath $desktop -Directory | Sort-Object LastWriteTime -Descending | ForEach-Object {
            Join-Path $_.FullName 'codex.exe'
        })
    }
    if (Get-Command Get-AppxPackage -ErrorAction SilentlyContinue) {
        $candidates += @(Get-AppxPackage -Name OpenAI.Codex -ErrorAction SilentlyContinue | ForEach-Object {
            Join-Path $_.InstallLocation 'app\resources\codex.exe'
        })
    }
    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $signature = Get-AuthenticodeSignature -LiteralPath $candidate
        if ($signature.Status -eq 'Valid' -and $signature.SignerCertificate -and
            $signature.SignerCertificate.Subject -match '(^|,\s*)(CN|O)="OpenAI OpCo, LLC"(?=,|$)') { return $candidate }
    }
    return $null
}

function Get-CodexHome {
    if ($env:CODEX_HOME) { return $env:CODEX_HOME }
    return Join-Path $env:USERPROFILE '.codex'
}

function Read-CodexTomlKeyPath([string]$Text) {
    # TOML keys, including literal/basic quoted keys, escaped Unicode and spaces around dots. Values are never
    # evaluated as PowerShell. Anything outside this grammar is a visible failure before configuration mutation.
    $keys = @(); $rest = $Text.Trim()
    while ($rest) {
        $key = [regex]::Match($rest, '^("(?:[^"\\\r\n]|\\.)*"|''[^''\r\n]*''|[A-Za-z0-9_-]+)[\t ]*')
        if (-not $key.Success) { throw 'Unsupported Codex TOML key; configuration was kept.' }
        $value = $key.Groups[1].Value
        if ($value.StartsWith("'")) { $value = $value.Substring(1, $value.Length - 2) }
        elseif ($value.StartsWith('"')) {
            # JSON and TOML basic keys share their escapes, except TOML's eight-digit Unicode form.
            $value = [regex]::Replace($value, '(?<!\\)((?:\\\\)*)\\U([0-9A-Fa-f]{8})', {
                param($match_)
                $match_.Groups[1].Value + [char]::ConvertFromUtf32([Convert]::ToInt32($match_.Groups[2].Value, 16))
            })
            $value = ConvertFrom-Json $value -ErrorAction Stop
        }
        $keys += $value
        $rest = $rest.Substring($key.Length)
        if (-not $rest) { break }
        if (-not $rest.StartsWith('.')) { throw 'Unsupported Codex TOML key; configuration was kept.' }
        $rest = $rest.Substring(1).TrimStart()
        if (-not $rest) { throw 'Incomplete Codex TOML key; configuration was kept.' }
    }
    return , $keys
}

function Get-CodexTomlTable([string]$Text) {
    # A small lexical scanner, not a regex over the whole document: a fake header inside a multiline string or
    # array must never become an editable table. Retain character offsets into the original text for surgical removal.
    $tables = @(); $current = [pscustomobject]@{ Path=@(); Index=0; Length=0; Body=''; Assignments=@(); Array=$false }
    $quote = ''; $depth = 0; $offset = 0
    $keyPattern = '(?:"(?:[^"\\\r\n]|\\.)*"|''[^''\r\n]*''|[A-Za-z0-9_-]+)'
    foreach ($lineMatch in [regex]::Matches($Text, '[^\r\n]*(?:\r?\n|\z)')) {
        if (-not $lineMatch.Length) { continue }
        $line = $lineMatch.Value.TrimEnd("`r", "`n")
        if (-not $quote -and -not $depth -and $line -match '^[\t ]*\[') {
            $header = [regex]::Match($line, '^[\t ]*(?<open>\[\[?)[\t ]*(?<path>' + $keyPattern + '(?:[\t ]*\.[\t ]*' + $keyPattern + ')*)[\t ]*(?<close>\]\]?)[\t ]*(?:#.*)?$')
            if (-not $header.Success -or $header.Groups['open'].Length -ne $header.Groups['close'].Length) {
                throw 'Unsupported Codex TOML table; configuration was kept.'
            }
            $current.Length = $offset - $current.Index
            $tables += $current
            $current = [pscustomobject]@{ Path=(Read-CodexTomlKeyPath $header.Groups['path'].Value); Index=$offset; Length=0; Body=''; Assignments=@(); Array=($header.Groups['open'].Length -eq 2) }
            $offset += $lineMatch.Length
            continue
        }
        if (-not $quote -and -not $depth -and $line -notmatch '^[\t ]*(#|$)') {
            $assignment = [regex]::Match($line, '^[\t ]*(?<key>' + $keyPattern + '(?:[\t ]*\.[\t ]*' + $keyPattern + ')*)[\t ]*=[\t ]*(?<value>.*)$')
            if (-not $assignment.Success) { throw 'Unsupported Codex TOML assignment; configuration was kept.' }
            $current.Assignments += [pscustomobject]@{ Path=(Read-CodexTomlKeyPath $assignment.Groups['key'].Value); Value=$assignment.Groups['value'].Value }
        }
        $current.Body += $lineMatch.Value
        for ($i=0; $i -lt $line.Length; $i++) {
            $c = $line[$i]
            if ($quote) {
                if ($quote.StartsWith('"') -and $c -eq '\') { $i++; continue }
                if ($quote.Length -eq 3) {
                    if ($i + 2 -lt $line.Length -and $line.Substring($i,3) -ceq $quote) {
                        $i += 2
                        while ($i + 1 -lt $line.Length -and $line[$i + 1] -eq $quote[0]) { $i++ }
                        $quote = ''
                    }
                } elseif ([string]$c -ceq $quote) { $quote = '' }
                continue
            }
            if ($c -eq '#') { break }
            if ($c -eq '"' -or $c -eq "'") {
                $quote = [string]$c
                if ($i + 2 -lt $line.Length -and $line.Substring($i,3) -ceq ($quote * 3)) { $quote *= 3; $i += 2 }
            } elseif ($c -eq '[' -or $c -eq '{') { $depth++ }
            elseif ($c -eq ']' -or $c -eq '}') { $depth-- }
        }
        if (($quote -and $quote.Length -eq 1) -or $depth -lt 0) { throw 'Incomplete Codex TOML value; configuration was kept.' }
        $offset += $lineMatch.Length
    }
    if ($quote -or $depth) { throw 'Incomplete Codex TOML value; configuration was kept.' }
    $current.Length = $Text.Length - $current.Index
    $tables += $current
    return $tables
}

function Get-CodexPluginState([string]$Text, [string]$Name, [string]$CatalogName = '') {
    $found = $false; $unknown = $false; $disabled = $false
    foreach ($table in @(Get-CodexTomlTable $Text)) {
        if ($table.Path.Count -ge 2 -and $table.Path[0] -ceq 'plugins' -and
            $table.Path[1].StartsWith("$Name@") -and (-not $CatalogName -or $table.Path[1] -ceq "$Name@$CatalogName")) {
            if ($table.Path.Count -ne 2 -or $table.Array) { $unknown = $true } else { $found = $true }
        }
        foreach ($assignment in $table.Assignments) {
            $path_ = @($table.Path) + @($assignment.Path)
            if ($path_[0] -cne 'plugins') { continue }
            if ($path_.Count -eq 1) { $unknown = $true; continue } # inline plugins = {...}: keep, never infer missing
            if (-not $path_[1].StartsWith("$Name@") -or ($CatalogName -and $path_[1] -cne "$Name@$CatalogName")) { continue }
            if ($path_.Count -eq 2) { $unknown = $true; continue } # inline plugin = {...}
            $found = $true
            if ($path_.Count -eq 3 -and $path_[2] -ceq 'enabled') {
                if ($assignment.Value -match '^false[\t ]*(?:#.*)?$') { $disabled = $true }
                elseif ($assignment.Value -notmatch '^true[\t ]*(?:#.*)?$') { $unknown = $true }
            }
        }
    }
    if ($disabled) { return 'disabled' }
    if ($unknown) { return 'unknown' }
    if ($found) { return 'enabled' }
    return 'missing'
}

function Get-CodexServerTable([string]$Text, [string]$Name) {
    foreach ($table in @(Get-CodexTomlTable $Text)) {
        if ($table.Path.Count -ge 2 -and $table.Path[0] -ceq 'mcp_servers' -and $table.Path[1] -ceq $Name) {
            $sub = if ($table.Path.Count -gt 2) { '.' + ($table.Path[2..($table.Path.Count-1)] -join '.') } else { '' }
            [pscustomobject]@{ Index=$table.Index; Length=$table.Length; Body=$table.Body; Sub=$sub; Assignments=$table.Assignments; Unsupported=$table.Array }
        } else {
            foreach ($assignment in $table.Assignments) {
                $path_ = @($table.Path) + @($assignment.Path)
                if ($path_[0] -ceq 'mcp_servers' -and ($path_.Count -eq 1 -or $path_[1] -ceq $Name)) {
                    # Inline/dotted declarations are valid TOML, but are not installer-owned editable tables.
                    [pscustomobject]@{ Index=$table.Index; Length=$table.Length; Body=$table.Body; Sub=''; Assignments=$table.Assignments; Unsupported=$true }
                    break
                }
            }
        }
    }
}

function Read-CodexTomlString([string]$Value) {
    if ($Value -match "^'([^']*)'$" ) { return $Matches[1] }
    if ($Value -match '^"(?:[^"\\]|\\.)*"$') { return ConvertFrom-Json $Value }
    throw 'Not a simple TOML string.'
}

function Test-CodexOwnedServer([string]$Text, [string]$Name) {
    # Recognize only complete declarations written by the official installers. A same-named Docker command,
    # extra argument, custom URL, environment variable or unsupported TOML form belongs to the user and is kept.
    $tables = @(Get-CodexServerTable $Text $Name)
    if (-not $tables.Count) { return $false }
    $root = @{}; $environment = @{}
    try {
        foreach ($table in $tables) {
            if ($table.Unsupported) { return $false }
            $sub = $table.Sub
            if ($sub -and $sub -ne '.env') { return $false }
            $values = if ($sub) { $environment } else { $root }
            foreach ($line in ($table.Body -split "`r?`n")) {
                if (-not $line.Trim() -or $line -match '^\s*#') { continue }
                if ($line -notmatch '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$') { return $false }
                $key = $Matches[1]; $value = $Matches[2]
                if ($values.ContainsKey($key)) { return $false }
                if ($key -eq 'args' -and -not $sub) {
                    if ($value -notmatch '^\[(.*)\]$') { return $false }
                    $items = $Matches[1]; $args_ = @()
                    while ($items.Trim()) {
                        if ($items -notmatch '^\s*("(?:[^"\\]|\\.)*"|''[^'']*'')\s*(,\s*|$)') { return $false }
                        $args_ += Read-CodexTomlString $Matches[1]
                        $items = $items.Substring($Matches[0].Length)
                    }
                    $values[$key] = $args_
                } elseif ($key -eq 'enabled' -and -not $sub) {
                    if ($value -ne 'true') { return $false }; $values[$key] = $true
                } elseif ($key -eq 'startup_timeout_sec' -and -not $sub) {
                    if ($value -notmatch '^\d+$') { return $false }; $values[$key] = $value
                } else { $values[$key] = Read-CodexTomlString $value }
            }
        }
    } catch { return $false }
    $allowed = @('command', 'args', 'startup_timeout_sec', 'enabled')
    if ($Name -eq 'nexus-relay') { $allowed = @('url', 'bearer_token_env_var', 'startup_timeout_sec', 'enabled') }
    if (@($root.Keys | Where-Object { $allowed -notcontains $_ }).Count) { return $false }
    $command = [IO.Path]::GetFileName([string]$root['command']).ToLowerInvariant()
    $args_ = @($root['args'])
    switch ($Name) {
        'nexus-windows' {
            return -not $environment.Count -and $command -in @('npx', 'npx.cmd', 'npx.exe') -and $args_.Count -eq 2 -and
                $args_[0] -in @('-y', '--yes') -and $args_[1] -match '^nexus-windows-codex(@latest)?$'
        }
        'nexus-android' {
            $package = $args_ -join ' '
            return -not $environment.Count -and $command -in @('uvx', 'uvx.exe') -and
                $package -match '^(--python 3\.14 )?nexus-android(==\d+\.\d+\.\d+)?$'
        }
        'nexus-browser' {
            if ($environment.Count -ne 1 -or $environment['NEXUS_BROWSER_AGENT'] -ne 'Codex' -or $args_.Count -ne 2 -or $args_[0] -ne '-B') { return $false }
            $locations = @(
                @((Join-Path $env:LOCALAPPDATA 'NexusBrowser\venv\Scripts\python.exe'), (Join-Path $env:LOCALAPPDATA 'NexusBrowser\app\client\mcp_server.py')),
                @((Join-Path $env:USERPROFILE '.nexus\nexus-browser\venv\Scripts\python.exe'), (Join-Path $env:LOCALAPPDATA 'NexusBrowser\app\client\mcp_server.py'))
            )
            foreach ($location in $locations) { if ($root['command'] -ieq $location[0] -and $args_[1] -ieq $location[1]) { return $true } }
            return $false
        }
        'nexus-relay' {
            return -not $environment.Count -and $root['url'] -eq 'http://127.0.0.1:16181/mcp?agent=codex' -and
                $root['bearer_token_env_var'] -eq 'NEXUS_MCP_TOKEN'
        }
    }
    return $false
}

function Remove-CodexOwnedServer {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([string]$Name)
    $path = Join-Path (Get-CodexHome) 'config.toml'
    if (-not (Test-Path -LiteralPath $path)) { return }
    $text = [IO.File]::ReadAllText($path)
    if (-not @(Get-CodexServerTable $text $Name).Count) { return }
    if ((Get-CodexPluginState $text $Name) -ne 'enabled' -or -not (Test-CodexOwnedServer $text $Name)) {
        throw "${Name}: direct Codex declaration changed or belongs to you; it was kept."
    }
    $updated = $text
    foreach ($table in @(Get-CodexServerTable $text $Name) | Sort-Object Index -Descending) {
        $updated = $updated.Remove($table.Index, $table.Length)
    }
    if (-not $PSCmdlet.ShouldProcess($path, "Remove only the old official $Name declaration, now served by its installed plugin")) { return }
    $backups = Join-Path $env:USERPROFILE '.nexus\pack\backups'
    New-Item -ItemType Directory -Force $backups | Out-Null
    Copy-Item -LiteralPath $path -Destination (Join-Path $backups ("codex-config-$([guid]::NewGuid().ToString('N')).toml"))
    $temporary = "$path.nexus-$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporary, $updated, (New-Object Text.UTF8Encoding $false))
        if ([IO.File]::ReadAllText($path) -cne $text) { throw 'Codex configuration changed meanwhile; run the pack again.' }
        Move-Item -LiteralPath $temporary -Destination $path -Force
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    Say "${Name}: the plugin now provides its tools and guide; the old direct declaration was removed."
}

function Test-CodexPluginInstalled([string]$Name, [string]$InstalledPath) {
    $home_ = Get-CodexHome
    $path = Join-Path $home_ 'config.toml'
    if (-not (Test-Path -LiteralPath $path) -or (Get-CodexPluginState ([IO.File]::ReadAllText($path)) $Name) -ne 'enabled') { return $false }
    $cache = Join-Path $home_ "plugins\cache\$Marketplace\$Name"
    $versions = if ($InstalledPath) { @(Get-Item -LiteralPath $InstalledPath -ErrorAction SilentlyContinue) }
                else { @(Get-ChildItem -LiteralPath $cache -Directory -ErrorAction SilentlyContinue) }
    foreach ($version in $versions) {
        if (-not $version.PSIsContainer -or (Split-Path $version.FullName) -ine $cache) { continue }
        $manifest = Read-Json (Join-Path $version.FullName '.codex-plugin\plugin.json')
        if ($manifest -and $manifest.name -eq $Name -and $manifest.skills -eq './skills/' -and
            (Test-Path -LiteralPath (Join-Path $version.FullName "skills\$Name\SKILL.md")) -and
            (Test-Path -LiteralPath (Join-Path $version.FullName '.mcp.json'))) { return $true }
    }
    return $false
}

function Install-CodexMarketplace([string]$Cli, [string]$Catalog) {
    return Invoke-Native 'Codex: Nexus plugin catalog' {
        # The CLI owns its configuration. Existing Git snapshots need an explicit refresh; local catalogs are read
        # in place (also how the offline recipe verifies the exact catalog about to be published).
        & $Cli plugin marketplace add $Catalog --json
        if ($LASTEXITCODE -eq 0 -and -not (Test-Path -LiteralPath $Catalog -PathType Container)) {
            & $Cli plugin marketplace upgrade $Marketplace --json
        }
    }
}

function Install-CodexPlugin([string]$Cli, [string]$Name) {
    return Invoke-Native "Codex: $Name tools and guide" {
        $output = & $Cli plugin add "$Name@$Marketplace" --json | Out-String
        $code = $LASTEXITCODE
        Write-Host $output
        if ($code) { $global:LASTEXITCODE = $code; return }
        $installed = $output | ConvertFrom-Json
        if ($installed.pluginId -ne "$Name@$Marketplace" -or -not $installed.installedPath -or
            -not (Test-CodexPluginInstalled $Name $installed.installedPath)) {
            throw 'Codex did not return an installed plugin with its MCP declaration and guide.'
        }
    }
}

function Install-ClaudePlugin([string[]]$Plugins) {
    # Claude Code: the plugins of this catalog (server pinned and locked, plus its skill). With the claude command,
    # its own plugin commands; without it (Claude Desktop's Code tab only), the documented settings.json keys, which
    # Claude installs from when the next conversations start.
    $cli = Get-ClaudeCli
    if ($cli) {
        return Invoke-Native 'Claude Code: Nexus plugins' {
            $list = & $cli plugin marketplace list 2>&1 | Out-String
            if ($list -notmatch "(?m)\b$Marketplace\b") {
                & $cli plugin marketplace add $Catalog
                if ($LASTEXITCODE) { return }
            } else {
                & $cli plugin marketplace update $Marketplace
                if ($LASTEXITCODE) { return }
            }
            foreach ($plugin in $Plugins) {
                & $cli plugin install "$plugin@$Marketplace" --scope user
                if ($LASTEXITCODE) { return }
            }
        }
    }
    return Invoke-Native 'Claude Code: Nexus plugins (settings.json)' {
        $path = Join-Path $env:USERPROFILE '.claude\settings.json'
        $settings = Read-Json $path
        $before = if ($settings) { $settings | ConvertTo-Json -Depth 100 } else { '' }
        if (-not $settings) { $settings = New-Object PSObject }
        if (-not $settings.PSObject.Properties['extraKnownMarketplaces']) { Write-Property $settings 'extraKnownMarketplaces' (New-Object PSObject) }
        if (-not $settings.extraKnownMarketplaces.PSObject.Properties[$Marketplace]) {
            $source = if (Test-Path -LiteralPath $Catalog) { @{ source = 'directory'; path = (Resolve-Path -LiteralPath $Catalog).Path } }
                      else { @{ source = 'github'; repo = $Catalog } }
            Write-Property $settings.extraKnownMarketplaces $Marketplace ([PSCustomObject]@{ source = [PSCustomObject]$source })
        }
        if (-not $settings.PSObject.Properties['enabledPlugins']) { Write-Property $settings 'enabledPlugins' (New-Object PSObject) }
        foreach ($plugin in $Plugins) { Write-Property $settings.enabledPlugins "$plugin@$Marketplace" $true }
        if (($settings | ConvertTo-Json -Depth 100) -eq $before) { Say "Already declared in $path."; return }
        Write-JsonWithBackup $path $settings
        Say "Declared in $path (installed by Claude when the next conversations start)."
    }
}

# --- Checks: what each AI app really declares --------------------------------------------------------------------
# Read before the installers (an AI app where the user turned a product off is left out for that product, never
# turned on again) and after them (the summary). A declaration is 'ok', 'missing', 'disabled' (turned off by the
# user) or 'invalid' (declared, but its command cannot be found).

function Test-CommandName([string]$Command) {
    if (-not $Command) { return $false }
    $expanded = [Environment]::ExpandEnvironmentVariables($Command)
    if ([IO.Path]::IsPathRooted($expanded)) { return Test-Path -LiteralPath $expanded }
    if ($expanded -eq 'uvx' -and (Get-Uvx)) { return $true }
    return [bool](Get-Command $expanded -ErrorAction SilentlyContinue)
}

function Get-JsonServerState($Server) {
    if ($Server.PSObject.Properties['disabled'] -and $Server.disabled -eq $true) { return 'disabled' }
    if ($Server.PSObject.Properties['url'] -and $Server.url) { return 'ok' }
    if (Test-CommandName ([string]$Server.command)) { return 'ok' }
    return 'invalid'
}

function Get-ClaudeCodeState($Product) {
    $settings = Read-Json (Join-Path $env:USERPROFILE '.claude\settings.json')
    $plugins = if ($settings -and $settings.enabledPlugins) { @($settings.enabledPlugins.PSObject.Properties) } else { @() }
    if ($Product.Plugin) {
        # This catalog's plugin: true installed and on, false turned off by the user.
        $mine = @($plugins | Where-Object { $_.Name -eq "$($Product.Plugin)@$Marketplace" })
        if ($mine.Count) {
            if ($mine[0].Value -eq $true) { return 'ok' }
            return 'disabled'
        }
        # No plugin (its route failed): the product's own installer then declares it itself, as below.
    }
    # Declared by the product's own installer, user scope (~\.claude.json), or a plugin of the same name the user has.
    $config = Read-Json (Join-Path $env:USERPROFILE '.claude.json')
    if ($config -and $config.mcpServers -and $config.mcpServers.PSObject.Properties[$Product.Server]) {
        return Get-JsonServerState $config.mcpServers.($Product.Server)
    }
    $named = @($plugins | Where-Object { $_.Name -like "$($Product.Server)@*" })
    if (@($named | Where-Object { $_.Value -eq $true }).Count) { return 'ok' }
    if ($named.Count) { return 'disabled' }
    return 'missing'
}

function Get-ClaudeDesktopState($Product) {
    $folders = Get-DesktopFolder
    if (-not $folders.Count) { return 'missing' }
    $states = @()
    foreach ($folder in $folders) {
        $config = Read-Json (Join-Path $folder 'claude_desktop_config.json')
        if (-not ($config -and $config.mcpServers -and $config.mcpServers.PSObject.Properties[$Product.Server])) { return 'missing' }
        $states += Get-JsonServerState $config.mcpServers.($Product.Server)
    }
    foreach ($state in 'disabled', 'invalid') { if ($states -contains $state) { return $state } }
    return 'ok'
}

function Get-TomlTable([string]$Text, [string]$Header) {
    # The lines of one TOML table, up to the next table header (its keys come before any sub-table); $null if absent.
    $body = $null
    foreach ($line in ($Text -split "`r?`n")) {
        if ($line -match '^\s*\[') {
            if ($null -ne $body) { break }
            if ($line -match $Header) { $body = New-Object System.Collections.ArrayList }
            continue
        }
        if ($null -ne $body) { [void]$body.Add($line) }
    }
    return , $body
}

function Get-CodexState($Product) {
    $home_ = Get-CodexHome
    $path = Join-Path $home_ 'config.toml'
    if (-not (Test-Path -LiteralPath $path)) { return 'missing' }
    $text = [IO.File]::ReadAllText($path)
    $pluginState = Get-CodexPluginState $text $Product.Server
    if ($pluginState -eq 'disabled') { return 'disabled' }
    $serverTables = @(Get-CodexServerTable $text $Product.Server)
    foreach ($table in $serverTables | Where-Object { -not $_.Sub }) {
        foreach ($assignment in $table.Assignments) {
            if ($assignment.Path.Count -eq 1 -and $assignment.Path[0] -ceq 'enabled' -and
                $assignment.Value -match '^false[\t ]*(?:#.*)?$') { return 'disabled' }
        }
    }
    foreach ($table in $serverTables | Where-Object { -not $_.Sub -and -not $_.Unsupported }) {
        foreach ($assignment in $table.Assignments) {
            if ($assignment.Path.Count -ne 1 -or $assignment.Path[0] -cnotin @('url', 'command')) { continue }
            try { $value = Read-CodexTomlString $assignment.Value } catch { continue }
            if ($assignment.Path[0] -ceq 'url' -or (Test-CommandName $value)) { return 'ok' }
        }
    }
    if ($pluginState -eq 'enabled') { return 'ok' }
    if ($pluginState -eq 'unknown' -or $serverTables.Count) { return 'invalid' }
    return 'missing'
}

function Get-AntigravityState($Product) {
    if (-not (Test-Path -LiteralPath (Join-Path $env:USERPROFILE ".gemini\config\plugins\$($Product.Agy)\plugin.json"))) { return 'missing' }
    # Turned off in Antigravity: plugins.<name>.enabled = false in ~\.gemini\config\config.json.
    $config = Read-Json (Join-Path $env:USERPROFILE '.gemini\config\config.json')
    if ($config -and $config.plugins -and $config.plugins.PSObject.Properties[$Product.Agy]) {
        $entry = $config.plugins.($Product.Agy)
        if ($entry.PSObject.Properties['enabled'] -and $entry.enabled -eq $false) { return 'disabled' }
    }
    return 'ok'
}

function Test-Device($Product) {
    # NexusKeypad: the state its plugin writes when loaded, else one of its editions in the folder the Logitech plugin
    # service loads: NexusKeypad (complete edition), TrinitePad (Marketplace "AI Coding Agents", or a build from the
    # sources), AiAgentsPc ("AI Coding Agents + PC"), PcDashboard ("PC Dashboard").
    if (Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.nexus\status\keypad.json')) { return $true }
    $plugins = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService\Plugins'
    return [bool]@('NexusKeypad', 'TrinitePad', 'AiAgentsPc', 'PcDashboard' | Where-Object { Test-Path -LiteralPath (Join-Path $plugins $_) }).Count
}

function Get-DeviceState($Product) { if (Test-Device $Product) { return 'ok' }; return 'missing' }

$Checks = @{
    'claude-code'    = ${function:Get-ClaudeCodeState}
    'claude-desktop' = ${function:Get-ClaudeDesktopState}
    'codex'          = ${function:Get-CodexState}
    'antigravity'    = ${function:Get-AntigravityState}
    'pc'             = ${function:Get-DeviceState}
}

# Products whose installer configures every AI app found in one run. It gets the AI apps to configure in
# NEXUS_PACK_TARGETS (pack names, comma separated). Until a product honors it (listed here), the pack does not run it
# when that would go beyond the selection: -Only/-Skip, or an AI app where the user turned the product off.
$OneRunForAll = @('android', 'browser', 'relay')
$HonorsTargets = @('android', 'relay', 'browser')  # nexus-android 0.14.1; NexusRelay 1.3.2, NexusBrowser 2.26.1 (their get.ps1 is always the latest)

# Why a product does not apply on this PC, or $null when it does.
function Get-Unfit($Product, [string[]]$Ais) {
    if ($Product.Device) {
        if (Test-LogiService) { return $null }
        return "needs $($Product.Device), opened once (https://www.logitech.com/software/logi-options-plus.html)"
    }
    if ($Product.Needs -and $Ais -notcontains $Product.Needs) { return "needs $($AiNames[$Product.Needs])" }
    if ($Product.MinAis -and @($Product.Targets | Where-Object { $Ais -contains $_ }).Count -lt $Product.MinAis) {
        return "needs two of $((@($Product.Targets) | ForEach-Object { $AiNames[$_] }) -join ', ')"
    }
    if (-not @($Product.Targets | Where-Object { $Ais -contains $_ }).Count) {
        return "for $((@($Product.Targets) | ForEach-Object { $AiNames[$_] }) -join ', ') only"
    }
    return $null
}

# The cells of a product: its AI apps found here, or 'pc' for a device.
function Get-Cell($Product, [string[]]$Ais) {
    if ($Product.Device) { return , @('pc') }
    return , @($Ais | Where-Object { $Product.Targets -contains $_ })
}

# --- Main ---------------------------------------------------------------------------------------------------------

function Invoke-NexusPack([string[]]$Only, [string[]]$Skip, [bool]$DryRun, [bool]$NoGuide, [string]$Catalog) {
    if ($env:OS -ne 'Windows_NT') { Say 'The Nexus pack runs on Windows 10 or 11 only.' Red; return 1 }
    $onlyList = Split-NameList $Only
    $skipList = Split-NameList $Skip
    $productKeys = @($Products | ForEach-Object { $_.Key })
    $aiKeys = @($AiNames.Keys)
    foreach ($name in $onlyList + $skipList) {
        if ($productKeys -notcontains $name -and $aiKeys -notcontains $name) {
            Say "Unknown name '$name'. Products: $($productKeys -join ', '). AI apps: $($aiKeys -join ', ')." Red
            return 2
        }
    }

    Say 'Nexus pack: computer use in the background for your AI apps (https://julienpiron.fr)' Cyan
    $found = Find-AiApp
    $ais = @($found | Where-Object { Test-Wanted $_ $aiKeys $onlyList $skipList })
    $wanted = @($Products | Where-Object { Test-Wanted $_.Key $productKeys $onlyList $skipList })
    $selected = @($wanted | Where-Object { -not (Get-Unfit $_ $ais) })
    if (-not $found.Count) {
        Say 'No AI app found (Claude Code, Claude Desktop, Codex, Antigravity): install one, open it once, then run this again.' Yellow
        return 1
    }
    Say ''
    Say "AI apps found: $(($found | ForEach-Object { $AiNames[$_] }) -join ', ')"
    Send-Event @{ type = 'found'; ais = @($found) }
    if (-not $ais.Count -or -not $selected.Count) {
        foreach ($product in $wanted | Where-Object { Get-Unfit $_ $ais }) { Say ("  {0,-13} {1}" -f $product.Name, (Get-Unfit $product $ais)) DarkGray }
        Say 'Nothing to install with these options.' Yellow
        return 0
    }
    Say 'It will install:'
    foreach ($product in $selected) {
        $for_ = if ($product.Device) { "your $($product.Device)" } else { ((Get-Cell $product $ais) | ForEach-Object { $AiNames[$_] }) -join ', ' }
        Say ("  {0,-13} for {1}" -f $product.Name, $for_)
    }
    foreach ($product in $wanted | Where-Object { Get-Unfit $_ $ais }) { Say ("  {0,-13} {1}" -f $product.Name, (Get-Unfit $product $ais)) DarkGray }

    # The user's own choices: an AI app where a product is turned off stays as it is for that product.
    $kept = @{}
    $codexCustom = @{}
    $codexExisting = @{}
    $codexUnrecognized = @{}
    foreach ($product in $selected) {
        $cells = Get-Cell $product $ais  # assigned first: a list returned with "," stays one item in foreach
        foreach ($ai in $cells) {
            if ($ai -ne 'pc' -and (& $Checks[$ai] $product) -eq 'disabled') { $kept["$($product.Key)|$ai"] = $true }
            if ($ai -eq 'codex' -and $product.Plugin -and -not $kept["$($product.Key)|$ai"]) {
                $configPath = Join-Path (Get-CodexHome) 'config.toml'
                $config = if (Test-Path -LiteralPath $configPath) { [IO.File]::ReadAllText($configPath) } else { '' }
                if ((Get-CodexPluginState $config $product.Server) -eq 'unknown') {
                    $kept["$($product.Key)|$ai"] = $true
                    $codexUnrecognized[$product.Key] = $true
                } elseif (@(Get-CodexServerTable $config $product.Server).Count -and -not (Test-CodexOwnedServer $config $product.Server)) {
                    $kept["$($product.Key)|$ai"] = $true
                    $codexCustom[$product.Key] = $true
                } elseif ((Get-CodexPluginState $config $product.Server) -eq 'enabled' -and
                    (Get-CodexPluginState $config $product.Server $Marketplace) -ne 'enabled') {
                    $kept["$($product.Key)|$ai"] = $true
                    $codexExisting[$product.Key] = $true
                }
            }
        }
    }
    $targets = @{}
    $refused = @{}
    foreach ($product in $selected) {
        if ($OneRunForAll -notcontains $product.Key) { continue }
        $touched = @($found | Where-Object {
            $product.Targets -contains $_ -and -not ($product.Plugin -and -not $product.Daemon -and $_ -eq 'claude-code') })
        $chosen = @($touched | Where-Object { $ais -contains $_ -and -not $kept["$($product.Key)|$_"] })
        $targets[$product.Key] = $chosen
        $beyond = @($touched | Where-Object { $chosen -notcontains $_ })
        if ($beyond.Count -and $HonorsTargets -notcontains $product.Key) { $refused[$product.Key] = $beyond }
    }
    foreach ($key in @($kept.Keys)) {
        $product, $ai = $key -split '\|'
        if ($ai -eq 'codex' -and $codexUnrecognized[$product]) {
            Say ("  {0,-13} Codex: unsupported plugin configuration kept; no second server or plugin added" -f (Get-Product $product).Name) Yellow
        } elseif ($ai -eq 'codex' -and $codexCustom[$product]) {
            Say ("  {0,-13} Codex: custom direct declaration kept; no second server or plugin added" -f (Get-Product $product).Name) Yellow
        } elseif ($ai -eq 'codex' -and $codexExisting[$product]) {
            Say ("  {0,-13} Codex: plugin from another catalog kept; no second plugin added" -f (Get-Product $product).Name) Yellow
        } else { Say ("  {0,-13} {1}: turned off by you, left as it is" -f (Get-Product $product).Name, $AiNames[$ai]) Yellow }
    }
    foreach ($key in @($refused.Keys)) {
        Say ("  {0,-13} not run: its installer would also configure {1}. Run it for every AI app, or wait for a {0} that takes a selection." -f (Get-Product $key).Name, (($refused[$key] | ForEach-Object { $AiNames[$_] }) -join ', ')) Yellow
    }

    $wants = @{}
    foreach ($product in $selected) { if (-not $refused[$product.Key]) { $wants[$product.Key] = $true } }
    $for = @{}
    foreach ($ai in $ais) { $for[$ai] = $true }
    $needsNode = $wants['windows'] -and $for['codex'] -and -not $kept['windows|codex'] -and (Get-NodeMajor) -lt 22
    # Claude Code adds a catalog by cloning it with git, which a new Windows lacks.
    $needsGit = (($for['claude-code'] -and @($selected | Where-Object { $_.Plugin -and -not $kept["$($_.Key)|claude-code"] }).Count) -or
        ($for['codex'] -and @($selected | Where-Object { $_.Plugin -and -not $kept["$($_.Key)|codex"] }).Count)) -and -not (Test-Git)
    Say ''
    $gitFor = @('claude-code', 'codex' | Where-Object { $for[$_] } | ForEach-Object { $AiNames[$_] }) -join ' and '
    Say "Prerequisites, once: uv$(if ($needsGit) { ", Git (for $gitFor)" })$(if ($needsNode) { ', Node.js LTS (for Codex)' })"
    Send-Event @{ type = 'plan'; ais = @($ais); products = @($selected | Where-Object { $wants[$_.Key] } | ForEach-Object { $_.Key }); node = [bool]$needsNode; git = [bool]$needsGit
                  installer_targets = $targets }  # AI apps told to each one-run installer (NEXUS_PACK_TARGETS)
    if ($DryRun) { Say 'Dry run: nothing installed.' Yellow; return 0 }

    $work = Join-Path ([IO.Path]::GetTempPath()) "nexus-pack-$PID"
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $savedPack = $env:NEXUS_PACK
    $env:NEXUS_PACK = '1'  # the product installers' non-interactive mode: no question, no wait, no window
    $results = @{}  # "product|ai" -> exit code of the route that serves it
    $own = @{}      # product -> exit code of its own installer, which declares Claude Code itself when no plugin does
    $prereq = @{}
    try {
        Sync-SessionPath
        if (-not (Get-Uvx)) {
            # The official installer saved to a file and run from it: a PowerShell started with "-Command irm ... | iex"
            # is what antivirus heuristics take for a malware loader (Microsoft Defender: Trojan:Win32/Commando.A!ml).
            $prereq['uv'] = Invoke-Native 'uv (Astral official installer)' {
                $installer = Join-Path $work 'uv-install.ps1'
                Invoke-WebRequest -UseBasicParsing 'https://astral.sh/uv/install.ps1' -OutFile $installer
                Invoke-Console 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'RemoteSigned', '-File', $installer)
            }
            Sync-SessionPath
            if (-not $prereq['uv'] -and -not (Get-Uvx)) { $prereq['uv'] = 1 }
        } else { $prereq['uv'] = 0 }
        if ($needsGit) {
            if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) {
                Say 'Git is needed to add the Nexus plugin catalog and winget is missing: install Git from https://git-scm.com/downloads/win, then run this again.' Red
                $prereq['git'] = 1
            } else {
                $prereq['git'] = Invoke-Native 'Git (official package, through winget)' {
                    Invoke-Console (Get-Command winget.exe).Source @('install', '--exact', '--id', 'Git.Git', '--source', 'winget', '--accept-source-agreements', '--accept-package-agreements')
                }
                Sync-SessionPath -Registry
                if (Test-Git) { $prereq['git'] = 0 } elseif (-not $prereq['git']) { $prereq['git'] = 1 }
            }
        }
        if ($needsNode) {
            if ((Get-NodeMajor) -ge 22) { $prereq['node'] = 0 }
            elseif (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) {
                Say 'Node.js 22 or later is needed by Codex and winget is missing: install the LTS from https://nodejs.org, then run this again.' Red
                $prereq['node'] = 1
            } else {
                $prereq['node'] = Invoke-Native 'Node.js LTS (official package, through winget)' {
                    Invoke-Console (Get-Command winget.exe).Source @('install', '--exact', '--id', 'OpenJS.NodeJS.LTS', '--source', 'winget', '--accept-source-agreements', '--accept-package-agreements')
                }
                Sync-SessionPath -Registry
                if ((Get-NodeMajor) -ge 22) { $prereq['node'] = 0 } elseif (-not $prereq['node']) { $prereq['node'] = 1 }
            }
        }

        # 1. Claude Code first: the product installers see its plugins and add no second server.
        if ($for['claude-code']) {
            $withPlugin = @($selected | Where-Object { $_.Plugin -and -not $kept["$($_.Key)|claude-code"] })
            if ($withPlugin.Count) {
                $code = if ($prereq['uv'] -or $prereq['git']) { 1 } else { Install-ClaudePlugin @($withPlugin | ForEach-Object { $_.Plugin }) }
                foreach ($product in $withPlugin) { $results["$($product.Key)|claude-code"] = $code }
            }
        }
        # Codex first gets the real plugin (MCP plus its guide). Product installers still prepare ADB, Chrome and
        # the relay daemon; the final pass below removes only their exact old MCP declarations after success.
        $codexPlugins = @($selected | Where-Object { $_.Plugin -and $for['codex'] -and -not $kept["$($_.Key)|codex"] })
        $codexCli = $null
        if ($codexPlugins.Count) {
            $codexCli = Get-CodexCli
            $catalogCode = if (-not $codexCli -or $prereq['git']) { 1 } else { Install-CodexMarketplace $codexCli $Catalog }
            if (-not $codexCli) { Say 'The installed Codex engine with a valid OpenAI signature was not found. Open or update Codex, then run this again.' Red }
            foreach ($product in $codexPlugins) {
                $code = if ($catalogCode -or ($product.Key -eq 'windows' -and $prereq['node'])) { 1 } else { Install-CodexPlugin $codexCli $product.Plugin }
                $results["$($product.Key)|codex"] = $code
            }
        }
        # 2. NexusWindows: one edition per AI app.
        if ($wants['windows']) {
            if ($for['claude-desktop'] -and -not $kept['windows|claude-desktop']) {
                $results['windows|claude-desktop'] = Invoke-Installer 'NexusWindows for Claude (Claude Desktop)' "$Raw/NexusWindows-Claude/main/setup.ps1" $work
            }
            # Codex's Windows plugin already contains its runtime and guide; do not add a direct MCP declaration.
            if ($for['antigravity'] -and -not $kept['windows|antigravity']) {
                $results['windows|antigravity'] = if ($prereq['uv']) { 1 } else {
                    Invoke-Native 'NexusWindows for Antigravity' {
                        Invoke-Console (Get-Uvx) @('--python', '3.14', 'nexus-windows-antigravity@latest', 'install')
                    }
                }
            }
        }
        # 3. NexusAndroid: one setup for every AI app (adb, declarations), without waiting for the phone. Its newest
        #    release, never one older than its reading of NEXUS_PACK_TARGETS (0.14.1), even while the index cache still serves it.
        if ($wants['android'] -and $targets['android'].Count) {
            $env:NEXUS_PACK_TARGETS = $targets['android'] -join ','
            $code = if ($prereq['uv']) { 1 } else { Invoke-Native 'NexusAndroid' {
                Invoke-Console (Get-Uvx) @('--python', '3.14', '--refresh-package', 'nexus-android', '--from', 'nexus-android>=0.14.1', 'nexus-android', 'setup')
            } }
            foreach ($ai in $targets['android']) { if (-not $results["android|$ai"]) { $results["android|$ai"] = $code } }
            $own['android'] = $code
        }
        # 4. NexusBrowser: local bridge and native host for Chrome, declared to every AI app.
        if ($wants['browser'] -and $targets['browser'].Count) {
            $env:NEXUS_PACK_TARGETS = $targets['browser'] -join ','
            $code = Invoke-Installer 'NexusBrowser' "$Raw/NexusBrowser/main/get.ps1" $work
            foreach ($ai in $targets['browser']) { if (-not $results["browser|$ai"]) { $results["browser|$ai"] = $code } }
            $own['browser'] = $code
            if ($code -and $for['claude-code']) { $results['browser|claude-code'] = $code }  # bridge missing: plugin useless
        }
        # 5. NexusRelay: local relay daemon, declared to Claude Code, Codex and Antigravity.
        if ($wants['relay'] -and $targets['relay'].Count) {
            $env:NEXUS_PACK_TARGETS = $targets['relay'] -join ','
            $code = Invoke-Installer 'NexusRelay' "$Raw/NexusRelay/main/get.ps1" $work
            # Claude Code: its plugin result (step 1) is kept if it failed, else the daemon's decides
            foreach ($ai in $targets['relay']) { if (-not $results["relay|$ai"]) { $results["relay|$ai"] = $code } }
        }
        # 6. NexusAGYmail: its Antigravity plugin (the Google sign-in is the user's own step, below).
        if ($wants['agymail'] -and -not $kept['agymail|antigravity']) {
            $results['agymail|antigravity'] = if ($prereq['uv']) { 1 } else {
                Invoke-Native 'NexusAGYmail' {
                    Invoke-Console (Get-Uvx) @('--python', '3.14', '--refresh-package', 'nexus-agymail', 'nexus-agymail@latest', 'setup')
                }
            }
        }
        # 7. NexusKeypad: the full plugin for Logi Options+ (keys placed by a guide, below).
        if ($wants['keypad']) {
            $results['keypad|pc'] = Invoke-Installer 'NexusKeypad' "$Raw/NexusKeypad/main/get.ps1" $work
        }
        foreach ($product in $codexPlugins) {
            if ($results["$($product.Key)|codex"]) { continue }
            $code = Invoke-Native "Codex: verify $($product.Plugin) guide and single server" {
                if (-not (Test-CodexPluginInstalled $product.Plugin)) { throw 'The installed Codex plugin or its guide is missing; the direct declaration was kept.' }
                Remove-CodexOwnedServer $product.Server
            }
            if ($code) { $results["$($product.Key)|codex"] = $code }
        }
    } finally {
        if ($null -eq $savedPack) { Remove-Item Env:NEXUS_PACK -ErrorAction SilentlyContinue } else { $env:NEXUS_PACK = $savedPack }
        Remove-Item Env:NEXUS_PACK_TARGETS -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Summary: a cell is green when its route succeeded AND the AI app now declares the product.
    Say ''
    Say 'Summary' Cyan
    $failed = 0
    foreach ($name in 'uv', 'git', 'node') {
        if ($prereq.ContainsKey($name) -and $prereq[$name]) { Say "  $No $name (the steps that need it were not run)" Red; $failed++ }
    }
    $width = 16
    $header = '  ' + ''.PadRight(14) + (($ais | ForEach-Object { $AiNames[$_].PadRight($width) }) -join '')
    Say $header
    $events = @()
    foreach ($product in $selected) {
        Write-Host ('  ' + $product.Name.PadRight(14)) -NoNewline
        $cells = Get-Cell $product $ais
        foreach ($ai in $(if ($product.Device) { @('pc') } else { $ais })) {
            if ($cells -notcontains $ai) { Write-Host '-'.PadRight($width) -ForegroundColor DarkGray -NoNewline; continue }
            $label = if ($product.Device) { "$($product.Device)" } else { '' }
            if ($kept["$($product.Key)|$ai"]) {
                if ($ai -eq 'codex' -and ($codexCustom[$product.Key] -or $codexExisting[$product.Key] -or $codexUnrecognized[$product.Key])) {
                    $events += @{ type = 'cell'; product = $product.Key; ai = $ai; ok = $false; state = 'kept' }
                    Write-Host '- kept'.PadRight($width) -ForegroundColor Yellow -NoNewline
                    continue
                }
                $events += @{ type = 'cell'; product = $product.Key; ai = $ai; ok = $false; state = 'disabled' }
                Write-Host '- off, kept'.PadRight($width) -ForegroundColor Yellow -NoNewline
                continue
            }
            $code = $results["$($product.Key)|$ai"]
            if ($null -eq $code -and $refused[$product.Key] -and $ai -ne 'claude-code') {
                $events += @{ type = 'cell'; product = $product.Key; ai = $ai; ok = $false; state = 'refused' }
                Write-Host '- not run'.PadRight($width) -ForegroundColor Yellow -NoNewline
                continue
            }
            $state = & $Checks[$ai] $product
            # The plugin route failed, but the product's own installer, run after it, declared Claude Code itself.
            if ($ai -eq 'claude-code' -and $code -and $own.ContainsKey($product.Key) -and $own[$product.Key] -eq 0) { $code = 0 }
            $good = ($null -ne $code) -and ($code -eq 0) -and ($state -eq 'ok')
            if (-not $good) { $failed++ }
            $events += @{ type = 'cell'; product = $product.Key; ai = $ai; ok = [bool]$good; state = $(if ($good) { 'ok' } else { $state }) }
            if ($good) { Write-Host "$Ok ok $label".PadRight($width) -ForegroundColor Green -NoNewline }
            elseif ($state -eq 'invalid') { Write-Host "$No invalid".PadRight($width) -ForegroundColor Red -NoNewline }
            else { Write-Host "$No failed $label".PadRight($width) -ForegroundColor Red -NoNewline }
        }
        Write-Host ''
    }
    foreach ($e in $events) { Send-Event $e }  # after the table, each on its own line
    foreach ($product in $wanted | Where-Object { Get-Unfit $_ $ais }) { Say ("  {0,-13} {1}" -f $product.Name, (Get-Unfit $product $ais)) DarkGray }

    Say ''
    Say 'Left to you' Cyan
    $step = 0
    if ($wants['browser']) {
        $step++
        Say "  $step. Chrome: load the NexusBrowser extension once (guide: https://github.com/pironjulien/NexusBrowser#install)."
        $guide = Join-Path $env:LOCALAPPDATA 'NexusBrowser\app\scripts\guide-extension.ps1'
        if (-not $NoGuide -and (Test-Path -LiteralPath $guide)) {
            $shell = (Get-Process -Id $PID).Path
            Invoke-Console $shell @('-NoProfile', '-ExecutionPolicy', 'RemoteSigned', '-File', $guide, '-WaitSeconds', '0')
        }
    }
    if ($wants['android']) {
        $step++
        Say "  $step. Phone: Settings > About phone, tap Build number 7 times; Developer options > turn on USB debugging."
        Say '     Plug it in with a USB data cable, then run: uvx --python 3.14 nexus-android setup'
        Say '     (https://github.com/pironjulien/NexusAndroid#quick-start)'
    }
    # The Codex project, only when Codex answers (Claude Code is the host); as the host, Codex needs none.
    if ($wants['relay'] -and $targets['relay'] -contains 'codex' -and $targets['relay'] -contains 'claude-code') {
        $step++
        Say "  $step. Codex, once: Add a project, name ""Nexus Relay"", folder $env:USERPROFILE\.nexus\relay\codex-projet"
        Say '     (https://github.com/pironjulien/NexusRelay#quick-start)'
    }
    if ($wants['agymail']) {
        $step++
        Say "  $step. Gmail: create your own Google OAuth client (Desktop app), then run:"
        Say '     uvx --python 3.14 nexus-agymail auth --client-secret <the downloaded JSON file>'
        Say '     (https://github.com/pironjulien/NexusAGYmail#quick-start)'
    }
    if ($wants['keypad']) {
        $step++
        Say "  $step. MX Creative Keypad: in Logi Options+, place the NexusKeypad keys as the guide shows"
        Say '     (https://github.com/pironjulien/NexusKeypad#install)'
    }
    $step++
    Say "  $step. Restart your AI apps: quit them completely (also from the notification area), then open them again."
    if ($failed) {
        Say ''
        Say "$failed item(s) failed: read the messages above, then run the same command again." Red
        return 1
    }
    Say ''
    # A first request that shows the first product installed.
    $examples = [ordered]@{
        windows = 'Open Notepad and write a shopping list: milk, eggs, bread.'
        android = 'Look at my phone and tell me what is on the screen.'
        browser = 'Open https://example.com in a background tab and give me the page as Markdown.'
        relay   = 'Use Nexus Relay to ask Codex for a second opinion on this file.'
        agymail = 'Find my unread emails from today and sum them up.'
    }
    $first = @($examples.Keys | Where-Object { $wants[$_] }) | Select-Object -First 1
    if ($first) { Say "Done. Ask your AI, for example: `"$($examples[$first])`"" Green } else { Say 'Done.' Green }
    return 0
}

$code = Invoke-NexusPack -Only $Only -Skip $Skip -DryRun $DryRun.IsPresent -NoGuide $NoGuide.IsPresent -Catalog $Catalog
Send-Event @{ type = 'done'; code = $code }
if ($PSCommandPath) {
    # Started from Explorer (right click > Run with PowerShell): the window would close at once, before the summary
    # and the steps left to the user could be read.
    $parent = (Get-CimInstance Win32_Process -Filter "ProcessId = $PID" -ErrorAction SilentlyContinue).ParentProcessId
    if ($parent -and (Get-Process -Id $parent -ErrorAction SilentlyContinue).ProcessName -eq 'explorer') {
        Read-Host 'Press Enter to close this window' | Out-Null
    }
    exit $code  # run as a file (release download, CI, tests); never close the window of an `irm | iex` run
}
