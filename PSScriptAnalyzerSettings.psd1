# Lint of the PowerShell scripts (test.ps1 runs it). get-nexus.ps1 is an installer read by a person in a console:
# Write-Host (colors, nothing in the pipeline) is what it must use.
@{
    Severity     = @('Error', 'Warning')
    ExcludeRules = @('PSAvoidUsingWriteHost')
}
