using System.Diagnostics;

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
