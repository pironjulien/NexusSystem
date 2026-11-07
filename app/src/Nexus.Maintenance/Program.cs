using Nexus.Core;

// NexusMaintenance.exe [--current] [--force] [--test-catalog <folder> --test-engines <folder>]
//   (no option)  the hourly cycle: replace this engine with the published one when it differs, then maintain products
//   --current    qualify this very engine: no self-replacement (a development build, or the engine just installed)
//   --force      run now even when the last check is less than an hour old
//   --test-catalog, --test-engines  tests only, always together (get-nexus.ps1 -MaintenanceFrom registers them in the
//                task): the local catalogue of a commit whose release does not exist yet, and the folder holding its
//                engines and SHA256SUMS.txt. This engine must be the one listed there (SHA-256) and of the catalogue's
//                version; it is never replaced, and Codex's catalogue may be that very local folder.
// Exit codes: 0 checked or current, 2 pending (including another run), 1 failed, 64 invalid options.
// The result is ~\.nexus\status\nexussystem-update.json; nothing is shown (the task runs it without a window).
if (!MaintenanceOptions.TryParse(args, out var options)) return 64;
var services = new MaintenanceServices(UserFolders.FromEnvironment()) { TestRelease = options.TestRelease };
return await new Maintainer(services).RunAsync(options.Current, options.Force).ConfigureAwait(false);
