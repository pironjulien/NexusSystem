using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

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

    /// <summary>Scalar YAML front matter fields of a SKILL.md.</summary>
    public static Dictionary<string, string> FrontMatter(string relative) => FrontMatterOf(PathOf(relative));

    /// <summary>Front matter fields of a SKILL.md given by its full path.</summary>
    public static Dictionary<string, string> FrontMatterOf(string path)
    {
        var match = Regex.Match(File.ReadAllText(path), @"\A---\r?\n(.*?)\r?\n---\r?\n", RegexOptions.Singleline);
        Assert.True(match.Success, $"{path}: missing YAML front matter");
        var yaml = new YamlStream();
        yaml.Load(new StringReader(match.Groups[1].Value));
        var fields = Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode).Children;
        return fields.Where(field => field.Key is YamlScalarNode && field.Value is YamlScalarNode)
            .ToDictionary(field => ((YamlScalarNode)field.Key).Value!, field => ((YamlScalarNode)field.Value).Value ?? "");
    }

    /// <summary>Width and height of a PNG, or null when the file is not one.</summary>
    public static (int Width, int Height)? PngSize(string relative) => PngSizeOf(PathOf(relative));

    /// <summary>Width and height of a PNG given by its full path, or null when the file is not one.</summary>
    public static (int Width, int Height)? PngSizeOf(string path)
    {
        var head = File.ReadAllBytes(path).Take(24).ToArray();
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
