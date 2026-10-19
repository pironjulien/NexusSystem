# Nexus Android plugin

Gives Claude Code your Android phone over plain ADB, with no root and no app on the phone: screen and numbered
elements, gestures, typing, SMS/RCS reading, reply drafts, notifications, files, location, calls, contacts, agenda
and quick settings. Sending, paying and other irreversible gestures need your explicit "yes".

## What it runs
The plugin starts the open-source MCP server [`nexus-android`](https://pypi.org/project/nexus-android/) 0.7.2 from
PyPI with `uvx --python 3.14`, over stdio (uv downloads Python 3.14 if needed). The exact version of the server and
of every dependency is recorded with its hash in `uv.lock`. The plugin itself contains only this launcher, the lock
file and a skill; it sends nothing anywhere.

## Requirements
- [uv](https://docs.astral.sh/uv/) and Android Platform Tools (`adb`), e.g. `winget install Google.PlatformTools`.
- USB debugging enabled on the phone and the PC's key accepted.

## Try it
- "Give me a health check of my phone: battery, storage and security patch."
- "Look at my phone and tell me what is on the screen."
- "Is do not disturb on? Turn it on."
- "Read my latest SMS conversation and draft a reply saying I will be ten minutes late. Do not send it."

## Testing without your own phone
The server only drives the phone it was paired with and ignores emulators unless one is named: start an Android
emulator (Android Studio, any recent image), then set `NEXUS_ANDROID_SERIAL=emulator-5554` before starting Claude
Code. `adb` must be on the PATH, or set `NEXUS_ANDROID_ADB` to its full path. Both variables are passed to the
server by the plugin.

Source, safeguards and privacy: https://github.com/pironjulien/NexusAndroid (MIT).
