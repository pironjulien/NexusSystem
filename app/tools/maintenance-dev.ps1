# The author's workstation: build the maintenance engine from this repository and make it the one the hourly task
# runs (developmentCoordinator in ~\.nexus\nexussystem.json). The engine is published natively (AOT) for this PC's
# architecture to ~\.nexus\nexussystem\development\NexusMaintenance.exe, outside the repository and outside the
# public engine's folder; get-nexus.ps1 -Maintain (or -InstallMaintenance) then points the task at it with --current.
# Nothing is started here: the task's next run (or -Maintain) uses the new build.
#   pwsh -File app/tools/maintenance-dev.ps1
[CmdletBinding(SupportsShouldProcess = $true)]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot)
$project = Join-Path $repository 'app\src\Nexus.Maintenance\Nexus.Maintenance.csproj'
$arch = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
$output = Join-Path $HOME ".nexus\build\NexusSystem\maintenance-engine\$arch"
# The native linker is found through vswhere (Visual Studio's C++ build tools).
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path -LiteralPath (Join-Path $installer 'vswhere.exe')) -and ($env:PATH -split ';') -notcontains $installer) { $env:PATH = "$installer;$env:PATH" }
dotnet publish $project -c Release -r "win-$arch" -o $output -nologo
if ($LASTEXITCODE) { throw "The maintenance engine did not build (exit code $LASTEXITCODE)." }

$folder = Join-Path $HOME '.nexus\nexussystem\development'
$engine = Join-Path $folder 'NexusMaintenance.exe'
if ($PSCmdlet.ShouldProcess($engine, 'Install the development maintenance engine')) {
    New-Item -ItemType Directory -Force $folder | Out-Null
    $temporary = "$engine.$PID.tmp"
    Copy-Item -LiteralPath (Join-Path $output 'NexusMaintenance.exe') -Destination $temporary -Force
    # A running engine is renamed aside, never stopped.
    if (Test-Path -LiteralPath $engine) {
        $previous = Join-Path $folder 'NexusMaintenance.previous.exe'
        Remove-Item -LiteralPath $previous -Force -ErrorAction SilentlyContinue
        Move-Item -LiteralPath $engine -Destination $previous -Force
    }
    Move-Item -LiteralPath $temporary -Destination $engine -Force
}

# The choice file keeps its other keys; only developmentCoordinator names this engine.
$choices = Join-Path $HOME '.nexus\nexussystem.json'
$settings = if (Test-Path -LiteralPath $choices) { [IO.File]::ReadAllText($choices) | ConvertFrom-Json } else { [pscustomobject]@{} }
if ($settings.developmentCoordinator -ne $engine -and $PSCmdlet.ShouldProcess($choices, "developmentCoordinator = $engine")) {
    $settings | Add-Member -NotePropertyName developmentCoordinator -NotePropertyValue $engine -Force
    [IO.File]::WriteAllText($choices, ($settings | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}
Write-Host "Development maintenance engine: $engine ($((Get-Item -LiteralPath $engine).VersionInfo.ProductVersion))"
