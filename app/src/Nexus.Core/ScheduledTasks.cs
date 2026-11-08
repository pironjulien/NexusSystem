using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Security;
using System.Security.Principal;

namespace Nexus.Core;

/// <summary>A registered task as Task Scheduler reports it.</summary>
public sealed record ScheduledTaskState(bool Enabled, bool Running);

/// <summary>
/// The Task Scheduler 2.0 API (taskschd.dll) through source-generated COM interop, usable from the native (AOT)
/// maintenance engine and from the app: a task is registered from its XML definition, read, started and deleted.
/// No PowerShell cmdlet or schtasks.exe is involved.
/// </summary>
public static partial class ScheduledTasks
{
    static readonly Guid TaskSchedulerClass = new("0F87369F-A4E5-4CFC-BD3E-73E6154572DD");
    static readonly Guid TaskServiceInterface = new("2FABA4C7-4DA9-4013-9697-20CC3FD40F85");
    static readonly StrategyBasedComWrappers Wrappers = new();

    const int TaskCreateOrUpdate = 6;
    const int TaskLogonInteractiveToken = 3;
    const int TaskStateRunning = 4;
    const int FileNotFound = unchecked((int)0x80070002);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint model);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out nint instance);

    static ITaskFolder Root()
    {
        // The calling thread joins the multithreaded apartment (already initialized threads keep theirs).
        _ = CoInitializeEx(0, 0);
        Marshal.ThrowExceptionForHR(CoCreateInstance(TaskSchedulerClass, 0, 1 /* CLSCTX_INPROC_SERVER */, TaskServiceInterface, out var pointer));
        try
        {
            var service = (ITaskService)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
            service.Connect(default, default, default, default);
            return service.GetFolder(@"\");
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary>The task's state, or null when it is not registered.</summary>
    public static ScheduledTaskState? Read(string name)
    {
        var hr = Root().GetTask(name, out var task);
        if (hr == FileNotFound || task is null) return null;
        Marshal.ThrowExceptionForHR(hr);
        return new ScheduledTaskState(task.GetEnabled() != 0, task.GetState() == TaskStateRunning);
    }

    /// <summary>Creates or replaces the task from its XML definition, for the signed-in user (interactive token).</summary>
    public static void Register(string name, string xml) =>
        Root().RegisterTask(name, xml, TaskCreateOrUpdate, default, default, TaskLogonInteractiveToken, default);

    /// <summary>Starts the task now (its own settings decide whether a second instance may run).</summary>
    public static void Run(string name)
    {
        var hr = Root().GetTask(name, out var task);
        Marshal.ThrowExceptionForHR(hr);
        var running = task!.Run(default);
        if (running != 0) Marshal.Release(running);
    }

    /// <summary>Deletes the task; false when it was not registered.</summary>
    public static bool Delete(string name)
    {
        var hr = Root().DeleteTask(name, 0);
        if (hr == FileNotFound) return false;
        Marshal.ThrowExceptionForHR(hr);
        return true;
    }

    /// <summary>
    /// The XML of a per-user task that starts one program directly: at the user's sign-in and every hour from an hour
    /// from now, hidden, one instance at a time (IgnoreNew), 30 minutes at most, restarted up to three times every 15
    /// minutes after a failure, on battery too, with the user's limited rights.
    /// </summary>
    public static string HourlyTaskXml(string description, string command, string? arguments, bool enabled, DateTimeOffset now, string? user = null, string? sid = null)
    {
        using var identity = user is null || sid is null ? WindowsIdentity.GetCurrent() : null;
        user ??= identity!.Name;
        sid ??= identity!.User!.Value;
        var start = now.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        static string X(string value) => SecurityElement.Escape(value);
        var exec = $"<Command>{X(command)}</Command>" + (string.IsNullOrEmpty(arguments) ? "" : $"<Arguments>{X(arguments)}</Arguments>");
        return
            "<Task version=\"1.3\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
            $"<RegistrationInfo><Description>{X(description)}</Description></RegistrationInfo>" +
            $"<Principals><Principal id=\"Author\"><UserId>{X(sid)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
            "<Settings><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
            "<ExecutionTimeLimit>PT30M</ExecutionTimeLimit><Hidden>true</Hidden><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
            "<RestartOnFailure><Count>3</Count><Interval>PT15M</Interval></RestartOnFailure><StartWhenAvailable>true</StartWhenAvailable>" +
            $"<Enabled>{(enabled ? "true" : "false")}</Enabled></Settings>" +
            $"<Triggers><LogonTrigger><UserId>{X(user)}</UserId></LogonTrigger>" +
            $"<TimeTrigger><StartBoundary>{start}</StartBoundary><Repetition><Interval>PT1H</Interval></Repetition></TimeTrigger></Triggers>" +
            $"<Actions Context=\"Author\"><Exec>{exec}</Exec></Actions></Task>";
    }
}

/// <summary>
/// A VARIANT passed by value, always VT_EMPTY here (the defaults of Connect, RegisterTask and Run): 24 bytes on 64-bit
/// Windows. Its marshaller passes it as is, so the source generator handles it without disabling runtime marshalling.
/// </summary>
[NativeMarshalling(typeof(EmptyVariantMarshaller))]
[StructLayout(LayoutKind.Sequential)]
internal readonly struct EmptyVariant
{
    readonly ushort type;
    readonly ushort reserved1, reserved2, reserved3;
    readonly nint value1, value2;
}

[CustomMarshaller(typeof(EmptyVariant), MarshalMode.Default, typeof(EmptyVariantMarshaller))]
internal static class EmptyVariantMarshaller
{
    public static EmptyVariant ConvertToUnmanaged(EmptyVariant managed) => managed;

    public static EmptyVariant ConvertToManaged(EmptyVariant unmanaged) => unmanaged;
}

// The three interfaces of taskschd.h used here, in their vtable order. They derive from IDispatch, whose four methods
// are declared as placeholders (never called) so that the slots after them line up.

[GeneratedComInterface]
[Guid("2FABA4C7-4DA9-4013-9697-20CC3FD40F85")]
internal partial interface ITaskService
{
    void DispatchGetTypeInfoCount();
    void DispatchGetTypeInfo();
    void DispatchGetIDsOfNames();
    void DispatchInvoke();
    ITaskFolder GetFolder([MarshalAs(UnmanagedType.BStr)] string path);
    void GetRunningTasks();
    void NewTask();
    void Connect(EmptyVariant serverName, EmptyVariant user, EmptyVariant domain, EmptyVariant password);
}

[GeneratedComInterface]
[Guid("8CFAC062-A080-4C15-9A88-AA7C2AF80DFC")]
internal partial interface ITaskFolder
{
    void DispatchGetTypeInfoCount();
    void DispatchGetTypeInfo();
    void DispatchGetIDsOfNames();
    void DispatchInvoke();
    void GetName();
    void GetPath();
    void GetSubFolder();
    void GetFolders();
    void CreateFolder();
    void DeleteFolder();
    [PreserveSig]
    int GetTask([MarshalAs(UnmanagedType.BStr)] string path, out IRegisteredTask? task);
    void GetTasks();
    [PreserveSig]
    int DeleteTask([MarshalAs(UnmanagedType.BStr)] string name, int flags);
    IRegisteredTask RegisterTask([MarshalAs(UnmanagedType.BStr)] string path, [MarshalAs(UnmanagedType.BStr)] string xml, int flags,
        EmptyVariant userId, EmptyVariant password, int logonType, EmptyVariant sddl);
}

[GeneratedComInterface]
[Guid("9C86F320-DEE3-4DD1-B972-A303F26B061E")]
internal partial interface IRegisteredTask
{
    void DispatchGetTypeInfoCount();
    void DispatchGetTypeInfo();
    void DispatchGetIDsOfNames();
    void DispatchInvoke();
    void GetName();
    void GetPath();
    int GetState();
    short GetEnabled();
    void SetEnabled(short enabled);
    nint Run(EmptyVariant parameters);
}
