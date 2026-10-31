using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexusSystem.Tests;

/// <summary>
/// Where a catalog entry's plugin lives. A relative source is a folder of this repository. A git-subdir source is the
/// `plugin` folder of a product's public repository at an exact release tag: it is fetched once (sparse, shallow) into
/// the build cache, so the same checks apply to what the AI apps really install.
/// </summary>
static class PluginSources
{
    static readonly Regex Tag = new(@"^v\d+\.\d+\.\d+$");
    static readonly Regex Repo = new(@"^(?:https://github\.com/)?pironjulien/(Nexus[A-Za-z]+?)(?:\.git)?$");
    static readonly string Cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "build", "NexusSystem", "plugin-sources");

    /// <summary>A published plugin is pinned: a product repository, its `plugin` folder, an exact release tag, and the
    /// entry's version (when it has one) equal to that tag.</summary>
    public static void AssertPinned(JsonNode entry)
    {
        var name = (string?)entry["name"];
        var source = entry["source"]!;
        Assert.True(source is JsonObject, $"{name}: a published plugin comes from its product repository (git-subdir), not a copy here");
        Assert.Equal("git-subdir", (string?)source["source"]);
        Assert.Matches(Repo, (string?)source["url"] ?? "");
        Assert.Equal("plugin", (string?)source["path"]);
        Assert.Matches(Tag, (string?)source["ref"] ?? "");
        if (entry["version"] is { } version) Assert.Equal(((string?)source["ref"])![1..], (string?)version);
    }

    /// <summary>Product repository and tag, the same whatever URL form the catalog uses.</summary>
    public static string Describe(JsonNode source) => source is JsonObject
        ? $"{Repo.Match((string?)source["url"] ?? "").Groups[1].Value}@{(string?)source["ref"]}"
        : (string?)source ?? "";

    public static string Folder(JsonNode source)
    {
        if (source is not JsonObject)
            return Repository.PathOf((string?)source ?? throw new InvalidDataException("empty source"));
        if ((string?)source["source"] == "local")  // Codex's form of a relative source
            return Repository.PathOf((string?)source["path"] ?? throw new InvalidDataException("empty path"));
        var product = Repo.Match((string?)source["url"] ?? "").Groups[1].Value;
        var tag = (string?)source["ref"] ?? "";
        var path = (string?)source["path"] ?? "";
        Assert.False(string.IsNullOrEmpty(product), $"unexpected repository {(string?)source["url"]}");
        Assert.Matches(Tag, tag);
        var checkout = Path.Combine(Cache, $"{product}-{tag}");
        var folder = Path.Combine(checkout, path);
        lock (typeof(PluginSources))
        {
            if (!Directory.Exists(folder))
            {
                if (Directory.Exists(checkout)) Directory.Delete(checkout, true);
                Git(null, "clone", "--quiet", "--depth", "1", "--branch", tag, "--filter=blob:none", "--sparse",
                    $"https://github.com/pironjulien/{product}.git", checkout);
                Git(checkout, "sparse-checkout", "set", path);
            }
        }
        Assert.True(Directory.Exists(folder), $"{product} {tag}: no {path} folder");
        return folder;
    }

    static void Git(string? directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        if (directory is not null) start.WorkingDirectory = directory;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var git = Process.Start(start)!;
        var error = git.StandardError.ReadToEnd();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
    }
}
