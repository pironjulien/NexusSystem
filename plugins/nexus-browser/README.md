# Nexus Browser plugin

Gives Claude Code your real Chrome, with your sessions, driven in background tabs: navigation, forms, trusted
clicks, network, dialogs, cross-origin iframes, screenshots, exports and downloads. Each agent session only touches
its own tabs; secrets are masked by the extension and the bridge.

## Requires a one-time install
This plugin only adds the MCP server and its skill. The **NexusBrowser Chrome extension and local bridge** must be
installed once (Windows 10/11, Chrome 116+): follow https://github.com/pironjulien/NexusBrowser#install. Without
them, the server starts but every call answers with an error pointing to that install.

## What it runs
The plugin starts the open-source MCP server [`nexus-browser`](https://pypi.org/project/nexus-browser/) 2.10.0 from
PyPI with `uv run --frozen` (version and dependencies locked with hashes in `uv.lock`; uv downloads Python 3.14 if
needed and keeps the environment in the plugin's data folder), over stdio, with `NEXUS_BROWSER_AGENT=Claude`
(the name of the agent's tab group). It talks only to the local bridge on this PC; the plugin sends nothing anywhere.

## Requirements
- [uv](https://docs.astral.sh/uv/), Chrome, and the NexusBrowser install above.

Source, safeguards and privacy: https://github.com/pironjulien/NexusBrowser (MIT).
