"""A made-up user profile for the apps' pictures (milestone reports, Store listing): the four AI apps, NexusWindows and
NexusAndroid declared, a day of journal lines on everyday apps. Nothing comes from a real PC.

    python -B tools/demo_profile.py <folder>      then start NexusSystem with USERPROFILE, APPDATA, LOCALAPPDATA there."""

import json
import random
import sys
from datetime import UTC, datetime, timedelta
from pathlib import Path

WINDOWS = [("notepad.exe", "type"), ("notepad.exe", "key"), ("mspaint.exe", "click"), ("explorer.exe", "look"),
           ("calculatorapp.exe", "click"), ("excel.exe", "set"), ("explorer.exe", "launch")]
BROWSER = [("example.com", "navigate"), ("wikipedia.org", "navigate"), ("wikipedia.org", "click"), ("example.com", "fill")]
RELAY = [("codex", "send"), ("antigravity", "send"), ("codex", "wait_reply"), ("codex", "wake")]
ANDROID = [("com.android.settings", "tap"), ("com.google.android.calculator", "tap"), ("com.android.chrome", "type"),
           ("com.google.android.apps.maps", "look"), ("com.android.settings", "scroll")]


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def journal(root: Path, product: str, actions: list[tuple[str, str]], count: int) -> None:
    now = datetime.now(UTC)
    rng = random.Random(product)  # the same pictures every time
    for agent in ("claude", "codex", "antigravity"):
        lines = []
        for _ in range(count):
            app, action = rng.choice(actions)
            issue = rng.choices(["✓", "=", "~", "✗"], weights=[80, 8, 8, 4])[0]
            at = now - timedelta(minutes=rng.randint(1, 600))
            lines.append(json.dumps({"v": 1, "ts": at.isoformat().replace("+00:00", "Z"), "agent": agent, "outil": "demo",
                                     "appel": None, "app": app, "cadre": None, "action": action, "technique": None,
                                     "issue": issue, "ms": rng.randint(40, 900), "premier_plan": False,
                                     "erreur": "element not found" if issue == "✗" else None}, ensure_ascii=False))
        write(root / "AppData" / "Local" / "NexusJournal" / product / agent / f"{now:%Y-%m}.jsonl", "\n".join(lines) + "\n")


def main(root: Path) -> None:
    servers = {"nexus-windows": {"command": "uvx", "args": ["--python", "3.14", "nexus-windows-claude"]},
               "nexus-android": {"command": "uvx", "args": ["--python", "3.14", "nexus-android"]}}
    write(root / ".claude" / "settings.json", json.dumps({"enabledPlugins": {
        "nexus-windows@nexus-plugins": True, "nexus-android@nexus-plugins": True}}))
    write(root / "AppData" / "Roaming" / "Claude" / "claude_desktop_config.json", json.dumps({"mcpServers": servers}))
    write(root / ".codex" / "config.toml", "[mcp_servers.nexus-windows]\ncommand = \"npx\"\n\n[mcp_servers.nexus-android]\ncommand = \"uvx\"\n")
    for plugin in ("nexus-windows", "nexus-android", "nexus-browser", "nexus-relay", "nexus-agymail"):
        write(root / ".gemini" / "config" / "plugins" / plugin / "plugin.json", "{}")
    (root / ".gemini" / "antigravity").mkdir(parents=True, exist_ok=True)
    # NexusBrowser (catalog plugin, Desktop, Codex), NexusRelay (declared by its installer), the keypad's plugin.
    settings = json.loads((root / ".claude" / "settings.json").read_text(encoding="utf-8"))
    settings["enabledPlugins"]["nexus-browser@nexus-plugins"] = True
    write(root / ".claude" / "settings.json", json.dumps(settings))
    write(root / ".claude.json", json.dumps({"mcpServers": {"nexus-relay": {"type": "http", "url": "http://127.0.0.1:16181/mcp"}}}))
    # Claude Desktop as the Store app leaves it: NexusWindows and NexusBrowser added as .mcpb extensions, NexusAndroid
    # not yet (its page shows the extension step).
    write(root / "AppData" / "Roaming" / "Claude" / "claude_desktop_config.json", json.dumps({"mcpServers": {}}))
    for name in ("nexus-windows-claude", "nexus-browser"):
        write(root / "AppData" / "Roaming" / "Claude" / "Claude Extensions" / f"local.mcpb.julien-piron.{name}" / "manifest.json",
              json.dumps({"name": name}))
    with (root / ".codex" / "config.toml").open("a", encoding="utf-8") as toml:
        toml.write("\n[mcp_servers.nexus-browser]\ncommand = \"python\"\n\n[mcp_servers.nexus-relay]\nurl = \"http://127.0.0.1:16181/mcp\"\n")
    (root / "AppData" / "Local" / "Logi" / "LogiPluginService" / "Plugins" / "NexusKeypad").mkdir(parents=True, exist_ok=True)
    # The state each product writes about its own version (~\.nexus\status), as the released versions do.
    states = {"windows-claude": ("3.3.0", "3.3.0", "nexus-windows-claude 3.3.0 is up to date"),
              "windows-codex": ("3.1.6", "3.1.6", "nexus-windows-codex 3.1.6 is up to date"),
              "windows-antigravity": ("3.2.1", "3.2.1", "nexus-windows-antigravity 3.2.1 is up to date"),
              "android": ("0.13.0", "0.13.1", "Version 0.13.1 is ready: it is used at the next start of your AI app."),
              "browser": ("2.25.0", "2.25.0", None),
              "relay": ("1.3.1", "1.3.1", "NexusRelay 1.3.1 is up to date"),
              "agymail": ("0.8.1", "0.8.1", None)}
    for name, (version, latest, message) in states.items():
        write(root / ".nexus" / "status" / f"{name}.json", json.dumps({"product": name, "version": version, "latest": latest,
              "state": "idle", "checked_at": "2026-10-26T08:00:00Z", "message": message}))
    journal(root, "windows", WINDOWS, 40)
    journal(root, "android", ANDROID, 12)
    journal(root, "browser", BROWSER, 20)
    journal(root, "relay", RELAY, 8)
    print(f"demo profile in {root}")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
