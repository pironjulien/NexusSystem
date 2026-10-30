using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Nexus.Core;

/// <summary>A completed observation, not proof that a cached plugin is loaded in a conversation.</summary>
public sealed record MaintenanceProfile(string Key, string State, string? PluginVersion, bool ActivationPending, string? Reason);

public sealed record MaintainedProduct(string Key, string? PublishedVersion, string? InstalledVersion,
    IReadOnlyList<string> LoadedVersions, string State, string? Message, IReadOnlyList<MaintenanceProfile> Profiles);

public sealed record MaintenanceState(string State, DateTimeOffset? CheckedAt, DateTimeOffset? NextCheckAt,
    bool ActivationPending, bool Retryable, IReadOnlyList<MaintainedProduct> Products)
{
    /// <summary>A missed maintenance cycle or a future timestamp must not look like a current observation.</summary>
    public bool IsFresh(DateTimeOffset now) => CheckedAt is { } at && at <= now && now - at <= TimeSpan.FromHours(2);
}

/// <summary>
/// The public NexusSystem maintenance worker owns installation and retries. The hub only reads its receipt and
/// asks Task Scheduler to run the already registered task; it never executes an installer or restarts an AI.
/// </summary>
public static class MaintenanceUpdates
{
    public const string TaskName = "NexusSystem Update";

    public static string ScriptPath(UserFolders folders) => Path.Combine(folders.Nexus, "nexussystem", "maintenance", "get-nexus.ps1");
    public static string StatusPath(UserFolders folders) => Path.Combine(folders.Nexus, "status", "nexussystem-update.json");
    public static bool IsInstalled(UserFolders folders) => File.Exists(ScriptPath(folders));
    public static bool Supports(Product product) => product == Products.Windows || product == Products.Browser;

    public static MaintenanceState? Read(UserFolders folders)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(StatusPath(folders)));
            return Parse(document.RootElement);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // A missing or partially replaced receipt is unknown, never up to date.
        }
    }

    static MaintenanceState? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) ||
            schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var number) || number != 1 ||
            Text(root, "state") is not { } state) return null;
        var products = new List<MaintainedProduct>();
        foreach (var product in Array(root, "products"))
        {
            if (Text(product, "key") is not { Length: > 0 } key) continue;
            var profiles = Array(product, "profiles").Where(profile => Text(profile, "key") is { Length: > 0 })
                .Select(profile => new MaintenanceProfile(Text(profile, "key")!, Text(profile, "state") ?? "unknown",
                    Text(profile, "pluginVersion"), Flag(profile, "activationPending"), Text(profile, "reason"))).ToList();
            var loaded = Array(product, "loadedVersions").Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToList();
            products.Add(new MaintainedProduct(key, Text(product, "publishedVersion"), Text(product, "installedVersion"),
                loaded, Text(product, "state") ?? "unknown", Text(product, "message"), profiles));
        }
        return new MaintenanceState(state, Date(root, "checked_at"), Date(root, "nextCheckAt"),
            Flag(root, "activationPending"), Flag(root, "retryable"), products);
    }

    static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    static bool Flag(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.True;
    static DateTimeOffset? Date(JsonElement value, string key) => DateTimeOffset.TryParse(Text(value, key), out var at) ? at : null;
    static List<JsonElement> Array(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(key, out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().ToList() : [];

    /// <summary>Only the fixed task is requested. No command from the status file is executable.</summary>
    public static ProcessStartInfo RequestCommand()
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/Run", "/TN", TaskName }) start.ArgumentList.Add(argument);
        return start;
    }

    public static async Task<UpdateRequest> RequestAsync(CancellationToken cancel = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            cancel.ThrowIfCancellationRequested();
            using var process = Process.Start(RequestCommand());
            if (process is null) return UpdateRequest.Unreachable;
            // Drain both pipes while Task Scheduler answers. Cancellation never terminates the maintenance task.
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            return process.ExitCode == 0 ? UpdateRequest.Started : UpdateRequest.Unreachable;
        }
        catch (Exception error) when (error is Win32Exception or IOException or OperationCanceledException)
        {
            return UpdateRequest.Unreachable;
        }
    }
}
