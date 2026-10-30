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

    /// <summary>Runs the pack for these product and AI keys; every output line goes to onLine, every event to onEvent.</summary>
    public static async Task<int> RunAsync(IEnumerable<string> only, bool dryRun, Action<string> onLine, Action<PackEvent> onEvent,
        CancellationToken cancel = default)
    {
        var script = ExtractScript();
        try
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
            var keys = only.ToList();
            if (keys.Count > 0)
            {
                start.ArgumentList.Add("-Only");
                start.ArgumentList.Add(string.Join(",", keys));
            }
            // From the Store package an edit of Claude Desktop's existing configuration would stay in the package:
            // Claude Desktop gets the .mcpb extensions instead (DesktopExtension).
            if (Packaging.IsPackaged)
            {
                start.ArgumentList.Add("-Skip");
                start.ArgumentList.Add("claude-desktop");
            }
            if (dryRun) start.ArgumentList.Add("-DryRun");
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            // Started from a PowerShell 7 terminal, the app inherits its module path, on which Windows PowerShell 5.1
            // loads PowerShell 7's modules and fails: removed, each PowerShell computes its own.
            start.Environment.Remove("PSModulePath");

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
}
