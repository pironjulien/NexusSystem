# Nexus Android plugin

Gives Claude Code your Android phone over plain ADB, with no root and no app on the phone: screen and numbered
elements, gestures, typing, SMS/RCS reading, reply drafts, notifications, files, location, calls, contacts, agenda
and quick settings. Sending, paying and other irreversible gestures need your explicit "yes".

## What it runs
Nothing by itself: the plugin declares the NexusAndroid daemon running on this PC
(`http://127.0.0.1:16180/mcp`, its access key read from your user variable `NEXUS_MCP_TOKEN`, never written in the
plugin) and adds its skill. The daemon is NexusAndroid's native Windows engine, which talks to your phone over ADB.
It never sends anything from your phone; see its [privacy section](https://github.com/pironjulien/NexusAndroid#privacy).
Install it once with NexusSystem (the Microsoft Store app, or the `get-nexus.ps1` file of its latest release, checked
and run with `-File`: https://github.com/pironjulien/NexusSystem#install), which runs the product's own native
installer (the `get.ps1` file of its release): no Python, no uv.
## Requirements
- Windows 10 or 11 with the NexusAndroid daemon installed (above).
- USB debugging enabled on the phone and the PC's key accepted: the tools list the steps, and NexusAndroid's
  [quick start](https://github.com/pironjulien/NexusAndroid#quick-start) guides the first connection.

## Try it
- "Give me a health check of my phone: battery, storage and security patch."
- "Look at my phone and tell me what is on the screen."
- "Is do not disturb on? Turn it on."
- "Read my latest SMS conversation and draft a reply saying I will be ten minutes late. Do not send it."

## Testing without your own phone
The daemon only drives the phone it was paired with and ignores emulators unless one is named: start an Android
emulator (Android Studio, any recent image) and follow NexusAndroid's README to name it.

Source, safeguards and privacy: https://github.com/pironjulien/NexusAndroid (MIT).