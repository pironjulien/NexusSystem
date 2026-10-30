# Nexus pack: the whole Nexus ecosystem on a Windows 10/11 PC, for the AI apps found on it. The NexusSystem app of the
# Microsoft Store runs it for you. By hand, always as a FILE of a NexusSystem release, checked, then run with -File
# (never run in memory from the network: antivirus heuristics rightly flag that):
#   $r = 'https://github.com/pironjulien/NexusSystem/releases/latest/download'
#   Invoke-WebRequest "$r/get-nexus.ps1" -OutFile get-nexus.ps1; Invoke-WebRequest "$r/SHA256SUMS.txt" -OutFile SHA256SUMS.txt
#   (Get-FileHash get-nexus.ps1).Hash   # the get-nexus.ps1 line of SHA256SUMS.txt
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\get-nexus.ps1 -Only windows,browser
# Each product's own installer is fetched the same way: the get.ps1 file of its latest release, checked against that
# release's SHA256SUMS.txt, run in a child powershell.exe with -File.
# Windows PowerShell 5.1 or PowerShell 7, no administrator rights. Git is installed through winget when an AI app
# needs it to add this catalog. Every product runs as its native Windows engine (x64 or ARM64).
# It finds Claude Code, Claude Desktop, Codex and Antigravity, shows the plan, gives Claude Code and Codex the plugins
# of this catalog, then runs each product's own official installer. No installer logic is copied here.
# Run it again at any time: every installer only does what is missing.
#   -Only / -Skip  product names (windows, android, browser, relay, agymail, keypad) or AI names (claude-code,
#                  claude-desktop, codex, antigravity), comma separated
#   -DryRun        show the plan and do nothing
#   -NoGuide       do not open Chrome on its extensions page at the end (the guide is printed instead)
#   -Catalog       marketplace given to Claude Code (default pironjulien/NexusSystem; a local folder for tests)
#   This PC's own choices: %USERPROFILE%\.nexus\nexussystem.json, {"skip": ["claude-desktop"]}, adds its names to -Skip
#                  (the NexusSystem app reads the same file).
#   -Json          also print one "NEXUS_PACK_EVENT {json}" line per event (found, plan, step, cell, done), for the
#                  Nexus apps that run this same script and show its progress
#   -Maintain     update installed products and existing active Nexus routes only, preserving running sessions
#   -MaintenanceStatus  read the last check as JSON without changing files or contacting GitHub
param(
    [string[]]$Only = @(),
    [string[]]$Skip = @(),
    [switch]$DryRun,
    [switch]$NoGuide,
    [string]$Catalog = 'pironjulien/NexusSystem',
    [switch]$Json,
    [switch]$Maintain,
    [switch]$MaintenanceStatus,
    [switch]$MaintenanceCurrent,
    [switch]$ForceMaintenance
)

$ErrorActionPreference = 'Stop'
# A Windows PowerShell child of PowerShell 7 can inherit Core modules before its own modules. Keep all user
# locations, but put this host's built-in modules first so Get-FileHash and other cmdlets load their Desktop build.
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    $builtinModules = Join-Path $PSHOME 'Modules'
    $modulePaths = @($env:PSModulePath -split ';' | Where-Object { $_ -and $_ -ine $builtinModules })
    $env:PSModulePath = (@($builtinModules) + $modulePaths) -join ';'
}
$EmitEvents = $Json.IsPresent
$ProgressPreference = 'SilentlyContinue'  # Invoke-WebRequest is many times slower with its progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$Ok = [string][char]0x2713
$No = [string][char]0x2717
$Marketplace = 'nexus-plugins'
$NexusMaintenanceVersion = '1.6.36'

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
    @{ Key = 'windows'; Name = 'NexusWindows'; Repo = 'NexusWindows'; Engine = $true; Plugin = 'nexus-windows'; Agy = 'nexus-windows'; AgyLegacy = @('nexus-computer'); Server = 'nexus-windows'
       Targets = $AllAi },
    # Native since 0.22.0: its get.ps1 installs the shared daemon (127.0.0.1:16180, token NEXUS_MCP_TOKEN) the Claude
    # Code and Codex plugins reach, so its installer is told every chosen AI app and sees the plugins.
    @{ Key = 'android'; Name = 'NexusAndroid'; Repo = 'NexusAndroid'; Engine = $true; Plugin = 'nexus-android'; Agy = 'nexus-android'; Server = 'nexus-android'
       Targets = $AllAi },
    @{ Key = 'browser'; Name = 'NexusBrowser'; Repo = 'NexusBrowser'; Engine = $true; Plugin = 'nexus-browser'; Agy = 'nexus-browser'; Server = 'nexus-browser'
       Targets = $AllAi },
    # Two AI apps talking: one host opens the exchanges (NexusRelay 1.4.0: the first of Claude Code, Codex,
    # Antigravity among those configured), the others answer. Claude Desktop's chat is not a relay client.
    # Claude Code gets its plugin from this catalog (1.6.26); the daemon, Codex and Antigravity come from its installer,
    # which sees the plugin and adds no second server. The installer still serves Claude Code (it is told every
    # chosen client, so the host stays Claude when present, and a daemon that failed marks Claude Code failed too).
    @{ Key = 'relay'; Name = 'NexusRelay'; Repo = 'NexusRelay'; Engine = $true; Plugin = 'nexus-relay'; Agy = 'nexus-relay'; Server = 'nexus-relay'
       Targets = @('claude-code', 'codex', 'antigravity'); MinAis = 2 },
    # Claude and Codex have their own official Gmail connector.
    @{ Key = 'agymail'; Name = 'NexusAGYmail'; Repo = 'NexusAGYmail'; Engine = $true; Agy = 'nexus-agymail'; Server = 'nexus-agymail'; Targets = @('antigravity') },
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

    # Claude Desktop's installed Code engine need not be on PATH. Search only Desktop's known data layouts,
    # never a source checkout or an arbitrary claude.exe found recursively. Discovery executes nothing.
    $roots = @(Join-Path $env:APPDATA 'Claude')
    foreach ($layout in @(
        @{ Root = (Join-Path $env:LOCALAPPDATA 'Claude-Data'); Suffix = '' },
        @{ Root = (Join-Path $env:LOCALAPPDATA 'Packages'); Suffix = 'LocalCache\Roaming\Claude' }
    )) {
        if (-not (Test-Path -LiteralPath $layout.Root -PathType Container)) { continue }
        if ((Get-Item -LiteralPath $layout.Root).Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        foreach ($package in @(Get-ChildItem -LiteralPath $layout.Root -Directory -ErrorAction SilentlyContinue)) {
            if ($package.Name -notmatch '^AnthropicPBC\.Claude_[a-z0-9]+$' -or
                ($package.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
            $desktop = $package.FullName
            $safe = $true
            foreach ($part in @($layout.Suffix -split '\\' | Where-Object { $_ })) {
                $desktop = Join-Path $desktop $part
                if (-not (Test-Path -LiteralPath $desktop -PathType Container) -or
                    ((Get-Item -LiteralPath $desktop).Attributes -band [IO.FileAttributes]::ReparsePoint)) { $safe = $false; break }
            }
            if ($safe) { $roots += $desktop }
        }
    }
    foreach ($root in @($roots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        if (Get-ChildItem -LiteralPath $root -Force -Filter '.user-data-left.*' -ErrorAction SilentlyContinue) { continue }
        $code = Join-Path $root 'claude-code'
        if (-not (Test-Path -LiteralPath $code -PathType Container) -or
            ((Get-Item -LiteralPath $code).Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
        $versions = @(Get-ChildItem -LiteralPath $code -Directory -ErrorAction SilentlyContinue | Where-Object {
            $parsedVersion = $null
            $_.Name -match '^\d+\.\d+\.\d+$' -and [version]::TryParse($_.Name, [ref]$parsedVersion) -and
                -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
        } | Sort-Object { [version]$_.Name } -Descending)
        foreach ($version in $versions) {
            $builds = @(Get-ChildItem -LiteralPath $version.FullName -Directory -ErrorAction SilentlyContinue | Where-Object {
                $_.Name -cmatch '^[0-9a-f]{12,64}$' -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
            } | Sort-Object LastWriteTimeUtc -Descending)
            foreach ($build in $builds) {
                $candidate = Join-Path $build.FullName 'claude.exe'
                if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
                $file = Get-Item -LiteralPath $candidate
                if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
                try { $signature = Get-AuthenticodeSignature -LiteralPath $candidate -ErrorAction Stop }
                catch { continue }
                if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
                    $signature.SignerCertificate.Subject -notmatch '(^|,\s*)(CN|O)="Anthropic, PBC"(?=,|$)') { continue }
                # Read the signed file's identity only after authenticating it. Another Anthropic application is
                # not a Code engine, even if somebody placed it in a Desktop engine directory.
                $identity = $file.VersionInfo
                if ($identity.ProductName -ne 'Claude Code' -or $identity.CompanyName -ne 'Anthropic PBC' -or
                    $identity.ProductVersion -notmatch '^([0-9]+\.[0-9]+\.[0-9]+)(?:\.0)?$' -or
                    $Matches[1] -ne $version.Name) { continue }
                return $candidate
            }
        }
    }
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

function Get-LocalSkip {
    # This PC's own choices (the NexusSystem app reads the same file): %USERPROFILE%\.nexus\nexussystem.json,
    # {"skip": ["claude-desktop"]}. A missing file means none; an unreadable one is reported and ignored.
    $file = Join-Path $env:USERPROFILE '.nexus\nexussystem.json'
    if (-not (Test-Path -LiteralPath $file)) { return , @() }
    try {
        $settings = [IO.File]::ReadAllText($file) | ConvertFrom-Json
        return , @(@($settings.skip) | Where-Object { $_ -is [string] -and $_.Trim() } | ForEach-Object { $_.Trim().ToLowerInvariant() })
    } catch {
        Say "$file is not readable JSON: ignored ($($_.Exception.Message))." Yellow
        return , @()
    }
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
    # PowerShell 7.5+ otherwise converts ISO timestamps into DateTime, whose later string conversion depends on
    # culture and loses the original offset. Windows PowerShell 5.1 already keeps JSON dates as strings.
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { return $text | ConvertFrom-Json -DateKind String }
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

function Get-LatestTag([string]$Repo) {
    # The latest release's tag from the redirect of github.com/<repo>/releases/latest: no GitHub API (60 anonymous
    # requests an hour per IP); the API stays as a fallback.
    try {
        $request = [Net.HttpWebRequest]::Create("https://github.com/pironjulien/$Repo/releases/latest")
        $request.AllowAutoRedirect = $false
        $request.Timeout = 30000
        $request.UserAgent = 'NexusSystem-get-nexus'
        $response = $request.GetResponse()
        try { $location = $response.Headers['Location'] } finally { $response.Close() }
        if ($location -match '/releases/tag/(v[0-9][^/?#]*)$') { return $Matches[1] }
    } catch { Write-Verbose "Redirect lookup failed: $($_.Exception.Message)" }
    return (Invoke-RestMethod -UseBasicParsing "https://api.github.com/repos/pironjulien/$Repo/releases/latest" -TimeoutSec 30).tag_name
}

function Get-ExpectedSha256([string]$Manifest, [string]$Name) {
    # The line for $Name in a SHA256SUMS.txt (or a lone "<hash>  <name>" .sha256 file); a malformed or ambiguous
    # manifest is refused.
    $expected = $null
    foreach ($line in ($Manifest -split '\r?\n' | Where-Object { $_.Trim() })) {
        if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(\S+)\s*$') { throw 'Malformed checksum file.' }
        if ($Matches[2] -ceq $Name) {
            if ($expected) { throw "$Name is listed twice in the checksum file." }
            $expected = $Matches[1].ToLowerInvariant()
        }
    }
    if (-not $expected) { throw "$Name is absent from the checksum file." }
    return $expected
}

function Save-VerifiedFile([string]$Url, [string]$File, [string]$Expected) {
    Invoke-WebRequest -UseBasicParsing $Url -OutFile $File -TimeoutSec 60
    $actual = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected) {
        Remove-Item -LiteralPath $File -Force -ErrorAction SilentlyContinue
        throw "SHA-256 mismatch for $Url`: the download is not the published file."
    }
}

function Invoke-ScriptFile([string]$What, [string]$File, [string[]]$Arguments = @()) {
    # A child Windows PowerShell running the checked file, never code in memory; its output in this console.
    $shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    return Invoke-Native $What { Invoke-Console $shell (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $File) + $Arguments) }
}

function Invoke-Installer([string]$What, [string]$Repo, [string]$Work) {
    # A product's official installer: the get.ps1 FILE of its latest release (versioned URL), checked against that
    # release's SHA256SUMS.txt (or the get.ps1.sha256 published beside it), then run with -File.
    $folder = Join-Path $Work $Repo
    try {
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        $tag = Get-LatestTag $Repo
        $base = "https://github.com/pironjulien/$Repo/releases/download/$tag"
        try { $manifest = [Text.Encoding]::UTF8.GetString((Invoke-WebRequest -UseBasicParsing "$base/SHA256SUMS.txt").Content) }
        catch { $manifest = $null }
        if (-not $manifest -or $manifest -notmatch '(?m)\s\*?get\.ps1\s*$') {
            $manifest = [Text.Encoding]::UTF8.GetString((Invoke-WebRequest -UseBasicParsing "$base/get.ps1.sha256").Content)
        }
        $file = Join-Path $folder 'get.ps1'
        Save-VerifiedFile "$base/get.ps1" $file (Get-ExpectedSha256 $manifest 'get.ps1')
    } catch {
        Say ''
        Say "== $What" Cyan
        Say "$What`: its release installer could not be fetched and checked ($($_.Exception.Message))." Red
        return 1
    }
    return Invoke-ScriptFile "$What $tag" $file
}

function Sync-SessionPath {
    # A program just installed (Git) is on the registry PATH, which this window only gets this way.
    $known = @($env:PATH -split ';' | Where-Object { $_ })
    foreach ($scope in 'Machine', 'User') {
        foreach ($entry in ([Environment]::GetEnvironmentVariable('Path', $scope) -split ';')) {
            if ($entry -and $known -notcontains $entry) { $env:PATH = "$env:PATH;$entry"; $known += $entry }
        }
    }
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
    # The forms the official installers write today (native engine, local daemon) and wrote before (npm and PyPI
    # launchers, recognised only to be replaced by the plugin, never started).
    $allowed = @('command', 'args', 'startup_timeout_sec', 'enabled')
    $daemon = @{ 'nexus-relay' = 'http://127.0.0.1:16181/mcp?agent=codex'; 'nexus-android' = 'http://127.0.0.1:16180/mcp'
                 'nexus-browser' = 'http://127.0.0.1:16182/mcp?agent=codex' }
    if ($root.ContainsKey('url')) {
        $allowed = @('url', 'bearer_token_env_var', 'startup_timeout_sec', 'enabled')
        if (@($root.Keys | Where-Object { $allowed -notcontains $_ }).Count) { return $false }
        return -not $environment.Count -and $daemon.ContainsKey($Name) -and $root['url'] -eq $daemon[$Name] -and
            $root['bearer_token_env_var'] -eq 'NEXUS_MCP_TOKEN'
    }
    if (@($root.Keys | Where-Object { $allowed -notcontains $_ }).Count) { return $false }
    $command = [IO.Path]::GetFileName([string]$root['command']).ToLowerInvariant()
    $args_ = @($root['args'])
    switch ($Name) {
        'nexus-windows' {
            if ($environment.Count) { return $false }
            $native = Join-Path $env:USERPROFILE '.nexus\nexus-windows'
            if ($command -eq 'nexuswindows.exe') {
                $folder = [IO.Path]::GetFullPath((Split-Path ([string]$root['command'])))
                return ($folder -ieq $native -or (Split-Path $folder) -ieq (Join-Path $native 'native')) -and ($args_ -join ' ') -ceq '--mcp --agent codex'
            }
            return $command -in @('npx', 'npx.cmd', 'npx.exe') -and $args_.Count -eq 2 -and
                $args_[0] -in @('-y', '--yes') -and $args_[1] -match '^nexus-windows-codex(@latest)?$'
        }
        'nexus-android' {
            $package = $args_ -join ' '
            return -not $environment.Count -and $command -in @('uvx', 'uvx.exe') -and
                $package -match '^(--python 3\.14 )?nexus-android(==\d+\.\d+\.\d+)?$'
        }
        'nexus-browser' {
            if ($environment.Count -ne 1 -or $environment['NEXUS_BROWSER_AGENT'] -ne 'Codex') { return $false }
            # Native 2.33.0 wrote this direct stdio declaration beside an already installed catalog plugin. Only
            # its immutable release directory, exact server mode and Codex tab group belong to the installer.
            if ($command -eq 'nexusbrowser.exe' -and $args_.Count -eq 1 -and $args_[0] -ceq '--mcp') {
                if (-not [IO.Path]::IsPathRooted([string]$root['command'])) { return $false }
                $folder = [IO.Path]::GetFullPath((Split-Path ([string]$root['command'])))
                $versions = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'NexusBrowser\versions'))
                return (Split-Path $folder) -ieq $versions -and (Split-Path $folder -Leaf) -cmatch '^[0-9a-f]{16}$'
            }
            if ($args_.Count -ne 2 -or $args_[0] -ne '-B') { return $false }
            $locations = @(
                @((Join-Path $env:LOCALAPPDATA 'NexusBrowser\venv\Scripts\python.exe'), (Join-Path $env:LOCALAPPDATA 'NexusBrowser\app\client\mcp_server.py')),
                @((Join-Path $env:USERPROFILE '.nexus\nexus-browser\venv\Scripts\python.exe'), (Join-Path $env:LOCALAPPDATA 'NexusBrowser\app\client\mcp_server.py'))
            )
            foreach ($location in $locations) { if ($root['command'] -ieq $location[0] -and $args_[1] -ieq $location[1]) { return $true } }
            return $false
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
            $installed = & $cli plugin list --json | Out-String
            if ($LASTEXITCODE) { return }
            foreach ($plugin in $Plugins) {
                if ($installed -match [regex]::Escape("$plugin@$Marketplace")) {
                    & $cli plugin update "$plugin@$Marketplace" --scope user --json
                } else {
                    & $cli plugin install "$plugin@$Marketplace" --scope user
                }
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
    $names = @($Product.Agy) + @($Product.AgyLegacy) | Where-Object { $_ }
    # Turned off in Antigravity: plugins.<name>.enabled = false in ~\.gemini\config\config.json.
    # A migration must keep a disabled legacy identity, including when its old folder has already been removed.
    $config = Read-Json (Join-Path $env:USERPROFILE '.gemini\config\config.json')
    foreach ($name in $names) {
        if ($config -and $config.plugins -and $config.plugins.PSObject.Properties[$name]) {
            $entry = $config.plugins.($name)
            if ($entry -eq $false -or ($entry.PSObject.Properties['enabled'] -and $entry.enabled -eq $false)) { return 'disabled' }
        }
    }
    foreach ($name in $names) {
        if (Test-Path -LiteralPath (Join-Path $env:USERPROFILE ".gemini\config\plugins\$name\plugin.json")) { return 'ok' }
    }
    return 'missing'
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
$OneRunForAll = @('windows', 'android', 'browser', 'relay', 'agymail')
$HonorsTargets = $OneRunForAll

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

# Maintenance is part of this released, checksum-verified file: no second downloaded helper or updater authority.
# Exit codes: 0 checked/current, 2 pending (including another run), 1 failed. Loaded means a live process, never cache.
function Get-NexusMaintenanceRoot { return Join-Path $env:USERPROFILE '.nexus\nexussystem\maintenance' }
function Get-NexusMaintenanceStatusPath { return Join-Path $env:USERPROFILE '.nexus\status\nexussystem-update.json' }
function Get-NexusMaintenanceStatus {
    try {$state = Read-Json (Get-NexusMaintenanceStatusPath)}catch{$state=$null}
    if (-not $state -or $state.schema -ne 1) {
        return [pscustomobject]@{ schema=1; state='pending'; checked_at=$null; latest=$null; catalogue=$null; products=@(); message='Maintenance has not completed a check.' }
    }
    # The disk receipt is historical. A status read never promotes it to a live health claim.
    $checked=[DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$state.checked_at,[ref]$checked) -or $checked -gt [DateTimeOffset]::UtcNow -or ([DateTimeOffset]::UtcNow - $checked).TotalHours -gt 2) {
        $state.state='stale'; Write-Property $state 'message' 'The last maintenance check is older than two hourly cycles.'
    }
    return $state
}
function Write-NexusMaintenanceStatus($State) {
    $path=Get-NexusMaintenanceStatusPath
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $temporary="$path.$PID.tmp"
    [IO.File]::WriteAllText($temporary,($State | ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $path -Force
}
function Install-NexusMaintenance([string]$Source) {
    $choices=Read-Json (Join-Path $env:USERPROFILE '.nexus\nexussystem.json')
    if ($choices -and $choices.autoUpdate -eq $false) { return }
    $root=Get-NexusMaintenanceRoot
    New-Item -ItemType Directory -Force $root | Out-Null
    $destination=Join-Path $root 'get-nexus.ps1'
    if ([IO.Path]::GetFullPath($Source) -ine [IO.Path]::GetFullPath($destination)) {
        Copy-Item -LiteralPath $Source -Destination "$destination.$PID.tmp" -Force
        Move-Item -LiteralPath "$destination.$PID.tmp" -Destination $destination -Force
    }
    $shell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments='-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+$destination+'" -Maintain'
    $action=New-ScheduledTaskAction -Execute $shell -Argument $arguments
    $triggers=@((New-ScheduledTaskTrigger -AtLogOn), (New-ScheduledTaskTrigger -Once -At (Get-Date).AddHours(1) -RepetitionInterval (New-TimeSpan -Hours 1)))
    $settings=New-ScheduledTaskSettingsSet -Hidden -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 15) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    $principal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
    $previous=Get-ScheduledTask -TaskName 'NexusSystem Update' -ErrorAction SilentlyContinue
    if ($previous -and -not $previous.Settings.Enabled) { $settings.Enabled=$false }
    Register-ScheduledTask -TaskName 'NexusSystem Update' -Action $action -Trigger $triggers -Settings $settings -Principal $principal -Description 'Verified Nexus releases; installed products and existing active AI routes only. Sessions are preserved.' -Force | Out-Null
}
function Assert-NexusUpdateContract([string]$File,[string[]]$Parameters) {
    $errors=$null;$tokens=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($File,[ref]$tokens,[ref]$errors)
    $names=@($ast.ParamBlock.Parameters | ForEach-Object {$_.Name.VariablePath.UserPath})
    if ($errors.Count -or @($Parameters | Where-Object {$names -notcontains $_}).Count) { throw 'The verified installer does not declare the required update-only contract; it was not executed.' }
}
function Assert-NexusOfficialUpdateEnvironment([string]$Product) {
    $prefix=switch($Product){'windows'{'NEXUS_WINDOWS_'}'browser'{'NEXUS_BROWSER_'}default{return}}
    foreach($suffix in @('SOURCE','TAG','ROOT','PROFILE')) {
        if([Environment]::GetEnvironmentVariable($prefix+$suffix,'Process')) {throw "A $Product development/test override is set; unattended maintenance will not execute it."}
    }
}
function Get-NexusPublishedInstaller([string]$Repo,[string]$Work,[string]$Name='get.ps1') {
    $tag=Get-LatestTag $Repo
    if ($tag -notmatch '^v\d+\.\d+\.\d+$') { throw "Unsupported release tag for $Repo." }
    $folder=Join-Path $Work $Repo
    New-Item -ItemType Directory -Force $folder | Out-Null
    $base="https://github.com/pironjulien/$Repo/releases/download/$tag"
    $response=Invoke-WebRequest -UseBasicParsing "$base/SHA256SUMS.txt" -TimeoutSec 30
    $manifest=if ($response.Content -is [byte[]]) {[Text.Encoding]::UTF8.GetString($response.Content)} else {[string]$response.Content}
    $file=Join-Path $folder $Name
    Save-VerifiedFile "$base/$Name" $file (Get-ExpectedSha256 $manifest $Name)
    if ($Repo -eq 'NexusSystem') {
        $parseErrors=$null; $tokens=$null
        $ast=[Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$parseErrors)
        if ($parseErrors.Count -or -not @($ast.ParamBlock.Parameters | Where-Object {$_.Name.VariablePath.UserPath -eq 'Maintain'}).Count -or -not $ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-NexusMaintenance'},$true)) {
            throw 'The published NexusSystem installer does not yet implement maintenance; it was not executed.'
        }
    }
    # When the existing official gh tool is available, also authenticate the build provenance. A failed
    # attestation is fatal; it never silently degrades to checksums. No gh installation or authentication is added.
    $gh=Get-Command gh.exe -ErrorAction SilentlyContinue
    if ($gh) {
        if (Invoke-Native "$Repo build provenance" { & $gh.Source attestation verify $file --repo "pironjulien/$Repo" --source-ref "refs/tags/$tag" }) { throw "Build provenance failed for $Repo $tag." }
    }
    return [pscustomobject]@{tag=$tag; version=$tag.Substring(1); file=$file; sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()}
}
function Get-NexusInstalledVersion($Product) {
    switch ($Product.Key) {
        'windows' {
            $record=Read-Json (Join-Path $env:USERPROFILE '.nexus\nexus-windows\installation.json')
            if (-not $record -or $record.Schema -ne 2 -or -not $record.NativeArtifacts -or $record.NativeHash -notmatch '^[a-f0-9]{64}$') { return $null }
            $expected=Join-Path $env:USERPROFILE ".nexus\nexus-windows\native\$($record.NativeHash)\NexusWindows.exe"
            if ($record.NativeExecutable -ine $expected) { return $null }
            foreach ($required in @('NexusWindows.exe','NexusWindows.dll','NexusWindows.deps.json','NexusWindows.runtimeconfig.json')) {
                if (-not $record.NativeArtifacts.PSObject.Properties[$required]) { return $null }
            }
            foreach ($artifact in $record.NativeArtifacts.PSObject.Properties) {
                if ($artifact.Name -match '[/\\:]' -or $artifact.Name -in @('.','..') -or $artifact.Value -notmatch '^[a-fA-F0-9]{64}$') { return $null }
                $file=Join-Path (Split-Path $expected) $artifact.Name
                if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $artifact.Value) { return $null }
            }
            return [string]$record.Version
        }
        'browser' {
            $record=Read-Json (Join-Path $env:LOCALAPPDATA 'NexusBrowser\current.json')
            if (-not $record -or $record.fingerprint -notmatch '^[a-f0-9]{64}$' -or -not $record.exe) { return $null }
            $expected=Join-Path $env:LOCALAPPDATA "NexusBrowser\versions\$($record.fingerprint.Substring(0,16))\NexusBrowser.exe"
            if ($record.exe -ine $expected -or -not (Test-Path -LiteralPath $expected)) { return $null }
            $assembly=Join-Path (Split-Path $expected) 'NexusBrowser.dll'
            if (-not (Test-Path -LiteralPath $assembly)) { $assembly=$expected }
            if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ine $record.fingerprint) { return $null }
            return [string]$record.version
        }
        default {
            # Other products keep their own distribution protocols. Their saved version remains installation
            # information only, and is never promoted to loaded state or used to run an unqualified installer.
            $record=Read-Json (Join-Path $env:USERPROFILE ".nexus\status\$($Product.Key).json")
            if ($record) { return [string]$record.version }
            return $null
        }
    }
}
function Test-NexusOwnedJsonRoute($Product,$Server,[string]$Agent) {
    if (-not $Server -or $Server.disabled -eq $true -or $Server.enabled -eq $false) { return $false }
    if ($Product.Key -eq 'browser') {
        $names=@($Server.PSObject.Properties.Name)
        if ($Server.command) {
            if (@($names | Where-Object {$_ -notin @('type','command','args','env')}).Count -or ($Server.type -and $Server.type -ne 'stdio')) {return $false}
            $command=[string]$Server.command
            if (-not [IO.Path]::IsPathRooted($command)) {return $false}
            $root=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'NexusBrowser')).TrimEnd('\')+'\'
            $full=[IO.Path]::GetFullPath($command)
            if (-not $full.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) {return $false}
            $relative=$full.Substring($root.Length).Replace('\','/')
            return $relative -match '^(versions/[0-9a-f]{16}|app/engine|native)/NexusBrowser\.exe$' -and (@($Server.args) -join '|') -ceq '--mcp' -and $Server.env -and @($Server.env.PSObject.Properties).Count -eq 1 -and [string]$Server.env.NEXUS_BROWSER_AGENT -ieq $Agent
        }
        $urlKey=@($names | Where-Object {$_ -in @('url','serverUrl','httpUrl')})
        $authorization=if($Server.headers){[string]$Server.headers.Authorization}else{''}
        $token=[Environment]::GetEnvironmentVariable('NEXUS_MCP_TOKEN','User')
        $ownedHeader=$authorization -ceq 'Bearer ${NEXUS_MCP_TOKEN}' -or ($token -and $authorization -ceq "Bearer $token")
        return $names.Count -eq 2 -and $urlKey.Count -eq 1 -and $Server.($urlKey[0]) -ceq "http://127.0.0.1:16182/mcp?agent=$Agent" -and $Server.headers -and @($Server.headers.PSObject.Properties).Count -eq 1 -and $ownedHeader
    }
    $keys=@($Server.PSObject.Properties.Name | Where-Object { $_ -notin @('type','command','args','url','headers','disabled','enabled','startup_timeout_sec','bearer_token_env_var') })
    if ($keys.Count) { return $false }
    if ($Product.Key -eq 'windows') {
        if (@($Server.PSObject.Properties.Name | Where-Object {$_ -notin @('type','command','args','env','disabled','enabled','startup_timeout_sec')}).Count -or ($Server.type -and $Server.type -ne 'stdio') -or ($Server.env -and @($Server.env.PSObject.Properties).Count)) {return $false}
        $stable=Join-Path $env:USERPROFILE '.nexus\nexus-windows\NexusWindows.exe'
        $command=[Environment]::ExpandEnvironmentVariables([string]$Server.command).Replace('${USERPROFILE}',$env:USERPROFILE)
        return $command -ieq $stable -and (@($Server.args) -join '|') -ceq "--mcp|--agent|$Agent"
    }
    $port=switch ($Product.Key) {'browser'{16182} 'android'{16180} 'relay'{16181} default{0}}
    if (-not $port) { return $false }
    $uri=$null
    return [Uri]::TryCreate([string]$Server.url,[UriKind]::Absolute,[ref]$uri) -and $uri.Scheme -eq 'http' -and $uri.Host -eq '127.0.0.1' -and $uri.Port -eq $port -and $uri.AbsolutePath -eq '/mcp' -and ($uri.Query -eq '' -or $uri.Query -ceq "?agent=$Agent")
}
function Get-NexusMaintenanceProfile($Product,[string]$Ai) {
    $state=& $Checks[$Ai] $Product
    if ($state -ne 'ok') { return [pscustomobject]@{key=$Ai; state=$state; route=$null; pluginVersion=$null; activationPending=$false; reason='Existing user selection is preserved.'} }
    $route='custom'; $version=$null
    switch ($Ai) {
        'codex' {
            $path=Join-Path (Get-CodexHome) 'config.toml'; $text=[IO.File]::ReadAllText($path)
            $tables=@(Get-CodexServerTable $text $Product.Server)
            if ($tables.Count) { if (Test-CodexOwnedServer $text $Product.Server) { $route='direct' } }
            elseif ((Get-CodexPluginState $text $Product.Server $Marketplace) -eq 'enabled') { $route='catalogue' }
        }
        'claude-code' {
            $settings=Read-Json (Join-Path $env:USERPROFILE '.claude\settings.json')
            $direct=Read-Json (Join-Path $env:USERPROFILE '.claude.json')
            if ($direct -and $direct.mcpServers -and $direct.mcpServers.PSObject.Properties[$Product.Server]) {
                if (Test-NexusOwnedJsonRoute $Product $direct.mcpServers.($Product.Server) 'claude') { $route='direct' }
            } elseif ($settings -and $settings.enabledPlugins -and $settings.enabledPlugins.PSObject.Properties["$($Product.Plugin)@$Marketplace"] -and $settings.enabledPlugins.("$($Product.Plugin)@$Marketplace") -eq $true) { $route='catalogue' }
            $installed=Read-Json (Join-Path $env:USERPROFILE '.claude\plugins\installed_plugins.json')
            if ($installed -and $installed.plugins -and $installed.plugins.PSObject.Properties["$($Product.Plugin)@$Marketplace"]) {
                $entry=@($installed.plugins.("$($Product.Plugin)@$Marketplace") | Where-Object { $_.scope -eq 'user' }) | Select-Object -First 1
                if ($entry) { $version=[string]$entry.version }
            }
        }
        'antigravity' {
            foreach ($name in @($Product.Agy)+@($Product.AgyLegacy) | Where-Object {$_}) {
                $folder=Join-Path $env:USERPROFILE ".gemini\config\plugins\$name"
                $manifest=Read-Json (Join-Path $folder 'plugin.json'); $config=Read-Json (Join-Path $folder 'mcp_config.json')
                if ($manifest -and $config -and $config.mcpServers -and $config.mcpServers.PSObject.Properties[$Product.Server] -and (Test-NexusOwnedJsonRoute $Product $config.mcpServers.($Product.Server) 'antigravity')) {
                    $route='product'; $version=[string]$manifest.version; break
                }
            }
        }
        'claude-desktop' {
            $owned=$true
            $folders=Get-DesktopFolder
            foreach ($folder in $folders) {
                $config=Read-Json (Join-Path $folder 'claude_desktop_config.json')
                if (-not $config -or -not (Test-NexusOwnedJsonRoute $Product $config.mcpServers.($Product.Server) 'claude')) { $owned=$false }
            }
            if ($owned) { $route='direct' }
        }
    }
    return [pscustomobject]@{key=$Ai;state=$(if($route -eq 'custom'){'kept'}else{'unknown'});route=$route;pluginVersion=$version;activationPending=$false;guideLoaded=$null;reason=$(if($route -eq 'custom'){'Custom declaration is preserved.'}else{'Guide loading in an existing conversation is unknown; runtime activation is checked separately.'})}
}
function Get-NexusProcessAgent($Process,$Processes) {
    if ($Process.CommandLine -notmatch '\s--mcp\s+--agent\s+(claude|codex|antigravity)\s*$') {return $null}
    $expected=$Matches[1]
    $parent=$Processes[[int]$Process.ParentProcessId]
    # The stable launcher is one inert hop; the Codex catalogue also uses one exact cmd.exe hop.
    if ($parent -and $parent.Name -ieq 'NexusWindows.exe' -and $parent.ExecutablePath -ieq (Join-Path $env:USERPROFILE '.nexus\nexus-windows\NexusWindows.exe') -and $parent.CommandLine -match ('\s--mcp\s+--agent\s+'+[regex]::Escape($expected)+'\s*$')) {$parent=$Processes[[int]$parent.ParentProcessId]}
    if ($parent -and $expected -eq 'codex' -and $parent.Name -ieq 'cmd.exe' -and $parent.CommandLine -match '\s/d\s+/c\s+cd\s+/d\s+\S+\\\.nexus\\nexus-windows\s+&&\s+\.\\NexusWindows\.exe\s+--mcp\s+--agent\s+codex\s*$') {$parent=$Processes[[int]$parent.ParentProcessId]}
    if (-not $parent) {return $null}
    if ($expected -eq 'codex' -and $parent.Name -ieq 'codex.exe' -and $parent.CommandLine -notmatch '(?:^|\s)exec(?:\s|$)') {return $expected}
    if ($expected -eq 'antigravity' -and ($parent.Name -in @('antigravity.exe','language_server_windows_x64.exe') -or ($parent.Name -ieq 'language_server.exe' -and $parent.ExecutablePath -like '*\Programs\Antigravity\resources\bin\language_server.exe'))) {return $expected}
    if ($expected -eq 'claude' -and $parent.Name -ieq 'claude.exe' -and $parent.CommandLine -notmatch '(?:^|\s)--type(?:\s|=)' -and ($parent.ExecutablePath -ieq (Join-Path $env:USERPROFILE '.local\bin\claude.exe') -or $parent.ExecutablePath -match '\\Claude-Data\\anthropicpbc\.claude_[a-z0-9]+\\claude-code\\\d+\.\d+\.\d+\\[a-f0-9]{12,64}\\claude\.exe$')) {return $expected}
    return $null # A qualification client below an AI's shell is deliberately not that AI's connection.
}
function Get-NexusLoadedVersion($Product) {
    $versions=@(); $unknown=$false;$agents=@()
    $processes=@(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,Name,CommandLine,ExecutablePath,CreationDate -ErrorAction Stop)
    $table=@{};foreach($process in $processes){$table[[int]$process.ProcessId]=$process}
    if ($Product.Key -eq 'windows') {
        $installed=Read-Json (Join-Path $env:USERPROFILE '.nexus\nexus-windows\installation.json')
        foreach ($process in $processes | Where-Object { $_.Name -ieq 'NexusWindows.exe' -and $_.CommandLine -match '\s--mcp\s+--agent\s+(claude|codex|antigravity)\s*$' }) {
            # Do not count an inert stable launcher alongside its child engine.
            if ($process.ExecutablePath -ieq (Join-Path $env:USERPROFILE '.nexus\nexus-windows\NexusWindows.exe')) {continue}
            $agent=Get-NexusProcessAgent $process $table
            if (-not $agent) {continue}
            $receipt=Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.nexus\status\runtime') -Filter "windows-$agent-$($process.ProcessId)-*.json" -ErrorAction SilentlyContinue | ForEach-Object {Read-Json $_.FullName} | Where-Object {
                $birth=[DateTimeOffset]::MinValue
                $_ -and $_.schema -eq 1 -and $_.product -eq 'windows' -and $_.state -in @('idle','busy') -and $_.agent -eq $agent -and $_.pid -eq $process.ProcessId -and $_.executable -ieq $process.ExecutablePath -and [DateTimeOffset]::TryParse([string]$_.processStartUtc,[ref]$birth) -and [Math]::Abs(($birth.UtcDateTime-$process.CreationDate.ToUniversalTime()).TotalSeconds) -lt 0.01 -and [string]$_.processStartTicks -ceq $birth.UtcDateTime.Ticks.ToString() -and $_.assembly -ieq (Join-Path (Split-Path $process.ExecutablePath) 'NexusWindows.dll') -and $_.assemblySha256 -match '^[a-f0-9]{64}$' -and (Test-Path -LiteralPath $_.assembly) -and (Get-FileHash -LiteralPath $_.assembly -Algorithm SHA256).Hash -ieq $_.assemblySha256
            } | Select-Object -First 1
            if ($receipt -and $receipt.version -match '^\d+\.\d+\.\d+$') {
                $versions += [string]$receipt.version
                if (-not $installed -or $receipt.executable -ine $installed.NativeExecutable -or $receipt.assemblySha256 -ine $installed.NativeArtifacts.'NexusWindows.dll') {$unknown=$true}
                else {$agents += [pscustomobject]@{agent=$agent;version=[string]$receipt.version;processStartUtc=$receipt.processStartUtc}}
            } else { $unknown=$true }
        }
    } elseif ($Product.Key -eq 'browser') {
        $installed=Read-Json (Join-Path $env:LOCALAPPDATA 'NexusBrowser\current.json')
        $health=Invoke-RestMethod -UseBasicParsing 'http://127.0.0.1:16182/health' -TimeoutSec 3 -ErrorAction SilentlyContinue
        if ($health -and $health.status -eq 'ok' -and $health.service -eq 'nexus-browser' -and $health.version -match '^\d+\.\d+\.\d+$' -and @($processes | Where-Object { $_.ProcessId -eq $health.pid -and $_.Name -ieq 'NexusBrowser.exe' -and $_.CommandLine -match '\s--http(?:\s|=)' }).Count) {
            $versions += [string]$health.version
            if (-not $installed -or -not @($processes | Where-Object {$_.ProcessId -eq $health.pid -and $_.ExecutablePath -ieq $installed.exe}).Count -or ($health.fingerprint -and $health.fingerprint -ine $installed.fingerprint)) {$unknown=$true}
        }
        elseif (@($processes | Where-Object {$_.Name -ieq 'NexusBrowser.exe' -and $_.CommandLine -match '\s--(?:http|bridge)(?:\s|=)'}).Count) { $unknown=$true }
        $tokenPath=Join-Path $env:USERPROFILE '.nexus\nexus-browser\bridge-token'
        if (Test-Path -LiteralPath $tokenPath) {
            try {
                $bridge=Invoke-RestMethod -UseBasicParsing 'http://127.0.0.1:9225/api/status' -Headers @{'X-Nexus-Browser-Token'=[IO.File]::ReadAllText($tokenPath).Trim()} -TimeoutSec 3
                if ($bridge.status -eq 'online' -and $bridge.version -match '^\d+\.\d+\.\d+$' -and @($processes | Where-Object {$_.ProcessId -eq $bridge.pid -and $_.Name -ieq 'NexusBrowser.exe' -and $_.CommandLine -match '\s--bridge(?:\s|=|$)'}).Count) {
                    $versions += [string]$bridge.version
                    if (-not $installed -or $bridge.fingerprint -ine $installed.fingerprint -or -not @($processes | Where-Object {$_.ProcessId -eq $bridge.pid -and $_.ExecutablePath -ieq $installed.exe}).Count) {$unknown=$true}
                }else{$unknown=$true}
                # Ping is an authenticated read-only extension command with no AI/session identity. It neither
                # takes tabs nor changes focus. Its live response, not a package manifest, proves Chrome's version.
                $ping=Invoke-RestMethod -UseBasicParsing 'http://127.0.0.1:9225/api/command' -Method Post -ContentType 'application/json' -Headers @{'X-Nexus-Browser-Token'=[IO.File]::ReadAllText($tokenPath).Trim()} -Body '{"action":"ping","params":{},"timeout":3}' -TimeoutSec 5
                if ($ping.success -eq $true -and $ping.data.status -eq 'pong' -and $ping.data.version -match '^\d+\.\d+\.\d+$') {$versions += [string]$ping.data.version}else{$unknown=$true}
            }catch{$unknown=$true}
        }else{$unknown=$true}
    }
    return [pscustomobject]@{versions=@($versions | Select-Object -Unique);unknown=$unknown;agents=$agents}
}
function Invoke-NexusPluginMaintenance($Product,$Profiles) {
    foreach ($integration in $Profiles | Where-Object {$_.route -eq 'catalogue'}) {
        if ($integration.key -eq 'codex') {
            $cli=Get-CodexCli
            if (-not $cli) { throw 'The signed Codex CLI is unavailable; no configuration was replaced.' }
            if (Install-CodexMarketplace $cli 'pironjulien/NexusSystem') { throw 'Codex catalogue refresh failed.' }
            if (Install-CodexPlugin $cli $Product.Plugin) { throw 'Codex plugin refresh failed.' }
            $catalogue=(& $cli plugin list --json | Out-String) | ConvertFrom-Json
            if ($LASTEXITCODE) { throw 'Codex could not report its installed plugin.' }
            $entry=@($catalogue.installed | Where-Object {$_.pluginId -eq "$($Product.Plugin)@$Marketplace" -or $_.id -eq "$($Product.Plugin)@$Marketplace"}) | Select-Object -First 1
            $integration.pluginVersion=if($entry){[string]$entry.version}else{$null}
            $integration.reason='Plugin files verified; active conversations may still require reloading.'
        } elseif ($integration.key -eq 'claude-code') {
            $cli=Get-ClaudeCli
            if (-not $cli) { throw 'The trusted Claude Code CLI is unavailable; plugin activation remains pending.' }
            & $cli plugin marketplace update $Marketplace 2>&1 | Out-Host
            if ($LASTEXITCODE) { throw 'Claude catalogue refresh failed.' }
            & $cli plugin update "$($Product.Plugin)@$Marketplace" --scope user --json 2>&1 | Out-Host
            if ($LASTEXITCODE) { throw 'Claude plugin update failed.' }
            $cache=Read-Json (Join-Path $env:USERPROFILE '.claude\plugins\installed_plugins.json')
            $entry=if($cache -and $cache.plugins -and $cache.plugins.PSObject.Properties["$($Product.Plugin)@$Marketplace"]){@($cache.plugins.("$($Product.Plugin)@$Marketplace") | Where-Object {$_.scope -eq 'user'}) | Select-Object -First 1}else{$null}
            if (-not $entry -or -not $entry.installPath -or -not (Test-Path -LiteralPath (Join-Path $entry.installPath "skills\$($Product.Plugin)\SKILL.md"))) { throw 'Claude did not return an installed guide; activation remains pending.' }
            $integration.pluginVersion=[string]$entry.version
            $settingsPath=Join-Path $env:USERPROFILE '.claude\settings.json'; $settings=Read-Json $settingsPath
            if ($settings -and $settings.extraKnownMarketplaces -and $settings.extraKnownMarketplaces.PSObject.Properties[$Marketplace]) {
                $entry=$settings.extraKnownMarketplaces.($Marketplace)
                if (-not $entry.PSObject.Properties['autoUpdate']) { Write-Property $entry 'autoUpdate' $true; Write-JsonWithBackup $settingsPath $settings }
            }
            $integration.reason='Plugin updated on disk; /reload-plugins or a new session activates it.'
        }
    }
}
function Invoke-NexusMaintenance([switch]$Current,[switch]$Force) {
    $root=Get-NexusMaintenanceRoot
    New-Item -ItemType Directory -Force $root | Out-Null
    # File lock is released by Windows even after an interrupted updater. No stale lock heuristics or process kills.
    $lock=$null
    try { $lock=[IO.File]::Open((Join-Path $root 'maintenance.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
    catch [IO.IOException] { return 2 }
    $work=Join-Path $root "run-$PID"
    $state=[ordered]@{schema=1;state='checking';checked_at=[DateTimeOffset]::UtcNow.ToString('O');nextCheckAt=[DateTimeOffset]::UtcNow.AddHours(1).ToString('O');activationPending=$false;retryable=$false;latest=$null;catalogue=$null;products=@();message='Checking published releases and installed routes.'}
    $savedPack=$env:NEXUS_PACK; $savedTargets=$env:NEXUS_PACK_TARGETS
    $bootstrap=$null;$preserveStatus=$false
    try {
        $choices=Read-Json (Join-Path $env:USERPROFILE '.nexus\nexussystem.json')
        if ($choices -and $choices.autoUpdate -eq $false) {
            $state.state='disabled';$state.message='Automatic updates are disabled in nexussystem.json.'
            return 0
        }
        $previous=Read-Json (Get-NexusMaintenanceStatusPath)
        $next=[DateTimeOffset]::MinValue
        if (-not $Force -and -not $Current -and $previous -and [DateTimeOffset]::TryParse([string]$previous.nextCheckAt,[ref]$next) -and $next -gt [DateTimeOffset]::UtcNow) {
            $state=$previous;$preserveStatus=$true
            return $(if($previous.state -eq 'failed'){1}elseif($previous.state -eq 'pending'){2}else{0})
        }
        New-Item -ItemType Directory -Force $work | Out-Null
        Write-NexusMaintenanceStatus $state
        if ($Current) {
            # Explicit qualification of this file: published products only, no downgrade or durable copy of a
            # development manager. The older public catalogue is not executed and need not implement maintenance.
            $tag=Get-LatestTag 'NexusSystem'
            if ($tag -notmatch '^v\d+\.\d+\.\d+$') {throw 'Invalid published catalogue version.'}
            $catalogue=[pscustomobject]@{tag=$tag;version=$tag.Substring(1);sha256=(Get-FileHash -LiteralPath $script:NexusPackScript -Algorithm SHA256).Hash}
        } else {$catalogue=Get-NexusPublishedInstaller 'NexusSystem' $work 'get-nexus.ps1'}
        $state.latest=$catalogue.version; $state.catalogue=$catalogue.tag
        if (-not $Current -and [version]$catalogue.version -lt [version]$NexusMaintenanceVersion) {
            $state.state='pending';$state.message='This manager is newer than the published release; no downgrade or development payload was propagated.'
            return 2
        }
        $durable=Join-Path $root 'get-nexus.ps1'
        if (-not $Current -and (Get-FileHash -LiteralPath $script:NexusPackScript -Algorithm SHA256).Hash -ine $catalogue.sha256) {
            # Hand the next run to the published manager only after releasing the lock below.
            Copy-Item -LiteralPath $catalogue.file -Destination "$durable.$PID.tmp" -Force
            Move-Item -LiteralPath "$durable.$PID.tmp" -Destination $durable -Force
            $bootstrap=$durable
        } else {
            $env:NEXUS_PACK='1'
            # These existing detection functions deliberately return one array object. Assign before piping so
            # multi-AI profiles and multi-entry skip lists are not accidentally nested and then overlooked.
            $skip=Get-LocalSkip; $found=Find-AiApp
            foreach ($product in $Products | Where-Object {$_.Repo -and $skip -notcontains $_.Key}) {
                $profiles=@($found | Where-Object {$product.Targets -contains $_ -and $skip -notcontains $_} | ForEach-Object {Get-NexusMaintenanceProfile $product $_})
                $active=@($profiles | Where-Object {$_.route -and $_.route -ne 'custom'})
                $installed=Get-NexusInstalledVersion $product
                if (-not $installed -and -not $active.Count) { continue }
                $row=[ordered]@{key=$product.Key;publishedVersion=$null;installedVersion=$installed;loadedVersions=@();state='pending';activationPending=$false;retryable=$false;message='';profiles=$profiles}
                try {
                    if (-not $active.Count) { $row.state='kept'; $row.message='No active owned route; disabled and custom routes were preserved.' }
                    else {
                        $tag=Get-LatestTag $product.Repo
                        if ($tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Invalid published version.' }
                        $published=$tag.Substring(1); $row.publishedVersion=$published
                        # UpdateOnly is a qualified release contract, never sent to older installers.
                        $minimum=switch($product.Key){'windows'{'3.9.5'} 'browser'{'2.33.5'} default{$null}}
                        if (-not $minimum) { $row.state='unsupported'; $row.message='This product owns its update protocol; automatic installer migration is not qualified.' }
                        elseif (-not $installed) { $row.message='The existing installation could not be verified; no fresh installation was attempted.' }
                        elseif ([version]$published -lt [version]$minimum) { $row.message='The published installer does not yet support preserving existing routes during maintenance.' }
                        else {
                            $env:NEXUS_PACK_TARGETS=@($active | ForEach-Object {$_.key}) -join ','
                            $old=@($previous.products | Where-Object {$_.key -eq $product.Key}) | Select-Object -First 1
                            $profileRefresh=@($active | Where-Object {$_.key -eq 'antigravity' -and $_.pluginVersion -ne $published}).Count -gt 0
                            # A deferred Browser switch is retried on the next bounded cycle even after files
                            # were installed. A plain open AI connection is not a reason to reinstall Windows.
                            $retry=($product.Key -eq 'browser' -and $old -and $old.retryable)
                            if ([version]$published -gt [version]$installed -or $profileRefresh -or $retry) {
                                $release=Get-NexusPublishedInstaller $product.Repo $work
                                if ($release.tag -ne $tag) { throw 'Release changed while preparing the update; retry on the next cycle.' }
                                $parameters=@('UpdateOnly');$arguments=@('-UpdateOnly')
                                if ($product.Key -eq 'windows' -and @($active | Where-Object {$_.key -eq 'antigravity'}).Count) {$parameters+='RefreshExistingProfiles';$arguments+='-RefreshExistingProfiles'}
                                Assert-NexusUpdateContract $release.file $parameters
                                Assert-NexusOfficialUpdateEnvironment $product.Key
                                if (Invoke-ScriptFile "$($product.Name) maintenance" $release.file $arguments) { throw 'The product update failed.' }
                                $installed=Get-NexusInstalledVersion $product; $row.installedVersion=$installed
                                if (-not $installed -or [version]$installed -lt [version]$published) { throw 'The installed version did not reach the published version.' }
                            }
                            Invoke-NexusPluginMaintenance $product $active
                            foreach($integration in $active | Where-Object {$_.route -eq 'catalogue'}) {
                                if (-not $integration.pluginVersion -or [version]$integration.pluginVersion -lt [version]$published) {
                                    $integration.state='pending';$integration.activationPending=$true;$integration.reason='The published catalogue has not supplied this product version yet; distribution remains pending.'
                                }
                            }
                            foreach($integration in $active | Where-Object {$_.key -eq 'antigravity'}) {
                                $refreshed=Get-NexusMaintenanceProfile $product 'antigravity';$integration.pluginVersion=$refreshed.pluginVersion
                                if ($integration.pluginVersion -ne $published) {throw 'The existing Antigravity plugin has not reached the published version.'}
                            }
                            $loaded=Get-NexusLoadedVersion $product
                            $row.loadedVersions=@($loaded.versions)
                            foreach($integration in $active) {
                                if ($product.Key -eq 'browser') {
                                    if(-not $integration.activationPending){$integration.state='unknown';$integration.reason='The shared daemon, bridge and extension are observed separately; this does not attest an individual chat guide.'}
                                    continue
                                }
                                if ($integration.activationPending) {continue}
                                $agent=switch($integration.key){'claude-code'{'claude'}'codex'{'codex'}'antigravity'{'antigravity'}default{$null}}
                                $connections=@($loaded.agents | Where-Object {$_.agent -eq $agent})
                                if (-not $integration.activationPending -and $connections.Count -and -not @($connections | Where-Object {$_.version -ne $installed}).Count) {
                                    $integration.state='runtime-verified';$integration.reason='The host owns a matching running engine; guide loading in existing chats remains unknown.'
                                } else {$integration.activationPending=$true;$integration.state='pending';$integration.reason='No matching host-owned engine attestation; the existing session is preserved.'}
                            }
                            if ($loaded.unknown -or @($loaded.versions | Where-Object {[version]$_ -ne [version]$installed}).Count) {
                                $row.state='pending';$row.activationPending=$true;$row.retryable=($product.Key -eq 'browser'); $row.message='Installed; an older or unattested running connection is preserved until its safe reload.'
                            } elseif (-not $loaded.versions.Count) {
                                $row.state='pending';$row.activationPending=$true;$row.retryable=($product.Key -eq 'browser'); $row.message='Installed; no running connection has attested activation yet.'
                            } else {
                                $row.state='success'; $row.message='Published version installed; observed running engine versions match. Guide loading in existing chats is not attested.'
                            }
                            if (@($active | Where-Object {$_.activationPending}).Count) {$row.activationPending=$true;if($row.state -eq 'success'){$row.state='pending'}}
                        }
                    }
                } catch { $row.state='failed'; $row.message=$_.Exception.Message }
                $state.products += [pscustomobject]$row
                Write-NexusMaintenanceStatus $state
            }
            $state.activationPending=@($state.products | Where-Object {$_.activationPending}).Count -gt 0
            $state.retryable=@($state.products | Where-Object {$_.retryable}).Count -gt 0
            $state.state=if (@($state.products | Where-Object {$_.state -eq 'failed'}).Count) {'failed'} elseif (@($state.products | Where-Object {$_.state -eq 'pending'}).Count) {'pending'} else {'success'}
            $state.message='Installed files and observed runtimes are reported separately; no AI process was stopped.'
        }
    } catch {$state.state='failed';$state.retryable=$true;$state.message=$_.Exception.Message}
    finally {
        if(-not $preserveStatus){
            $state.checked_at=[DateTimeOffset]::UtcNow.ToString('O')
            if($state.state -eq 'failed'){$state.nextCheckAt=[DateTimeOffset]::UtcNow.AddMinutes(15).ToString('O')}
            Write-NexusMaintenanceStatus $state
        }
        if ($null -eq $savedPack) {Remove-Item Env:NEXUS_PACK -ErrorAction SilentlyContinue}else{$env:NEXUS_PACK=$savedPack}
        if ($null -eq $savedTargets) {Remove-Item Env:NEXUS_PACK_TARGETS -ErrorAction SilentlyContinue}else{$env:NEXUS_PACK_TARGETS=$savedTargets}
        # Only this run's absolute child directory is disposable. Never clean product payloads or runtime folders.
        $absolute=[IO.Path]::GetFullPath($work);$parentRoot=[IO.Path]::GetFullPath($root).TrimEnd('\')+'\'
        if ($absolute.StartsWith($parentRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $absolute -Leaf) -ceq "run-$PID" -and (Test-Path -LiteralPath $absolute) -and -not ((Get-Item -LiteralPath $absolute).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Remove-Item -LiteralPath $absolute -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($lock) {$lock.Dispose()}
    }
    if ($bootstrap) { return Invoke-ScriptFile 'NexusSystem maintenance manager' $bootstrap @('-Maintain','-MaintenanceCurrent') }
    if ($state.state -eq 'failed') {return 1}
    if ($state.state -eq 'pending') {return 2}
    return 0
}

function Invoke-NexusPack([string[]]$Only, [string[]]$Skip, [bool]$DryRun, [bool]$NoGuide, [string]$Catalog) {
    if ($env:OS -ne 'Windows_NT') { Say 'The Nexus pack runs on Windows 10 or 11 only.' Red; return 1 }
    $onlyList = Split-NameList $Only
    $skipList = Split-NameList $Skip
    $productKeys = @($Products | ForEach-Object { $_.Key })
    $aiKeys = @($AiNames.Keys)
    $localSkip = Get-LocalSkip
    foreach ($name in $localSkip) {
        if ($productKeys -notcontains $name -and $aiKeys -notcontains $name) { Say "nexussystem.json: unknown name '$name' ignored." Yellow; continue }
        if ($skipList -notcontains $name) { $skipList += $name }
    }
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
            $product.Targets -contains $_ -and -not ($product.Plugin -and -not $product.Engine -and $_ -eq 'claude-code') })
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
    # Claude Code and Codex add a catalog by cloning it with git, which a new Windows lacks.
    $needsGit = (($for['claude-code'] -and @($selected | Where-Object { $_.Plugin -and -not $kept["$($_.Key)|claude-code"] }).Count) -or
        ($for['codex'] -and @($selected | Where-Object { $_.Plugin -and -not $kept["$($_.Key)|codex"] }).Count)) -and -not (Test-Git)
    Say ''
    $gitFor = @('claude-code', 'codex' | Where-Object { $for[$_] } | ForEach-Object { $AiNames[$_] }) -join ' and '
    Say "Prerequisites, once: $(if ($needsGit) { "Git (for $gitFor)" } else { 'none' })"
    Send-Event @{ type = 'plan'; ais = @($ais); products = @($selected | Where-Object { $wants[$_.Key] } | ForEach-Object { $_.Key }); git = [bool]$needsGit
                  installer_targets = $targets }  # AI apps told to each one-run installer (NEXUS_PACK_TARGETS)
    if ($DryRun) { Say 'Dry run: nothing installed.' Yellow; return 0 }

    $work = Join-Path ([IO.Path]::GetTempPath()) "nexus-pack-$PID"
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $savedPack = $env:NEXUS_PACK
    $env:NEXUS_PACK = '1'  # the product installers' non-interactive mode: no question, no wait, no window
    $results = @{}  # "product|ai" -> exit code of the route that serves it
    $prereq = @{}
    try {
        if ($needsGit) {
            if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) {
                Say 'Git is needed to add the Nexus plugin catalog and winget is missing: install Git from https://git-scm.com/downloads/win, then run this again.' Red
                $prereq['git'] = 1
            } else {
                $prereq['git'] = Invoke-Native 'Git (official package, through winget)' {
                    Invoke-Console (Get-Command winget.exe).Source @('install', '--exact', '--id', 'Git.Git', '--source', 'winget', '--accept-source-agreements', '--accept-package-agreements')
                }
                Sync-SessionPath
                if (Test-Git) { $prereq['git'] = 0 } elseif (-not $prereq['git']) { $prereq['git'] = 1 }
            }
        }

        # 1. The plugins first: the product installers see them and add no second server for Claude Code or Codex.
        if ($for['claude-code']) {
            $withPlugin = @($selected | Where-Object { $_.Plugin -and $wants[$_.Key] -and -not $kept["$($_.Key)|claude-code"] })
            if ($withPlugin.Count) {
                $code = if ($prereq['git']) { 1 } else { Install-ClaudePlugin @($withPlugin | ForEach-Object { $_.Plugin }) }
                foreach ($product in $withPlugin) { $results["$($product.Key)|claude-code"] = $code }
            }
        }
        # Codex gets the real plugin (MCP plus its guide); the final pass below removes only the exact direct
        # declarations an official installer wrote (today's or a former one), once the plugin is verified.
        $codexPlugins = @($selected | Where-Object { $_.Plugin -and $wants[$_.Key] -and $for['codex'] -and -not $kept["$($_.Key)|codex"] })
        $codexCli = $null
        if ($codexPlugins.Count) {
            $codexCli = Get-CodexCli
            $catalogCode = if (-not $codexCli -or $prereq['git']) { 1 } else { Install-CodexMarketplace $codexCli $Catalog }
            if (-not $codexCli) { Say 'The installed Codex engine with a valid OpenAI signature was not found. Open or update Codex, then run this again.' Red }
            foreach ($product in $codexPlugins) {
                $code = if ($catalogCode) { 1 } else { Install-CodexPlugin $codexCli $product.Plugin }
                $results["$($product.Key)|codex"] = $code
            }
        }
        # 2. Each product's own native installer, in this order: NexusWindows (engine at its stable path), NexusAndroid
        #    (daemon and adb, without waiting for the phone), NexusBrowser (daemon and Chrome's native host), NexusRelay
        #    (daemon), NexusAGYmail (its Antigravity plugin; the Google sign-in is the user's own step, below). Each one
        #    is told the AI apps chosen here; a failed installer fails every cell it serves, plugin cells included.
        foreach ($key in 'windows', 'android', 'browser', 'relay', 'agymail') {
            $product = Get-Product $key
            if (-not $wants[$key] -or -not $targets[$key].Count) { continue }
            $env:NEXUS_PACK_TARGETS = $targets[$key] -join ','
            $code = Invoke-Installer $product.Name $product.Repo $work
            foreach ($ai in $targets[$key]) { if (-not $results["$key|$ai"]) { $results["$key|$ai"] = $code } }
        }
        # 3. NexusKeypad: the full plugin for Logi Options+ (keys placed by a guide, below).
        if ($wants['keypad']) {
            $results['keypad|pc'] = Invoke-Installer 'NexusKeypad' 'NexusKeypad' $work
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

    # A successful product installation also establishes the one shared update authority. Never register a task
    # from a dry run, a selection without products, or a test that loaded these functions instead of this file.
    $maintenanceFailed = $false
    if ($script:NexusPackScript -and @($results.Values | Where-Object { $_ -eq 0 }).Count) {
        try { Install-NexusMaintenance $script:NexusPackScript }
        catch { $maintenanceFailed = $true; Say "Maintenance could not be registered: $($_.Exception.Message)" Red }
    }

    # Summary: a cell is green when its route succeeded AND the AI app now declares the product.
    Say ''
    Say 'Summary' Cyan
    $failed = if ($maintenanceFailed) { 1 } else { 0 }
    foreach ($name in @('git')) {
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
        Say '     Plug it in with a USB data cable and accept this PC on the phone; the first connection is guided by'
        Say '     https://github.com/pironjulien/NexusAndroid#quick-start'
    }
    # The Codex project, only when Codex answers (Claude Code is the host); as the host, Codex needs none.
    if ($wants['relay'] -and $targets['relay'] -contains 'codex' -and $targets['relay'] -contains 'claude-code') {
        $step++
        Say "  $step. Codex, once: Add a project, name ""Nexus Relay"", folder $env:USERPROFILE\.nexus\relay\codex-projet"
        Say '     (https://github.com/pironjulien/NexusRelay#quick-start)'
    }
    if ($wants['agymail']) {
        $step++
        Say "  $step. Gmail: create your own Google OAuth client (Desktop app), then sign in with it as shown by"
        Say '     https://github.com/pironjulien/NexusAGYmail#quick-start'
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

$script:NexusPackScript = $PSCommandPath
if ($MaintenanceStatus) {
    Get-NexusMaintenanceStatus | ConvertTo-Json -Depth 12
    exit 0
}
if ($Maintain) {
    if ($DryRun -or $Only.Count -or $Skip.Count -or $Catalog -ne 'pironjulien/NexusSystem') {
        throw 'Maintenance uses installed products and local choices only; DryRun/Only/Skip/Catalog are installation options.'
    }
    $code = Invoke-NexusMaintenance -Current:$MaintenanceCurrent -Force:$ForceMaintenance
    exit $code
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
    exit $code  # run as a file (release download, CI, tests); never close the window of a session that dot-sourced it
}
