# Nexus Android plugin

Gives Claude Code your Android phone over plain ADB, with no root and no app on the phone: screen and numbered
elements, gestures, typing, SMS/RCS reading, reply drafts, notifications, files, location, calls, contacts, agenda
and quick settings. Sending, paying and other irreversible gestures need your explicit "yes".

## What it runs
The plugin starts the open-source MCP server [`nexus-android`](https://pypi.org/project/nexus-android/) 0.6.2 from
PyPI with `uv run --frozen`, over stdio. Its exact version and every dependency are locked with hashes in `uv.lock`;
uv downloads Python 3.14 if needed and keeps the environment in the plugin's data folder. The plugin itself
contains only this launcher, the lock file and a skill; it sends nothing anywhere.

## Requirements
- [uv](https://docs.astral.sh/uv/) and Android Platform Tools (`adb`), e.g. `winget install Google.PlatformTools`.
- USB debugging enabled on the phone and the PC's key accepted.

Source, safeguards and privacy: https://github.com/pironjulien/NexusAndroid (MIT).
