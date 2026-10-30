using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexusSystem.Tests;

/// <summary>The repository under test (its folder is written into the assembly at build time).</summary>
static class Repository
{
    public static readonly string Root = typeof(Repository).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "NexusSystem.Repository").Value!;

    public static string PathOf(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public static string Text(string relative) => File.ReadAllText(PathOf(relative));

    public static JsonNode Json(string relative) => JsonNode.Parse(Text(relative))
        ?? throw new InvalidDataException($"{relative}: empty JSON");

    /// <summary>Front matter fields of a SKILL.md (name: value lines).</summary>
    public static Dictionary<string, string> FrontMatter(string relative)
    {
        var match = Regex.Match(Text(relative), @"\A---\r?\n(.*?)\r?\n---\r?\n", RegexOptions.Singleline);
        Assert.True(match.Success, $"{relative}: missing YAML front matter");
        return match.Groups[1].Value.Split('\n')
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim().Trim('"'));
    }

    /// <summary>Width and height of a PNG, or null when the file is not one.</summary>
    public static (int Width, int Height)? PngSize(string relative)
    {
        var head = File.ReadAllBytes(PathOf(relative)).Take(24).ToArray();
        if (head.Length < 24 || !head.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return null;
        static int BigEndian(byte[] b, int at) => b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3];
        return (BigEndian(head, 16), BigEndian(head, 20));
    }

    /// <summary>Files Git publishes (tracked, or new and not ignored), relative with forward slashes.</summary>
    public static IReadOnlyList<string> TrackedFiles()
    {
        var start = new System.Diagnostics.ProcessStartInfo("git", "ls-files -z --cached --others --exclude-standard")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var git = System.Diagnostics.Process.Start(start)!;
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        Assert.Equal(0, git.ExitCode);
        // Deleted but not yet committed files are still listed by ls-files: only what exists counts.
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(f => File.Exists(PathOf(f))).ToList();
    }
}
