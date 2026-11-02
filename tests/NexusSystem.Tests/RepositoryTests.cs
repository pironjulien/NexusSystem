using YamlDotNet.Core;

namespace NexusSystem.Tests;

public class RepositoryTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void FrontMatterPreservesFoldedDescription(string newline)
    {
        var path = Path.GetTempFileName();
        try
        {
            var skill = """
                ---
                name: folded-skill
                description: >-
                  Create layouts: preserve "quoted" text # literally.
                  Keep the same instructions.
                metadata:
                  short-description: A nested field is not a top-level field.
                ---
                # Skill

                """;
            File.WriteAllText(path, skill.ReplaceLineEndings(newline));
            var fields = Repository.FrontMatterOf(path);
            Assert.Equal("folded-skill", fields["name"]);
            Assert.Equal("Create layouts: preserve \"quoted\" text # literally. Keep the same instructions.", fields["description"]);
            Assert.False(fields.ContainsKey("short-description"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FrontMatterRejectsMalformedYaml()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "---\nname: invalid-skill\ndescription: Unquoted : colon\n---\n");
            Assert.ThrowsAny<YamlException>(() => Repository.FrontMatterOf(path));
        }
        finally { File.Delete(path); }
    }
}
