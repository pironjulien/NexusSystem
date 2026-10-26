# Changelog

All notable changes to this marketplace are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- Tests: right after a PyPI release, the `uv.lock` check and the MCP servers' smoke test fetch the index again
  (`--refresh`) and retry for a few minutes, instead of failing on an index that does not list the new version yet.

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
