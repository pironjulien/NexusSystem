# Nexus Relay plugin

Gives Claude Code a direct line to Codex and Antigravity on the same Windows PC: a shared room per topic, reviews by
both peers with independent opinions, delegated tasks. The other agent is woken by the relay itself.

## What it runs
Nothing by itself: the plugin declares the NexusRelay daemon running on this PC
(`http://127.0.0.1:16181/mcp?agent=claude`, its access key read from your user variable `NEXUS_MCP_TOKEN`, never
written in the plugin) and adds its skill. The daemon is NexusRelay's native Windows engine; its installer also sets
up Codex and Antigravity.
Install it once with NexusSystem (the Microsoft Store app, or the `get-nexus.ps1` file of its latest release, checked
and run with `-File`: https://github.com/pironjulien/NexusSystem#install), which runs the product's own native
installer (the `get.ps1` file of its release): no Python, no uv.
## Requirements
- Windows 10 or 11 with the NexusRelay daemon installed (above).
- Codex and/or Antigravity on the same PC for someone to talk to.

## License
MIT. Source: https://github.com/pironjulien/NexusRelay