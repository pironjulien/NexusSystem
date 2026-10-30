using System.Diagnostics;
using System.Text.Json;

namespace Nexus.Core;

/// <summary>One event of get-nexus.ps1 -Json (a "NEXUS_PACK_EVENT {json}" line).</summary>
public sealed record PackEvent(string Type, JsonElement Data)
{
    public string? Text(string name) => Data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public int? Number(string name) => Data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    public bool Flag(string name) => Data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>
/// Runs the one-line install's own script, embedded in the app at build time (pack/get-nexus.ps1, the released
/// copy), so that the app and get-nexus never diverge: same detection, same official installers, same checks.
/// </summary>
public static class PackRunner
{
    public const string EventPrefix = "NEXUS_PACK_EVENT ";

    public static PackEvent? ParseEvent(string line)
    {
        // Anywhere in the line: the console table writes its row label first, without a line break.
        var at = line.IndexOf(EventPrefix, StringComparison.Ordinal);
        if (at < 0) return null;
        try
        {
            using var document = JsonDocument.Parse(line[(at + EventPrefix.Length)..]);
            var root = document.RootElement.Clone();
            return root.TryGetProperty("type", out var type) && type.GetString() is { } name ? new PackEvent(name, root) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The embedded script, written to a fresh file in the user's temporary folder.</summary>
    public static string ExtractScript()
    {
        using var stream = typeof(PackRunner).Assembly.GetManifestResourceStream("get-nexus.ps1")
            ?? throw new InvalidOperationException("get-nexus.ps1 is not embedded in this build.");
        var file = Path.Combine(Path.GetTempPath(), $"nexus-app-{Environment.ProcessId}-{Guid.NewGuid():N}.ps1");
        using var output = File.Create(file);
        stream.CopyTo(output);
        return file;
    }

    /// <summary>
    /// Existing engines need the maintenance worker once after upgrading an older hub. Reading installation
    /// files here only selects the bootstrap; it never reports a running connection or executes those engines.
    /// </summary>
    public static bool NeedsMaintenanceBootstrap(UserFolders folders)
    {
        if (MaintenanceUpdates.IsInstalled(folders)) return false;
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

    /// <summary>The same embedded pack, either normal installation or maintenance bootstrap without product selection.</summary>
    public static ProcessStartInfo CreateCommand(string script, IEnumerable<string> only, bool dryRun, bool maintenanceOnly = false)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", script, "-Json", "-NoGuide" }) start.ArgumentList.Add(arg);
        if (maintenanceOnly) start.ArgumentList.Add("-InstallMaintenance");
        else
        {
            var keys = only.ToList();
            if (keys.Count > 0)
            {
                start.ArgumentList.Add("-Only");
                start.ArgumentList.Add(string.Join(",", keys));
            }
            // Store apps leave Claude Desktop's chat configuration to the .mcpb extensions.
            if (Packaging.IsPackaged)
            {
                start.ArgumentList.Add("-Skip");
                start.ArgumentList.Add("claude-desktop");
            }
        }
        if (dryRun) start.ArgumentList.Add("-DryRun");
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        // A PowerShell 7 parent's module path must not make Windows PowerShell load incompatible modules.
        start.Environment.Remove("PSModulePath");
        return start;
    }

    /// <summary>Runs the pack; maintenanceOnly installs its worker without installing or reconfiguring products.</summary>
    public static async Task<int> RunAsync(IEnumerable<string> only, bool dryRun, Action<string> onLine, Action<PackEvent> onEvent,
        CancellationToken cancel = default, bool maintenanceOnly = false)
    {
        var script = ExtractScript();
        try
        {
            var start = CreateCommand(script, only, dryRun, maintenanceOnly);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("powershell.exe did not start.");
            void Handle(string? line)
            {
                if (line is null) return;
                if (ParseEvent(line) is { } e) onEvent(e);
                else onLine(line);
            }
            process.OutputDataReceived += (_, a) => Handle(a.Data);
            process.ErrorDataReceived += (_, a) => Handle(a.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                await process.WaitForExitAsync(cancel).ConfigureAwait(false);
                process.WaitForExit();  // the last output lines are read before returning
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            File.Delete(script);
        }
    }

    /// <summary>Preserve callers that pass the cancellation token as the fifth argument.</summary>
    public static Task<int> RunAsync(IEnumerable<string> only, bool dryRun, Action<string> onLine, Action<PackEvent> onEvent,
        CancellationToken cancel) => RunAsync(only, dryRun, onLine, onEvent, maintenanceOnly: false, cancel);
}
