using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace NexusSystem.Tests;

/// <summary>
/// The AI apps' own validators, beside <see cref="CatalogTests"/>: `claude plugin validate` (Claude Code: the
/// marketplace, each plugin manifest, its skills' front matter, hooks and MCP declarations) and `agy plugin validate`
/// (Antigravity: plugin.json, MCP servers, hooks, skills and logo). A validator runs when its command line is on PATH
/// and is skipped, saying so, when it is not: nothing is installed for it. CI installs both (test.yml) and names them
/// in NEXUS_REQUIRED_VALIDATORS, where a missing one fails instead. Each run gets a throwaway profile and no key or
/// token: validation reads local files only, never an account.
/// </summary>
static class VendorValidator
{
    public const string RequiredVariable = "NEXUS_REQUIRED_VALIDATORS";

    /// <summary>Variables that would sign a command line in: never passed to a validator.</summary>
    static readonly string[] Credentials =
    [
        "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "GEMINI_API_KEY", "GOOGLE_API_KEY",
        "GOOGLE_APPLICATION_CREDENTIALS", "AGY_ADC_AUTH",
    ];

    /// <summary>The tool's executable (or its .cmd launcher) in the first folder of <paramref name="path"/> that has one.</summary>
    public static string? Find(string tool, string? path)
    {
        foreach (var folder in (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in new[] { ".exe", ".cmd" })
            {
                var candidate = Path.Combine(folder.Trim('"'), tool + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Whether this run must have the tool (a comma-separated list, CI's NEXUS_REQUIRED_VALIDATORS).</summary>
    public static bool Required(string tool, string? required) =>
        (required ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(tool, StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs the tool on a throwaway profile, without any credential variable, for two minutes at most.</summary>
    public static (int Code, string Output) Run(string executable, params string[] arguments)
    {
        var profile = Directory.CreateTempSubdirectory("nexus-validator-").FullName;
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = profile,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            foreach (var variable in Credentials) start.Environment.Remove(variable);
            foreach (var variable in new[] { "USERPROFILE", "HOME", "APPDATA", "LOCALAPPDATA", "CLAUDE_CONFIG_DIR" })
                start.Environment[variable] = profile;
            start.Environment["DISABLE_AUTOUPDATER"] = "1";
            start.Environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
            using var process = Process.Start(start)!;
            process.StandardInput.Close();  // nothing can wait for an answer
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                process.Kill(entireProcessTree: true);
                return (-1, $"{Path.GetFileName(executable)} did not finish within two minutes.");
            }
            process.WaitForExit();
            return (process.ExitCode, (output.Result + error.Result).Trim());
        }
        finally
        {
            try { Directory.Delete(profile, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* a throwaway folder of the temporary directory */ }
        }
    }
}

public class VendorValidatorTests
{
    const string ClaudeCatalog = ".claude-plugin/marketplace.json";
    static readonly string[] ClaudePlugins = ["nexus-android", "nexus-windows", "nexus-browser", "nexus-relay", "nexus-keypad", "nexus-free"];

    /// <summary>The products whose plugin folder carries an Antigravity plugin (`antigravity/`, installed by the product's installer).</summary>
    static readonly string[] AntigravityPlugins = ["nexus-android", "nexus-windows", "nexus-browser", "nexus-relay"];

    public static TheoryData<string> ClaudeNames => new(ClaudePlugins);

    public static TheoryData<string> AntigravityNames => new(AntigravityPlugins);

    static string Folder(string name) => PluginSources.Folder(
        Repository.Json(ClaudeCatalog)["plugins"]!.AsArray().Single(e => (string?)e!["name"] == name)!["source"]!);

    /// <summary>The validator's command line, or the test is skipped (failed where the run requires it).</summary>
    static string Tool(string tool)
    {
        var found = VendorValidator.Find(tool, Environment.GetEnvironmentVariable("PATH"));
        if (found is not null) return found;
        Assert.False(VendorValidator.Required(tool, Environment.GetEnvironmentVariable(VendorValidator.RequiredVariable)),
            $"{tool} is required by {VendorValidator.RequiredVariable} and is not on PATH.");
        Assert.Skip($"{tool} is not installed here: `{tool} plugin validate` runs in CI (test.yml).");
        return "";
    }

    static void AssertValid(string tool, string folder)
    {
        var (code, output) = VendorValidator.Run(Tool(tool), "plugin", "validate", folder);
        Assert.True(code == 0, $"{tool} plugin validate {folder} (exit code {code}):{Environment.NewLine}{output}");
    }

    [Fact]
    public void ClaudeValidatesTheMarketplace() => AssertValid("claude", Repository.Root);

    [Theory]
    [MemberData(nameof(ClaudeNames))]
    public void ClaudeValidatesThePlugin(string name) => AssertValid("claude", Folder(name));

    [Theory]
    [MemberData(nameof(AntigravityNames))]
    public void AntigravityValidatesThePlugin(string name) => AssertValid("agy", Path.Combine(Folder(name), "antigravity"));

    /// <summary>A green verdict means something only if the validator refuses a broken plugin on the same run.</summary>
    [Theory]
    [InlineData("claude", ".claude-plugin/plugin.json")]
    [InlineData("agy", "plugin.json")]
    public void TheValidatorRefusesABrokenManifest(string tool, string manifest)
    {
        var executable = Tool(tool);
        var folder = Directory.CreateTempSubdirectory("nexus-validator-broken-").FullName;
        try
        {
            var file = Path.Combine(folder, manifest.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "{ \"name\": ");
            var (code, output) = VendorValidator.Run(executable, "plugin", "validate", folder);
            Assert.True(code != 0, $"{tool} plugin validate accepted an unreadable {manifest}:{Environment.NewLine}{output}");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void TheValidatedPluginsAreTheCatalogs()
    {
        var names = Repository.Json(ClaudeCatalog)["plugins"]!.AsArray().Select(e => (string?)e!["name"]).ToArray();
        Assert.Equal(names, ClaudePlugins);
        // Every plugin folder that carries an Antigravity plugin is validated, and only those.
        Assert.Equal(names.Where(n => File.Exists(Path.Combine(Folder(n!), "antigravity", "plugin.json"))), AntigravityPlugins);
    }

    [Fact]
    public void FindsAnExecutableOrItsLauncherOnPath()
    {
        var folder = Directory.CreateTempSubdirectory("nexus-validator-path-").FullName;
        try
        {
            var empty = Path.Combine(folder, "empty");
            var bin = Path.Combine(folder, "bin");
            Directory.CreateDirectory(empty);
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "claude.cmd"), "@exit /b 0");
            var path = $"{Path.Combine(folder, "missing")};;\"{empty}\"; {bin} ";
            Assert.Equal(Path.Combine(bin, "claude.cmd"), VendorValidator.Find("claude", path));
            Assert.Null(VendorValidator.Find("agy", path));
            Assert.Null(VendorValidator.Find("claude", null));
            File.WriteAllText(Path.Combine(bin, "claude.exe"), "");
            Assert.Equal(Path.Combine(bin, "claude.exe"), VendorValidator.Find("claude", path));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData("claude", "claude,agy", true)]
    [InlineData("agy", " claude , AGY ", true)]
    [InlineData("agy", "claude", false)]
    [InlineData("claude", "", false)]
    [InlineData("claude", null, false)]
    public void AMissingValidatorFailsOnlyWhereItIsRequired(string tool, string? required, bool expected) =>
        Assert.Equal(expected, VendorValidator.Required(tool, required));

    [Fact]
    public void RunsOnAThrowawayProfileWithoutCredentials()
    {
        var folder = Directory.CreateTempSubdirectory("nexus-validator-run-").FullName;
        var before = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            var tool = Path.Combine(folder, "tool.cmd");
            File.WriteAllText(tool, "@echo profile=%USERPROFILE%\r\n@echo config=%CLAUDE_CONFIG_DIR%\r\n@echo key=[%ANTHROPIC_API_KEY%]\r\n@echo arguments=%*\r\n@echo refused 1>&2\r\n@exit /b 3\r\n");
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "not-a-real-key");
            var (code, output) = VendorValidator.Run(tool, "plugin", "validate", folder);
            Assert.Equal(3, code);
            Assert.Contains("key=[]", output);
            Assert.Contains("arguments=plugin validate " + folder, output);
            Assert.Contains("refused", output);
            var profile = output.Split('\n').Single(l => l.StartsWith("profile=", StringComparison.Ordinal))["profile=".Length..].Trim();
            Assert.StartsWith(Path.GetTempPath().TrimEnd('\\'), profile, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), profile);
            Assert.Contains("config=" + profile, output);
            Assert.False(Directory.Exists(profile), "the throwaway profile is removed after the run");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", before);
            Directory.Delete(folder, recursive: true);
        }
    }
}
