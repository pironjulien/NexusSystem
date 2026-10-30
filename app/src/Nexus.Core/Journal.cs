using System.Globalization;
using System.Text.Json;

namespace Nexus.Core;

/// <summary>One line of the common Nexus journal (NexusShared/journal/FORMAT.md, v1): what an AI did, never screen text.</summary>
public sealed record JournalEntry(
    DateTimeOffset At,
    string Agent,
    string? App,
    string? Action,
    string? Outcome,
    int? Milliseconds,
    bool Foreground,
    string? Error);

/// <summary>Reads %USERPROFILE%\.nexus\journal\&lt;product&gt;\&lt;agent&gt;\&lt;yyyy-MM&gt;.jsonl, newest first.</summary>
public static class Journal
{
    public static readonly IReadOnlyList<string> Agents = ["claude", "codex", "antigravity"];

    public static IReadOnlyList<JournalEntry> Read(Product product, UserFolders folders, DateTimeOffset since, int limit = 500)
    {
        if (product.JournalFolder is null) return [];
        var entries = new List<JournalEntry>();
        foreach (var agent in Agents)
        {
            var folder = Path.Combine(folders.Journal, product.JournalFolder, agent);
            if (!Directory.Exists(folder)) continue;
            for (var month = new DateTime(since.Year, since.Month, 1); month <= DateTime.UtcNow; month = month.AddMonths(1))
            {
                var file = Path.Combine(folder, month.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".jsonl");
                if (!File.Exists(file)) continue;
                foreach (var line in ReadShared(file))
                {
                    if (Parse(line) is { } entry && entry.At >= since) entries.Add(entry);
                }
            }
        }
        return entries.OrderByDescending(e => e.At).Take(limit).ToList();
    }

    /// <summary>Summary of a list of entries: count, verified share, foreground uses.</summary>
    public static (int Count, int Verified, int Failed, int Foreground) Summarize(IReadOnlyList<JournalEntry> entries) => (
        entries.Count,
        entries.Count(e => e.Outcome is "✓" or "="),
        entries.Count(e => e.Outcome is "✗"),
        entries.Count(e => e.Foreground));

    public static JournalEntry? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("ts", out var ts) || !DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                return null;
            return new JournalEntry(
                at,
                Text(root, "agent") ?? "?",
                Text(root, "app"),
                Text(root, "action"),
                Text(root, "issue"),
                root.TryGetProperty("ms", out var ms) && ms.ValueKind == JsonValueKind.Number ? ms.GetInt32() : null,
                root.TryGetProperty("premier_plan", out var fg) && fg.ValueKind == JsonValueKind.True,
                Text(root, "erreur"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // The servers keep appending while the app reads.
    static IEnumerable<string> ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0) yield return line;
        }
    }
}
