# The native maintenance engine (Native AOT, one executable, no assembly or DLL beside it) keeps working when its
# file is replaced while it runs, the way MaintenanceServices.InstallPublishedEngineAsync, get-nexus.ps1 and
# maintenance-dev.ps1 replace it: the running file is renamed aside and the new one is moved to its path.
# Unlike a .NET single-file bundle (NexusFree 1.3.2, whose open sessions re-read their own path and failed after an
# update), the whole image is mapped when the process is created, so nothing is read from the path afterwards.
# The engine is created suspended, before any of its code has run, its file is swapped for bytes that are not an
# engine, then it runs a complete cycle on a throwaway profile: every page of its code is read after the swap.
#   pwsh -File tests/test_engine_replacement.ps1 [-Engine <NexusMaintenance.exe>]   (default: publish it for this PC)
param([string]$Engine)
$ErrorActionPreference = 'Stop'
function Assert([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }

if (-not $Engine) {
    $arch = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
    $output = Join-Path $HOME ".nexus\build\NexusSystem\maintenance-engine\$arch"
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if ((Test-Path -LiteralPath (Join-Path $installer 'vswhere.exe')) -and ($env:PATH -split ';') -notcontains $installer) { $env:PATH = "$installer;$env:PATH" }
    dotnet publish (Join-Path (Split-Path $PSScriptRoot) 'app\src\Nexus.Maintenance\Nexus.Maintenance.csproj') -c Release -r "win-$arch" -o $output -nologo | Out-Host
    if ($LASTEXITCODE) { throw "The maintenance engine did not build (exit code $LASTEXITCODE)." }
    $Engine = Join-Path $output 'NexusMaintenance.exe'
}
$Engine = (Resolve-Path -LiteralPath $Engine).ProviderPath
# The premise: a native engine alone. A managed assembly or DLL beside it would be loaded from its folder on demand.
$beside = @(Get-ChildItem -LiteralPath (Split-Path $Engine) -File | Where-Object { $_.Extension -in '.dll', '.json' })
Assert ($beside.Count -eq 0) "The engine is published with files it may load later: $($beside.Name -join ', ')"

if (-not ('NexusSuspendedProcess' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.ComponentModel; using System.Runtime.InteropServices;
public static class NexusSuspendedProcess {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StartupInfo {
        public int cb; public string reserved, desktop, title; public int x, y, xSize, ySize, xChars, yChars, fill, flags;
        public short show, reserved2; public IntPtr reserved3, input, output, error; }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInformation { public IntPtr process, thread; public int processId, threadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessW(string application, System.Text.StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inherit, uint flags, string environment, string directory, ref StartupInfo startup, out ProcessInformation information);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    const uint Suspended = 0x4, NoWindow = 0x08000000, UnicodeEnvironment = 0x400;
    public sealed class Started { public IntPtr Process, Thread; public int Id; }
    // The image is mapped and the process exists, but none of its code (loader included) has run yet.
    public static Started Start(string exe, string arguments, string environment) {
        var startup = new StartupInfo(); startup.cb = Marshal.SizeOf(startup);
        ProcessInformation info;
        if (!CreateProcessW(exe, new System.Text.StringBuilder("\"" + exe + "\" " + arguments), IntPtr.Zero, IntPtr.Zero, false,
            Suspended | NoWindow | UnicodeEnvironment, environment, null, ref startup, out info)) throw new Win32Exception();
        return new Started { Process = info.process, Thread = info.thread, Id = info.processId };
    }
    public static int ResumeAndWait(Started started, uint milliseconds) {
        if (ResumeThread(started.Thread) == uint.MaxValue) throw new Win32Exception();
        if (WaitForSingleObject(started.Process, milliseconds) != 0) { TerminateProcess(started.Process, 1); throw new TimeoutException("The engine did not finish."); }
        uint code; if (!GetExitCodeProcess(started.Process, out code)) throw new Win32Exception();
        CloseHandle(started.Thread); CloseHandle(started.Process);
        return (int)code;
    }
    public static void Stop(Started started) {
        TerminateProcess(started.Process, 1); WaitForSingleObject(started.Process, 10000);
        CloseHandle(started.Thread); CloseHandle(started.Process);
    }
}
'@
}

$work = Join-Path ([IO.Path]::GetTempPath()) "nexus-engine-replacement-$PID"
try {
    # A throwaway profile whose choices disable automatic updates: a complete cycle (options, lock, settings, status
    # receipt written as JSON) that touches no network, task, product or AI app.
    $profilePath = Join-Path $work 'profile'
    New-Item -ItemType Directory -Force (Join-Path $profilePath '.nexus'), (Join-Path $profilePath 'AppData\Local'), (Join-Path $profilePath 'AppData\Roaming') | Out-Null
    [IO.File]::WriteAllText((Join-Path $profilePath '.nexus\nexussystem.json'), '{"autoUpdate":false}')
    $variables = [Environment]::GetEnvironmentVariables()
    $variables['USERPROFILE'] = $profilePath
    $variables['LOCALAPPDATA'] = Join-Path $profilePath 'AppData\Local'
    $variables['APPDATA'] = Join-Path $profilePath 'AppData\Roaming'
    $block = (($variables.Keys | Sort-Object | ForEach-Object { "$_=$($variables[$_])" }) -join "`0") + "`0`0"
    $folder = Join-Path $profilePath '.nexus\nexussystem\maintenance'
    New-Item -ItemType Directory -Force $folder | Out-Null
    $durable = Join-Path $folder 'NexusMaintenance.exe'
    $previous = Join-Path $folder 'NexusMaintenance.previous.exe'
    $status = Join-Path $profilePath '.nexus\status\nexussystem-update.json'
    function Read-Status { Get-Content -Raw -LiteralPath $status | ConvertFrom-Json }

    # Without a swap: the reference result.
    Copy-Item -LiteralPath $Engine -Destination $durable
    $code = [NexusSuspendedProcess]::ResumeAndWait([NexusSuspendedProcess]::Start($durable, '--force', $block), 60000)
    Assert ($code -eq 0 -and (Read-Status).state -eq 'disabled') "Reference run: exit code $code, status $((Read-Status).state)."
    Remove-Item -LiteralPath $status
    Write-Host 'OK the engine completes a cycle on a throwaway profile'

    # Its file cannot be overwritten in place while it runs: only renamed aside, which is what every installer does.
    $running = [NexusSuspendedProcess]::Start($durable, '--force', $block)
    $refused = $false
    try { [IO.File]::WriteAllBytes($durable, [byte[]](1, 2, 3)) } catch [IO.IOException], [UnauthorizedAccessException] { $refused = $true }
    Assert $refused 'A running engine file was overwritten in place.'

    # The update while it runs: renamed aside, and at its path a file that is not an engine at all. If anything were
    # read from that path after the process was created, the run below would fail.
    Move-Item -LiteralPath $durable -Destination $previous
    [IO.File]::WriteAllText($durable, 'not an engine: the newer release would be here')
    $code = [NexusSuspendedProcess]::ResumeAndWait($running, 60000)
    Assert ($code -eq 0 -and (Read-Status).state -eq 'disabled') "After its file was replaced, the running engine failed: exit code $code."
    Write-Host 'OK a running engine whose file is replaced completes its cycle from the original image'

    # The test discriminates: the bytes now at the path cannot run.
    $failed = $false
    try { [NexusSuspendedProcess]::Stop([NexusSuspendedProcess]::Start($durable, '', $block)) } catch [ComponentModel.Win32Exception] { $failed = $true }
    Assert $failed 'The replacement bytes ran as an engine; the swap proved nothing.'
    Write-Host 'OK the replacement file at the engine''s path is not runnable, so nothing was loaded from it'
} finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
