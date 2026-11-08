namespace Nexus.Core;

/// <summary>What NexusMaintenance.exe was asked to do.</summary>
public enum EngineMode
{
    /// <summary>The hourly cycle (the task's command line): see <see cref="MaintenanceOptions"/>.</summary>
    Hourly,
    /// <summary>--install: the Nexus pack (get-nexus.ps1's installation mode).</summary>
    Install,
    /// <summary>--install-maintenance [--start]: this engine and its task, without reinstalling products.</summary>
    InstallMaintenance,
    /// <summary>--maintain [--force]: the hand-over, this engine's cycle as current, then its task.</summary>
    Maintain,
    /// <summary>--status: the last check as JSON, read only.</summary>
    Status,
}

/// <summary>
/// NexusMaintenance.exe's command line. Without a mode option: the hourly cycle. --install takes get-nexus.ps1's
/// options: --only and --skip (names, comma separated, repeatable), --dry-run, --no-guide, --catalog &lt;source&gt;,
/// --json, --maintenance-from &lt;folder&gt; (tests only: with a local --catalog, the folder holding this release's engines
/// and SHA256SUMS.txt).
/// </summary>
public sealed record EngineCommand(EngineMode Mode, MaintenanceOptions? Hourly = null, PackOptions? Pack = null, bool Start = false, bool Force = false)
{
    public static EngineCommand? Parse(IReadOnlyList<string> args)
    {
        var mode = args.Count == 0 ? null : args[0].ToLowerInvariant() switch
        {
            "--install" => (EngineMode?)EngineMode.Install,
            "--install-maintenance" => EngineMode.InstallMaintenance,
            "--maintain" => EngineMode.Maintain,
            "--status" => EngineMode.Status,
            _ => null,
        };
        if (mode is null) return MaintenanceOptions.TryParse(args, out var hourly) ? new EngineCommand(EngineMode.Hourly, Hourly: hourly) : null;
        var rest = args.Skip(1).Select(a => a.ToLowerInvariant()).ToList();
        switch (mode)
        {
            case EngineMode.Status:
                return rest.Count == 0 ? new EngineCommand(EngineMode.Status) : null;
            case EngineMode.InstallMaintenance:
                return rest is [] or ["--start"] ? new EngineCommand(EngineMode.InstallMaintenance, Start: rest.Count == 1) : null;
            case EngineMode.Maintain:
                return rest is [] or ["--force"] ? new EngineCommand(EngineMode.Maintain, Force: rest.Count == 1) : null;
        }
        List<string> only = [], skip = [];
        bool dryRun = false, noGuide = false, json = false;
        string? catalog = null, from = null;
        for (var i = 1; i < args.Count; i++)
        {
            var name = args[i].ToLowerInvariant();
            string? Value() => i + 1 < args.Count ? args[++i] : null;
            switch (name)
            {
                case "--dry-run" when !dryRun: dryRun = true; break;
                case "--no-guide" when !noGuide: noGuide = true; break;
                case "--json" when !json: json = true; break;
                case "--only": if (Value() is { } o) only.Add(o); else return null; break;
                case "--skip": if (Value() is { } s) skip.Add(s); else return null; break;
                case "--catalog" when catalog is null: catalog = Value(); if (string.IsNullOrWhiteSpace(catalog)) return null; break;
                case "--maintenance-from" when from is null: from = Value(); if (string.IsNullOrWhiteSpace(from)) return null; break;
                default: return null;
            }
        }
        catalog ??= PackOptions.OfficialCatalog;
        // A test option: an installation from a local catalogue, with a folder holding SHA256SUMS.txt.
        if (from is not null && (!Directory.Exists(catalog) || !File.Exists(Path.Combine(from, "SHA256SUMS.txt")))) return null;
        return new EngineCommand(EngineMode.Install, Pack: new PackOptions(only, skip, dryRun, noGuide, catalog, json, from));
    }
}
