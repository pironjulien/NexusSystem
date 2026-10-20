# Changelog

All notable changes to this marketplace are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
