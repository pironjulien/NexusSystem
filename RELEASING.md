# Releasing a Nexus product

One path for every Nexus product and for every AI that works on them (Claude, Codex, Antigravity). The user asks
for an improvement; the AI changes, tests and ships it; GitHub builds every release; a validated release reaches
everyone. Nothing here asks the user to run a command.

```text
change ─► test locally ─► push main ─► CI: tests + build ─► vX.Y.Z-dev.N (pre-release, dev channel)
                                                              │
                                    validated ─► promote ─────┴─► vX.Y.Z (same files, stable) ─► stores, registry
```

## 1. Change and test locally

- Work in the product's own repository (`NexusHarness\<Product>`, one public repository per product).
- Run the product's `test.ps1` (xUnit and the product's own checks) until it is green.
- Plugin changes live in the product's `plugin/` folder, the single source for the three AI apps:
  `.claude-plugin/` and `.mcp.json` (Claude Code), `.codex-plugin/` and `.codex-mcp.json` (Codex), Antigravity's
  manifest and guide (shipped with the engine and installed by its installer). On a development PC,
  `app/tools/dev-catalog.ps1 -Register` (this repository) serves those folders to Claude Code and Codex directly.
- Raise the version in every file the release check lists (patch for fixes) and add the `CHANGELOG.md` section
  (Keep a Changelog, English). Commit precise files with an English message.

## 2. Push: a development build

Every push to `main` runs the product's `Release` workflow: tests, build, `SHA256SUMS.txt`, then NexusSystem's shared
[`product-dev-release`](.github/workflows/product-dev-release.yml) attests the files (Sigstore build provenance) and
publishes the pre-release `vX.Y.Z-dev.N`. `releases/latest` never points to it.

A development PC follows these builds with `"channel": "dev"` in `%USERPROFILE%\.nexus\nexussystem.json`: the
NexusSystem app shows the newest one on each product page, and `get-nexus.ps1` installs it (it passes
`NEXUS_RELEASE_TAG` to the product's installer, which still checks that release's checksums).

## 3. Validate, then promote to stable

When the development build is verified on the PC, run the product's `Release` workflow by hand ("Run workflow",
or `gh workflow run release.yml -R pironjulien/<Product> -f promote=vX.Y.Z-dev.N`; empty = the newest). The shared
[`product-promote`](.github/workflows/product-promote.yml) publishes the SAME files, checked against their
checksums, as `vX.Y.Z` with the CHANGELOG section, then the product's `server.json` to the MCP registry. Nothing is
rebuilt; the build provenance of each file stays valid (`gh attestation verify <file> --repo pironjulien/<Product>
--source-ref refs/heads/main`).

Then pin the stable plugin in both catalogs and merge it through a pull request here:

```text
pwsh app/tools/pin-plugin.ps1 -Name nexus-<product> -Product <Product> -Tag vX.Y.Z
```

Claude Code and Codex read the catalog from `main`: no NexusSystem release is needed for a plugin or engine update.
A NexusSystem release (`vX.Y.Z` here) is needed only when `get-nexus.ps1` changes; the Store app is resubmitted only
for its own interface or features.

## 4. Stores

Store reviews never block local tests or the GitHub channels.

| Store | Product | How it is published |
|---|---|---|
| MCP registry | MCP products | Automatic, at promotion (GitHub OIDC). |
| Microsoft Store | NexusSystem app | Partner Center submission by the AI; the submission API needs a Microsoft Entra app the user authorizes once. |
| Chrome Web Store | NexusBrowser extension | Automatic at promotion: the `cws-publisher` service account, reached by GitHub OIDC (workload identity federation, no stored secret), uploads and submits for review. |
| Logitech Marketplace | NexusKeypad | Logitech's developer portal (no public API). |

Prefer keyless access (GitHub OIDC with the store's identity federation) over stored secrets. Pages that Chrome
protects from extensions (Chrome Web Store dashboard) are driven through Windows UI Automation (NexusWindows) when a
step has no API; asking the user is the last resort.

## 5. Checks before saying "done"

- The product's CI and the release run are green; the stable release lists the same `SHA256SUMS.txt` as the
  promoted development build.
- `gh workflow run test.yml -R pironjulien/NexusSystem` (job `pack`) installs every product on a clean Windows
  runner and reaches every server each AI app declares.
- What a running AI session uses is checked separately from what is installed: a new session or a normal reload
  picks up a new version; maintenance never closes an AI app.
