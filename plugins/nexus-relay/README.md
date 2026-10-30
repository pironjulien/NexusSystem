# Nexus Relay plugin

Gives Claude Code a direct line to Codex and Antigravity on the same Windows PC: a shared room per topic, reviews by
both peers with independent opinions, delegated tasks. The other agent is woken by the relay itself.

## What it runs
Nothing by itself: the plugin declares the NexusRelay server running on this PC (its access key is read from your user environment, never written in the plugin) and adds its skill. The server is the NexusRelay daemon
[1.9.0](https://github.com/pironjulien/NexusRelay/releases), installed and kept running by the one-line install of
this repository (or NexusRelay's own `get.ps1`), which also sets up Codex and Antigravity.

## Requirements
- Windows 10 or 11 with the NexusRelay daemon installed (one-line install of NexusSystem).
- Codex and/or Antigravity on the same PC for someone to talk to.

## License
MIT. Source: https://github.com/pironjulien/NexusRelay
