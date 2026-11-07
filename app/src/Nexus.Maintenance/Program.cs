using Nexus.Core;

// NexusMaintenance.exe [--current] [--force]
//   (no option)  the hourly cycle: replace this engine with the published one when it differs, then maintain products
//   --current    qualify this very engine: no self-replacement (a development build, or the engine just installed)
//   --force      run now even when the last check is less than an hour old
// Exit codes: 0 checked or current, 2 pending (including another run), 1 failed, 64 unknown option.
// The result is ~\.nexus\status\nexussystem-update.json; nothing is shown (the task runs it without a window).
var known = new[] { "--current", "--force" };
if (args.Any(argument => !known.Contains(argument, StringComparer.OrdinalIgnoreCase))) return 64;
var current = args.Contains("--current", StringComparer.OrdinalIgnoreCase);
var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
return await new Maintainer(new MaintenanceServices(UserFolders.FromEnvironment())).RunAsync(current, force).ConfigureAwait(false);
