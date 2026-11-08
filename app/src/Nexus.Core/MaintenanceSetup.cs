using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>The engine the "NexusSystem Update" task starts, with its arguments.</summary>
public sealed record MaintenanceEngine(string Path, string Arguments, bool Development);

/// <summary>An engine file and the SHA-256 it must have (its release's SHA256SUMS.txt line).</summary>
public sealed record EngineFile(string Path, string Sha256);

/// <summary>
/// Installs the hourly maintenance: the native engine of a NexusSystem release at its durable path
/// (~\.nexus\nexussystem\maintenance\NexusMaintenance.exe, a running engine renamed aside and never stopped, the
/// previous one restored when the replacement fails), then the "NexusSystem Update" task that starts it directly,
/// through the Task Scheduler API. The engine itself (--install, --install-maintenance, --maintain) and the app use it;
/// get-nexus.ps1 only fetches and checks the engine. Each outside effect is virtual so tests run on a throwaway profile.
/// </summary>
public partial class MaintenanceSetup(UserFolders folders)
{
    public const string TaskDescription = "Verified Nexus releases; installed products and existing active AI routes only. Sessions are preserved.";
    const string Owner = "pironjulien";

    public UserFolders Folders { get; } = folders;

    /// <summary>The catalog release this build belongs to: get-nexus.ps1's $NexusMaintenanceVersion, set at build time.</summary>
    public static string CatalogVersion { get; } = typeof(MaintenanceSetup).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "NexusCatalogVersion")?.Value ?? throw new InvalidOperationException("This build carries no catalog version.");

    public virtual ScheduledTaskState? ReadTask() => ScheduledTasks.Read(MaintenanceUpdates.TaskName);

    public virtual void RegisterTask(string xml) => ScheduledTasks.Register(MaintenanceUpdates.TaskName, xml);

    public virtual void StartTask() => ScheduledTasks.Run(MaintenanceUpdates.TaskName);

    /// <summary>Runs an engine without a window and waits for its verdict.</summary>
    public virtual int RunEngine(string path, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{path} did not start.");
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>The last step of a replacement: the checked copy takes the engine's path.</summary>
    protected virtual void MoveIntoPlace(string temporary, string destination) => File.Move(temporary, destination);

    /// <summary>
    /// The engine the task starts; null when automatic updates are turned off in nexussystem.json. On the author's
    /// workstation, developmentCoordinator names an engine built from the repository (app/tools/maintenance-dev.ps1):
    /// it is qualified with --current and no public engine is installed. Otherwise the checked engine file becomes the
    /// durable one; with a test release (the pack check), the task names that release's catalogue and engine folder.
    /// </summary>
    public MaintenanceEngine? PrepareEngine(EngineFile source, TestRelease? test)
    {
        if (JsonFiles.Read(LocalSettings.FilePath(Folders)) is { ValueKind: JsonValueKind.Object } choices)
        {
            if (Property(choices, "autoUpdate") is { ValueKind: JsonValueKind.False }) return null;
            if (Property(choices, "developmentCoordinator") is { } selected)
            {
                if (selected.ValueKind != JsonValueKind.String || selected.GetString() is not { } path || !LocalPath().IsMatch(path) ||
                    !Path.GetFileName(path).Equals(MaintenanceServices.EngineName, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    throw new InvalidOperationException("developmentCoordinator must name an existing local absolute NexusMaintenance.exe built from the repository; no public engine was installed.");
                return new MaintenanceEngine(Path.GetFullPath(path), "--current", true);
            }
        }
        var durable = MaintenanceUpdates.ExecutablePath(Folders);
        InstallEngineFile(source, durable);
        var arguments = test is null ? "" : $"--test-catalog \"{Folder(test.Catalog)}\" --test-engines \"{Folder(test.Engines)}\"";
        return new MaintenanceEngine(durable, arguments, false);
    }

    static string Folder(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    static JsonElement? Property(JsonElement value, string name) =>
        value.EnumerateObject().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => (JsonElement?)p.Value).FirstOrDefault();

    [GeneratedRegex(@"^[a-zA-Z]:[\\/]")]
    private static partial Regex LocalPath();

    /// <summary>
    /// The checked engine at its durable path. Nothing is written when it is already there. A running engine is renamed
    /// aside (NexusMaintenance.previous.exe, or a unique name while an older one still runs), never stopped; if the new
    /// file cannot take its place, the previous one is put back. Engines set aside are removed once nothing runs them.
    /// </summary>
    public void InstallEngineFile(EngineFile source, string durable)
    {
        if (!MaintenanceServices.Sha256(source.Path).Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 mismatch for {Path.GetFileName(source.Path)}: it is not the published engine.");
        var folder = Path.GetDirectoryName(durable)!;
        Directory.CreateDirectory(folder);
        if (File.Exists(durable) && MaintenanceServices.Sha256(durable).Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            RemoveSetAside(folder);
            return;
        }
        if (Path.GetFullPath(source.Path).Equals(Path.GetFullPath(durable), StringComparison.OrdinalIgnoreCase)) return;
        var temporary = $"{durable}.{Environment.ProcessId}.tmp";
        try
        {
            File.Copy(source.Path, temporary, overwrite: true);
            if (!MaintenanceServices.Sha256(temporary).Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The engine copy differs from the checked file; nothing was replaced.");
            string? aside = null;
            if (File.Exists(durable)) aside = SetAside(durable);
            try { MoveIntoPlace(temporary, durable); }
            catch when (aside is not null && !File.Exists(durable))
            {
                File.Move(aside, durable); // restored: the task keeps its working engine
                throw;
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        RemoveSetAside(folder);
    }

    static string SetAside(string durable)
    {
        var folder = Path.GetDirectoryName(durable)!;
        var previous = Path.Combine(folder, "NexusMaintenance.previous.exe");
        try { File.Delete(previous); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The previous engine still runs: this one steps aside under its own name.
            previous = Path.Combine(folder, $"NexusMaintenance.previous-{Guid.NewGuid():N}.exe");
        }
        File.Move(durable, previous);
        return previous;
    }

    /// <summary>Removes engines set aside under a unique name; Windows refuses to delete one that still runs.</summary>
    static void RemoveSetAside(string folder)
    {
        foreach (var old in Directory.EnumerateFiles(folder, "NexusMaintenance.previous-*.exe"))
        {
            try { File.Delete(old); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* still running: next time */ }
        }
    }

    /// <summary>
    /// Registers the task that starts the engine directly, at sign-in and every hour, one at a time. A task its owner
    /// disabled stays disabled. The PowerShell worker of releases up to 1.6.54 is removed. Started only when asked,
    /// enabled and not already running.
    /// </summary>
    public void Register(MaintenanceEngine engine, bool start)
    {
        var previous = ReadTask();
        var enabled = previous?.Enabled ?? true;
        RegisterTask(ScheduledTasks.HourlyTaskXml(TaskDescription, engine.Path, engine.Arguments, enabled, DateTimeOffset.Now));
        var legacy = MaintenanceUpdates.ScriptPath(Folders);
        if (File.Exists(legacy)) File.Delete(legacy);
        if (start && enabled && previous?.Running != true) StartTask();
    }

    /// <summary>The engine, then its task; null when automatic updates are turned off.</summary>
    public MaintenanceEngine? Install(EngineFile source, TestRelease? test, bool start)
    {
        var engine = PrepareEngine(source, test);
        if (engine is not null) Register(engine, start);
        return engine;
    }

    /// <summary>
    /// The hand-over (get-nexus.ps1 -Maintain, also how the PowerShell worker of releases up to 1.6.54 hands over): this
    /// release's engine runs the cycle as current, then the task is pointed at it, so an interrupted hand-over is simply
    /// completed by the next run.
    /// </summary>
    public int Handover(EngineFile source, bool force)
    {
        var engine = PrepareEngine(source, null);
        if (engine is null) return 0;
        List<string> arguments = ["--current"];
        if (force) arguments.Add("--force");
        var code = RunEngine(engine.Path, arguments);
        Register(engine, start: false);
        return code;
    }

    /// <summary>
    /// The last check as JSON, read only: a missing or foreign receipt is "pending", and one older than two hourly
    /// cycles (or dated in the future) is "stale", never a live health claim.
    /// </summary>
    public static string Status(UserFolders folders, DateTimeOffset now)
    {
        JsonObject? state = null;
        try { state = File.Exists(MaintenanceUpdates.StatusPath(folders)) ? JsonNode.Parse(File.ReadAllText(MaintenanceUpdates.StatusPath(folders))) as JsonObject : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { /* unknown: pending */ }
        if (state is null || state["schema"] is not JsonValue schema || !schema.TryGetValue<int>(out var number) || number != 1)
        {
            state = new JsonObject
            {
                ["schema"] = 1, ["state"] = "pending", ["checked_at"] = null, ["latest"] = null, ["catalogue"] = null,
                ["products"] = new JsonArray(), ["message"] = "Maintenance has not completed a check.",
            };
        }
        else if (state["checked_at"] is not JsonValue at || !at.TryGetValue<string>(out var text) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var checkedAt) || checkedAt > now || now - checkedAt > TimeSpan.FromHours(2))
        {
            state["state"] = "stale";
            state["message"] = "The last maintenance check is older than two hourly cycles.";
        }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            state.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// An existing Store installation may predate the maintenance task: existing engines need it once. Reading the
    /// installation files only selects the bootstrap; it never reports a running connection or executes those engines.
    /// </summary>
    public static bool NeedsBootstrap(UserFolders folders, bool? taskRegistered = null)
    {
        if (MaintenanceUpdates.IsInstalled(folders) && (taskRegistered ?? MaintenanceUpdates.IsRegistered())) return false;
        try
        {
            var choices = LocalSettings.FilePath(folders);
            if (File.Exists(choices))
            {
                using var settings = JsonDocument.Parse(File.ReadAllText(choices), new JsonDocumentOptions
                    { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (settings.RootElement.ValueKind != JsonValueKind.Object) return false;
                if (settings.RootElement.EnumerateObject().Any(property => property.Name.Equals("autoUpdate", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.False)) return false;
            }
            if (WindowsRuntime.ReadInstallation(folders) is not null) return true;
            var browser = Path.Combine(folders.LocalAppData, "NexusBrowser");
            using var installation = JsonDocument.Parse(File.ReadAllText(Path.Combine(browser, "current.json")));
            var root = installation.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("exe", out var executable) ||
                executable.ValueKind != JsonValueKind.String || executable.GetString() is not { } path ||
                !Path.IsPathFullyQualified(path) || !Path.GetFileName(path).Equals("NexusBrowser.exe", StringComparison.OrdinalIgnoreCase)) return false;
            return Path.GetFullPath(path).StartsWith(Path.GetFullPath(browser) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return false; // Unreadable choices or no existing engine: do not infer permission for a bootstrap.
        }
    }

    static readonly Lazy<HttpClient> Http = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusSystem-setup");
        return http;
    });

    /// <summary>
    /// The engine of this build's own catalog release for this PC's architecture, downloaded next to its durable path
    /// (never under %LOCALAPPDATA%, which an MSIX app such as this one sees virtualized), checked against that
    /// release's SHA256SUMS.txt and, when the official gh tool is signed in, its build provenance.
    /// </summary>
    public virtual async Task<EngineFile> DownloadEngineAsync(CancellationToken cancel)
    {
        var tag = "v" + CatalogVersion;
        var name = MaintenanceServices.PublishedEngineName;
        var release = $"https://github.com/{Owner}/NexusSystem/releases/download/{tag}";
        var manifest = await Http.Value.GetStringAsync(new Uri($"{release}/SHA256SUMS.txt"), cancel).ConfigureAwait(false);
        var expected = ReleaseRules.ExpectedSha256(manifest, name);
        var bytes = await Http.Value.GetByteArrayAsync(new Uri($"{release}/{name}"), cancel).ConfigureAwait(false);
        if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 mismatch for {name} of NexusSystem {tag}: the download is not the published file.");
        var folder = MaintenanceUpdates.Root(Folders);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"NexusMaintenance.download-{Environment.ProcessId}.tmp");
        await File.WriteAllBytesAsync(file, bytes, cancel).ConfigureAwait(false);
        VerifyProvenance(file, tag);
        return new EngineFile(file, expected);
    }

    /// <summary>
    /// Build provenance through gh, when it is installed: exit code 4 means gh is not signed in and cannot read
    /// attestations (the SHA-256 is already verified); any other failure is fatal.
    /// </summary>
    public virtual void VerifyProvenance(string file, string tag)
    {
        if (AiApps.OnPath("gh") is not { } gh || !gh.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
        var run = MaintenanceServices.Run(gh, ["attestation", "verify", file, "--repo", $"{Owner}/NexusSystem", "--source-ref", $"refs/tags/{tag}"]);
        if (run.Code is not (0 or 4)) throw new InvalidDataException($"Build provenance failed for NexusSystem {tag} ({MaintenanceServices.Explain(run)}).");
    }

    /// <summary>The app's start: an existing installation without the maintenance task gets it, from this build's release.</summary>
    public async Task<int> BootstrapAsync(CancellationToken cancel = default)
    {
        if (!NeedsBootstrap(Folders)) return 0;
        var download = await DownloadEngineAsync(cancel).ConfigureAwait(false);
        try
        {
            Install(download, null, start: true);
            return 0;
        }
        finally
        {
            if (File.Exists(download.Path)) File.Delete(download.Path);
        }
    }

    /// <summary>The running engine as its own source, checked against a test release's SHA256SUMS.txt when there is one.</summary>
    public static EngineFile Self(TestRelease? test)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("The engine's own file is unknown.");
        var own = MaintenanceServices.Sha256(path);
        if (test is not null)
        {
            var name = MaintenanceServices.PublishedEngineName;
            var expected = ReleaseRules.ExpectedSha256(File.ReadAllText(Path.Combine(test.Engines, "SHA256SUMS.txt"), Encoding.UTF8), name);
            if (!expected.Equals(own, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"SHA-256 mismatch for {name} in {test.Engines}.");
        }
        return new EngineFile(path, own);
    }
}
