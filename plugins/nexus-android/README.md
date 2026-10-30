# Nexus Android plugin

Gives Claude Code your Android phone over plain ADB, with no root and no app on the phone: screen and numbered
elements, gestures, typing, SMS/RCS reading, reply drafts, notifications, files, location, calls, contacts, agenda
and quick settings. Sending, paying and other irreversible gestures need your explicit "yes".

## What it runs
The plugin starts the open-source MCP server [`nexus-android`](https://pypi.org/project/nexus-android/) 0.21.0 from
PyPI with `uvx --python 3.14`, over stdio (uv downloads Python 3.14 if needed). The exact version of the server and
of every dependency is recorded with its hash in `uv.lock`. The plugin itself contains only this launcher, the lock
file and a skill.

The server talks to your phone over ADB on your PC. It contacts only three sites, and never sends anything from your
phone: pypi.org (its own latest version, once a day), dl.google.com (Android's platform-tools, once, if adb is
missing) and github.com (the scrcpy server, once, the first time it types accented text).

## Requirements
- [uv](https://docs.astral.sh/uv/) (`winget install --id astral-sh.uv -e`). `adb` is found on the PC or downloaded once
  from Google (Android Platform Tools).
- USB debugging enabled on the phone and the PC's key accepted: the tools list the steps, or run
  `uvx --python 3.14 nexus-android setup` once in a terminal to be guided.

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
