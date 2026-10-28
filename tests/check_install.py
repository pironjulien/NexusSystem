"""After a real run of get-nexus.ps1: start every Nexus MCP server that each AI app now declares, exactly as that app
would (command, args, env from its own configuration), and list its tools.

    python -B tests/check_install.py --ais claude-code,claude-desktop,codex,antigravity --products windows,android,browser

Every (AI app, product) pair asked for must be declared and its server must answer initialize and tools/list."""

import argparse
import json
import os
import shutil
import sys
import tempfile
import tomllib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from smoke_mcp import smoke  # noqa: E402

HOME = Path(os.environ["USERPROFILE"])
APPDATA = Path(os.environ["APPDATA"])
LOCALAPPDATA = Path(os.environ["LOCALAPPDATA"])
SERVERS = {"windows": "nexus-windows", "android": "nexus-android", "browser": "nexus-browser", "agymail": "nexus-agymail"}
# Nom actuel d'abord, puis l'ancien d'une installation pas encore migrée (NexusWindows : nexus-computer).
AGY_PLUGINS = {"windows": ("nexus-windows", "nexus-computer"), "android": ("nexus-android",), "browser": ("nexus-browser",),
               "agymail": ("nexus-agymail",)}


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig")) if path.is_file() else {}


def resolved(server: dict) -> dict:
    """Windows: `npx` or `uvx` named without extension are found the way the app finds them (PATHEXT)."""
    command = shutil.which(server["command"]) or server["command"]
    return {**server, "command": command, "args": server.get("args", []), "env": server.get("env", {})}


def claude_code(product: str) -> list[tuple[dict, Path]]:
    cache = HOME / ".claude" / "plugins" / "cache" / "nexus-plugins" / SERVERS[product]
    found = []
    for mcp in sorted(cache.glob("*/.mcp.json")):
        server = read_json(mcp).get("mcpServers", {}).get(SERVERS[product])
        if server:
            found.append((server, mcp.parent))
    if found:
        return found[-1:]  # the newest installed version
    # Declared in settings.json only (no claude command): Claude installs it when a conversation starts. With a local
    # catalog (tests), the plugin it will install is that folder's.
    settings = read_json(HOME / ".claude" / "settings.json")
    source = settings.get("extraKnownMarketplaces", {}).get("nexus-plugins", {}).get("source", {})
    enabled = settings.get("enabledPlugins", {}).get(f"{SERVERS[product]}@nexus-plugins") is True
    if enabled and source.get("source") == "directory":
        folder = Path(source["path"]) / "plugins" / SERVERS[product]
        server = read_json(folder / ".mcp.json").get("mcpServers", {}).get(SERVERS[product])
        return [(server, folder)] if server else []
    return []


def claude_desktop(product: str) -> list[tuple[dict, Path]]:
    folders = [APPDATA / "Claude", *sorted((LOCALAPPDATA / "Packages").glob("*Claude_*/LocalCache/Roaming/Claude")),
               *sorted((LOCALAPPDATA / "Claude-Data").glob("*/"))]
    found = []
    for folder in folders:
        if folder.is_dir() and not any(folder.glob(".user-data-left.*")):  # data moved: Claude no longer reads it
            server = read_json(folder / "claude_desktop_config.json").get("mcpServers", {}).get(SERVERS[product])
            if server:
                found.append((server, folder))
    return found


def codex(product: str) -> list[tuple[dict, Path]]:
    folder = Path(os.environ.get("CODEX_HOME") or HOME / ".codex")
    config = folder / "config.toml"
    if not config.is_file():
        return []
    data = tomllib.loads(config.read_text(encoding="utf-8-sig"))
    name = SERVERS[product]
    if data.get("mcp_servers", {}).get(name) is not None:
        raise ValueError(f"{name}: direct MCP declaration remains; expected only the installed plugin and guide")
    plugins = [(key.split("@", 1)[1], value) for key, value in data.get("plugins", {}).items()
               if key.startswith(name + "@") and value.get("enabled", True)]
    if len(plugins) != 1:
        raise ValueError(f"{name}: expected one enabled plugin, found {len(plugins)}")
    cache = folder / "plugins" / "cache" / plugins[0][0] / name
    installed = []
    for root in cache.iterdir() if cache.is_dir() else []:
        manifest = read_json(root / ".codex-plugin" / "plugin.json")
        if manifest.get("name") == name and manifest.get("skills") == "./skills/" and \
                (root / "skills" / name / "SKILL.md").is_file():
            server = read_json(root / ".mcp.json").get("mcpServers", {}).get(name)
            if server:
                installed.append((server, root))
    if not installed:
        raise ValueError(f"{name}: installed MCP plugin and bundled guide not found")
    return installed[-1:]


def antigravity(product: str) -> list[tuple[dict, Path]]:
    plugins = HOME / ".gemini" / "config" / "plugins"
    plugin = next((plugins / n for n in AGY_PLUGINS[product] if (plugins / n / "plugin.json").is_file()),
                  plugins / AGY_PLUGINS[product][0])
    servers = read_json(plugin / "mcp_config.json").get("mcpServers", {})
    return [(server, plugin) for server in servers.values() if "command" in server][:1]


READERS = {"claude-code": claude_code, "claude-desktop": claude_desktop, "codex": codex, "antigravity": antigravity}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ais", required=True)
    parser.add_argument("--products", default="windows,android,browser")
    args = parser.parse_args()
    failures = 0
    with tempfile.TemporaryDirectory(prefix="nexus-pack-check-") as tmp:
        for ai in args.ais.split(","):
            for product in args.products.split(","):
                label = f"{ai}/{SERVERS[product]}"
                try:
                    declared = READERS[ai](product)
                except (OSError, ValueError) as error:
                    print(f"FAIL {label}: {error}")
                    failures += 1
                    continue
                if not declared:
                    print(f"FAIL {label}: not declared")
                    failures += 1
                    continue
                for server, root in declared:
                    failures += smoke(label, resolved(server), root, Path(tmp) / label.replace("/", "-"))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
