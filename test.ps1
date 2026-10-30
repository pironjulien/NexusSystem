# Full offline check (exit code 0 when everything is fine): PowerShell lint, then `dotnet test` on the solution:
# catalogs (Claude Code, Codex), get-nexus.ps1 on throwaway profiles under Windows PowerShell 5.1, the app's build
# (warnings are errors) and tests. Build and test output: ~\.nexus\build\NexusSystem (Directory.Build.props).
$ErrorActionPreference = 'Stop'
if (-not (Get-Module -ListAvailable PSScriptAnalyzer)) { throw 'PSScriptAnalyzer is missing: Install-Module PSScriptAnalyzer -Scope CurrentUser' }
$lint = Invoke-ScriptAnalyzer -Path $PSScriptRoot -Recurse -Settings (Join-Path $PSScriptRoot 'PSScriptAnalyzerSettings.psd1')
if ($lint) { $lint | Format-Table -AutoSize ScriptName, Line, RuleName, Message | Out-String -Width 200 | Write-Host; exit 1 }
Push-Location $PSScriptRoot  # dotnet test reads global.json from the current folder
try {
    dotnet test --solution NexusSystem.slnx --results-directory (Join-Path $HOME '.nexus\build\NexusSystem\TestResults')
} finally { Pop-Location }
exit $LASTEXITCODE