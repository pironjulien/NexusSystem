using System.Diagnostics;
using System.Text.Json;

namespace Nexus.Core;

public enum PhoneState
{
    /// <summary>ADB answers and a phone is ready.</summary>
    Connected,
    /// <summary>A phone is plugged in but has not accepted "Allow USB debugging?" yet.</summary>
    Unauthorized,
    /// <summary>ADB answers, no phone.</summary>
    None,
    /// <summary>No adb on this PC (NexusAndroid's install brings it).</summary>
    NoAdb,
}

public sealed record Phone(PhoneState State, string? Model);

/// <summary>NexusAGYmail's own sign-in state, as `NexusAGYmail.exe status` reports it (no secret is read).</summary>
public sealed record GmailSignIn(bool Connected, string? Account, string? Executable);

/// <summary>
/// What each product needs beyond its install, checked on this PC the way the product itself checks it, so that the app
/// says "connected" or offers the one button that connects, instead of showing a guide for something already done.
/// </summary>
public static class SetupChecks
{
    /// <summary>The phone as `adb devices -l` sees it (adb from PATH, or the platform tools winget installs).</summary>
    public static Phone ReadPhone(UserFolders folders)
    {
        if (Adb(folders) is not { } adb) return new Phone(PhoneState.NoAdb, null);
        if (Run(adb, ["devices", "-l"]) is not { } output) return new Phone(PhoneState.None, null);
        return ParseDevices(output);
    }

    /// <summary>The first phone of `adb devices -l`: ready, waiting for its authorization, or none.</summary>
    public static Phone ParseDevices(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("List of devices", StringComparison.Ordinal) && !line.StartsWith('*')).ToList();
        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[1] != "device") continue;
            var model = parts.FirstOrDefault(part => part.StartsWith("model:", StringComparison.Ordinal))?[6..].Replace('_', ' ');
            return new Phone(PhoneState.Connected, model);
        }
        return lines.Any(line => line.Contains(" unauthorized", StringComparison.Ordinal))
            ? new Phone(PhoneState.Unauthorized, null) : new Phone(PhoneState.None, null);
    }

    static string? Adb(UserFolders folders)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), "adb.exe");
            if (File.Exists(candidate)) return candidate;
        }
        var winget = Path.Combine(folders.LocalAppData, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(winget)) return null;
        return Directory.EnumerateDirectories(winget, "Google.PlatformTools*")
            .Select(package => Path.Combine(package, "platform-tools", "adb.exe")).FirstOrDefault(File.Exists);
    }

    /// <summary>NexusAGYmail's sign-in: connected (with its account) or not, and its executable for the button.</summary>
    public static GmailSignIn ReadGmail(UserFolders folders)
    {
        var executable = GmailExecutable(folders);
        if (executable is null || Run(executable, ["status"]) is not { } output) return new GmailSignIn(false, null, executable);
        return ParseGmail(output, executable);
    }

    public static GmailSignIn ParseGmail(string output, string? executable)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var present = root.TryGetProperty("present", out var p) && p.ValueKind == JsonValueKind.True;
            var reauth = root.TryGetProperty("scopes", out var scopes) && scopes.TryGetProperty("reauth_required", out var r) && r.ValueKind == JsonValueKind.True;
            var account = root.TryGetProperty("account", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            return new GmailSignIn(present && !reauth, account, executable);
        }
        catch (JsonException) { return new GmailSignIn(false, null, executable); }
    }

    /// <summary>The newest installed NexusAGYmail (~\.nexus\nexus-agymail\app\&lt;version&gt;\NexusAGYmail.exe).</summary>
    public static string? GmailExecutable(UserFolders folders)
    {
        var app = Path.Combine(folders.Nexus, "nexus-agymail", "app");
        if (!Directory.Exists(app)) return null;
        return Directory.EnumerateDirectories(app)
            .OrderByDescending(directory => Path.GetFileName(directory), Comparer<string>.Create(ProductState.CompareVersions))
            .Select(directory => Path.Combine(directory, "NexusAGYmail.exe")).FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Opens NexusAGYmail's guided Google sign-in in its own console window (it asks its questions and opens the
    /// browser there), as `get.ps1 -Auth` does.
    /// </summary>
    public static void StartGmailSignIn(string executable)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = true };
        start.ArgumentList.Add("auth");
        Process.Start(start)?.Dispose();
    }

    static string? Run(string executable, string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            return output.Result;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
