# Nexus Windows plugin

Gives Claude Code your Windows desktop applications in the background: it sees a window, clicks, types, picks,
uses menus and shortcuts and launches apps without taking your mouse, keyboard or foreground, and reads back every
action. Irreversible gestures need your explicit "yes"; Ctrl+Alt+Shift+Esc stops everything for two minutes.

## What it runs
The plugin starts the open-source MCP server [`nexus-windows`](https://pypi.org/project/nexus-windows/) 0.9.3 from
PyPI with `uvx --python 3.14`, over stdio (uv downloads Python 3.14 if needed). The exact version of the server and
of every dependency is recorded with its hash in `uv.lock`. The plugin itself contains only this launcher, the lock
file and a skill; it sends nothing anywhere.

## Requirements
- Windows 10 or 11.
- [uv](https://docs.astral.sh/uv/).

Source, safeguards and privacy: https://github.com/pironjulien/NexusWindows-Claude (MIT).
