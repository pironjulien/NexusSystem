using System.Globalization;
using System.Text.Json;

namespace Nexus.Core;

/// <summary>
/// The common emergency stop (NexusWindows/commun/emergency-stop.md): writing ~\.nexus\stop halts every Nexus agent's
/// actions for two minutes, exactly like Ctrl+Alt+Shift+Esc. The apps only press it; the servers hold the stop.
/// </summary>
public static class EmergencyStop
{
    public const int HoldSeconds = 120;

    public static string StopFile(UserFolders folders) => Path.Combine(folders.Nexus, "stop");

    public static string OwnerFile(UserFolders folders) => Path.Combine(folders.Nexus, "stop.owner");

    public static void Press(UserFolders folders, string by = "nexus-app")
    {
        Directory.CreateDirectory(folders.Nexus);
        var stop = new Dictionary<string, object>
        {
            ["by"] = by,
            ["pid"] = Environment.ProcessId,
            ["at"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["hold_s"] = HoldSeconds,
            ["nonce"] = Guid.NewGuid().ToString("N"),
        };
        var temporary = StopFile(folders) + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stop));
        File.Move(temporary, StopFile(folders), overwrite: true);  // a new version each press: date and content differ
    }

    /// <summary>Seconds left in the current stop, 0 when none.</summary>
    public static int SecondsLeft(UserFolders folders)
    {
        var file = StopFile(folders);
        if (!File.Exists(file)) return 0;
        var age = (DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalSeconds;
        return age is >= 0 and < HoldSeconds ? (int)Math.Ceiling(HoldSeconds - age) : 0;
    }

    /// <summary>True when an agent holds the Ctrl+Alt+Shift+Esc shortcut (heartbeat younger than 9 s).</summary>
    public static bool ShortcutHeld(UserFolders folders)
    {
        var file = OwnerFile(folders);
        return File.Exists(file) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalSeconds < 9;
    }
}
