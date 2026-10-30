# Nexus Browser plugin

Gives Claude Code your real Chrome, with your sessions, driven in background tabs: navigation, forms, trusted
clicks, network, dialogs, cross-origin iframes, screenshots, exports and downloads. Each agent session only touches
its own tabs; secrets are masked by the extension and the bridge.

## What it runs
Nothing by itself: the plugin declares the NexusBrowser daemon running on this PC
(`http://127.0.0.1:16182/mcp?agent=claude`, its access key read from your user variable `NEXUS_MCP_TOKEN`, never
written in the plugin) and adds its skill. The daemon talks only to the NexusBrowser Chrome extension on this PC.
Install it once with NexusSystem (the Microsoft Store app, or in PowerShell:
`irm https://raw.githubusercontent.com/pironjulien/NexusSystem/main/get-nexus.ps1 | iex`), which runs the product's own
native installer (`get.ps1` of its repository): no Python, no uv.
Then load the extension in `chrome://extensions` as the installer shows (details:
https://github.com/pironjulien/NexusBrowser#install).

## Requirements
- Windows 10 or 11, Chrome 116+, the NexusBrowser daemon and its Chrome extension (above).

## Try it
- "Open https://httpbin.org/forms/post in a background tab and fill the form with test values, without submitting it."
- "Open news.ycombinator.com and summarise the top five stories."
- "Open https://example.com in a background tab and give me the page as Markdown."

## Testing
After the one-time install, any Chrome profile works: no account is needed for the examples above.
`https://httpbin.org/forms/post` is a public test form; the agent's tab opens in the background, in its own tab
group, and every field is read back.

Source, safeguards and privacy: https://github.com/pironjulien/NexusBrowser (MIT).