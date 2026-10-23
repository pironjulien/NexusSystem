# Nexus Windows plugin

Gives Claude Code your Windows desktop applications in the background: it sees a window, clicks, types, picks,
uses menus and shortcuts and launches apps without taking your mouse, keyboard or foreground, and reads back every
action. Irreversible gestures need your explicit "yes"; Ctrl+Alt+Shift+Esc stops everything for two minutes.

## What it runs
The plugin starts the open-source MCP server [`nexus-windows-claude`](https://pypi.org/project/nexus-windows-claude/) 3.0.1 from
PyPI with `uvx --python 3.14`, over stdio (uv downloads Python 3.14 if needed). The exact version of the server and
of every dependency is recorded with its hash in `uv.lock`. The plugin itself contains only this launcher, the lock
file and a skill; it sends nothing anywhere.

## Requirements
- Windows 10 or 11.
- [uv](https://docs.astral.sh/uv/).

## Try it
- "Open Notepad and write a shopping list: milk, eggs, bread."
- "Open Calculator, compute 1234 × 5678 and tell me the result."
- "Open my Downloads folder in File Explorer and tell me what is in it."

## Testing
Any Windows 10 or 11 PC (or VM) with uv works: no account, no sample data. Notepad and Calculator are enough to see
the background flow (look, act, read back) while you keep using the mouse and keyboard.

Source, safeguards and privacy: https://github.com/pironjulien/NexusWindows-Claude (MIT).
