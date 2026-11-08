# Development instructions

## Pitfalls

- The catalogue keeps its internal name `nexus-plugins` (plugins `…@nexus-plugins`, `Declarations.cs` of the app) even
  though the repository is NexusSystem: renaming it would orphan every installed plugin.
- A plugin enabled in Claude's `settings.json` is installed or updated only when Claude restarts; Claude does not show
  icons for a third-party catalogue. To check a plugin: version in Claude's plugin cache plus the daemon's `/health`.
- Antigravity accepts no third-party catalogue (Google's store only).
- The `.mcpb` bundles stay in the releases and the MCP registry: they are the public's install path, even where a
  workstation does not use them.
- A new product must be listed in `products.json` AND in `get-nexus.ps1` (a CI test checks it).
