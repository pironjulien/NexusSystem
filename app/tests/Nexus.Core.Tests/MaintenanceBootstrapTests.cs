using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
        Assert.False(MaintenanceSetup.NeedsBootstrap(profile.Folders));
        profile.Write(@".claude\plugins\cache\nexus-plugins\nexus-browser\2.33.5\plugin.json", "{}");
        Assert.False(MaintenanceSetup.NeedsBootstrap(profile.Folders));
        var executable = ExistingBrowser(profile);
        Assert.True(MaintenanceSetup.NeedsBootstrap(profile.Folders, taskRegistered: false));
        File.Delete(executable);
        Assert.False(MaintenanceSetup.NeedsBootstrap(profile.Folders, taskRegistered: false));
        ExistingBrowser(profile);
        profile.Write(@".nexus\nexussystem\maintenance\NexusMaintenance.exe", "# installed engine");
        Assert.False(MaintenanceSetup.NeedsBootstrap(profile.Folders, taskRegistered: true));
        Assert.True(MaintenanceSetup.NeedsBootstrap(profile.Folders, taskRegistered: false));
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
        Assert.False(MaintenanceSetup.NeedsBootstrap(profile.Folders, taskRegistered: false));
    }

    [Fact]
    public void ForeignPointerIsNotAnExistingBrowserInstallation()
    {
        using var profile = new FakeProfile();
        var executable = profile.Write(@"elsewhere\NexusBrowser.exe", "unrelated fixture");
        profile.Write(@"AppData\Local\NexusBrowser\current.json", JsonSerializer.Serialize(new { exe = executable }));
        Assert.False(MaintenanceSetup.NeedsBootstrap(profile.Folders, taskRegistered: false));
    }
}

/// <summary>
/// The maintenance installation's real decisions on a throwaway profile: Task Scheduler, the engine's run and the final
/// rename are stand-ins that record what they were asked. No real task, engine run or download is involved.
/// </summary>
sealed class FakeSetup(UserFolders folders) : MaintenanceSetup(folders)
{
    public ScheduledTaskState? Task;
    public string? Xml;
    public int Registrations, Starts, Moves;
    public bool FailMove;
    public List<(string Path, string Arguments, int RegisteredBefore)> Runs = [];

    public override ScheduledTaskState? ReadTask() => Task;

    public override void RegisterTask(string xml)
    {
        Xml = xml;
        Registrations++;
        Task = new ScheduledTaskState(xml.Contains("<Enabled>true</Enabled>", StringComparison.Ordinal), Task?.Running ?? false);
    }

    public override void StartTask() => Starts++;

    public override int RunEngine(string path, IReadOnlyList<string> arguments)
    {
        Runs.Add((path, string.Join(' ', arguments), Registrations));
        return 2;
    }

    protected override void MoveIntoPlace(string temporary, string destination)
    {
        Moves++;
        if (FailMove) throw new IOException("fixture: the new engine could not take its place");
        base.MoveIntoPlace(temporary, destination);
    }
}

public class MaintenanceSetupTests
{
    static EngineFile Fixture(FakeProfile profile, string bytes, string name = "source.exe")
    {
        var path = profile.Write($@"downloads\{name}", bytes);
        return new EngineFile(path, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(bytes))));
    }

    static string Engine(FakeProfile profile) => MaintenanceUpdates.ExecutablePath(profile.Folders);

    [Fact]
    public void StatusSeparatesStaleHistoricalReceiptsFromLiveState()
    {
        using var profile = new FakeProfile();
        var path = MaintenanceUpdates.StatusPath(profile.Folders);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("pending", JsonDocument.Parse(MaintenanceSetup.Status(profile.Folders, now)).RootElement.Text("state"));
        Assert.False(File.Exists(path));
        profile.Write(@".nexus\status\nexussystem-update.json", """{"schema":1,"state":"current","checked_at":"invalid","products":[]}""");
        var bytes = File.ReadAllText(path);
        Assert.Equal("stale", JsonDocument.Parse(MaintenanceSetup.Status(profile.Folders, now)).RootElement.Text("state"));
        Assert.Equal(bytes, File.ReadAllText(path));
        profile.Write(@".nexus\status\nexussystem-update.json", $$"""{"schema":1,"state":"failed","checked_at":"{{now.AddMinutes(-5):O}}","products":[]}""");
        Assert.Equal("failed", JsonDocument.Parse(MaintenanceSetup.Status(profile.Folders, now)).RootElement.Text("state"));
        profile.Write(@".nexus\status\nexussystem-update.json", $$"""{"schema":1,"state":"success","checked_at":"{{now.AddHours(3):O}}","products":[]}""");
        Assert.Equal("stale", JsonDocument.Parse(MaintenanceSetup.Status(profile.Folders, now)).RootElement.Text("state"));
    }

    [Fact]
    public void FreshInstallationInstallsTheCheckedEngineAndItsTask()
    {
        using var profile = new FakeProfile();
        var setup = new FakeSetup(profile.Folders);
        var legacy = profile.Write(@".nexus\nexussystem\maintenance\get-nexus.ps1", "# PowerShell worker of 1.6.54");
        var engine = setup.Install(Fixture(profile, "engine 1"), null, start: false)!;
        Assert.Equal((Engine(profile), "", false), (engine.Path, engine.Arguments, engine.Development));
        Assert.Equal("engine 1", File.ReadAllText(Engine(profile)));
        Assert.False(File.Exists(legacy));
        Assert.Equal(0, setup.Starts);
        var xml = setup.Xml!;
        Assert.Contains($"<Command>{Engine(profile)}</Command>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Arguments>", xml, StringComparison.Ordinal);
        foreach (var part in new[] { "<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>", "<Hidden>true</Hidden>", "<RunLevel>LeastPrivilege</RunLevel>",
            "<LogonType>InteractiveToken</LogonType>", "<Interval>PT1H</Interval>", "<ExecutionTimeLimit>PT30M</ExecutionTimeLimit>", "<StartWhenAvailable>true</StartWhenAvailable>",
            "<Count>3</Count>", "<Enabled>true</Enabled>", $"<UserId>{System.Security.Principal.WindowsIdentity.GetCurrent().Name}</UserId>" })
            Assert.Contains(part, xml, StringComparison.Ordinal);
        // The same engine again: nothing is copied, the task is registered again (its next hour), and started on request.
        setup.Install(Fixture(profile, "engine 1"), null, start: true);
        Assert.Equal(1, setup.Moves);
        Assert.Equal((2, 1), (setup.Registrations, setup.Starts));
    }

    [Fact]
    public void TamperedEngineIsRefusedAndLeavesNothing()
    {
        using var profile = new FakeProfile();
        var setup = new FakeSetup(profile.Folders);
        var source = Fixture(profile, "engine 1") with { Sha256 = new string('0', 64) };
        Assert.Throws<InvalidDataException>(() => setup.Install(source, null, start: true));
        Assert.False(File.Exists(Engine(profile)));
        Assert.Equal(0, setup.Registrations);
        var folder = Path.GetDirectoryName(Engine(profile))!;
        Assert.True(!Directory.Exists(folder) || !Directory.EnumerateFiles(folder, "*.tmp").Any());
    }

    [Fact]
    public void DisabledTaskStaysDisabledAndARunningOneIsNotStartedAgain()
    {
        using var profile = new FakeProfile();
        var setup = new FakeSetup(profile.Folders) { Task = new ScheduledTaskState(Enabled: false, Running: false) };
        setup.Install(Fixture(profile, "engine 1"), null, start: true);
        Assert.Contains("<Enabled>false</Enabled>", setup.Xml!, StringComparison.Ordinal);
        Assert.Equal(0, setup.Starts);
        setup.Task = new ScheduledTaskState(Enabled: true, Running: true);
        setup.Install(Fixture(profile, "engine 1"), null, start: true);
        Assert.Equal(0, setup.Starts);
    }

    [Fact]
    public void DevelopmentEngineIsKeptAndInvalidSelectionsNeverFallBack()
    {
        using var profile = new FakeProfile();
        var setup = new FakeSetup(profile.Folders);
        var built = profile.Write(@"development\NexusMaintenance.exe", "local build");
        profile.Write(@".nexus\nexussystem.json", JsonSerializer.Serialize(new { developmentCoordinator = built }));
        var legacy = profile.Write(@".nexus\nexussystem\maintenance\get-nexus.ps1", "# PowerShell worker of 1.6.54");
        var engine = setup.Install(Fixture(profile, "public engine"), null, start: false)!;
        Assert.Equal((built, "--current", true), (engine.Path, engine.Arguments, engine.Development));
        Assert.Contains("<Arguments>--current</Arguments>", setup.Xml!, StringComparison.Ordinal);
        Assert.False(File.Exists(Engine(profile)));
        Assert.False(File.Exists(legacy));
        var registered = setup.Registrations;
        foreach (var invalid in new[] { @"relative\NexusMaintenance.exe", Path.Combine(profile.Root, @"development\get-nexus.ps1"), Path.Combine(profile.Root, @"development\missing\NexusMaintenance.exe") })
        {
            profile.Write(@".nexus\nexussystem.json", JsonSerializer.Serialize(new { developmentCoordinator = invalid }));
            Assert.Throws<InvalidOperationException>(() => setup.Install(Fixture(profile, "public engine"), null, start: false));
            Assert.False(File.Exists(Engine(profile)));
            Assert.Equal(registered, setup.Registrations);
        }
        profile.Write(@".nexus\nexussystem.json", JsonSerializer.Serialize(new { autoUpdate = false, developmentCoordinator = built }));
        Assert.Null(setup.Install(Fixture(profile, "public engine"), null, start: true));
        Assert.Equal((registered, 0), (setup.Registrations, setup.Starts));
    }

    [Fact]
    public void HandoverRunsTheEngineAsCurrentThenRegistersIt()
    {
        using var profile = new FakeProfile();
        var setup = new FakeSetup(profile.Folders);
        Assert.Equal(2, setup.Handover(Fixture(profile, "engine 1"), force: false));
        Assert.Equal([(Engine(profile), "--current", 0)], setup.Runs);
        Assert.Equal((1, 0), (setup.Registrations, setup.Starts));
        setup.Handover(Fixture(profile, "engine 1"), force: true);
        Assert.Equal("--current --force", setup.Runs[1].Arguments);
        profile.Write(@".nexus\nexussystem.json", """{"autoUpdate":false}""");
        Assert.Equal(0, setup.Handover(Fixture(profile, "engine 1"), force: false));
        Assert.Equal(2, setup.Runs.Count);
    }

    [Fact]
    public void TestReleaseIsNamedByItsFolders()
    {
        using var profile = new FakeProfile(@"catalog with space", "engines");
        var setup = new FakeSetup(profile.Folders);
        var test = new TestRelease(Path.Combine(profile.Root, "catalog with space") + "\\", Path.Combine(profile.Root, "engines"));
        var engine = setup.Install(Fixture(profile, "engine 1"), test, start: false)!;
        Assert.Equal($"--test-catalog \"{Path.Combine(profile.Root, "catalog with space")}\" --test-engines \"{Path.Combine(profile.Root, "engines")}\"", engine.Arguments);
        Assert.Contains($"<Arguments>--test-catalog &quot;{Path.Combine(profile.Root, "catalog with space")}&quot;", setup.Xml!, StringComparison.Ordinal);
        Assert.Equal("", setup.Install(Fixture(profile, "engine 1"), null, start: false)!.Arguments);
    }

    [Fact]
    public void FailedReplacementRestoresThePreviousEngine()
    {
        using var profile = new FakeProfile();
        var setup = new FakeSetup(profile.Folders);
        setup.Install(Fixture(profile, "engine 1"), null, start: false);
        setup.FailMove = true;
        Assert.Throws<IOException>(() => setup.Install(Fixture(profile, "engine 2"), null, start: false));
        Assert.Equal("engine 1", File.ReadAllText(Engine(profile)));
        Assert.Equal(1, setup.Registrations);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(Engine(profile))!, "*.tmp"));
        setup.FailMove = false;
        setup.Install(Fixture(profile, "engine 2"), null, start: false);
        Assert.Equal("engine 2", File.ReadAllText(Engine(profile)));
        Assert.Equal("engine 1", File.ReadAllText(Path.Combine(Path.GetDirectoryName(Engine(profile))!, "NexusMaintenance.previous.exe")));
    }

    [Fact]
    public void UpdateWhileTheEngineRunsSetsItAsideThenRemovesIt()
    {
        using var profile = new FakeProfile();
        var setup = new FakeSetup(profile.Folders);
        // A real program running from the engine's path (a copy of ping.exe, waiting, without a window).
        var program = File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "PING.EXE"));
        var running = profile.Write(@"downloads\running.exe", "");
        File.WriteAllBytes(running, program);
        setup.Install(new EngineFile(running, MaintenanceServices.Sha256(running)), null, start: false);
        var folder = Path.GetDirectoryName(Engine(profile))!;
        Process? first = null, second = null;
        try
        {
            first = Start(Engine(profile));
            setup.Install(Fixture(profile, "engine 2"), null, start: false);
            Assert.False(first.HasExited);
            Assert.Equal("engine 2", File.ReadAllText(Engine(profile)));
            Assert.Equal(program, File.ReadAllBytes(Path.Combine(folder, "NexusMaintenance.previous.exe")));
            // The previous engine still runs when the next update comes: the current one steps aside under its own name.
            File.WriteAllBytes(Engine(profile), program);
            second = Start(Engine(profile));
            setup.Install(Fixture(profile, "engine 3"), null, start: false);
            Assert.Equal("engine 3", File.ReadAllText(Engine(profile)));
            Assert.Single(Directory.EnumerateFiles(folder, "NexusMaintenance.previous-*.exe"));
        }
        finally
        {
            foreach (var process in new[] { first, second }.OfType<Process>())
            {
                if (!process.HasExited) process.Kill();
                process.WaitForExit();
                process.Dispose();
            }
        }
        setup.Install(Fixture(profile, "engine 4"), null, start: false);
        Assert.Empty(Directory.EnumerateFiles(folder, "NexusMaintenance.previous-*.exe"));
        Assert.Equal("engine 4", File.ReadAllText(Engine(profile)));
    }

    static Process Start(string path)
    {
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "-n", "60", "127.0.0.1" }) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    [Fact]
    public void TaskSchedulerRoundTrip()
    {
        // The real Task Scheduler API on a task of this test only, disabled (it never runs), then deleted.
        var name = $"NexusSystem test {Guid.NewGuid():N}";
        var xml = ScheduledTasks.HourlyTaskXml("NexusSystem test task", Path.Combine(Path.GetTempPath(), "nexus-missing", "NexusMaintenance.exe"),
            "--test-catalog \"C:\\a b\"", enabled: false, DateTimeOffset.Now);
        try
        {
            Assert.Null(ScheduledTasks.Read(name));
            ScheduledTasks.Register(name, xml);
            Assert.Equal(new ScheduledTaskState(Enabled: false, Running: false), ScheduledTasks.Read(name));
        }
        finally
        {
            ScheduledTasks.Delete(name);
        }
        Assert.Null(ScheduledTasks.Read(name));
        Assert.False(ScheduledTasks.Delete(name));
    }
}
