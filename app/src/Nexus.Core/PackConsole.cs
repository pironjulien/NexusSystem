using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nexus.Core;

/// <summary>
/// The output of the engine's interactive modes. The engine is a windowless program (the hourly task starts it without
/// a console): when its standard output is a pipe or a file (a test, CI), it writes UTF-8 there; otherwise it attaches to
/// the console of the PowerShell that started it (get-nexus.ps1), writes there in color, and makes that console the
/// standard handles its child programs inherit, so the products' installers and the AI apps' command lines show in it.
/// </summary>
public sealed partial class PackConsole : IPackOutput, IDisposable
{
    const int StdInput = -10, StdOutput = -11, StdError = -12;
    const uint FileTypeDisk = 1, FileTypePipe = 3;
    const uint AttachParent = unchecked((uint)-1);

    readonly Stream? stream;
    readonly SafeFileHandle? console;
    readonly ushort attributes;
    readonly object gate = new();

    /// <summary>Whether the engine writes in a console that its child programs share.</summary>
    public bool IsConsole => console is not null;

    public PackConsole()
    {
        var output = GetStdHandle(StdOutput);
        if (output != 0 && output != -1 && GetFileType(output) is FileTypeDisk or FileTypePipe)
        {
            stream = System.Console.OpenStandardOutput();
            return;
        }
        if (!AttachConsole(AttachParent)) return; // started without a console: nothing to show
        var inherited = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = 1 };
        console = CreateFile("CONOUT$", 0xC0000000 /* read, write */, 3 /* share read, write */, ref inherited, 3 /* open existing */, 0, 0);
        if (console.IsInvalid)
        {
            console = null;
            return;
        }
        var input = CreateFile("CONIN$", 0xC0000000, 3, ref inherited, 3, 0, 0);
        if (!input.IsInvalid) SetStdHandle(StdInput, input.DangerousGetHandle());
        SetStdHandle(StdOutput, console.DangerousGetHandle());
        SetStdHandle(StdError, console.DangerousGetHandle());
        attributes = GetConsoleScreenBufferInfo(console, out var info) ? info.Attributes : (ushort)7;
    }

    public void Line(string text = "", PackColor color = PackColor.Gray) => Write(text + Environment.NewLine, color);

    public void Write(string text, PackColor color = PackColor.Gray)
    {
        lock (gate)
        {
            if (stream is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes);
                stream.Flush();
                return;
            }
            if (console is null) return;
            // Foreground only, on the console's own background.
            var foreground = color switch
            {
                PackColor.DarkGray => 8,
                PackColor.Cyan => 11,
                PackColor.Green => 10,
                PackColor.Yellow => 14,
                PackColor.Red => 12,
                _ => 7,
            };
            SetConsoleTextAttribute(console, (ushort)((attributes & 0xF0) | foreground));
            WriteConsole(console, text, (uint)text.Length, out _, 0);
            SetConsoleTextAttribute(console, attributes);
        }
    }

    public void Dispose()
    {
        stream?.Dispose();
        console?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SecurityAttributes
    {
        public int Length;
        public nint Descriptor;
        public int InheritHandle;
    }

    // CONSOLE_SCREEN_BUFFER_INFO: 22 bytes, 2-byte aligned.
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct ScreenBufferInfo
    {
        public uint Size;
        public uint Cursor;
        public ushort Attributes;
        public short WindowLeft, WindowTop, WindowRight, WindowBottom;
        public uint MaximumSize;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int which);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetStdHandle(int which, nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetFileType(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint process);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, ref SecurityAttributes security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleScreenBufferInfo(SafeFileHandle console, out ScreenBufferInfo info);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleTextAttribute(SafeFileHandle console, ushort attributes);

    [LibraryImport("kernel32.dll", EntryPoint = "WriteConsoleW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteConsole(SafeFileHandle console, string text, uint length, out uint written, nint reserved);
}
