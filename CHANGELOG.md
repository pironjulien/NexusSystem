# Changelog

All notable changes to NexusSystem are documented here: the plugin catalogs and one-line install (versions `x.y.z`,
tags `vx.y.z`) and the Windows app on the Microsoft Store (versions `app a.b.c.d`, tags `nexussystem-va.b.c.d`).
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.6.33] - 2026-10-30

### Changed
- Complete native Windows catalog: NexusWindows 3.9.1 starts its stable C# engine for Claude Code and Codex;
  NexusBrowser 2.33.0 reaches the shared local daemon. Their engines are installed by each product's published
  `get.ps1`, checked against release SHA-256 sums and run as files. No Python, uv or Node.js prerequisite remains.
- Every native installer receives exactly the chosen AI apps through `NEXUS_PACK_TARGETS`; disabled products and
  per-PC preferences remain respected. Failed engine installation fails the plugin's status too.
- The Codex NexusWindows plugin is part of this catalog, removing the dependency on the archived per-profile
  repository. Existing native and legacy direct declarations migrate only when their exact signature is known.
- Removed obsolete PyPI locks, uv CI setup and PyPI smoke checks. Clean-machine CI reaches each native server
  through its AI app's installed declaration and repeats the installation to verify it stays green.

## [1.6.32] - 2026-10-30

### Added
- This PC's own choices, outside the code: `%USERPROFILE%\.nexus\nexussystem.json`, for example
  `{"skip": ["claude-desktop"]}` (names of `-Skip`: products or AI apps). `get-nexus.ps1` adds them to `-Skip`; the app
  never offers what they skip. A missing or unreadable file means no choice.
- App: Claude Desktop's chat gets each product's extension (`.mcpb` of its latest release, checked against its
  `SHA256SUMS.txt` when listed), downloaded into `~\.nexus\claude-desktop` and shown in File Explorer, to drop on Claude
  Desktop; offered for NexusWindows, NexusAndroid and NexusBrowser when Claude Desktop is on the PC, unless skipped.

### Changed
- Install documentation follows the file-based method of release 1.6.31: the NexusSystem app of the Microsoft Store
  first (`winget install --id 9NSH2P3QLWT1 --source msstore`), then the `get-nexus.ps1` file of the latest release,
  checked against its `SHA256SUMS.txt` and run with `-File` (README, `INSTALL-AI.md`, plugin READMEs and skills).
- App: the AI prompt asks for the pack script as a checked file run with `-File`; the NexusBrowser pairing fallback
  and the Gmail sign-in step point to the products' install pages instead of an in-memory command.
- Tests: no file may run downloaded code in memory; the post-install check finds `npx`-style commands through PATH.

## [1.6.31] - 2026-10-30

A product moves to its native form only once its native release and `get.ps1` are published: what is installed is
always published before the installer that depends on it. The native catalogs pushed earlier today (b170a22) came
before those releases and broke the public install; they are back to the forms that work, and the native work waits
on a local branch to be applied product by product.

### Security
- No installer code is ever run in memory from the network: Microsoft Defender flagged the one-line forms
  (`irm … | iex`, a script block of a download) as Trojan:Win32/Commando.A!ml. `get-nexus.ps1` now fetches each
  product's `get.ps1` as a FILE of its latest release (versioned URL), checks it against that release's
  `SHA256SUMS.txt` (or the `get.ps1.sha256` beside it), and runs it in a child `powershell.exe -File`. NexusWindows
  for Claude Desktop, whose release does not carry an installer yet, uses its v3.8.0 `claude/setup.ps1` pinned by
  commit and SHA-256. uv comes from Astral's release binaries, SHA-256 checked (no installer script).
- CI installs Claude Code from its installer saved to a file; the Defender probe tries only the release file.
### Changed
- NexusAndroid is native (0.22.0): the `nexus-android` plugins (Claude Code 0.22.0, Codex) reach its daemon
  (`127.0.0.1:16180/mcp`, token read from the user variable `NEXUS_MCP_TOKEN`), installed by its `get.ps1`, which the
  pack runs with `NEXUS_PACK=1` and `NEXUS_PACK_TARGETS`; its `pyproject.toml` and `uv.lock` are gone. The Codex
  migration also recognises the daemon declaration its installer may write.
- NexusRelay is native (1.10.0): the `nexus-relay` plugins (1.10.0) keep its daemon; the Codex edit guard calls the
  engine NexusRelay's installer keeps at `%USERPROFILE%\.nexus\nexus-relay\bin\NexusRelay.exe` through `cmd.exe`
  (before, after the edit and at the end of the turn), and Claude Code's plugin now carries the edit guard and the
  unread-message notice from the same engine. uv is no longer needed for the relay.- App: the Claude Desktop extension of NexusWindows comes from the single NexusWindows repository
  (`nexus-windows-claude.mcpb` of its latest release), no longer from the archived NexusWindows-Claude.
- NexusAGYmail is installed by its native `get.ps1` (v0.14.0: Windows engine, SHA-256 checked, Antigravity plugin);
  its Google sign-in is `get.ps1 -Auth`, in the pack's last steps and in the app. uv is a prerequisite only when a
  product still served from PyPI is chosen (NexusWindows, NexusAndroid, NexusBrowser, NexusRelay).
- Tests: one `dotnet test` (xUnit v3, Microsoft.Testing.Platform) from the repository root, run by `test.ps1` and CI
  alike: catalog checks (ported from `check_catalog.py`), pack dry runs under Windows PowerShell 5.1, the app's tests,
  and in CI every PyPI server of the catalogs started as its `.mcp.json` says (`NEXUS_CATALOG_SMOKE=1`, ported from
  `smoke_mcp.py`). The CI pack job (run by hand) reaches every declared server after a real install (ported from
  `check_install.py`). Build and test output in `~\.nexus\build\NexusSystem`.
- The NexusSystem app also counts the stable NexusWindows path (`~\.nexus\nexus-windows\NexusWindows.exe`) as the
  common core, ready for NexusWindows' native release.
- CI: gitleaks and zizmor jobs on every push and pull request.

### Added
- `nexus-keypad` plugin for Claude Code: live states for the Logitech keypad, exact usage and Nexus Relay's peers
  under the prompt (NexusKeypad's native helper and Claude's modules).

### Removed
- `tests/check_catalog.py`, `tests/check_install.py`, `tests/smoke_mcp.py` (ported to xUnit), `app/tools/demo_profile.py`
  and `icon_from_site.py` (now PowerShell: `capture.ps1`, `icon-from-site.ps1`).
## [1.6.30] - 2026-10-30

### Changed
- One repository for NexusSystem: the Microsoft Store app's source (`app/`) now lives beside the catalogs and the
  one-line install, with one changelog.
- NexusWindows links point to its single repository, `pironjulien/NexusWindows` (the per-profile repositories are
  archived; the Codex plugin and the Claude Desktop bundle are still fetched from them until NexusWindows releases them).
- Plugins pinned to nexus-android 0.21.0, nexus-browser 2.32.0 and nexus-relay 1.9.0.

### Added
- `INSTALL-AI.md`: structured installation and verification guide for AI coding agents (Claude Code, Codex, Antigravity), supporting à-la-carte product selection (`-Only ... -NoGuide`).
- Updated `README.md` to put AI-driven installation first alongside the `NexusSystem` app and the one-line PowerShell installer.

## [app 1.0.10.0] - 2026-10-30

Submitted to the Store on 2026-10-30 (submission 8).

### Changed
- Reads the common journal from `%USERPROFILE%\.nexus\journal`, where every Nexus product now writes it (AppData is
  virtualized for MSIX apps); 1.0.9.0 still reads the old folder and would show no activity.

## [app 1.0.9.0] - 2026-10-30

Submitted to the Store on 2026-10-30 (submission 7, x64+ARM64 bundle; listing FR/EN updated: no Claude Desktop
extension, simple states).

### Changed
- Plain words for everyone: each product page shows one line per AI ("Ready", "To install", "Turned off by you",
  "Needs repair"), plus only what you can act on ("in use right now", "restart Codex for the latest version").
  Versions read "up to date (version X)" or "update ready: X → Y" instead of "latest ?" and check dates; the
  activity card is a one-sentence summary with the raw journal behind "Show details".
- Nexus lives in Claude Code, Codex and Antigravity: the hub no longer offers or reports Claude Desktop extensions.
- The Codex "Nexus Relay" project step is hidden once Relay's Codex workspace is chosen.

### Fixed
- The still-loaded legacy `NexusWindows.Antigravity.exe` MCP client and the observed Antigravity
  `language_server.exe` host now appear as an older active connection instead of idle. Stop guards and mixed modes
  remain excluded. Claude Desktop's Store/Electron host is distinguished from the native Code CLI by executable
  metadata; Desktop configuration and a matching live client are both required to show its common connection active.
- The Windows page displays the single shared C# core from its verified Schema 2 installation manifest, separately
  from each AI's live connection. Older loaded clients remain visible, while qualification clients, stop guards,
  UIA helpers and command-line tools never count as an active AI connection.
- Once the common Windows core is installed, the hub no longer offers an older Claude-specific extension that
  would create a second engine. Claude Desktop's own missing, disabled or different declaration is shown separately
  and is never inferred from an enabled Claude Code plugin.
- Local unpackaged deployment preserves the Store installation and previous local releases. It checks every copied
  payload file and the physical executable/shortcut paths, and uses an unpackaged user worker when the invoking
  terminal inherits an MSIX identity. Core tests run the freshly built executable instead of a recursive stale match.
- NexusSystem recognizes the native Android, Browser, Relay, AGYmail and Windows MCP processes, while excluding
  Chrome's one-shot authentication hosts and UIA helpers. Product pages refresh declarations and running servers
  without blocking the interface, and check the actual Chrome connection separately.
- Saved update metadata is dated and marked when older than the daily check interval. A saved version no longer
  appears as a green service-health result, and a missing state is no longer silently treated as idle.
- Unpackaged publishing includes the application's generated XAML resource index in its normal publish items;
  the Windows App SDK otherwise omits it and the deployed executable crashes while loading its first window.
- A closed Chrome is shown as idle separately from an unpaired extension; older bridges remain compatible.

## [app 1.0.8.0] - 2026-10-29

### Added
- `Products.AiPrompt` and the **Copy prompt for my AI** primary action in `HomePage`: generates a tailored AI installation prompt for the exact products ticked by the user (`get-nexus.ps1 -Only ... -NoGuide` + `INSTALL-AI.md`).
- Official AI logos (`ai-claude.png`, `ai-codex.png`, `ai-antigravity.png`) in the detected AI banner, on each product card's per-AI declaration pills, and on each product page's status lines.
- Select all / Clear all quick actions and a 2-column à-la-carte product grid where every ready product remains selectable at any time.

### Changed
- Suppression du doublon Claude dans NexusSystem : alignement sur le trio souverain d'IA de code (Claude Code, Codex, Antigravity). L'entree distincte Claude Desktop est retiree de l'enumeration AiApp et de l'affichage du hub. La detection de Claude Code couvre la presence de Claude Desktop, son onglet Code (~/.claude), ses extensions .mcpb et ses configurations.
### Fixed
- Sandbox certification supplies WACK's required TAEF runtime and SDK reference metadata as read-only inputs and starts the kit in its own
  directory. Missing TAEF is refused before launch instead of crashing appcert in ExecuteTAEFTaskInstance. WACK's
  binary analyzer COM class is registered in the disposable guest only, and its XML overall result is read explicitly.
  `-CertificationOnly` permits a separate certification run without repeating an already recorded native smoke;
  unsigned inputs require explicit guest signing in that mode and are otherwise refused before deployment.
  reports include the guest's application-control state. A native close now requires an available zero exit code,
  and the host guard uses the current Sandbox inventory instead of treating a leftover RemoteSession client as an active guest.
- The screenshot tool finds the app under the LocalAppData build root and selects the host's architecture instead
  of the last ARM64 or x64 build. Codex and journal paths are explicitly confined to the demonstration profile.
  Cleanup accepts only the default capture folder or a marked custom output, refuses roots and foreign content,
  and removes only recognized generated PNG files instead of recursively deleting the output directory.

## [1.6.29] - 2026-10-28

### Fixed
- The real Windows CI installs Codex's official 0.165.0 engine, pinned by release and SHA-256 and checked for a valid
  OpenAI signature, before installing its plugins. An empty profile no longer substitutes for the actual engine.
- NexusWindows for Antigravity is detected as `nexus-windows`; legacy `nexus-computer` installs and disabled choices
  remain recognised, including an old disabled identity whose directory has already been removed.

## [1.6.28] - 2026-10-28

### Fixed
- The public installer now installs Codex's four Nexus plugins, including their bundled guides, through Codex's own
  plugin commands. It finds the installed OpenAI-signed desktop engine when no CLI is on PATH; no second Codex is
  installed. Product installers still prepare ADB, Chrome and the Relay daemon.
- Codex keeps products the user disabled, custom direct MCP declarations and plugins from another catalog. Only
  exact declarations from the official installers are migrated after the plugin and its guide are verified in
  Codex's cache; the post-install pass removes declarations added by older product installers. Plugin failures and
  missing guides remain visible failures instead of being hidden by a successful runtime installer.
- Codex migration reads literal and escaped TOML keys and spaces around dotted headers. Inline or unsupported
  declarations are preserved; apparent headers inside multiline strings and arrays are never edited as tables.

### Changed
- Android 0.20.3, Browser 2.31.6 and Relay 1.8.7 ship the same public guides used by the catalog. Codex Android and Browser now carry the official product icons. The Relay edit guard ships with its plugin and uses its exact published version.

## [1.6.27] - 2026-10-28

### Changed
- `nexus-windows` 3.7.4: F2 and Enter now work in a covered File Explorer window (simulated activation, the user's
  foreground untouched). Plugin, lock and marketplace entry.

## [1.6.26] - 2026-10-28

### Added
- `nexus-relay` in both catalogs (Claude Code and Codex): the plugin declares the local NexusRelay daemon (its key read
  from the user variable `NEXUS_MCP_TOKEN`, never written) and adds the relay's skill. The daemon is still installed
  by the one-line install or NexusRelay's `get.ps1`; for Claude Code, the one-line install now adds the plugin from
  this catalog, and NexusRelay 1.8.6 then adds no second server (nor for Codex).

### Changed
- The catalog source lives with the NexusSystem app (private repository `nexus-apps`, folder `catalog/`), its history
  kept: one product, one source.

## [1.6.25] - 2026-10-28

### Changed
- The repository is now **NexusSystem** (`pironjulien/NexusSystem`, was `pironjulien/nexus-plugins`; GitHub redirects
  the old address): one product, two ways to install the Nexus ecosystem, the Microsoft Store app or this repository
  (one-line install and plugin catalogs). The catalog name stays `nexus-plugins`, so installed plugins
  (`nexus-windows@nexus-plugins`…) keep working; the Codex catalog is shown as "NexusSystem". `get-nexus.ps1` adds the
  catalog from the new address.

## [1.6.24] - 2026-10-28

### Changed
- `nexus-android` 0.20.2 (was 0.19.0) and `nexus-browser` 2.31.4 (was 2.28.0): the catalogue starts the latest
  published servers (CodeQL fixes, reconnect waits that cancellation stops, extension messages accepted from older or
  newer extensions). Plugins, Codex plugins, locks and marketplace entries.

## [1.6.23] - 2026-10-28

### Changed
- `nexus-windows` 3.7.3 (was 3.5.0): a single key is verified by an effect only it can produce (keyboard focus on a
  new element or window, Ctrl+A selecting more items), dialog [N] numbers stay valid when the dialog is targeted by
  its title, images up to 2000 px with lossless WebP, Tk wheel and covered-window capture fixes, guardrails that see
  through invisible characters and look-alike letters. Plugin, lock and marketplace entry.

## [app 1.0.7.0] - 2026-10-28

### Changed
- The embedded installer gives Codex each Nexus plugin and its bundled guide before installing the product runtime.
  Existing disabled plugins and custom server settings are preserved; an installation is reported as complete only
  after the plugin and its guide have been verified. The public product guides include the distinction between real
  questions and ordinary progress updates.
- Store packaging discovers the installed Visual Studio symbol tool and includes the x64 and ARM64 public-symbol
  packages in the upload, so Partner Center can resolve crash reports. Missing symbol tooling is an explicit error.
- `tools/sandbox.ps1` verifies an exact MSIX hash and version in a fresh Windows Sandbox, with native activation,
  product UI Automation, a screenshot and optional WACK. Inputs stay read-only and reports live in LocalAppData.
  Activation failures include Windows event logs; unsigned builds rejected by Smart App Control remain failures.

### Fixed
- Certification (`tools/wack.ps1`) refuses an MSIX identity already installed on the profile before starting WACK.
  Package mode leaves deployment cleanup to WACK; registered-layout mode removes only its own exact registration,
  preserving a later replacement. Reports default to LocalAppData. Eight isolated ownership scenarios run with the
  app checks, without using WACK or changing the user's installed packages.
- The throwaway-profile runner finds the app under the configured LocalAppData build root.

## [app 1.0.6.0] - 2026-10-28

Package built for the Store on 2026-10-28 (x64+ARM64 bundle).

### Changed
- NexusSystem and the plugin catalog are one product: the public repository `pironjulien/nexus-plugins` is now
  `pironjulien/NexusSystem` (GitHub redirects the old address), and the catalog source lives in this repository
  (`catalog/`). The embedded pack (`pack/get-nexus.ps1`, catalog 1.6.26) adds the catalog from the new address (its
  name stays `nexus-plugins`) and gives Claude Code the `nexus-relay` plugin, the relay's daemon still coming from its
  installer. Store listing and privacy links point to the new repository.

### Fixed
- Error log (`app.log`): one line per start, so an empty log means "no error" and not "the log does not work"; crashes
  outside the UI thread and background tasks nobody awaited are written too; capped at about 1.6 MB (the old file
  becomes `app.log.1`). Written by `Nexus.Core.ErrorLog` (tested). Installed from the MSIX package, Windows keeps it
  in `%LOCALAPPDATA%\Packages\<package>\LocalCache\Local\NexusSystem\app.log`.
- Product page: a product running a newer version than its last daily check (NexusWindows for Antigravity 3.3.0
  with a check saying 3.2.8) was shown with a red cross and that older number as the latest. Only a newer published
  version now counts as an update (`ProductState.CompareVersions`, numeric x.y.z), and the running version is shown
  as the latest known.

## [1.6.22] - 2026-10-27

### Changed
- `nexus-android` 0.19.0: the phone captures its screen without loss, so the image sent to the AI is no longer
  compressed twice.

## [1.6.21] - 2026-10-27

### Changed
- Servers updated for sharper images (long side up to 2000 px, lossless WebP up to 500 KB): `nexus-android`
  0.18.0, `nexus-browser` 2.28.0, `nexus-windows-claude` 3.5.0 (plugins, Codex plugins and locks).
- `nexus-browser` skill (Claude Code and Codex): inline screenshots are now at most 2000 px (was 1568 px), as the
  NexusBrowser server sends them.

## [1.6.20] - 2026-10-27

### Changed
- Plugin icons of `nexus-android`, `nexus-browser` and `nexus-windows`: the Nexus family medallion (512 px) replaces
  the bare pictogram, as on julienpiron.fr and the other stores.

## [app 1.0.5.0] - 2026-10-27

Sent to the Store on 2026-10-27 (submission 4, x64+ARM64 bundle, Windows App Certification Kit PASS with the same 3 optional failures as 1.0.4.0; published automatically once certified).

### Added
- Store package for x64 and ARM64: `tools/package.ps1 -Store` builds both `.msix` and bundles them
  (`NexusSystem_<version>_x64_arm64_bundle.msixupload`, makeappx from the Windows SDK build tools); `-Platforms`
  picks one. The test package stays x64 by default.
- Dependabot version updates, weekly (NuGet and GitHub Actions), grouped; no workflow runs on them.
- Git tags `v1.0.2.0`, `v1.0.3.0` and `v1.0.4.0` on the commits that made those Store versions.
- `test.ps1`: builds the solution (warnings as errors) and runs the Nexus.Core tests, results outside OneDrive
  (`%LOCALAPPDATA%\NexusApps\TestResults`); the workstation sync expects one in every project.

### Changed
- One version for everything: `<Version>` in `Directory.Build.props` (1.0.5.0) gives the assemblies,
  `NexusSystem.exe`'s file version (it said 0.1.0) and, through `tools/package.ps1`, the package manifest;
  `-Version` overrides it for one build.
- The `probe` workflow is turned off on GitHub: its runs never started (the account's Actions billing is blocked).
- Build output (`bin`, `obj`) goes to `%LOCALAPPDATA%\NexusApps\build\<project>` (`Directory.Build.props`), outside
  OneDrive: about 1.5 GB no longer counts against the 5 GB quota nor syncs on every build. The pack test finds the
  catalog from its source file instead of the build folder.

### Fixed
- The NexusBrowser pairing code copied by "Show a code" stays out of the clipboard history (Win+V), the cloud
  clipboard and clipboard monitors (`CanIncludeInClipboardHistory` and `CanUploadToCloudClipboard` at 0,
  `ExcludeClipboardContentFromMonitorProcessing`).
- Release build without any warning: the 7 xUnit analyzer warnings of the tests (test cancellation token,
  `Assert.Contains`).

## [app 1.0.4.0] - 2026-10-27

### Changed
- Small icons are the julienpiron.fr medallion cut round (kit `-round`, transparent corners), never the pictogram
  alone (Julien, 27/10): navigation, title bar, `Square44x44Logo`, `nexus.ico` up to 40 px, NexusSystem included.
  New `Square44x44Logo.targetsize-16/24/32/48` (plated and unplated) so the taskbar, Start and Alt+Tab take a size
  drawn for them instead of scaling the 44 px one. In the app the small icons are 96 px PNG (`nexus<product>-round.png`):
  WinUI's `SvgImageSource` does not draw the image embedded in the kit's SVG (it showed a dark disc only).
- Package logos at every display scale (`scale-100`, 125, 150, 200, 400) for `Square150x150Logo`, `Wide310x150Logo`,
  `StoreLogo` and `Square44x44Logo`, and `Square44x44Logo` at every target size (16 to 256 px, plated, unplated and
  light-unplated), all from the round medallion: Windows never enlarges a tile or mixes shapes on the taskbar.
  Windows App Certification Kit: PASS.

## [1.6.19] - 2026-10-26

### Fixed
- Pack on a new Windows without Git: Claude Code clones the catalog with `git`, so `get-nexus.ps1` now installs Git
  first through winget (`Git.Git`, official package, Windows asks once), only when Claude Code gets a plugin and git
  is missing; without winget it says where to get Git and skips the Claude Code plugins step. Checked in Windows
  Sandbox: Git installed, the three plugins installed and connected.
- Summary: a product whose plugin route failed but whose own installer declared it to Claude Code
  (`~\.claude.json`, NexusAndroid and NexusBrowser) shows "ok" instead of a false "failed"; the state of a
  declaration reads `~\.claude.json` when the plugin is missing.

## [1.6.18] - 2026-10-26

### Changed
- `nexus-windows` pinned to 3.3.1: a document whose default app is a Store app (an MP4 with Media Player) is opened by
  that app's file activation and its window recognized by its process and the document name read in it (it did not
  open in the background before); the UI Automation tree is read again on a transient Electron or Edge error.
  `uv.lock` updated.

## [1.6.17] - 2026-10-26

### Changed
- `nexus-android` pinned to 0.16.0 (Claude Code and Codex plugins): a lock screen without a code (trusted place or
  device, Smart Lock) is lifted by `look` or the first gesture, without asking the user; only a phone that requires
  its code is refused. Skill updated to match. `uv.lock` updated.

### Fixed
- Tests: a server that exits before replying (uvx not finding a version PyPI has just published, even with
  `--refresh`) is tried again for a few minutes, also in the pack's check of every declared server; the message says
  the server exited instead of reporting a 300 s wait.

## [1.6.16] - 2026-10-26

### Changed
- `nexus-browser` pinned to 2.26.4 (Claude Code and Codex plugins): `browser_scroll` turns on focus emulation, so
  infinite feeds (X, Reddit...) keep loading in a background tab. Only that tool's description changes, no tool or
  parameter. `uv.lock` updated.

## [1.6.15] - 2026-10-26

### Changed
- `nexus-android` pinned to 0.15.0 (Claude Code and Codex plugins): the `nexus-android mirror` command (scrcpy with
  the same adb); nothing changes in how the server is declared. `uv.lock` updated. Its README says which sites the
  server contacts (pypi.org, dl.google.com, github.com), never with anything from the phone.
- Tests: right after a PyPI release, the `uv.lock` check and the MCP servers' smoke test fetch the index again
  (`--refresh`) and retry for a few minutes, instead of failing on an index that does not list the new version yet.
- README Quick start: the "Install uv" step shows its command in a block (`winget install --id=astral-sh.uv -e`),
  uv's own installer as the fallback.

### Added
- NexusSystem releases (`nexussystem-v*` tags, never marked latest): the Store app's version, readable by the site.

## [1.6.14] - 2026-10-26

### Changed
- NexusRelay without Claude Code (1.4.0): it needs two AI apps among Claude Code, Codex and Antigravity, and the
  first of them hosts the exchanges, so Codex + Antigravity is enough. The Codex project step is shown only when Codex
  answers (Claude Code hosts); as the host, Codex needs none.

## [1.6.13] - 2026-10-26

### Changed
- `nexus-browser` pinned to 2.26.2 (Claude Code and Codex plugins): a lighter tool list for the model (`tab_id` in
  one sentence, `browser_click`, `browser_fill` and `browser_drag_and_drop` tightened; same tools and parameters),
  a failed update keeps the working version, the pack's AI selection (`NEXUS_PACK_TARGETS`) honored, no second
  Claude Desktop server next to the `.mcpb` extension. `uv.lock` updated.

## [1.6.12] - 2026-10-26

### Changed
- Antigravity: a product turned off there (`plugins.<name>.enabled = false` in `~/.gemini/config/config.json`) counts
  as turned off by the user, like in Claude Code and Codex: kept as it is, never turned on again.

## [1.6.11] - 2026-10-26

### Changed
- `nexus-android` 0.14.1 (Claude Code and Codex), the version the pack requires: `setup` tells the phone and the AI
  apps apart and honors the pack's scope (`NEXUS_PACK_TARGETS`). Skill updated.

## [1.6.10] - 2026-10-26

### Changed
- NexusBrowser takes the selection: from 2.26.1 its `get.ps1` (always the latest) configures only the AI apps in
  `NEXUS_PACK_TARGETS`. The three installers that configure every AI app found in one run now all stay within
  `-Only`/`-Skip` and leave an AI app where the user turned the product off as it is; the pack still refuses any
  future one that would not.

## [1.6.9] - 2026-10-26

### Changed
- NexusRelay takes the selection: from 1.3.2 its `get.ps1` (always the latest) configures only the AI apps in
  `NEXUS_PACK_TARGETS`, so the pack runs it with `-Only`/`-Skip` or beside an AI app where it is turned off. Only
  NexusBrowser is still not run beyond the selection.

## [1.6.8] - 2026-10-26

### Changed
- NexusAndroid takes the selection: from 0.14.1 its `setup` configures only the AI apps in `NEXUS_PACK_TARGETS`, so
  the pack runs it with `-Only`/`-Skip` or beside an AI app where it is turned off (`nexus-android>=0.14.1`, never an
  older one from the index cache). NexusBrowser and NexusRelay are still not run beyond the selection until theirs do.

## [1.6.7] - 2026-10-26

### Changed
- The pack stays within the selection. A product whose installer configures every AI app found in one run
  (NexusAndroid, NexusBrowser, NexusRelay) gets the AI apps to configure in `NEXUS_PACK_TARGETS` (pack names, comma
  separated); until it honors that list, the pack does not run it when it would go beyond `-Only`/`-Skip` or an AI app
  where the user turned the product off, and says so.
- What an AI app declares is read for real: a declaration is ok, missing, turned off by the user (`enabled = false`
  in Codex, a plugin set to `false` in Claude Code, `"disabled": true`) or invalid (its command cannot be found). A
  product turned off by the user is left as it is, never turned on again; the summary shows "off, kept", "not run"
  or "invalid" instead of a green cell for a broken or disabled entry. `-Json` cell events carry this `state`.

### Fixed
- uv's folder (`~\.local\bin`) is put on the session's PATH again: a control character had replaced `\b` in its path.

## [1.6.6] - 2026-10-26

### Changed
- `nexus-browser` pinned to 2.26.0 (Claude Code and Codex plugins): self-update by the bridge (state in
  `~/.nexus/status/browser.json`, `GET`/`POST /api/update`, "Nexus Browser Update" task on a one-line install),
  pairing without the native host (`pair.html`, single-use secret, non-extractable key), Claude Desktop extension
  `nexus-browser.mcpb` on every release, `get.ps1` without the GitHub API, compact tool results, date/time/color
  inputs, field labels in the interactive map. `uv.lock` updated.

## [1.6.5] - 2026-10-26

### Changed
- `nexus-windows` pinned to `nexus-windows-claude` 3.3.0: update state in `~/.nexus/status/windows-claude.json`
  (latest version read from PyPI at most once a day; `NEXUS_UPDATE_CHECK=off` turns it off), WPF wheel one step per
  notch, no false success at the end of a Chromium area. `uv.lock` updated.

## [1.6.4] - 2026-10-26

### Changed
- `nexus-android` 0.13.1 (Claude Code and Codex): `adb` downloaded when missing, guided first connection
  (`nexus-android setup`), step-by-step messages, update state in `~/.nexus/status/android.json`. The plugins set
  `NEXUS_ANDROID_MANAGED`, so the server leaves updates to this catalog. Skill and requirements updated.

## [1.6.3] - 2026-10-26

### Fixed
- NexusKeypad counts as installed from the state its plugin writes (`~\.nexus\status\keypad.json`), else from any of
  its editions in Logi's plugin folder: NexusKeypad (complete edition), TrinitePad (Marketplace "AI Coding Agents",
  or a build from the sources), AiAgentsPc and PcDashboard. Only the complete edition was recognized before.

## [app 1.0.3.0] - 2026-10-26

### Changed
- New icons of the Nexus family kit (`NexusPublish/communication/icones`, as on julienpiron.fr): NexusSystem is the
  cyan chip with the π in the site's medallion; from 44 px the mark (cards, page headers, Store tiles, `nexus.ico`
  from 48 px), under 44 px the icon alone (navigation, title bar, Square44x44, `nexus.ico` up to 40 px).
  `tools/icon_from_site.py` builds them from the kit.

### Fixed
- Install on a new Windows without Git: the pack 1.6.19 installs Git through winget before Claude Code adds the
  Nexus catalog (it clones it with git); without winget it says where to get Git. The results table shows each
  product's real state: NexusAndroid and NexusBrowser declared by their own installer count as installed, no more
  false "failed". The app reads Claude Code's declarations the same way (`~\.claude.json` when the plugin is missing).
- "Open Chrome on its extensions" never does nothing: without Chrome it says so and offers "Install Chrome" (its
  official page); NexusBrowser not installed yet, or Chrome failing to open, is said too. "Pair Chrome" says Chrome
  is missing instead of asking to open it.
- Checked in Windows Sandbox (no .NET, no Git, no Chrome, Claude Code only): Git installed, the three plugins installed
  and connected, every cell ok, the Chrome button answers. Windows App Certification Kit PASS.

## [app 1.0.2.0] - 2026-10-26

### Fixed
- The app now starts on PCs without the .NET runtime: .NET 10 is in the package (`SelfContained`, ReadyToRun, no
  trimming), next to the Windows App SDK already there. 1.0.1.0 stopped on a new Windows with "You must install .NET
  Desktop Runtime" (seen in Windows Sandbox). Nothing else is needed: no VC++ runtime either.

### Added
- Capture mode: `--size WxH` (pictures at 1920 x 1080 for videos) and `--frames` (the home page with products unticked, ticked again, then the screen while the pack runs); `tools/capture.ps1 -Size -Frames`.
- NexusRelay without Claude Code (NexusRelay 1.4.0): it needs two of Claude Code, Codex and Antigravity; the Codex project step only when Codex answers. Pack 1.6.14.
- Antigravity: a product turned off there (`plugins.<name>.enabled = false`) shows as turned off by you, kept. Test of NexusBrowser's update request against a stand-in bridge. Store package 1.0.1.0 built with the pack 1.6.12, built against Windows 11 24H2 (MaxVersionTested 10.0.26100, minimum still Windows 10 2004), Windows App Certification Kit PASS. README: how the hub works from the Store package.
- Declarations read for real, as the pack does: declared, missing, turned off by the user (kept, shown as such) or invalid (command not found). Results table: "off, kept", "not run" (beyond the selection) and "invalid" from the pack's cell state.
- Version card on each product page: what the product itself reports in `~\.nexus\status\<name>.json` (installed,
  latest, state, message; one line per NexusWindows edition). "Update" only where the product takes the request
  through its own interface: NexusRelay's daemon (`POST /api/update`, 1.3.1), NexusBrowser's bridge (the same, 2.26.0) and NexusKeypad's complete edition (its
  pipe, command `update`, the only one ever sent). The others update themselves at their AI app's next start. From
  the Store package, a product already installed is not installed again (it updates itself), and the home button
  never turns into "Update". 33 tests.

### Changed
- The hub is named NexusSystem (one word, like the products): window title, menu, home, manifest DisplayName,
  executable `NexusSystem.exe`, log in `%LOCALAPPDATA%\NexusSystem`, test identity `NexusSystemTest`, Store listing
  and privacy text (julienpiron.fr/nexussystem/confidentialite/). "The Nexus products" stays the family's name. The
  logo stays Julien Piron's π.
- NexusKeypad counts as installed from the state its plugin writes (`~\.nexus\status\keypad.json`), else from any of
  its editions in Logi's plugin folder (NexusKeypad, TrinitePad, AiAgentsPc, PcDashboard), not only the complete one.

### Added
- Store package: `tools/package.ps1` builds the MSIX (test identity, unsigned, for developer mode) or, with `-Store`,
  the `.msixupload` with the identity Partner Center gives. Manifest with `runFullTrust` only, no
  `unvirtualizedResources` (measured useless by the probe). Draft listing in French and English
  (`store/listing.md`), privacy text for julienpiron.fr/nexus/confidentialite/ (`store/privacy.md`), captures at
  1366 px for the Store's desktop screenshots.
- Claude Desktop through its `.mcpb` extensions (NexusWindows, NexusAndroid, NexusBrowser): a step that downloads the
  latest release's extension into `~\.nexus\claude-desktop` and shows it in File Explorer, to drop on Claude Desktop,
  and "Open Claude". An extension installed in Claude Desktop counts as declared. From the Store package, the pack
  skips Claude Desktop (an edit of its existing configuration would stay in the package) and only the extension
  counts.
- NexusBrowser pairing without the native host (NexusBrowser 2.25.0): the bridge's state read natively, "Pair Chrome"
  opens the extension's pairing page in the running Chrome, "Show a code" copies a single-use code for the popup; an
  older bridge gets the guided fallback (the official PowerShell line, run by the user). 21 tests.
- Hub, milestone 2: NexusBrowser, NexusKeypad, NexusRelay and NexusAGYmail installed from the app (the pack's own
  script, `-Only`). Each product says for which AI apps it installs here, or why it does not fit (needs Claude Code,
  Antigravity only, needs Logi Options+), as get-nexus.ps1 does. Product pages show the status of the AI apps it
  serves (NexusRelay's shared daemon, the keypad's plugin) and its steps left to the user, with a button where the app
  can do the next click: Chrome opened on its extensions by NexusBrowser's own guide, the relay's Codex folder copied.
  Results table with `-` for the AI apps a product does not serve and a Logi Options+ column for the keypad.
- `Nexus.Core`: AI apps found the way get-nexus.ps1 finds them (Claude Desktop from the Store in
  `%LOCALAPPDATA%\Claude-Data`, folders it left ignored), declarations read in each app's own configuration, the
  common journal (`%LOCALAPPDATA%\NexusJournal`), the common emergency stop (`~\.nexus\stop`), running servers and the
  AI app that started them, and the one-line install's own script embedded and run with `-Json` (`pack/`, checked
  byte for byte against the catalog). 12 tests.
- `Nexus` hub (WinUI 3, Windows App SDK 2.5.1, Mica, Fluent, light and dark, French and English from Windows'
  language): home with the AI apps found, product cards in the colors and logos of julienpiron.fr, one Install button
  in a bar that stays in sight, live steps, ✓ / ✗ per product and AI app, phone guide, details; product pages for
  NexusWindows and NexusAndroid (status per AI app, server running, 24 h of activity, safeguards, emergency stop); the
  other products shown with how they come (later in the app, Logitech Marketplace, coming soon). Unpackaged for now.
- The hub's logo is Julien Piron's π medal from julienpiron.fr (`Assets/julienpiron-logo.png`), reduced, never
  redrawn, into `Assets/nexus.ico` by `tools/icon_from_site.py`; every product shows the icon and color it has on the
  site (NexusRelay and NexusAGYmail included).
- `tools/capture.ps1`: pictures of every page, FR/EN, light/dark, on a made-up profile (`tools/demo_profile.py`),
  the window shown away from the desktop without activation, foreground checked unchanged.
- `--quiet`: the window never takes the foreground, for tests driven in the background; `tools/run-on-profile.ps1`
  starts it on a throwaway profile and refuses the real user profile without `-AllowRealProfile`.
- `probe/`: MSIX container probe and its CI job (writes from the packaged process, a normal child, a child started
  with the desktop app breakaway policy; read back from outside the package).

## [1.6.2] - 2026-10-25

### Changed
- The scripts the pack starts (product installers, uv's official installer, NexusBrowser's extension guide) run with
  `-ExecutionPolicy RemoteSigned -File` instead of `Bypass`: local files, so the same effect, with the narrowest
  setting. No PowerShell is ever started with `-Command`.

## [1.6.1] - 2026-10-25

### Added
- `get-nexus.ps1` attached to each release with its `SHA256SUMS.txt` and a provenance attestation (workflow
  `release-assets`): the way without `irm | iex`, download it, then right click > Run with PowerShell.

### Changed
- Started from Explorer (Run with PowerShell), the pack waits for Enter before closing its window, so that the summary
  and the steps left to the user stay on screen.

## [1.6.0] - 2026-10-25

### Added
- Pack: NexusRelay (`get.ps1`, Claude Code, Codex and Antigravity; needs Claude Code), NexusAGYmail (`setup`, Antigravity
  only) and NexusKeypad (`get.ps1`, when Logi Options+ is there), each with its steps left to a human (the relay's
  project in Codex, the user's own Google sign-in, the keys). A product that does not fit the PC is listed with why;
  cells of AI apps a product does not serve show `-`. `-Only`/`-Skip` names `relay`, `agymail`, `keypad`.
- CI: NexusAGYmail's server started as Antigravity starts it.
- CI `defender` (measurement): Microsoft Defender's real-time protection on, the one-line install started the way a
  beginner pastes it, from a file, and with the URL on the command line; detections read back.

### Fixed
- Pack: uv's official installer is saved to a file and run from it, never through a PowerShell started with
  `-Command "irm ... | iex"`, the command line Microsoft Defender's heuristics report as `Trojan:Win32/Commando.A!ml`.

## [1.5.0] - 2026-10-25

### Added
- Nexus pack, `get-nexus.ps1`: the whole Nexus ecosystem in one line
  (`irm https://raw.githubusercontent.com/pironjulien/nexus-plugins/main/get-nexus.ps1 | iex`), Windows PowerShell
  5.1 or 7, no administrator rights, idempotent. Finds Claude Code, Claude Desktop, Codex and Antigravity, shows the
  plan, installs uv (and Node.js LTS through winget when Codex is there) once, gives Claude Code this catalog's
  plugins, then runs each product's official installer in its non-interactive mode (`NEXUS_PACK=1`); ends with a
  ✓ / ✗ summary per product and AI app, checked in each app's own configuration, and the steps left to a human.
  `-Only`, `-Skip`, `-DryRun`, `-NoGuide`, `-Catalog`, `-Json` (one `NEXUS_PACK_EVENT` line per event, for the Nexus
  apps that run the same script). Places kept for NexusRelay and NexusGmail; NexusKeypad shown as
  coming through the Logitech Marketplace.
- Codex marketplace in the same repository (`.agents/plugins/marketplace.json`): `nexus-windows` from
  NexusWindows-Codex, `nexus-android` and `nexus-browser` from `codex/`, pinned like their Claude twins, same skills.
- Tests: PowerShell lint (PSScriptAnalyzer), `tests/test_pack.ps1` (detection, plan, options, `irm | iex` keeps the
  window open, under Windows PowerShell 5.1), Codex manifests and pins in `check_catalog.py`, Codex servers started
  by `smoke_mcp.py`, `tests/check_install.py` (starts every server each AI app declares after a real run).
- Claude Desktop from the Microsoft Store found where it reads its configuration (`%LOCALAPPDATA%\Claude-Data\<package>`,
  packages named `*Claude_*`), and a folder it left (`.user-data-left.*`) ignored.
- CI job `pack`: the one-liner on a fresh `windows-latest` runner with the official Claude Code installer, every
  declared server started, then a second run.

### Changed
- README: "Install everything" first, Codex section, the uv command in the Claude Code quick start.
- Product installers called by the pack: NexusWindows-Claude and NexusWindows-Codex `setup.ps1` (main), NexusAndroid
  0.12.1 or newer (`setup` declares the server to every AI app, Antigravity as a plugin, Claude Desktop in Claude-Data), NexusBrowser 2.24.5 or newer
  (`get.ps1`: Antigravity as a plugin, Claude Desktop in Claude-Data, latest release read without the GitHub API, so
  no rate limit).
- `nexus-windows` pinned to `nexus-windows-claude` 3.2.2: one UI Automation step per wheel notch in WPF (three used to
  reach the app as three scrolls), no false success when a Chromium area is already at its end. `uv.lock` updated.

## [1.4.4] - 2026-10-25

### Changed
- `nexus-windows` pinned to `nexus-windows-claude` 3.2.1: dialogs (file pickers "Open", "Save as") listed by
  `windows` and attached to their owner; a click on a Chromium file picker trigger succeeds only when the dialog
  opens (awaited up to 4 s), otherwise `foreground=true` is suggested; no false confirmation for a line break next to a help button.
  Skill updated. `uv.lock` updated.

## [1.4.3] - 2026-10-25

### Changed
- `nexus-browser` pinned to 2.24.1: one-line install of the Chrome extension and local bridge
  (`irm https://raw.githubusercontent.com/pironjulien/NexusBrowser/main/get.ps1 | iex`), given in the plugin's README,
  the catalog README, the skill and every tool's error before the install; with this plugin enabled, the installer
  adds no second server for Claude Code. Python 3.11+ and Pillow 13 (security advisories). `uv.lock` updated.

## [1.4.2] - 2026-10-25

### Changed
- `nexus-windows` pinned to `nexus-windows-claude` 3.1.5: maintenance release (clearer auto-reload error message,
  project-scoped hygiene test, bench reports redacted). `uv.lock` updated.

## [1.4.1] - 2026-10-25

### Changed
- `nexus-browser` pinned to 2.23.0: the user's and other agents' tabs show their origin only in `browser_list_tabs`
  and `browser_active_tab` (full title and URL for the agent's own tabs); submitting a form or a WebMCP submission
  that carries entered data needs the user's accord; extension reload reserved to the installer, downloads erased
  only by the client whose tab started them; fields with an unknown input type can be filled; optional Gmail unread
  counter (off by default). Skill updated. `uv.lock` updated.

## [1.4.0] - 2026-10-25

### Changed
- `nexus-android` pinned to 0.9.11: text first (the element list alone; the image is joined when a gesture is not
  read back, when nothing is readable, for a zoom or on request), images within the common Nexus limits (WebP, at
  most 1568 px), no duplicate image, Home and Back read back from Android's own state, faster screen reads; skill
  updated.
- `nexus-windows` pinned to `nexus-windows-claude` 3.1.4: text first by default (`auto`: the image only when the tree
  is too thin to act on or a step ends `~` or `✗`), WebP images without duplicates, Tk and scrolling in the
  background, Alt+F4 on every window, Win32 menu entries with an ellipsis, File Explorer clicks and right-clicks
  like a real mouse; skill updated.
- `nexus-browser` pinned to 2.22.0: inline captures as real MCP images (`inline=True`) with zoom on a region and no
  duplicate image, `browser_batch` attaches its captures, WebMCP input checked against the tool's schema, stricter
  tab ownership; three duplicated tools removed (`browser_click_mark`, `browser_semantic_click`,
  `browser_semantic_fill`: use `browser_click` and `browser_fill`); skill updated (emergency stop by the shortcut).
- `uv.lock` files updated (`nexus-browser` now also locks Pillow).

## [1.3.3] - 2026-10-23

### Changed
- `nexus-windows` plugin: starts `nexus-windows-claude==3.0.1`, the new PyPI name of NexusWindows for Claude (common
  tool names `windows`, `look`, `act`, `launch`, `window` across the NexusWindows family, step outcomes ✓ = ~ ✗).
  `uv.lock` updated.

## [1.3.2] - 2026-10-20

### Changed
- `nexus-windows` pinned to 0.15.8: the first background typing in a Chromium or Electron app launched a moment ago
  is no longer lost (the app's one-time launch foreground right is handled before any key); live suite 14/14.
  `uv.lock` updated.

### Added
- README: how Claude installs plugins enabled through `settings.json` (installed when a conversation starts, only
  from a marketplace already downloaded; tools available in the following conversation; project-only enabling does
  not install), checked with a real install of 1.3.1 in fresh conversations.

## [1.3.1] - 2026-10-20

### Changed
- `nexus-windows` pinned to 0.15.7: password fields also refuse Shift+letter and editing shortcuts; `launch` follows
  the window the user picks while it waits; sharper focus checks for typing in rich editors and with the real
  keyboard; sign-in codes masked in notifications even without the word "code". `uv.lock` updated.

## [1.3.0] - 2026-10-20

### Changed
- `nexus-android` pinned to 0.9.3: new `map` (where a setting is, open it; `snapshot`/`diff` of the phone state),
  `apps`, `web` (read the page shown in Chrome or a WebView) and `functions` (actions apps expose to assistants)
  tools; a screen video is erased if a banking app comes to the front; skill updated.
- `nexus-windows` pinned to 0.15.2: new `notifications` tool (Windows notification center, codes masked); `launch`
  opens Settings pages and asks `confirm=true` for addresses that call or message someone; outward actions (send,
  call, pay, publish, transfer) need a single-use accord token after the user's yes; skill updated.
- `nexus-browser` pinned to 2.17.0: new `browser_batch`, `browser_wait_for`, `browser_webmcp_tools` and
  `browser_webmcp_call` tools; outward gestures need a one-time accord token after the user's yes; tabs opened by
  the agent's tab and orphaned tabs after a restart; skill updated.
- CI: `actions/checkout` 7.0.1 and `astral-sh/setup-uv` 10.3.0 (pinned by commit SHA). `uv.lock` files updated.

## [1.2.5] - 2026-10-20

### Changed
- GitHub Actions pinned by commit SHA, checkout without persisted credentials (zizmor clean).

### Added
- OpenSSF Scorecard workflow and badge; Dependabot for GitHub Actions and the plugins' `uv.lock` (7-day cooldown).

## [1.2.4] - 2026-10-19

### Changed
- `nexus-android` pinned to 0.7.2: new `capture` tool (screenshots with security codes blacked out, screen videos up
  to 180 s, saved on the PC, e.g. for a support request; never while a banking app is open) and `logs` tool
  (grouped crashes, ANR, recent log of one app, with codes, tokens, e-mail addresses and phone numbers masked);
  another automation tool holding the phone's accessibility connection is named; skill updated; `uv.lock` updated.

## [1.2.3] - 2026-10-19

### Changed
- `nexus-android` pinned to 0.7.1: `act(then="changes")` returns only what changed on screen after the gestures
  (numbers kept), phone numbers are no longer masked as security codes, larger screenshot marks; its skill now
  documents marks, `then="changes"`, precise screens with `launch` and the `clipboard` tool; `uv.lock` updated.

## [1.2.2] - 2026-10-19

### Changed
- `nexus-android` pinned to 0.7.0: screen reads in about 0.2 s (was about 3 s), numbered marks on screenshots, a
  `clipboard` tool, `launch` opening a precise screen (calls, sends, payments and power actions need `confirm=true`,
  banking and identity apps refused), stronger guardrails (payment pages, subscriptions, bookings, price buttons),
  security codes masked in more languages, English output; `uv.lock` updated.

## [1.2.1] - 2026-10-19

### Changed
- `nexus-windows` pinned to 0.10.1 and `nexus-browser` to 2.11.0: everything their servers send to the client (tool
  and parameter descriptions, step reports, refusals, errors) is now in English, with the same guardrails; `uv.lock`
  updated.

## [1.2.0] - 2026-10-19

### Added
- "Try it" example prompts in every plugin README (each one run for real before release) and a "Testing" section for
  reviewers: Android emulator through `NEXUS_ANDROID_SERIAL`, any Windows PC, public test pages for the browser.
- `nexus-android` passes `NEXUS_ANDROID_ADB` and `NEXUS_ANDROID_SERIAL` from the environment to its server.

## [1.1.3] - 2026-10-19

### Changed
- `nexus-browser` pinned to 2.10.1: its server instructions no longer compare it with other tools (a server must not
  steer the model toward its tools or away from other ones); `uv.lock` updated.

## [1.1.2] - 2026-10-19

### Added
- `documentationUrl`, `privacyPolicyUrl` and `supportUrl` in every manifest: the Claude directory shows them on the
  listing (Claude Code ignores them).

## [1.1.1] - 2026-10-19

### Fixed
- Launchers back to `uvx --python 3.14 <package>==<version>`, the only form the Claude directory's validator reads
  (`uv run --project` and a `UV_PROJECT_ENVIRONMENT` setting are refused); each plugin keeps its `pyproject.toml`
  and `uv.lock`, which record the server and all its dependencies by version and hash.

## [1.1.0] - 2026-10-19

### Changed
- Every plugin now starts its server with `uv run --frozen` on a `uv.lock` that pins the server and all its
  dependencies by version and hash (instead of `uvx`), as the Claude directory asks for its Verified badge; the
  environment lives in the plugin's data folder, never in the plugin folder.
- `test.ps1 -Full` also checks that every `uv.lock` matches its `pyproject.toml`.

### Added
- A square icon for each plugin (`.claude-plugin/icon.png`).

### Removed
- `privacyPolicyUrl` and `supportUrl` from the manifests (not plugin fields; the links stay in each README).

## [1.0.0] - 2026-10-19

### Added
- Marketplace `nexus-plugins` with three plugins, each starting its MCP server from PyPI with `uvx`, pinned to an
  exact version, plus an English skill:
  - `nexus-android` 0.6.2: Android phone over plain ADB.
  - `nexus-windows` 0.9.3: Windows desktop apps in the background.
  - `nexus-browser` 2.10.0: the user's real Chrome in background tabs (needs the NexusBrowser extension and bridge).
- `test.ps1`: offline catalog checks; `-Full` starts every server from PyPI, lists its tools and fails if the
  server instructions or tool list name anyone (neutral texts since nexus-android 0.6.2, nexus-windows 0.9.3 and
  nexus-browser 2.10.0). CI on Windows.
