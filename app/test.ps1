# Repository checks: solution build (warnings are errors), then the Nexus.Core tests. Build outputs and test results
# live outside OneDrive, under %LOCALAPPDATA%\NexusApps (Directory.Build.props). The MSIX package is checked
# separately (tools\package.ps1, then tools\wack.ps1).
$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot 'NexusApps.slnx'
$results = Join-Path $env:LOCALAPPDATA 'NexusApps\TestResults'
dotnet build $solution -c Debug --nologo -v q
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$testExe = Join-Path $env:LOCALAPPDATA 'NexusApps\build\Nexus.Core.Tests\bin\Debug\net10.0-windows10.0.19041.0\Nexus.Core.Tests.exe'
if (Test-Path -LiteralPath $testExe) {
    & $testExe
} else {
    dotnet test $solution --no-build --results-directory $results
}
if ($LASTEXITCODE) { exit $LASTEXITCODE }
# Certification must preserve a user's existing installation. Isolated command doubles, no real WACK/Appx action.
& (Join-Path $PSScriptRoot 'tests\test_wack.ps1')
# Screenshot output must never turn a mistaken -Out into arbitrary recursive deletion.
& (Join-Path $PSScriptRoot 'tests\test_capture.ps1')
# An immutable local release and its XAML resources are verified before a user shortcut is changed.
& (Join-Path $PSScriptRoot 'tests\test_local_install.ps1')
exit $LASTEXITCODE
