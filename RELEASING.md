# Releasing a Nexus product

One path for every Nexus product and for every AI that works on them (Claude, Codex, Antigravity). The user asks
for an improvement; the AI changes, tests and ships it; GitHub builds every release; a validated release reaches
everyone. Nothing here asks the user to run a command.

```text
change ─► test locally ─► push main ─► CI: tests + build ─► vX.Y.Z-dev.N ─► vX.Y.Z (same files, stable) ─► stores, registry
                                                            (new version only; otherwise it stays a development build)
```

## 1. Change and test locally

- Work in the product's own repository (`NexusHarness\<Product>`, one public repository per product).
- Run the product's `test.ps1` (xUnit and the product's own checks) until it is green.
- NexusSystem app: the user's PC runs the Microsoft Store app, the very build the public gets (since 2026-11-04:
  certification is fast). A change is checked before submission with `app/tools/capture.ps1` (every page, both
  languages and themes, on a made-up profile) and the test package; after certification, `winget install --id
  9NSH2P3QLWT1 --source msstore --force` takes it on the PC without waiting for the Store's own update.
- Plugin changes live in the product's `plugin/` folder, the single source for the three AI apps:
  `.claude-plugin/` and `.mcp.json` (Claude Code), `.codex-plugin/` and `.codex-mcp.json` (Codex), Antigravity's
  manifest and guide (shipped with the engine and installed by its installer).
- Raise the version in every file the release check lists (patch for fixes) and add the `CHANGELOG.md` section
  (Keep a Changelog, English). Commit precise files with an English message.

## 2. Push: a development build, then stable

Every push to `main` runs the product's `Release` workflow: tests, build, `SHA256SUMS.txt`, then NexusSystem's shared
[`product-dev-release`](.github/workflows/product-dev-release.yml) attests the files (Sigstore build provenance) and
publishes the pre-release `vX.Y.Z-dev.N`, a build record that nothing installs. `releases/latest` never points to it.

## 3. Stable, right after the build

The local test is the validation: when the pushed version is not released yet, the same `Release` run promotes its
development build at once. The shared [`product-promote`](.github/workflows/product-promote.yml) publishes the SAME
files, checked against their checksums, as `vX.Y.Z` with the CHANGELOG section, then the product's `server.json` to
the MCP registry. Nothing is rebuilt; the build provenance of each file stays valid (`gh attestation verify <file>
--repo pironjulien/<Product> --signer-repo pironjulien/NexusSystem --source-ref refs/heads/main`: the shared workflow
signs it). A push that keeps an already released version stays a
development build. "Run workflow" (`gh workflow run release.yml -R pironjulien/<Product> -f promote=vX.Y.Z-dev.N`;
empty = the newest) still promotes by hand.

Then pin the stable plugin in both catalogs, commit only the changed files and push straight to `main` here (no
branch, no pull request: the push runs the same checks):

```text
pwsh app/tools/pin-plugin.ps1 -Name nexus-<product> -Product <Product> -Tag vX.Y.Z
```

NexusKeypad's plugin folder is `connecteurs/` (its `plugin/` is the Logitech plugin): add `-Path connecteurs`.

On the user's PC, do not wait for the hourly maintenance: once the stable release is published, run the task's
engine at once, forced past its hourly check (`$a = (Get-ScheduledTask 'NexusSystem Update').Actions[0];
(Start-Process $a.Execute -ArgumentList ("$($a.Arguments) --force".Trim()) -Wait -PassThru).ExitCode`; the task alone
returns at once while its `nextCheckAt` is ahead), then read
`%USERPROFILE%\.nexus\status\nexussystem-update.json` and check that the product reached the new version (exit code
2, state `pending`: installed, running sessions keep their engine until a normal reload). Between releases, changes are tested from the working folder.

Claude Code and Codex read the catalog from `main`: no NexusSystem release is needed for a plugin or engine update.
A NexusSystem release (`vX.Y.Z` here) is needed only when `get-nexus.ps1` or the maintenance engine
(`app/src/Nexus.Maintenance`, `Nexus.Core`) changes: raise the version, add its
`CHANGELOG.md` section, merge, then push the tag `vX.Y.Z` alone (`git tag vX.Y.Z origin/main && git push origin vX.Y.Z`);
the [`release`](.github/workflows/release.yml) workflow publishes it with its files. Before publishing anything it
builds the engines, then runs the full installation on a clean Windows runner from the tagged commit with those very
engines ([`pack`](.github/workflows/pack.yml): every product installed, every declared server reached, a second run
green, the installed engine started from PowerShell 7 returning 0); a red `pack` publishes nothing. Fix on `main`,
then tag the next patch version (a tag never moves). The Store app is resubmitted only for its own interface or
features.

Releases are immutable in every product repository (Settings > General > Releases): a published release gets no file
added, replaced or removed, and its tag never moves. Every file is attached at creation, as the workflows above do;
`gh release verify vX.Y.Z -R pironjulien/<Product>` checks GitHub's release attestation.

## 4. Stores

Store reviews never block local tests or the GitHub channels.

| Store | Product | How it is published |
|---|---|---|
| MCP registry | MCP products | Automatic, at promotion (GitHub OIDC). |
| Microsoft Store | NexusSystem app | Partner Center submission by the AI; the submission API needs a Microsoft Entra app the user authorizes once. |
| Chrome Web Store | NexusBrowser extension | Sent by the AI when the user asks, never by GitHub: `scripts/publish-store.ps1` (NexusBrowser) sends the latest stable release's extension as the `cws-publisher` service account (gcloud token, no stored secret) and says when an earlier version is still in review. The developer dashboard, when a step has no API, through NexusWindows. |
| Logitech Marketplace | NexusKeypad | Logitech's developer portal (no public API). |

Prefer keyless access (GitHub OIDC with the store's identity federation) over stored secrets. Pages that Chrome
protects from extensions (Chrome Web Store dashboard) are driven through Windows UI Automation (NexusWindows) when a
step has no API; asking the user is the last resort.

## 5. Checks before saying "done"

- The product's CI and the release run are green; the stable release lists the same `SHA256SUMS.txt` as the
  promoted development build.
- `gh workflow run pack.yml -R pironjulien/NexusSystem` installs every product on a clean Windows runner with this
  commit's maintenance engine and reaches every server each AI app declares (a NexusSystem release runs it itself and
  publishes only when it is green). Run it by hand after a product release or a catalog change.
- What a running AI session uses is checked separately from what is installed: a new session or a normal reload
  picks up a new version; maintenance never closes an AI app.
