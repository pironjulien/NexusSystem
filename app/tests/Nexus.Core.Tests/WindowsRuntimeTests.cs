using System.Security.Cryptography;
using System.Text.Json;
using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

public class WindowsRuntimeTests
{
    static readonly string Hash = new('a', 64);

    static (string Manifest, string Executable) Install(FakeProfile profile, string? executable = null, int schema = 2)
    {
        var artifacts = new Dictionary<string, string>();
        foreach (var name in new[] { "NexusWindows.exe", "NexusWindows.dll", "NexusWindows.deps.json", "NexusWindows.runtimeconfig.json" })
        {
            var file = profile.Write($".nexus\\nexus-windows\\native\\{Hash}\\{name}", "fixture:" + name);
            artifacts[name] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }
        var expected = Path.Combine(profile.Folders.Nexus, "nexus-windows", "native", Hash, "NexusWindows.exe");
        var manifest = profile.Write(@".nexus\nexus-windows\installation.json", JsonSerializer.Serialize(new
        {
            Schema = schema, Version = "3.8.0", NativeHash = Hash, NativeExecutable = executable ?? expected, NativeArtifacts = artifacts,
        }));
        return (manifest, expected);
    }

    [Fact]
    public void OneVerifiedInstallationDoesNotRequireLegacyEditionState()
    {
        using var profile = new FakeProfile();
        var (_, executable) = Install(profile);
        var installation = Assert.IsType<WindowsInstallation>(WindowsRuntime.ReadInstallation(profile.Folders));
        Assert.Equal("3.8.0", installation.Version);
        Assert.Equal(executable, installation.ExecutablePath);
        Assert.True(ProductUpdates.IsInstalled(Products.Windows, [], profile.Folders));
        Assert.Empty(ProductUpdates.Read(Products.Windows, profile.Folders));
    }

    [Theory]
    [InlineData("NexusWindows.exe")]
    [InlineData("NexusWindows.dll")]
    [InlineData("NexusWindows.runtimeconfig.json")]
    public void ChangedOrMissingArtifactsInvalidateInstalledVersion(string name)
    {
        using var profile = new FakeProfile();
        var (_, executable) = Install(profile);
        var file = Path.Combine(Path.GetDirectoryName(executable)!, name);
        File.AppendAllText(file, "changed");
        Assert.Null(WindowsRuntime.ReadInstallation(profile.Folders));
        File.Delete(file);
        Assert.Null(WindowsRuntime.ReadInstallation(profile.Folders));
    }

    [Fact]
    public void ForeignPathOldSchemaAndPartialManifestAreNotInstallationProof()
    {
        using var profile = new FakeProfile();
        Install(profile, Path.Combine(profile.Root, "other", "NexusWindows.exe"));
        Assert.Null(WindowsRuntime.ReadInstallation(profile.Folders));
        var (manifest, _) = Install(profile, schema: 1);
        Assert.Null(WindowsRuntime.ReadInstallation(profile.Folders));
        File.WriteAllText(manifest, "{");
        Assert.Null(WindowsRuntime.ReadInstallation(profile.Folders));
        File.WriteAllText(manifest, "[]");
        Assert.Null(WindowsRuntime.ReadInstallation(profile.Folders));
    }

    [Theory]
    [InlineData("--mcp --agent codex", true)]
    [InlineData("--mcp --agent claude", true)]
    [InlineData("--mcp --agent antigravity", true)]
    [InlineData("--serve", false)]
    [InlineData("--selftest --mcp --agent codex", false)]
    [InlineData("--mcp --agent codex --selftest", false)]
    [InlineData("--mcp --agent unknown", false)]
    [InlineData("--mcp", false)]
    [InlineData("--stop-guard 3.8.0", false)]
    [InlineData("--tool windows --mcp --agent codex", false)]
    [InlineData("--mcp --agent codex --serve", false)]
    public void OnlyAnExactSharedMcpClientCounts(string arguments, bool expected)
    {
        var table = new Dictionary<int, (int, string, string)>
        {
            [1] = (0, "NexusWindows.exe", $"\"C:\\Local Programs\\NexusWindows.exe\" {arguments}"),
        };
        Assert.Equal(expected ? 1 : 0, Servers.Match(table).Count);
    }

    [Fact]
    public void CurrentAndPreviousConnectionsAreIndependentOfCoreInstallation()
    {
        using var profile = new FakeProfile();
        var (_, executable) = Install(profile);
        var installation = WindowsRuntime.ReadInstallation(profile.Folders);
        var table = new Dictionary<int, (int, string, string)>
        {
            [10] = (0, "language_server_windows_x64.exe", "language_server_windows_x64.exe"),
            [11] = (0, "Codex.exe", "Codex.exe"),
            [1] = (10, "NexusWindows.exe", $"\"{executable}\" --mcp --agent antigravity"),
            [2] = (11, "NexusWindows.exe", "C:\\previous\\NexusWindows.exe --mcp --agent codex"),
        };
        var running = Servers.Match(table);
        Assert.Equal(WindowsConnection.Common, WindowsRuntime.Connection(AiApp.Antigravity, running, installation));
        Assert.Equal(WindowsConnection.Previous, WindowsRuntime.Connection(AiApp.Codex, running, installation));
        Assert.Equal(WindowsConnection.Idle, WindowsRuntime.Connection(AiApp.ClaudeCode, running, installation));
        Assert.Equal(WindowsConnection.Previous, WindowsRuntime.Connection(AiApp.Antigravity, running, null));
        Assert.Equal(WindowsConnection.Previous, WindowsRuntime.Connection(AiApp.Antigravity,
            running.Append(new RunningServer(Products.Windows, 3, AiApp.Antigravity)), installation));
    }

    [Fact]
    public void SharedCoreIsNotHiddenByALegacyWrapper()
    {
        var running = Servers.Match(new Dictionary<int, (int, string, string)>
        {
            [10] = (0, "Codex.exe", "Codex.exe"),
            [1] = (10, "cmd.exe", "cmd /c nexus-windows-codex"),
            [2] = (1, "NexusWindows.exe", "C:\\common\\NexusWindows.exe --mcp --agent codex"),
        });
        var server = Assert.Single(running);
        Assert.True(server.CommonWindowsCore);
        Assert.Equal(2, server.ProcessId);
        Assert.Null(server.StartedBy); // A wrapper is not the direct, configured common-core host.
    }

    [Fact]
    public void QualificationAndMismatchedProfilesAreNotAClientConnection()
    {
        var running = Servers.Match(new Dictionary<int, (int, string, string)>
        {
            [10] = (0, "pwsh.exe", "pwsh.exe"),
            [11] = (0, "Codex.exe", "Codex.exe"),
            [12] = (11, "codex.exe", "codex.exe exec --json qualification"),
            [13] = (11, "python.exe", "python.exe qualification.py"),
            [1] = (10, "NexusWindows.exe", "C:\\common\\NexusWindows.exe --mcp --agent antigravity"),
            [2] = (11, "NexusWindows.exe", "C:\\common\\NexusWindows.exe --mcp --agent claude"),
            [3] = (12, "NexusWindows.exe", "C:\\common\\NexusWindows.exe --mcp --agent codex"),
            [4] = (13, "NexusWindows.exe", "C:\\common\\NexusWindows.exe --mcp --agent codex"),
        });
        Assert.Equal(4, running.Count);
        Assert.All(running, server => Assert.Null(server.StartedBy));
        Assert.All(AiApps.All, app => Assert.Equal(WindowsConnection.Idle, WindowsRuntime.Connection(app, running, null)));
    }

    [Fact]
    public void ClaudeCodePluginNeverProvesDesktopIsConfigured()
    {
        using var profile = new FakeProfile(@"AppData\Roaming\Claude");
        Install(profile);
        profile.Write(@".claude\settings.json", """{"enabledPlugins":{"nexus-windows@catalog":true}}""");
        Assert.Equal(DeclarationState.Declared, Declarations.State(Products.Windows, AiApp.ClaudeCode, profile.Folders));
        Assert.Equal(DeclarationState.Missing, WindowsRuntime.DesktopDeclaration(profile.Folders, WindowsRuntime.ReadInstallation(profile.Folders)));
    }

    [Theory]
    [InlineData(false, false, DeclarationState.Declared)]
    [InlineData(true, false, DeclarationState.Disabled)]
    [InlineData(false, true, DeclarationState.Invalid)]
    public void DesktopDeclarationUsesItsOwnExactCoreAndPreservesDisabledChoice(bool disabled, bool previous, DeclarationState expected)
    {
        using var profile = new FakeProfile();
        var (_, executable) = Install(profile);
        profile.Write(@"AppData\Roaming\Claude\claude_desktop_config.json", JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object> { ["nexus-windows"] = new
                { command = previous ? "C:\\previous\\NexusWindows.exe" : executable, args = new[] { "--mcp", "--agent", "claude" }, disabled } },
        }));
        Assert.Equal(expected, WindowsRuntime.DesktopDeclaration(profile.Folders, WindowsRuntime.ReadInstallation(profile.Folders)));
    }

    [Fact]
    public void MalformedDesktopConfigurationIsNotAConfiguredClient()
    {
        using var profile = new FakeProfile();
        Install(profile);
        profile.Write(@"AppData\Roaming\Claude\claude_desktop_config.json", "{");
        Assert.Equal(DeclarationState.Invalid, WindowsRuntime.DesktopDeclaration(profile.Folders, WindowsRuntime.ReadInstallation(profile.Folders)));
    }

    [Theory]
    [InlineData("--mcp --agent antigravity", true)]
    [InlineData("--stop-guard 3.7.4", false)]
    [InlineData("--serve", false)]
    [InlineData("--mcp --agent codex", false)]
    [InlineData("--selftest --mcp --agent antigravity", false)]
    [InlineData("--mcp --agent antigravity --stop-guard 3.7.4", false)]
    public void LegacyAntigravityModeAndObservedHostPathAreRecognized(string arguments, bool expected)
    {
        var table = new Dictionary<int, ServerProcess>
        {
            [15380] = new(36648, "language_server.exe", "language_server.exe", @"C:\Users\fixture\AppData\Local\Programs\Antigravity\resources\bin\language_server.exe"),
            [7668] = new(15380, "pwsh.exe", "pwsh.exe -NoProfile legacy-launcher.ps1"),
            [5164] = new(7668, "NexusWindows.Antigravity.exe", "C:\\native\\NexusWindows.Antigravity.exe " + arguments),
        };
        var running = Servers.Match(table);
        if (!expected) { Assert.Empty(running); return; }
        var server = Assert.Single(running);
        Assert.Equal(AiApp.Antigravity, server.StartedBy);
        Assert.False(server.CommonWindowsCore);
        Assert.Equal(WindowsConnection.Previous, WindowsRuntime.Connection(AiApp.Antigravity, running, null));
    }

    [Fact]
    public void GenericLanguageServerDoesNotClaimAntigravity()
    {
        var running = Servers.Match(new Dictionary<int, ServerProcess>
        {
            [1] = new(0, "language_server.exe", "language_server.exe", @"C:\another-product\language_server.exe"),
            [2] = new(1, "NexusWindows.exe", "C:\\common\\NexusWindows.exe --mcp --agent antigravity"),
        });
        Assert.Null(Assert.Single(running).StartedBy);
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\AnthropicPBC.Claude_4.29.0.0_x64__fnn82j28hfe8t\app\Claude.exe", "", true, false)]
    [InlineData(@"C:\Program Files\WindowsApps\AnthropicPBC.Claude_4.29.0.0_x64__fnn82j28hfe8t\app\Claude.exe", " --type=utility", true, false)]
    [InlineData(@"C:\Program Files\WindowsApps\AnthropicPBC.Claude_4.29.0.0_x64__fnn82j28hfe8t\app\Claude.exe", " --type=renderer", true, false)]
    [InlineData(@"C:\Users\fixture\.local\bin\claude.exe", "", false, true)]
    [InlineData(@"C:\Users\fixture\.local\bin\claude.exe", " --type=utility", false, false)]
    [InlineData(@"C:\unknown\claude.exe", "", false, false)]
    public void DesktopElectronAndNativeCodeCliAreDistinctHosts(string path, string options, bool desktop, bool code)
    {
        using var profile = new FakeProfile();
        var (_, executable) = Install(profile);
        var installation = WindowsRuntime.ReadInstallation(profile.Folders);
        var running = Servers.Match(new Dictionary<int, ServerProcess>
        {
            [1] = new(0, "claude.exe", '"' + path + '"' + options, path),
            [2] = new(1, "NexusWindows.exe", '"' + executable + "\" --mcp --agent claude", executable),
        });
        var server = Assert.Single(running);
        Assert.Equal(desktop, server.ClaudeDesktopClient);
        Assert.Equal(code ? AiApp.ClaudeCode : (AiApp?)null, server.StartedBy);
        Assert.Equal(code ? WindowsConnection.Common : WindowsConnection.Idle, WindowsRuntime.Connection(AiApp.ClaudeCode, running, installation));
        Assert.Equal(desktop ? WindowsConnection.Common : WindowsConnection.Idle,
            WindowsRuntime.DesktopConnection(DeclarationState.Declared, running, installation));
        if (desktop)
        {
            Assert.Equal(WindowsConnection.Unconfigured, WindowsRuntime.DesktopConnection(DeclarationState.Missing, running, installation));
            Assert.Equal(WindowsConnection.Unconfigured, WindowsRuntime.DesktopConnection(DeclarationState.Disabled, running, installation));
        }
    }

    [Fact]
    public void DesktopDeclarationAndCodeProcessCannotInventAnActiveDesktopClient()
    {
        using var profile = new FakeProfile();
        var (_, executable) = Install(profile);
        var installation = WindowsRuntime.ReadInstallation(profile.Folders);
        var running = Servers.Match(new Dictionary<int, ServerProcess>
        {
            [1] = new(0, "claude.exe", "claude.exe", @"C:\Users\fixture\.local\bin\claude.exe"),
            [2] = new(1, "NexusWindows.exe", '"' + executable + "\" --mcp --agent claude", executable),
        });
        Assert.Equal(WindowsConnection.Idle, WindowsRuntime.DesktopConnection(DeclarationState.Declared, running, installation));
        var desktopClient = new RunningServer(Products.Windows, 3, null, executable, true, true);
        var previousDesktopClient = desktopClient with { ProcessId = 4, ExecutablePath = @"C:\previous\NexusWindows.exe" };
        Assert.Equal(WindowsConnection.Previous, WindowsRuntime.DesktopConnection(DeclarationState.Declared,
            running.Append(desktopClient).Append(previousDesktopClient), installation));
        Assert.Equal(WindowsConnection.Common, WindowsRuntime.Connection(AiApp.ClaudeCode,
            running.Append(previousDesktopClient), installation));
    }

    [Fact]
    public void TheCatalogPluginsStablePathIsTheCommonCore()
    {
        // Claude Code's plugin starts ${USERPROFILE}\.nexus\nexus-windows\NexusWindows.exe directly; Codex's plugin goes
        // through cmd.exe from that folder (Codex reads no variable in a command). Both are the host's own connection.
        using var profile = new FakeProfile();
        Install(profile);
        var stable = WindowsRuntime.StablePath(profile.Folders);
        Assert.Null(WindowsRuntime.ReadInstallation(profile.Folders)!.StablePath);  // not put there by its installer yet
        profile.Write(@".nexus\nexus-windows\NexusWindows.exe", "fixture:stable");
        var installation = WindowsRuntime.ReadInstallation(profile.Folders)!;
        Assert.Equal(stable, installation.StablePath);
        var running = Servers.Match(new Dictionary<int, ServerProcess>
        {
            [1] = new(0, "claude.exe", "claude.exe", @"C:\Users\fixture\.local\bin\claude.exe"),
            [2] = new(1, "NexusWindows.exe", '"' + stable + "\" --mcp --agent claude", stable),
            [10] = new(0, "codex.exe", "codex.exe app-server"),
            [11] = new(10, "cmd.exe", @"cmd.exe /d /c cd /d %USERPROFILE%\.nexus\nexus-windows && .\NexusWindows.exe --mcp --agent codex"),
            [12] = new(11, "NexusWindows.exe", @".\NexusWindows.exe  --mcp --agent codex", stable),
        });
        Assert.Equal(2, running.Count);
        Assert.Equal(AiApp.Codex, running.Single(s => s.ProcessId == 12).StartedBy);
        Assert.Equal(WindowsConnection.Common, WindowsRuntime.Connection(AiApp.ClaudeCode, running, installation));
        Assert.Equal(WindowsConnection.Common, WindowsRuntime.Connection(AiApp.Codex, running, installation));
        // Another cmd.exe line is not the catalog's launcher.
        Assert.False(Servers.IsCodexLauncher(new ServerProcess(10, "cmd.exe", @"cmd.exe /c C:\other\run.cmd && .\NexusWindows.exe --mcp --agent codex")));
        profile.Write(@"AppData\Roaming\Claude\claude_desktop_config.json", JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object> { ["nexus-windows"] = new { command = stable, args = new[] { "--mcp", "--agent", "claude" } } },
        }));
        Assert.Equal(DeclarationState.Declared, WindowsRuntime.DesktopDeclaration(profile.Folders, installation));
    }

    [Fact]
    public void LoadedExecutableMetadataWinsOverAConfiguredPathInArgv()
    {
        using var profile = new FakeProfile();
        var (_, executable) = Install(profile);
        var running = Servers.Match(new Dictionary<int, ServerProcess>
        {
            [1] = new(0, "codex.exe", "codex.exe app-server"),
            [2] = new(1, "NexusWindows.exe", '"' + executable + "\" --mcp --agent codex", @"C:\previous\NexusWindows.exe"),
        });
        Assert.Equal(WindowsConnection.Previous, WindowsRuntime.Connection(AiApp.Codex, running, WindowsRuntime.ReadInstallation(profile.Folders)));
    }
}
