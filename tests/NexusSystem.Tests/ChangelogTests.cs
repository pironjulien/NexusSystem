using System.Text.RegularExpressions;

namespace NexusSystem.Tests;

/// <summary>CHANGELOG.md follows Keep a Changelog: one dated section per catalog version and per Store app version,
/// each with its link at the end of the file, and the app's current version has its section.</summary>
public partial class ChangelogTests
{
    static readonly string[] Lines = Repository.Text("CHANGELOG.md").ReplaceLineEndings("\n").Split('\n');

    [GeneratedRegex(@"^## \[(?<name>[^\]]+)\](?: - (?<date>\S+))?$")]
    private static partial Regex Section();

    [GeneratedRegex(@"^\[(?<name>[^\]]+)\]: https://github\.com/pironjulien/NexusSystem/\S+$")]
    private static partial Regex Link();

    static List<(string Name, string Date)> Sections() => Lines.Select(l => Section().Match(l)).Where(m => m.Success)
        .Select(m => (m.Groups["name"].Value, m.Groups["date"].Value)).ToList();

    [Fact]
    public void EverySectionIsAVersionWithADate()
    {
        var sections = Sections();
        Assert.Equal("Unreleased", sections[0].Name);
        foreach (var (name, date) in sections.Skip(1))
        {
            Assert.Matches(@"^(\d+\.\d+\.\d+|app \d+\.\d+\.\d+\.0)$", name);
            Assert.True(DateOnly.TryParseExact(date, "yyyy-MM-dd", out _), $"[{name}] has no YYYY-MM-DD date: '{date}'");
        }
        Assert.Equal(sections.Count, sections.Select(s => s.Name).Distinct().Count());
        // Every line starting with "## " is a section heading of that form (no stray level-2 heading).
        Assert.All(Lines.Where(l => l.StartsWith("## ", StringComparison.Ordinal)), l => Assert.Matches(Section(), l));
    }

    [Fact]
    public void AppChangesHaveTheirOwnSection()
    {
        // An app version is a section of its own, never a "### Changed (app ...)" subsection of a catalog version.
        var stray = Lines.Where(l => l.StartsWith("### ", StringComparison.Ordinal) && l.Contains('(')).ToList();
        Assert.True(stray.Count == 0, "Subsections with a qualifier: " + string.Join(" | ", stray));
        Assert.All(Lines.Where(l => l.StartsWith("### ", StringComparison.Ordinal)),
            l => Assert.Matches(@"^### (Added|Changed|Deprecated|Removed|Fixed|Security)$", l));
    }

    [Fact]
    public void EverySectionHasItsLink()
    {
        var links = Lines.Select(l => Link().Match(l)).Where(m => m.Success).Select(m => m.Groups["name"].Value).ToList();
        var names = Sections().Select(s => s.Name).ToList();
        Assert.Equal(names, links);
    }

    [Fact]
    public void TheAppVersionHasASection()
    {
        var props = System.Xml.Linq.XDocument.Load(Repository.PathOf("app/Directory.Build.props"));
        var version = props.Descendants("Version").Select(e => e.Value.Trim()).Single(v => v.Length > 0);
        Assert.Contains($"app {version}", Sections().Select(s => s.Name));
    }
}
