using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Nexus.Core;

/// <summary>A process as Windows reports it: the image name, its parent, and (for this user's processes) its command line,
/// executable and start time. A process this user cannot open keeps only its name and parent.</summary>
public sealed record ProcessInfo(int ProcessId, int ParentProcessId, string Name, string CommandLine, string? ExecutablePath, DateTime? StartUtc);

/// <summary>
/// The process list read natively (Toolhelp snapshot, then each process opened for limited query only): no WMI, no COM,
/// so the hourly maintenance stays a small native executable. Nothing is written to any process.
/// </summary>
public static partial class NativeProcesses
{
    const uint SnapProcess = 0x2;
    const uint QueryLimitedInformation = 0x1000;
    const int ProcessCommandLineInformation = 60;

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32First(nint snapshot, ref ProcessEntry entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32Next(nint snapshot, ref ProcessEntry entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(nint process, uint flags, nint buffer, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(nint process, int informationClass, nint buffer, uint length, out uint returned);

    public static IReadOnlyList<ProcessInfo> Read()
    {
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var found = new List<ProcessInfo>();
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
            {
                var image = ImageName(ref entry);
                var (commandLine, executable, start) = Details(entry.ProcessId);
                found.Add(new ProcessInfo((int)entry.ProcessId, (int)entry.ParentProcessId, image, commandLine ?? "", executable, start));
            }
        }
        finally { CloseHandle(snapshot); }
        return found;
    }

    static unsafe string ImageName(ref ProcessEntry entry)
    {
        fixed (char* name = entry.ExeFile)
        {
            var span = new ReadOnlySpan<char>(name, 260);
            var end = span.IndexOf('\0');
            return (end < 0 ? span : span[..end]).ToString();
        }
    }

    static (string? CommandLine, string? Executable, DateTime? Start) Details(uint processId)
    {
        if (processId == 0) return (null, null, null);
        var process = OpenProcess(QueryLimitedInformation, false, processId);
        if (process == 0) return (null, null, null);
        try
        {
            DateTime? start = GetProcessTimes(process, out var creation, out _, out _, out _) && creation > 0 ? DateTime.FromFileTimeUtc(creation) : null;
            return (CommandLine(process), Executable(process), start);
        }
        finally { CloseHandle(process); }
    }

    static string? Executable(nint process)
    {
        var size = 32768u;
        var buffer = Marshal.AllocHGlobal((int)size * 2);
        try
        {
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? Marshal.PtrToStringUni(buffer, (int)size) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    static string? CommandLine(nint process)
    {
        // ProcessCommandLineInformation (Windows 8.1+): a UNICODE_STRING followed by its characters.
        NtQueryInformationProcess(process, ProcessCommandLineInformation, 0, 0, out var needed);
        if (needed == 0 || needed > 1 << 20) return null;
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, needed, out _) < 0) return null;
            var length = (ushort)Marshal.ReadInt16(buffer);
            var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            return text == 0 ? "" : Marshal.PtrToStringUni(text, length / 2);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}

/// <summary>
/// Authenticode, as Get-AuthenticodeSignature reads it: a file whose embedded signature Windows trusts (no network
/// revocation lookup), and the subject of the certificate that signed it.
/// </summary>
public static partial class Authenticode
{
    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    struct FileInfo
    {
        public uint Size;
        public nint FilePath;
        public nint File;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TrustData
    {
        public uint Size;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint File;
        public uint StateAction;
        public nint StateData;
        public nint UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);

    // The app asks again on each refresh: a file keeps its verdict while its size and write time are unchanged.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, long, DateTime, string), bool> Verdicts = new();

    /// <summary>True when the file carries a valid embedded signature whose signer's subject names this publisher.</summary>
    public static bool IsSignedBy(string path, string publisher)
    {
        var file = new System.IO.FileInfo(path);
        if (!file.Exists) return false;
        return Verdicts.GetOrAdd((file.FullName.ToUpperInvariant(), file.Length, file.LastWriteTimeUtc, publisher), _ => Verify(file.FullName, publisher));
    }

    static bool Verify(string path, string publisher)
    {
        var name = Marshal.StringToHGlobalUni(path);
        var file = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), FilePath = name }, file, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(),
                UiChoice = 2,              // WTD_UI_NONE
                RevocationChecks = 0,      // WTD_REVOKE_NONE
                UnionChoice = 1,           // WTD_CHOICE_FILE
                File = file,
                ProviderFlags = 0x1000,    // WTD_CACHE_ONLY_URL_RETRIEVAL: never a network lookup
            };
            var action = GenericVerifyV2;
            if (WinVerifyTrust(-1, ref action, ref data) != 0) return false;
        }
        finally
        {
            Marshal.FreeHGlobal(file);
            Marshal.FreeHGlobal(name);
        }
        return SubjectNames(SignerSubject(path), publisher);
    }

    static string? SignerSubject(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // No loader replaces reading the signer of a signed file.
            using var certificate = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return certificate.Subject;
        }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    /// <summary>The subject names the publisher as its CN or O, quoted as Windows writes a name with a comma.</summary>
    public static bool SubjectNames(string? subject, string publisher)
    {
        if (subject is null) return false;
        var quoted = publisher.Contains(',', StringComparison.Ordinal) ? "\"" + publisher + "\"" : publisher;
        return System.Text.RegularExpressions.Regex.IsMatch(subject,
            "(^|,\\s*)(CN|O)=" + System.Text.RegularExpressions.Regex.Escape(quoted) + "(?=,|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
