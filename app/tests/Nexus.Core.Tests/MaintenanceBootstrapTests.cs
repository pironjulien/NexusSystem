using System.Text.Json;
using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

public class MaintenanceBootstrapTests
{
    static string ExistingBrowser(FakeProfile profile)
    {
        var executable = profile.Write(@"AppData\Local\NexusBrowser\versions\existing\NexusBrowser.exe", "existing engine fixture");
        profile.Write(@"AppData\Local\NexusBrowser\current.json", JsonSerializer.Serialize(new { exe = executable, version = "2.33.5" }));
        return executable;
    }

    [Fact]
    public void OnlyExistingEngineNeedsWorkerAndBootstrapStopsOnceInstalled()
    {
        using var profile = new FakeProfile();
        Assert.False(PackRunner.NeedsMaintenanceBootstrap(profile.Folders));
        profile.Write(@".claude\plugins\cache\nexus-plugins\nexus-browser\2.33.5\plugin.json", "{}");
        Assert.False(PackRunner.NeedsMaintenanceBootstrap(profile.Folders));
        var executable = ExistingBrowser(profile);
        Assert.True(PackRunner.NeedsMaintenanceBootstrap(profile.Folders));
        File.Delete(executable);
        Assert.False(PackRunner.NeedsMaintenanceBootstrap(profile.Folders));
        ExistingBrowser(profile);
        profile.Write(@".nexus\nexussystem\maintenance\get-nexus.ps1", "# existing public worker");
        Assert.False(PackRunner.NeedsMaintenanceBootstrap(profile.Folders, taskRegistered: true));
        Assert.True(PackRunner.NeedsMaintenanceBootstrap(profile.Folders, taskRegistered: false));
    }

    [Theory]
    [InlineData("{\"autoUpdate\":false}")]
    [InlineData("{\"autoupdate\":false}")]
    [InlineData("{\"skip\":[],\"autoUpdate\":false}")]
    [InlineData("[]")]
    [InlineData("{")]
    public void DisabledOrUnreadableChoicesNeverBootstrap(string json)
    {
        using var profile = new FakeProfile();
        ExistingBrowser(profile);
        profile.Write(@".nexus\nexussystem.json", json);
        Assert.False(PackRunner.NeedsMaintenanceBootstrap(profile.Folders));
    }

    [Fact]
    public void ForeignPointerIsNotAnExistingBrowserInstallation()
    {
        using var profile = new FakeProfile();
        var executable = profile.Write(@"elsewhere\NexusBrowser.exe", "unrelated fixture");
        profile.Write(@"AppData\Local\NexusBrowser\current.json", JsonSerializer.Serialize(new { exe = executable }));
        Assert.False(PackRunner.NeedsMaintenanceBootstrap(profile.Folders));
    }

    [Fact]
    public void MaintenanceModeInvokesEmbeddedFileWithoutAnyProductSelection()
    {
        var script = Path.Combine(Path.GetTempPath(), "embedded pack.ps1");
        var command = PackRunner.CreateCommand(script, ["windows", "browser"], dryRun: false, maintenanceOnly: true);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), command.FileName);
        Assert.Equal(["-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", script, "-Json", "-NoGuide", "-InstallMaintenance"], command.ArgumentList);
        Assert.True(command.CreateNoWindow);
        Assert.False(command.UseShellExecute);
    }

    [Fact]
    public void NormalModePreservesProductSelectionAndDryRun()
    {
        var command = PackRunner.CreateCommand("embedded.ps1", ["windows", "browser"], dryRun: true);
        Assert.Contains("-Only", command.ArgumentList);
        Assert.Contains("windows,browser", command.ArgumentList);
        Assert.Contains("-DryRun", command.ArgumentList);
        Assert.DoesNotContain("-InstallMaintenance", command.ArgumentList);
    }
}
