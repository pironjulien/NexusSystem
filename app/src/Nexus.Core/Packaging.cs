using System.Runtime.InteropServices;

namespace Nexus.Core;

/// <summary>
/// Whether the app runs from its MSIX package (Microsoft Store). There, its writes to existing files under AppData and
/// to HKCU stay in the package's own store (measured by the probe, 25/10/2026): Claude Desktop's existing configuration
/// gets the products through their .mcpb extensions instead, and the pack skips Claude Desktop.
/// </summary>
public static class Packaging
{
    const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged { get; } = Check();

    static bool Check()
    {
        var length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != AppModelErrorNoPackage;
    }

    [DllImport("kernel32.dll")]
    static extern int GetCurrentPackageFullName(ref int length, IntPtr name);
}
