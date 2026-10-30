// MSIX container probe: run installed from a package, it writes to every place the Nexus apps need (new and existing
// files under AppData, the user profile outside AppData, HKCU, a user variable) three ways: from the packaged process
// itself, from a child started normally, and from a child started with the documented desktop app breakaway policy
// (both children are this program, writing natively). tests/probe.ps1 then reads, from outside the package, which writes are really there.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

static class Probe
{
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static readonly string Done = Path.Combine(Home, ".nexusprobe", "done.txt");

    // No PowerShell anywhere: a child is this same program started again with "--child <tag>" (native writes), normally
    // or with the desktop app breakaway policy. A PowerShell started from a package with a command line is what
    // Microsoft Defender blocks (Trojan:Win32/Commando.A!ml, 25/10/2026).
    static void Main(string[] args)
    {
        if (args is ["--child", var childTag])
        {
            Write(childTag);
            return;
        }
        if (args is ["--task-create"])
        {
            Directory.CreateDirectory(Path.Combine(Home, ".nexusprobe"));
            try { CreateTask(); File.WriteAllText(Path.Combine(Home, ".nexusprobe", "task-created.txt"), "ok"); }
            catch (Exception e) { File.WriteAllText(Path.Combine(Home, ".nexusprobe", "task-created.txt"), "error: " + e); }
            return;
        }
        if (args is ["--task-ran"])
        {
            // Started by the Task Scheduler: proof that the task created from the package can run.
            Directory.CreateDirectory(Path.Combine(Home, ".nexusprobe"));
            File.WriteAllText(Path.Combine(Home, ".nexusprobe", "task-ran.txt"), "ran");
            return;
        }
        try
        {
            Write("direct");
            RunChild("child", breakaway: false);
            RunChild("breakaway", breakaway: true);
            RunScript("script", breakaway: false);
            RunScript("scriptaway", breakaway: true);
            // From a child of the package, as an installer would register its bridge task.
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--task-create");
            using (var creator = Process.Start(start)!) creator.WaitForExit();
            Directory.CreateDirectory(Path.GetDirectoryName(Done)!);
            File.WriteAllText(Done, "ok");
        }
        catch (Exception e)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Done)!);
            File.WriteAllText(Done, "error: " + e);
        }
    }

    static void Write(string tag)
    {
        Directory.CreateDirectory(Path.Combine(Local, "NexusProbe"));
        File.WriteAllText(Path.Combine(Local, "NexusProbe", tag + ".txt"), tag);
        Directory.CreateDirectory(Path.Combine(Roaming, "NexusProbe"));
        File.WriteAllText(Path.Combine(Roaming, "NexusProbe", tag + ".txt"), tag);
        File.AppendAllText(Path.Combine(Local, "NexusProbe", "existing.txt"), tag + Environment.NewLine);
        Directory.CreateDirectory(Path.Combine(Home, ".nexusprobe"));
        File.WriteAllText(Path.Combine(Home, ".nexusprobe", tag + ".txt"), tag);
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\NexusProbe")) key.SetValue(tag, tag);
        Environment.SetEnvironmentVariable("NEXUSPROBE_" + tag.ToUpperInvariant(), tag, EnvironmentVariableTarget.User);
    }

    static void RunChild(string tag, bool breakaway)
    {
        var self = Environment.ProcessPath!;
        if (!breakaway)
        {
            var start = new ProcessStartInfo(self) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--child");
            start.ArgumentList.Add(tag);
            using var child = Process.Start(start)!;
            child.WaitForExit();
            return;
        }
        Native.StartBrokenAway($"\"{self}\" --child {tag}");
    }

    // The Task Scheduler's own COM API (no schtasks, no PowerShell): a task "NexusProbe", without trigger, hidden, run once
    // at once; its action is this program with "--task-ran". run-local.ps1 deletes it and checks it is gone.
    static void CreateTask()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Nexus MSIX probe: created by a test, deleted right after.";
        definition.Settings.Hidden = true;
        definition.Settings.AllowDemandStart = true;
        dynamic action = definition.Actions.Create(0);  // TASK_ACTION_EXEC
        action.Path = Environment.ProcessPath!;
        action.Arguments = "--task-ran";
        folder.RegisterTaskDefinition("NexusProbe", definition, 6, null, null, 3);  // CREATE_OR_UPDATE, interactive token
        folder.GetTask("NexusProbe").Run(null);
    }

    // As the app runs an installer: a .ps1 file (written outside AppData, in ~\.nexusprobe) run by Windows PowerShell
    // with -File, never -Command. Its writes: the same markers, the HKCU value as an installer would set a key.
    static void RunScript(string tag, bool breakaway)
    {
        var folder = Path.Combine(Home, ".nexusprobe");
        Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, $"probe-{tag}.ps1");
        File.WriteAllText(script, string.Join(Environment.NewLine, new[]
        {
            "New-Item -ItemType Directory -Force \"$env:LOCALAPPDATA\\NexusProbe\" | Out-Null",
            $"Set-Content \"$env:LOCALAPPDATA\\NexusProbe\\{tag}.txt\" {tag}",
            "New-Item -ItemType Directory -Force \"$env:APPDATA\\NexusProbe\" | Out-Null",
            $"Set-Content \"$env:APPDATA\\NexusProbe\\{tag}.txt\" {tag}",
            $"Add-Content \"$env:LOCALAPPDATA\\NexusProbe\\existing.txt\" {tag}",
            $"Set-Content \"$env:USERPROFILE\\.nexusprobe\\{tag}.txt\" {tag}",
            "New-Item -Force 'HKCU:\\Software\\NexusProbe' | Out-Null",
            $"Set-ItemProperty 'HKCU:\\Software\\NexusProbe' {tag} {tag}",
            $"[Environment]::SetEnvironmentVariable('NEXUSPROBE_{tag.ToUpperInvariant()}', '{tag}', 'User')",
        }));
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        if (!breakaway)
        {
            var start = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", script }) start.ArgumentList.Add(arg);
            using var child = Process.Start(start)!;
            child.WaitForExit();
            return;
        }
        Native.StartBrokenAway($"\"{powershell}\" -NoProfile -ExecutionPolicy RemoteSigned -File \"{script}\"");
    }
}

static class Native
{
    const int ProcThreadAttributeDesktopAppPolicy = 0x00020012;  // PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY
    const uint BreakawayEnableProcessTree = 0x01;               // PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE
    const uint ExtendedStartupInfoPresent = 0x00080000;
    const uint CreateNoWindow = 0x08000000;

    // The created process (and its tree) runs outside the desktop app runtime environment (the package container).
    public static void StartBrokenAway(string commandLine)
    {
        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var list = Marshal.AllocHGlobal(size);
        var policy = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)) throw new Win32Exception();
            Marshal.WriteInt32(policy, (int)BreakawayEnableProcessTree);
            if (!UpdateProcThreadAttribute(list, 0, (IntPtr)ProcThreadAttributeDesktopAppPolicy, policy, (IntPtr)sizeof(uint), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();
            var info = new STARTUPINFOEX { lpAttributeList = list };
            info.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, ExtendedStartupInfoPresent | CreateNoWindow,
                    IntPtr.Zero, null, ref info, out var process))
                throw new Win32Exception();
            WaitForSingleObject(process.hProcess, 120_000);
            CloseHandle(process.hProcess);
            CloseHandle(process.hThread);
        }
        finally
        {
            DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
            Marshal.FreeHGlobal(policy);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessW(string? application, string commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string? directory, ref STARTUPINFOEX startup, out PROCESS_INFORMATION info);

    [DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
}
