using System.Diagnostics;

namespace NexusSystem.Tests;

/// <summary>
/// The repository's PowerShell checks, each run as the user's shell would run it: get-nexus.ps1 on throwaway profiles
/// under Windows PowerShell 5.1 (the one-line install's shell), and the app's tooling guards under PowerShell 7.
/// </summary>
public class ScriptTests
{
    static string WindowsPowerShell => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    static void Run(string shell, string script, int timeoutMinutes = 15)
    {
        var start = new ProcessStartInfo(shell)
        {
            WorkingDirectory = Repository.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Repository.PathOf(script) })
            start.ArgumentList.Add(arg);
        // PowerShell 7's module path would make Windows PowerShell 5.1 load PowerShell 7's modules.
        start.Environment.Remove("PSModulePath");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        if (!process.WaitForExit(TimeSpan.FromMinutes(timeoutMinutes)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{script} did not finish within {timeoutMinutes} min");
        }
        process.WaitForExit();
        TestContext.Current.TestOutputHelper?.WriteLine(output.Result + error.Result);
        Assert.True(process.ExitCode == 0, $"{script} failed ({process.ExitCode}):\n{output.Result}\n{error.Result}");
    }

    static string PowerShell7 => Environment.ProcessPath is { } self && Path.GetFileName(self).Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)
        ? self : "pwsh.exe";


    [Fact]
    public void CertificationPreservesTheUsersInstallation() => Run(PowerShell7, "app/tests/test_wack.ps1");

    [Fact]
    public void SandboxGoneStopsTheWait() => Run(PowerShell7, "app/tests/test_sandbox_watch.ps1");

    [Fact]
    public void SandboxSmartAppControlStaysInTheGuest() => Run(PowerShell7, "app/tests/test_sandbox_policy.ps1");

    [Fact]
    public void CaptureOutputGuards() => Run(PowerShell7, "app/tests/test_capture.ps1");

    [Fact]
    public void LocalInstallerGuards() => Run(PowerShell7, "app/tests/test_local_install.ps1");

    [Fact]
    public void TheAppNoLongerRunsThePackScript()
    {
        // The app and the engine read the catalog version from get-nexus.ps1 at build time; nothing embeds or runs it.
        var core = Repository.Text("app/src/Nexus.Core/Nexus.Core.csproj");
        Assert.Contains("<AssemblyMetadata Include=\"NexusCatalogVersion\"", core, StringComparison.Ordinal);
        Assert.DoesNotContain("EmbeddedResource", core, StringComparison.Ordinal);
        Assert.False(File.Exists(Repository.PathOf("app/src/Nexus.Core/PackRunner.cs")));
    }

    [Fact]
    public void BootstrapKeepsTheHandOverContract()
    {
        // The PowerShell worker of releases up to 1.6.54 checks a published file for these before running it with
        // -Maintain -MaintenanceCurrent; the documented options stay the same for the AI apps' install requests.
        var script = Repository.Text("get-nexus.ps1");
        foreach (var name in new[] { "Only", "Skip", "DryRun", "NoGuide", "Catalog", "MaintenanceFrom", "Json", "Maintain", "MaintenanceStatus",
            "MaintenanceCurrent", "ForceMaintenance", "InstallMaintenance" })
            Assert.Matches($@"(?m)^\s+\[(string\[\]|string|switch)\]\${name}\b", script);
        Assert.Contains("function Invoke-NexusMaintenance(", script, StringComparison.Ordinal);
        // A minimal bootstrap: the logic is the engine's.
        Assert.True(script.Split('\n').Length < 200, "get-nexus.ps1 only fetches, checks and starts the engine");
    }

    /// <summary>
    /// This commit's engine, published for the test as a single file (framework-dependent: the bootstrap copies one
    /// file), with its SHA256SUMS.txt: the -MaintenanceFrom folder of the release being checked.
    /// </summary>
    static string PublishEngine(string folder)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Repository.Root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "publish", "app/src/Nexus.Maintenance/Nexus.Maintenance.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "false",
            "-p:PublishAot=false", "-p:PublishSingleFile=true", "-o", Path.Combine(folder, "build"), "-nologo", "-v:q" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { /* already done */ }
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"engine publish failed:\n{output.Result}\n{error.Result}");
        var engines = Directory.CreateDirectory(Path.Combine(folder, "engines")).FullName;
        File.Copy(Path.Combine(folder, "build", "NexusMaintenance.exe"), Path.Combine(engines, "NexusMaintenance-x64.exe"));
        using var stream = File.OpenRead(Path.Combine(engines, "NexusMaintenance-x64.exe"));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
        File.WriteAllText(Path.Combine(engines, "SHA256SUMS.txt"), $"{hash}  NexusMaintenance-x64.exe\n{new string('0', 64)}  get-nexus.ps1\n");
        return engines;
    }

    static (int Code, string Output) Bootstrap(string profile, string arguments)
    {
        var start = new ProcessStartInfo(WindowsPowerShell) { WorkingDirectory = Repository.Root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        start.Environment["USERPROFILE"] = profile;
        start.Environment["APPDATA"] = Path.Combine(profile, @"AppData\Roaming");
        start.Environment["LOCALAPPDATA"] = Path.Combine(profile, @"AppData\Local");
        start.Environment["PATH"] = $@"{Environment.SystemDirectory};{Path.GetDirectoryName(Environment.SystemDirectory)};{Path.GetDirectoryName(WindowsPowerShell)}";
        start.Environment.Remove("CODEX_HOME");
        start.Environment.Remove("PSModulePath");
        start.Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"& '{Repository.PathOf("get-nexus.ps1")}' {arguments}; exit $LASTEXITCODE\"";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(5)), "get-nexus.ps1 did not finish");
        process.WaitForExit();
        return (process.ExitCode, output.Result + error.Result);
    }

    [Fact]
    public void BootstrapChecksAndStartsTheEngine()
    {
        var work = Directory.CreateTempSubdirectory("nexus-bootstrap-");
        try
        {
            var engines = PublishEngine(work.FullName);
            var profile = Path.Combine(work.FullName, "profile");
            foreach (var folder in new[] { ".claude", ".codex", @".gemini\antigravity", @"AppData\Roaming\Claude", @"AppData\Local" })
                Directory.CreateDirectory(Path.Combine(profile, folder));
            var (code, output) = Bootstrap(profile, $"-DryRun -Json -Catalog '{Repository.Root}' -MaintenanceFrom '{engines}'");
            Assert.True(code == 0, output);
            foreach (var text in new[] { "AI apps found: Claude Code, Claude Desktop, Codex, Antigravity", "NexusWindows  for Claude Code, Claude Desktop, Codex, Antigravity",
                "Dry run: nothing installed.", "\"type\":\"done\",\"code\":0" })
                Assert.True(output.Contains(text, StringComparison.Ordinal), $"missing: {text}\n{output}");
            Assert.False(Directory.Exists(Path.Combine(profile, ".nexus", "nexussystem")), "a dry run installed the maintenance");

            (code, output) = Bootstrap(profile, $"-DryRun -Catalog '{Repository.Root}' -MaintenanceFrom '{engines}' -Only nexuswindows");
            Assert.True(code == 2 && output.Contains("Unknown name 'nexuswindows'", StringComparison.Ordinal), output);

            // A file that is not the listed engine is never started.
            File.AppendAllText(Path.Combine(engines, "NexusMaintenance-x64.exe"), "tampered");
            (code, output) = Bootstrap(profile, $"-DryRun -Catalog '{Repository.Root}' -MaintenanceFrom '{engines}'");
            Assert.True(code == 1 && output.Contains("SHA-256 mismatch", StringComparison.Ordinal) && !output.Contains("AI apps found", StringComparison.Ordinal), output);
        }
        finally
        {
            try { work.Delete(recursive: true); }
            catch (IOException) { /* a temporary folder of this test */ }
        }
    }
}
