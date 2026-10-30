# Nexus Keypad plugin

Connects Claude Code to NexusKeypad, the plugin for the Logitech MX Creative Keypad in Logi Options+: live states of
Claude for the pad's keys (hooks), an end-of-task note, Claude's exact usage (5-hour and weekly windows) for the pad,
and, under the prompt, whether Codex and Antigravity are reachable through Nexus Relay. No alert, no network.

## What it runs
- Hooks that call NexusKeypad's native helper, `%LOCALAPPDATA%\NexusKeypad\hook\nexuskeypad-hook.exe`, installed by
  NexusKeypad; without it they do nothing.
- Two modules of Claude's app: `quota.ts` writes `~/.nexus/trinite/quota-claude.json` from the app's own figures;
  `register.tsx` reads Nexus Relay's mailbox (`~/.nexus/relay/v1`) to show its peers under the prompt.
Install it once with NexusSystem (the Microsoft Store app, or in PowerShell:
`irm https://raw.githubusercontent.com/pironjulien/NexusSystem/main/get-nexus.ps1 | iex`), which runs the product's own
native installer (`get.ps1` of its repository): no Python, no uv.
## Requirements
- Windows 10 or 11, Logi Options+ with an MX Creative Keypad, and NexusKeypad installed (above).

Source and privacy: https://github.com/pironjulien/NexusKeypad (MIT).