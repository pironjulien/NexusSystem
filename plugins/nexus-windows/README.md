# Nexus Windows plugin

Gives Claude Code your Windows desktop applications in the background: it sees a window, clicks, types, picks,
uses menus and shortcuts and launches apps without taking your mouse, keyboard or foreground, and reads back every
action. Irreversible gestures need your explicit "yes"; Ctrl+Alt+Shift+Esc stops everything for two minutes.

## What it runs
The plugin starts the native NexusWindows engine over stdio, from the stable path its installer keeps up to date:
`%USERPROFILE%\.nexus\nexus-windows\NexusWindows.exe --mcp --agent claude`. The plugin itself contains only this
launcher and a skill; it sends nothing anywhere (engine privacy:
[NexusWindows](https://github.com/pironjulien/NexusWindows/tree/main/claude#privacy)).
Install it once with NexusSystem (the Microsoft Store app, or in PowerShell:
`irm https://raw.githubusercontent.com/pironjulien/NexusSystem/main/get-nexus.ps1 | iex`), which runs the product's own
native installer (`get.ps1` of its repository): no Python, no uv.
## Requirements
- Windows 10 or 11 with NexusWindows installed (above).

## Try it
- "Open Notepad and write a shopping list: milk, eggs, bread."
- "Open Calculator, compute 1234 × 5678 and tell me the result."
- "Open my Downloads folder in File Explorer and tell me what is in it."

## Testing
Any Windows 10 or 11 PC (or VM) works: no account, no sample data. Notepad and Calculator are enough to see the
background flow (look, act, read back) while you keep using the mouse and keyboard.

Source, safeguards and privacy: https://github.com/pironjulien/NexusWindows (MIT).