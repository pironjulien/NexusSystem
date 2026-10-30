"""Offline checks of the marketplace: manifests, pinned servers, skills, nothing local or private."""

import json
import re
import struct
import sys
import tomllib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NAME = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
SEMVER = re.compile(r"^\d+\.\d+\.\d+$")
# A public catalog never points to a local daemon, a token or a path of the author's PC; the one exception is the
# relay's plugin (DAEMON below), whose server IS the local NexusRelay daemon installed by the one-line install.
FORBIDDEN = [
    (re.compile(r"127\.0\.0\.1|localhost:\d+"), "local daemon address"),
    (re.compile(r"NEXUS_MCP_TOKEN|Bearer"), "token"),
    (re.compile(r"[A-Za-z]:\\Users\\|/home/|USERPROFILE|OneDrive", re.I), "local path"),
]
# Plugins served by a local daemon (no uvx, no lock): exactly this declaration, the token never written, only read from
# the user variable NEXUS_MCP_TOKEN (28/10: the relay joins the catalog, its daemon stays installed by the pack).
DAEMON = {
    "nexus-relay": {
        "claude": {"nexus-relay": {"type": "http", "url": "http://127.0.0.1:16181/mcp?agent=claude",
                                   "headers": {"Authorization": "Bearer ${NEXUS_MCP_TOKEN}"}}},
        "codex": {"nexus-relay": {"url": "http://127.0.0.1:16181/mcp?agent=codex",
                                  "bearer_token_env_var": "NEXUS_MCP_TOKEN", "startup_timeout_sec": 60}},
    }
}
# PyPI package started by a plugin, when it differs from the plugin name (NexusWindows for Claude took the family
# name nexus-windows-claude in 3.0.1; the plugin keeps its name so the tool prefix stays the same).
PACKAGES = {"nexus-windows": "nexus-windows-claude"}
errors: list[str] = []


def fail(msg: str) -> None:
    errors.append(msg)


def load(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        fail(f"{path.relative_to(ROOT)}: unreadable JSON ({exc})")
        return {}


def frontmatter(path: Path) -> dict[str, str]:
    text = path.read_text(encoding="utf-8")
    match = re.match(r"^---\r?\n(.*?)\r?\n---\r?\n", text, re.S)
    if not match:
        fail(f"{path.relative_to(ROOT)}: missing YAML frontmatter")
        return {}
    fields = {}
    for line in match.group(1).splitlines():
        key, _, value = line.partition(":")
        fields[key.strip()] = value.strip()
    return fields


market = load(ROOT / ".claude-plugin" / "marketplace.json")
if not NAME.match(market.get("name", "")):
    fail("marketplace: name must be kebab-case")
if not market.get("owner", {}).get("name"):
    fail("marketplace: owner.name is required")

entries = market.get("plugins", [])
folders = sorted(p.name for p in (ROOT / "plugins").iterdir() if p.is_dir())
if sorted(e.get("name", "") for e in entries) != folders:
    fail(f"marketplace: listed plugins differ from plugins/ folders {folders}")

for entry in entries:
    name = entry.get("name", "")
    where = f"plugin {name}"
    if entry.get("source") != f"./plugins/{name}":
        fail(f"{where}: source must be ./plugins/{name}")
    folder = ROOT / "plugins" / name
    manifest = load(folder / ".claude-plugin" / "plugin.json")
    if manifest.get("name") != name:
        fail(f"{where}: plugin.json name differs")
    version = manifest.get("version", "")
    if not SEMVER.match(version) or entry.get("version") != version:
        fail(f"{where}: version must be semver and equal in marketplace.json and plugin.json")
    for field in ("description", "license", "repository", "homepage"):
        if not manifest.get(field) or not entry.get(field if field != "license" else "license"):
            fail(f"{where}: {field} missing")
    servers = load(folder / ".mcp.json").get("mcpServers", {})
    if name in DAEMON:
        if servers != DAEMON[name]["claude"]:
            fail(f"{where}: .mcp.json must declare exactly the local daemon {DAEMON[name]['claude']}")
        if "daemon" not in entry.get("description", "").lower():
            fail(f"{where}: description must say that the daemon is required")
    elif list(servers) != [name]:
        fail(f"{where}: .mcp.json must declare exactly the server {name}")
    server = servers.get(name, {})
    args = server.get("args", [])
    package = PACKAGES.get(name, name)
    # The directory's validator reads exactly "uvx <package>==<version>", with uv.lock next to the manifest (a daemon
    # plugin starts nothing: its declaration was checked above).
    if name not in DAEMON and (server.get("command") != "uvx" or args != ["--python", "3.14", f"{package}=={version}"]):
        fail(f"{where}: server must start with uvx --python 3.14 {package}=={version}")
    if any(key.startswith("UV_") for key in server.get("env", {})):
        fail(f"{where}: no uv setting in the server env (the directory blocks registry redirects)")
    if name in DAEMON:
        if (folder / "pyproject.toml").exists() or (folder / "uv.lock").exists():
            fail(f"{where}: a daemon plugin starts nothing: no pyproject.toml nor uv.lock")
    else:
        project = tomllib.loads((folder / "pyproject.toml").read_text(encoding="utf-8")).get("project", {})
        if project.get("dependencies") != [f"{package}=={version}"] or project.get("version") != version:
            fail(f"{where}: pyproject.toml must depend on exactly {package}=={version}")
        lock = (folder / "uv.lock").read_text(encoding="utf-8") if (folder / "uv.lock").is_file() else ""
        if f'name = "{package}"\nversion = "{version}"' not in lock or "sha256:" not in lock:
            fail(f"{where}: uv.lock must lock {package} {version} with hashes")
    icon = folder / ".claude-plugin" / "icon.png"
    if manifest.get("icon") != "./.claude-plugin/icon.png" or not icon.is_file():
        fail(f"{where}: icon must be ./.claude-plugin/icon.png")
    else:
        head = icon.read_bytes()[:24]
        width, height = struct.unpack(">II", head[16:24])
        if head[:8] != bytes.fromhex("89504e470d0a1a0a") or width != height or not 512 <= width <= 2048:
            fail(f"{where}: icon must be a square PNG of 512 to 2048 px")
    if name == "nexus-browser" and "extension" not in entry.get("description", "").lower():
        fail(f"{where}: description must say that the Chrome extension is required")
    skills = sorted((folder / "skills").glob("*/SKILL.md"))
    if [s.parent.name for s in skills] != [name]:
        fail(f"{where}: expected one skill skills/{name}/SKILL.md")
    for skill in skills:
        meta = frontmatter(skill)
        if meta.get("name") != skill.parent.name or len(meta.get("description", "")) < 40:
            fail(f"{skill.relative_to(ROOT)}: frontmatter needs name = folder and a real description")

# Codex marketplace (.agents/plugins/marketplace.json): the same plugins for Codex. NexusWindows comes from its own
# repository (its Codex edition); the others are folders of this catalog, pinned like their Claude twin.
CODEX_URLS = {"nexus-windows": "https://github.com/pironjulien/NexusWindows-Codex.git"}
CODEX_AGENT = {"nexus-browser": "Codex"}  # NEXUS_BROWSER_AGENT names the client in the tab group
codex = load(ROOT / ".agents" / "plugins" / "marketplace.json")
if codex.get("name") != market.get("name"):
    fail("codex marketplace: name must be the Claude marketplace's")
codex_entries = codex.get("plugins", [])
if [e.get("name") for e in codex_entries] != [e.get("name") for e in entries]:
    fail("codex marketplace: must list the same plugins, in the same order")
codex_folders = sorted(p.name for p in (ROOT / "codex").iterdir() if p.is_dir())
if codex_folders != sorted(set(folders) - set(CODEX_URLS)):
    fail(f"codex/: folders {codex_folders} differ from the local Codex plugins")
claude_versions = {e.get("name"): e.get("version") for e in entries}
for entry in codex_entries:
    name = entry.get("name", "")
    where = f"codex plugin {name}"
    if entry.get("policy") != {"installation": "AVAILABLE", "authentication": "ON_INSTALL"} or not entry.get("category"):
        fail(f"{where}: policy AVAILABLE/ON_INSTALL and a category are required")
    source = entry.get("source", {})
    if name in CODEX_URLS:
        if source != {"source": "url", "url": CODEX_URLS[name]}:
            fail(f"{where}: source must be the url {CODEX_URLS[name]}")
        continue
    if source != {"source": "local", "path": f"./codex/{name}"}:
        fail(f"{where}: source must be the local path ./codex/{name}")
    folder = ROOT / "codex" / name
    manifest = load(folder / ".codex-plugin" / "plugin.json")
    version = claude_versions.get(name)
    if manifest.get("name") != name or manifest.get("version") != version:
        fail(f"{where}: plugin.json name and version must match the Claude plugin ({version})")
    if manifest.get("mcpServers") != "./.mcp.json" or manifest.get("skills") != "./skills/":
        fail(f"{where}: plugin.json must point to ./.mcp.json and ./skills/")
    if not manifest.get("interface", {}).get("displayName"):
        fail(f"{where}: interface.displayName missing")
    for field, size in (("composerIcon", 64), ("logo", 512)):
        relative = manifest.get("interface", {}).get(field, "")
        asset = folder / relative
        if not relative.startswith("./assets/") or not asset.is_file():
            fail(f"{where}: interface.{field} must point to a bundled assets/ PNG")
            continue
        head = asset.read_bytes()[:24]
        if len(head) < 24 or head[:8] != bytes.fromhex("89504e470d0a1a0a"):
            fail(f"{where}: interface.{field} must be a PNG")
        elif struct.unpack(">II", head[16:24]) != (size, size):
            fail(f"{where}: interface.{field} must be {size}x{size}")
    if name == "nexus-relay":
        expected_hooks = {"hooks": {"PreToolUse": [{
            "matcher": "apply_patch", "hooks": [{
                "type": "command",
                "command": f"uvx --python 3.14 --from nexus-relay=={version} python -I -B -m nexus_relay.edit_guard codex",
                "timeout": 60, "async": False,
            }],
        }]}}
        if manifest.get("hooks") != expected_hooks:
            fail(f"{where}: edit guard must use this plugin's exact published version, synchronously")
    server = load(folder / ".mcp.json").get("mcpServers", {})
    package = PACKAGES.get(name, name)
    if name in DAEMON:
        if server != DAEMON[name]["codex"]:
            fail(f"{where}: .mcp.json must declare exactly the local daemon {DAEMON[name]['codex']}")
        skill = folder / "skills" / name / "SKILL.md"
        meta = frontmatter(skill) if skill.is_file() else {}
        if meta.get("name") != name or len(meta.get("description", "")) < 40:
            fail(f"{where}: skills/{name}/SKILL.md needs name = folder and a real description (its own Codex skill)")
        continue
    if list(server) != [name] or server[name].get("command") != "uvx" or \
            server[name].get("args") != ["--python", "3.14", f"{package}=={version}"]:
        fail(f"{where}: .mcp.json must start only uvx --python 3.14 {package}=={version}")
    elif server[name].get("env", {}).get("NEXUS_BROWSER_AGENT") != CODEX_AGENT.get(name):
        fail(f"{where}: NEXUS_BROWSER_AGENT must be {CODEX_AGENT.get(name)}")
    twin = ROOT / "plugins" / name / "skills" / name / "SKILL.md"
    copy = folder / "skills" / name / "SKILL.md"
    if not copy.is_file() or copy.read_bytes() != twin.read_bytes():
        fail(f"{where}: skills/{name}/SKILL.md must be a copy of the Claude plugin's skill")

for path in ROOT.rglob("*"):
    if not path.is_file() or ".git" in path.parts or path.suffix not in {".json", ".md", ".toml"}:
        continue
    # What agents read is the catalog; app/ is the NexusSystem app's source, dist/ and TestResults/ local outputs.
    if path.relative_to(ROOT).parts[0] in {"app", "dist", "TestResults"}:
        continue
    if path.name == "CHANGELOG.md":
        continue
    text = path.read_text(encoding="utf-8")
    rel = path.relative_to(ROOT).parts
    if len(rel) > 2 and rel[0] in {"plugins", "codex"} and rel[1] in DAEMON and path.name == ".mcp.json":
        continue  # checked above: exactly the daemon declaration, token read from the user variable
    for pattern, label in FORBIDDEN:
        if pattern.search(text):
            fail(f"{path.relative_to(ROOT)}: {label} ({pattern.search(text).group(0)})")
    if re.search(r"\bJulien\b", text) and path.suffix == ".md" and path.name not in {"README.md", "LICENSE"}:
        fail(f"{path.relative_to(ROOT)}: names the author where a third-party agent reads it")

if errors:
    print("\n".join(f"FAIL {e}" for e in errors))
    sys.exit(1)
print(f"OK catalog: {len(entries)} plugins, manifests, pins and skills consistent")
