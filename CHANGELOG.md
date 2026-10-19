# Changelog

All notable changes to this marketplace are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

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
