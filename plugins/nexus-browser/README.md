# Nexus Browser plugin

Gives Claude Code your real Chrome, with your sessions, driven in background tabs: navigation, forms, trusted
clicks, network, dialogs, cross-origin iframes, screenshots, exports and downloads. Each agent session only touches
its own tabs; secrets are masked by the extension and the bridge.

## Requires a one-time install
This plugin only adds the MCP server and its skill. The **NexusBrowser Chrome extension and local bridge** must be
installed once (Windows 10/11, Chrome 116+), in PowerShell:

```powershell
irm https://raw.githubusercontent.com/pironjulien/NexusBrowser/main/get.ps1 | iex
```

then load the extension in `chrome://extensions` as the installer shows (details:
https://github.com/pironjulien/NexusBrowser#install). With this plugin enabled, the installer adds no second
server for Claude Code (run before the plugin, running it again removes its own entry). Without the bridge and the
extension, the server starts but every call answers with an error giving that command.

## What it runs
The plugin starts the open-source MCP server [`nexus-browser`](https://pypi.org/project/nexus-browser/) 2.24.1 from
PyPI with `uvx --python 3.14` (uv downloads Python 3.14 if needed; versions and hashes of the server and its
dependencies recorded in `uv.lock`), over stdio, with `NEXUS_BROWSER_AGENT=Claude`
(the name of the agent's tab group). It talks only to the local bridge on this PC; the plugin sends nothing anywhere.

## Try it
- "Open https://httpbin.org/forms/post in a background tab and fill the form with test values, without submitting it."
- "Open news.ycombinator.com and summarise the top five stories."
- "Open https://example.com in a background tab and give me the page as Markdown."

## Testing
After the one-time install (Windows, Chrome 116+), any Chrome profile works: no account is needed for the examples
above. `https://httpbin.org/forms/post` is a public test form; the agent's tab opens in the background, in its own
tab group, and every field is read back.

## Requirements
- [uv](https://docs.astral.sh/uv/), Chrome, and the NexusBrowser install above.

Source, safeguards and privacy: https://github.com/pironjulien/NexusBrowser (MIT).
