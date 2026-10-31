# NexusSystem — AI Installation Guide (`INSTALL-AI.md`)

This file is written for AI coding agents (**Claude Code**, **Codex**, **Antigravity**) when the user asks you to install, update or repair **Nexus** on their Windows 10/11 PC.

## 1. Every Nexus product is optional (à la carte)

Never assume the user wants every product if they only need one or two. The available products are:

| Key | Product | What it gives the AI | Extra requirement |
| --- | --- | --- | --- |
| `windows` | **NexusWindows** | Windows desktop apps in the background (look, click, type, menus, shortcuts, no focus theft) | Windows 10 or 11 |
| `browser` | **NexusBrowser** | The user's real Chrome (with their sessions) in background tabs | Google Chrome |
| `android` | **NexusAndroid** | The user's Android phone over ADB (screen, gestures, SMS, notifications, files, settings) | Android phone with USB debugging |
| `relay` | **NexusRelay** | Shared local room where Claude Code, Codex and Antigravity collaborate on the same PC | At least two of Claude Code, Codex, Antigravity |
| `keypad` | **NexusKeypad** | Live pixel-art agent states, quotas, dictation and quick actions on the Logitech MX Creative Keypad | Logi Options+ |
| `agymail` | **NexusAGYmail** | Gmail with safeguards (for Antigravity; Claude and Codex use their official Gmail connector) | Antigravity |
| `indesign` | **NexusInDesign** | Adobe InDesign through its COM automation: scripts, previews, preflight, verified exports, backups (needs InDesign 2024 to 2026) | Claude Code, Codex |

- **If the user's prompt already specifies a `-Only` list** (for example when copied from the **NexusSystem** app): run the exact command they gave you.
- **If the user named specific products** (e.g. *"install NexusWindows and NexusBrowser"*): map them to their keys (`-Only windows,browser`).
- **If the user just said *"Install Nexus"* without naming products**: briefly ask which products from the table above they want (or run with `-DryRun` first to show what fits their PC).

---

## 2. Run the official installer

**Never run downloaded code in memory** (a download piped into PowerShell, or a script block or encoded command built from it): Microsoft
Defender rightly blocks it as a malware loader. Always download the pack script as a **file** of the latest
NexusSystem release, check it, then run it with `-File`. It requires no administrator rights, detects the AI apps
installed on the PC, installs Git when a selected AI app needs it to add the catalog, and runs each selected product's official native installer the same way (the `get.ps1`
file of its release, checked against its `SHA256SUMS.txt`, run with `-File`).

If the user prefers an app: the NexusSystem app of the Microsoft Store does all this
(`winget install --id 9NSH2P3QLWT1 --source msstore`).

### Download and check (once)

```powershell
$dir = Join-Path $env:TEMP 'nexus-pack'; New-Item -ItemType Directory -Force $dir | Out-Null; Set-Location $dir
$r = 'https://github.com/pironjulien/NexusSystem/releases/latest/download'
Invoke-WebRequest "$r/get-nexus.ps1" -OutFile get-nexus.ps1
Invoke-WebRequest "$r/SHA256SUMS.txt" -OutFile SHA256SUMS.txt
$expected = (Select-String -Path SHA256SUMS.txt -Pattern '^([0-9a-f]{64})\s+get-nexus\.ps1$').Matches[0].Groups[1].Value
if ((Get-FileHash get-nexus.ps1 -Algorithm SHA256).Hash -ne $expected) { throw 'get-nexus.ps1 does not match SHA256SUMS.txt' }
```

### Selected products (recommended)

Replace `windows,browser` with the comma-separated keys chosen by the user:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\get-nexus.ps1 -Only windows,browser -NoGuide
```

### All products that fit this PC

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\get-nexus.ps1 -NoGuide
```

*(Why `-NoGuide` when run by an AI agent: without `-NoGuide`, the NexusBrowser installer waits up to 5 minutes in the terminal for the user to load the Chrome extension manually. With `-NoGuide`, the command finishes immediately so you can read the summary and guide the user yourself.)*
---

## 3. Check the output and guide the user through the final manual step

Read the `✓` / `✗` summary printed by `get-nexus.ps1`, then give the user **only** the short instructions that match the products you just installed:

1. **If `browser` (`NexusBrowser`) was installed:**
   - Open Chrome on its extensions page and copy the extension folder path to the clipboard by running:
     ```powershell
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\NexusBrowser\app\scripts\guide-extension.ps1" -WaitSeconds 0
     ```
   - Tell the user: *"Chrome just opened on `chrome://extensions` and the extension folder path is in your clipboard. Turn on **Developer mode** (top right), click **Load unpacked**, press `Ctrl+V`, and click **Select Folder**."*
2. **If `android` (`NexusAndroid`) was installed:**
   - Tell the user: *"On your Android phone, enable **Developer options > USB debugging**, plug the phone into the PC with a USB data cable, and tap **Allow** on the phone screen."*
3. **If `relay` (`NexusRelay`) was installed and both Claude Code and Codex are present:**
   - Tell the user to add the `Nexus Relay` project folder printed in the installer summary once inside the Codex app.
4. **If `keypad` (`NexusKeypad`) was installed:**
   - Tell the user: *"In **Logi Options+ > MX Creative Keypad**, drag the keys from the **Trinity** group onto the 9 keypad buttons."*
5. **Restart the AI app:**
   - Remind the user to **quit and restart** their AI app completely so the newly registered MCP servers and skills are loaded in the next conversation.
