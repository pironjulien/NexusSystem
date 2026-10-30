using System.Text.Json;

namespace Nexus.Core;

/// <summary>
/// This PC's own choices for NexusSystem, outside the code and outside the app's virtualized AppData:
/// %USERPROFILE%\.nexus\nexussystem.json, for example {"skip": ["claude-desktop"]}. "skip" takes the names of
/// get-nexus.ps1's -Skip (products: windows, android, browser, relay, agymail, keypad; AI apps: claude-code,
/// claude-desktop, codex, antigravity). The app never offers what is skipped there, and get-nexus.ps1 adds them to its
/// -Skip. A missing or unreadable file means no choice.
/// </summary>
public sealed record LocalSettings(IReadOnlyList<string> Skip)
{
    public static readonly LocalSettings None = new([]);

    public static string FilePath(UserFolders folders) => Path.Combine(folders.Nexus, "nexussystem.json");

    public bool Skips(string name) => Skip.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static LocalSettings Read(UserFolders folders)
    {
        try
        {
            var file = FilePath(folders);
            if (!File.Exists(file)) return None;
            using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("skip", out var skip) ||
                skip.ValueKind != JsonValueKind.Array) return None;
            return new(skip.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim().ToLowerInvariant())
                .Where(s => s.Length > 0).Distinct().ToList());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return None;
        }
    }
}
