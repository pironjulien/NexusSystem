using Nexus.Core;

// NexusMaintenance.exe [--current] [--force] [--test-catalog <folder> --test-engines <folder>]
//   (no option)  the hourly cycle: replace this engine with the published one when it differs, then maintain products
//   --current    qualify this very engine: no self-replacement (a development build, or the engine just installed)
//   --force      run now even when the last check is less than an hour old
//   --test-catalog, --test-engines  tests only, always together (--install --maintenance-from registers them in the
//                task): the local catalogue of a commit whose release does not exist yet, and the folder holding its
//                engines and SHA256SUMS.txt. This engine must be the one listed there (SHA-256) and of the catalogue's
//                version; it is never replaced, and Codex's catalogue may be that very local folder.
// NexusMaintenance.exe --install [--only <names>] [--skip <names>] [--dry-run] [--no-guide] [--catalog <source>] [--json]
//                      [--maintenance-from <folder>]   the Nexus pack, as get-nexus.ps1 starts it (EngineCommand)
// NexusMaintenance.exe --install-maintenance [--start] | --maintain [--force] | --status
// Exit codes: 0 checked, current or done, 2 pending (including another run) or an unknown name, 1 failed, 64 invalid
// options. The hourly result is ~\.nexus\status\nexussystem-update.json; the interactive modes write to the console of
// the PowerShell that started them (get-nexus.ps1), the hourly cycle shows nothing (the task runs it without a window).
if (EngineCommand.Parse(args) is not { } command) return 64;
var folders = UserFolders.FromEnvironment();
if (command.Mode == EngineMode.Hourly)
{
    var services = new MaintenanceServices(folders) { TestRelease = command.Hourly!.TestRelease };
    return await new Maintainer(services).RunAsync(command.Hourly.Current, command.Hourly.Force).ConfigureAwait(false);
}
using var output = new PackConsole();
try
{
    switch (command.Mode)
    {
        case EngineMode.Status:
            output.Line(MaintenanceSetup.Status(folders, DateTimeOffset.UtcNow));
            return 0;
        case EngineMode.InstallMaintenance:
            new MaintenanceSetup(folders).Install(MaintenanceSetup.Self(null), null, command.Start);
            return 0;
        case EngineMode.Maintain:
            return new MaintenanceSetup(folders).Handover(MaintenanceSetup.Self(null), command.Force);
    }
    var pack = new PackServices(folders, output, command.Pack!.Json) { Console = output.IsConsole };
    var code = 1;
    try { code = new PackInstaller(pack).Run(command.Pack); }
    finally { pack.Event(new Dictionary<string, object?> { ["type"] = "done", ["code"] = code }); }
    return code;
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    output.Line(error.Message, PackColor.Red);
    return 1;
}
