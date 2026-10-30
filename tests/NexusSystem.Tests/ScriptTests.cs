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
    public void PackDetectionPlanAndOptions() => Run(WindowsPowerShell, "tests/test_pack.ps1");

    [Fact]
    public void PackCodexContract() => Run(WindowsPowerShell, "tests/test_codex_pack.ps1");

    [Fact]
    public void CertificationPreservesTheUsersInstallation() => Run(PowerShell7, "app/tests/test_wack.ps1");

    [Fact]
    public void CaptureOutputGuards() => Run(PowerShell7, "app/tests/test_capture.ps1");

    [Fact]
    public void LocalInstallerGuards() => Run(PowerShell7, "app/tests/test_local_install.ps1");

    [Fact]
    public void AppEmbedsTheReleasedPackScript()
    {
        // The app runs get-nexus.ps1 embedded at build time (app/pack, copied by app/pack/sync.ps1): never another one.
        Assert.Equal(Repository.Text("get-nexus.ps1").ReplaceLineEndings(), Repository.Text("app/pack/get-nexus.ps1").ReplaceLineEndings());
    }
}
