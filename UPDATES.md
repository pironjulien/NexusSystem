# Updates

NexusSystem coordinates updates of installed NexusWindows and NexusBrowser engines and their existing AI
integrations. Publishing source code is separate from publishing a release. Only published releases are installed;
development checkouts are never pulled, built or changed by maintenance.

## Distribution

After a successful installation, the pack keeps its maintenance script under
`~\.nexus\nexussystem\maintenance` and registers the current user's **NexusSystem Update** task. It runs at sign-in
and retries periodically in the background. It uses the same official product installers as the pack, downloaded
as files and checked against the exact release's SHA-256 manifest. The coordinator refreshes itself from the
published NexusSystem catalog release using the same checks.

For an existing Store hub upgraded to the new app, the first launch establishes this same task when NexusWindows
or NexusBrowser is already installed. It does not reinstall products. An explicit `autoUpdate: false` in
`~\.nexus\nexussystem.json`, or a disabled maintenance task, remains respected.

Maintenance updates existing supported installations and their selected integrations. A disabled integration,
custom server, skipped product or AI app is not an invitation to install or reconfigure it. Unsupported product
installers are reported explicitly rather than run with guessed arguments. User choices in
`~\.nexus\nexussystem.json` still apply.

To request maintenance immediately, an agent can run the installed `get-nexus.ps1 -Maintain`. To read the latest
result without installing or restarting anything, use `get-nexus.ps1 -MaintenanceStatus`.

## Installation and activation

The installed version and the version a running AI connection uses are different facts. New immutable engine
files can be prepared while an older connection is working. Existing Windows MCP connections retain their
observations, approvals and session state; new connections use the installed engine. Browser activation is
cooperative and waits for owned tabs and outstanding work to be released.

Maintenance never closes an AI app or kills a working connection. If a host cannot reload its plugins safely
through a supported interface, the update remains **installed, activation pending** until that host's next normal
reload or connection. A new test client is useful for qualification, but does not prove that an existing AI session
has switched versions. Similarly, an updated skill on disk does not prove it was reread by a conversation.

The maintenance result at `~\.nexus\status\nexussystem-update.json` distinguishes the published release,
verified installation, observed running versions and pending activation. A failed or partial check must not be
shown as fully updated. Deferred work is retried by the same coordinator.

## Chrome extension

The Chrome Web Store extension has its own publication and review cycle. Chrome distributes an approved,
published version automatically; requesting an update check does not bypass Google's review or guarantee
immediate activation. A reload alone does not download an extension update.

An unpacked development extension follows its local files, not the Store. Its activation is deferred while an
agent owns tabs. Maintenance must identify the installation type instead of treating these two routes as the
same distribution channel.

## Release checks

Publish and qualify each native product release before publishing a catalog that requires it. Qualify the
coordinator with missing, disabled and custom integrations, concurrent maintenance, failed downloads or hashes,
and installed-but-old running sessions. Keep the previous working runtime available until normal connections
have moved to the new one.
