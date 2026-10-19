"""Offline checks of the marketplace: manifests, pinned servers, skills, nothing local or private."""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NAME = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
SEMVER = re.compile(r"^\d+\.\d+\.\d+$")
# A public catalog never points to a local daemon, a token or a path of the author's PC.
FORBIDDEN = [
    (re.compile(r"127\.0\.0\.1|localhost:\d+"), "local daemon address"),
    (re.compile(r"NEXUS_MCP_TOKEN|Bearer"), "token"),
    (re.compile(r"[A-Za-z]:\\Users\\|/home/|USERPROFILE|OneDrive", re.I), "local path"),
    (re.compile(r"nexus-relay", re.I), "private plugin"),
]
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
    if list(servers) != [name]:
        fail(f"{where}: .mcp.json must declare exactly the server {name}")
    server = servers.get(name, {})
    if server.get("command") != "uvx" or f"{name}=={version}" not in server.get("args", []):
        fail(f"{where}: server must start with uvx pinned to {name}=={version}")
    if name == "nexus-browser" and "extension" not in entry.get("description", "").lower():
        fail(f"{where}: description must say that the Chrome extension is required")
    skills = sorted((folder / "skills").glob("*/SKILL.md"))
    if [s.parent.name for s in skills] != [name]:
        fail(f"{where}: expected one skill skills/{name}/SKILL.md")
    for skill in skills:
        meta = frontmatter(skill)
        if meta.get("name") != skill.parent.name or len(meta.get("description", "")) < 40:
            fail(f"{skill.relative_to(ROOT)}: frontmatter needs name = folder and a real description")

for path in ROOT.rglob("*"):
    if not path.is_file() or ".git" in path.parts or path.suffix not in {".json", ".md"}:
        continue
    if path.name == "CHANGELOG.md":
        continue
    text = path.read_text(encoding="utf-8")
    for pattern, label in FORBIDDEN:
        if pattern.search(text):
            fail(f"{path.relative_to(ROOT)}: {label} ({pattern.search(text).group(0)})")
    if re.search(r"\bJulien\b", text) and path.suffix == ".md" and path.name not in {"README.md", "LICENSE"}:
        fail(f"{path.relative_to(ROOT)}: names the author where a third-party agent reads it")

if errors:
    print("\n".join(f"FAIL {e}" for e in errors))
    sys.exit(1)
print(f"OK catalog: {len(entries)} plugins, manifests, pins and skills consistent")
