# NexusSystem app

The Windows app of NexusSystem, on the Microsoft Store as 9NSH2P3QLWT1: the hub that installs the Nexus products.
One code base: `src/Nexus.Core` (shared logic, no UI) and `src/Nexus.App` (WinUI 3 hub). Commands below run from
this `app/` folder; `pack/get-nexus.ps1` is a byte-for-byte copy of the repository root's `get-nexus.ps1`.

```powershell
dotnet test --project tests/Nexus.Core.Tests                       # core
dotnet build src/Nexus.App -c Release -p:Platform=x64 -r win-x64   # hub
pwsh -File tools/capture.ps1                                       # pictures, demo profile, foreground never taken
pwsh -File pack/sync.ps1                                           # take the catalog's get-nexus.ps1 (then build with --no-incremental)
pwsh -File tools/package.ps1                                       # test MSIX (identity NexusSystemTest)
pwsh -File tools/package.ps1 -Store -IdentityName julienpiron.fr.NexusSystem -Publisher CN=C3E3A6F0-11D2-4EE1-B3F2-34EED4CAE7FA -PublisherDisplayName julienpiron.fr -Out $env:LOCALAPPDATA\NexusApps\dist\store107   # x64 + ARM64 bundle and public symbols, version from Directory.Build.props
pwsh -File tools/wack.ps1 -Msix <the .msix>                        # Windows App Certification Kit (elevated)
```

`tools/sandbox.ps1 -Msix <package.msix> -ExpectedSha256 <64-digit SHA256> -Version 1.0.7.0 -Certification` runs the
given package in a fresh Windows Sandbox, with native activation, product names read from UI Automation, a screenshot,
a clean close and the real Windows App Certification Kit. Omit `-Certification` for the native smoke alone. Windows
Sandbox and WACK must already be installed; the Store build also needs Visual Studio's C++ symbol tools. Inputs and
WACK are mapped read-only, networking and clipboard sharing are disabled, and only a unique report directory under
`%LOCALAPPDATA%\NexusApps` is writable. The guest shuts itself down; an existing Sandbox or host app is preserved.
A failed activation records its HRESULT and Windows event logs instead of treating a launcher process as proof.
Signed MSIX files retain their signature and use normal package deployment. Unsigned Store submissions use a
development layout and may be refused by Sandbox's Smart App Control before the app starts (`0x800711C7`); the test
keeps that failure visible and never disables the policy. Complete validation with that policy enabled needs the
Store-signed artifact or a signing identity the policy trusts.
Certification maps WACK, its sibling TAEF runtime and SDK `References` metadata read-only; missing dependencies are
refused before launch. Its binary analyzer is registered inside the disposable guest, as the SDK installer would.
`-CertificationOnly` runs WACK without repeating the native smoke on the same package.
It requires a signed MSIX or `-TestSignInGuest`; it refuses an unsigned layout before launching Sandbox.
Reports record the guest's application-control state; a development-signature pass does not prove Store trust.
`-TestSignInGuest` can create a one-day, non-exportable development certificate inside the disposable guest and
test-sign a guest-only copy. Its original MSIX hash, derived signature hash and unchanged payload digest are recorded.
This development certificate is never installed on the host and does not claim Microsoft Store or SAC trust.

## Local installation alongside the Store app

Publish `src/Nexus.App` to a new directory outside OneDrive, then run
`pwsh -File tools/install-local.ps1 -Source <publish-directory> -Start`. The installer verifies the complete payload,
keeps each release under `%LOCALAPPDATA%\NexusApps\local\<version>\<payload-sha256>` and updates only the distinct
**NexusSystem (local)** Start menu shortcut. It preserves the signed Store app and all earlier local releases.
If the terminal inherits a packaged identity, an ordinary interactive-user scheduled worker performs the local
installation and launch. A file-handle check records the physical executable and shortcut paths; a version string
or a launcher process alone does not prove a successful UI launch.

The Windows page shows one installed core version from `~\.nexus\nexus-windows\installation.json` (Schema 2,
exact immutable executable and artifact hashes). Each AI connection is reported separately from that installed
version. A current `--mcp --agent` process must have a matching AI host ancestor; test clients and helpers do not
make an AI appear connected. An older loaded connection takes precedence until that client is normally reloaded.
Claude Desktop's own configuration is read separately: an enabled Claude Code plugin does not configure Desktop.
The observed Store Desktop image (including its Electron child modes) is attributed separately from the native
Code CLI; a Desktop process never makes the Code connection active. Unknown Claude hosts remain unidentified.
The previous `NexusWindows.Antigravity.exe` MCP client is still recognized while its older session is loaded.

## How the hub works from the Store package
- **Install**: the pack's own script (`pack/get-nexus.ps1`, byte for byte the catalog's) runs each product's official
  installer as a child process, so MSIX virtualization applies. From the package it skips Claude Desktop (an edit of
  its existing configuration would stay in the package): Claude Desktop gets each product as a `.mcpb` extension the
  user drops on it. Chrome is paired with the NexusBrowser extension (single-use code), no native host key needed.
- **Selection**: the pack passes the AI apps to configure in `NEXUS_PACK_TARGETS`; NexusAndroid, NexusRelay and
  NexusBrowser honor it. An AI app where the user turned a product off is left as it is.
- **Declarations**: read for real (declared, missing, turned off by the user, invalid command) in Claude Code, Codex
  and Antigravity, the same way as the pack.
- **Updates**: the public NexusSystem maintenance task coordinates supported Windows and Browser updates outside
  the app package, using verified official release installers. The app requests that task and reads its result;
  it distinguishes published, installed and actually loaded versions. Existing AI sessions can remain on an older
  engine until their normal reload. Older installations retain the product's own update interface where available
  (Relay and Browser HTTP APIs); NexusKeypad is updated by the task only. See [Updates](../UPDATES.md); an absent update path is never shown
  as a promise that the product will update itself.

`store/` holds the listing (FR/EN), the privacy text and the certification notes. `probe/` measures what an installed
MSIX and its child processes really write outside the package container (locally, NexusProbe markers only, erased
and checked).
