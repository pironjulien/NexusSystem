"""Start each plugin's MCP server (Claude and Codex) exactly as its .mcp.json says (uvx from PyPI) and list its tools."""

import json
import os
import re
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TIMEOUT = 300  # first run downloads Python 3.14 and the package
PERSONAL = "Julien"  # what any third-party client receives must name nobody


def rpc(proc: subprocess.Popen, msg: dict) -> None:
    proc.stdin.write(json.dumps(msg) + "\n")
    proc.stdin.flush()


def read_reply(proc: subprocess.Popen, want: int) -> dict:
    result: dict = {}

    def reader() -> None:
        for line in proc.stdout:
            try:
                data = json.loads(line)
            except ValueError:
                continue
            if data.get("id") == want:
                result.update(data)
                return

    thread = threading.Thread(target=reader, daemon=True)
    thread.start()
    thread.join(TIMEOUT)
    if not result:
        raise TimeoutError(f"no reply to request {want} within {TIMEOUT} s")
    return result


def smoke(name: str, server: dict, root: Path, data: Path) -> int:
    def expand(text: str) -> str:  # what Claude Code does for a plugin's .mcp.json: ${VAR} and ${VAR:-default}
        known = {**os.environ, "CLAUDE_PLUGIN_ROOT": str(root), "CLAUDE_PLUGIN_DATA": str(data)}
        return re.sub(r"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::-([^}]*))?\}",
                      lambda m: known.get(m.group(1)) or (m.group(2) or ""), text)

    env = {**os.environ, **{k: expand(v) for k, v in server.get("env", {}).items()}}
    cmd = [server["command"], *map(expand, server["args"])]
    if server["command"] == "uvx":
        # Right after a PyPI release the index uv reads can still lack the new version: fetched again, not cached.
        cmd.insert(1, "--refresh")
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                            text=True, encoding="utf-8", env=env)
    try:
        rpc(proc, {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
            "protocolVersion": "2025-06-18", "capabilities": {},
            "clientInfo": {"name": "nexus-plugins-smoke", "version": "1"}}})
        init = read_reply(proc, 1)["result"]
        info = init["serverInfo"]
        rpc(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})
        rpc(proc, {"jsonrpc": "2.0", "id": 2, "method": "tools/list"})
        tools = read_reply(proc, 2)["result"]["tools"]
        seen = (init.get("instructions") or "") + json.dumps(tools, ensure_ascii=False)
        if PERSONAL in seen.replace("julien_asked", ""):  # julien_asked: former parameter name, kept as an alias
            raise ValueError(f"server text sent to every client names {PERSONAL!r}")
        print(f"OK {name}: {info.get('name')} {info.get('version')}, {len(tools)} tools")
        return 0
    except Exception as exc:  # noqa: BLE001 - report every failure kind the same way
        print(f"FAIL {name}: {exc}")
        return 1
    finally:
        proc.kill()


if __name__ == "__main__":
    failures = 0
    with tempfile.TemporaryDirectory(prefix="nexus-plugins-data-") as tmp:
        for mcp in sorted([*(ROOT / "plugins").glob("*/.mcp.json"), *(ROOT / "codex").glob("*/.mcp.json")]):
            for name, server in json.loads(mcp.read_text(encoding="utf-8"))["mcpServers"].items():
                label = f"{mcp.parent.parent.name}/{name}"
                # A version just published can take a few minutes to reach PyPI's index: tried again before failing.
                for attempt in range(4):
                    code = smoke(label, server, mcp.parent, Path(tmp) / label)
                    if not code or attempt == 3:
                        break
                    print(f"  {label}: trying again in 60 s (PyPI index not up to date yet?)")
                    time.sleep(60)
                failures += code
    sys.exit(1 if failures else 0)
