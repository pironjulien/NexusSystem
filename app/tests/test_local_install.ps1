# Payload and immutable-release guards only; no real shortcut, scheduled task, package or app launch.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\tools\install-local.ps1')
$temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$fixture = Join-Path $temporary ('nexus-local-install-tests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$checks = 0
function Assert-LocalRefused([scriptblock]$Action, [string]$Reason) {
    try { & $Action | Out-Null } catch { if ($_.Exception.Message -notmatch $Reason) { throw }; return }
    throw "Local installer accepted an invalid input: $Reason"
}
try {
    $source = Join-Path $fixture 'source'
    [IO.Directory]::CreateDirectory($source) | Out-Null
    Assert-LocalRefused { Get-NexusLocalPayload $source } 'missing NexusSystem.exe'; $checks++
    foreach ($name in 'NexusSystem.exe', 'NexusSystem.dll', 'Nexus.Core.dll', 'NexusSystem.pri', 'NexusSystem.deps.json', 'NexusSystem.runtimeconfig.json') {
        Set-Content -LiteralPath (Join-Path $source $name) -Value ('fixture ' + $name)
    }
    $payload = Get-NexusLocalPayload $source
    if ($payload.Entries.Count -ne 6 -or $payload.Hash -notmatch '^[A-F0-9]{64}$') { throw 'Incomplete payload manifest.' }; $checks++
    $destination = Get-NexusLocalDestination (Join-Path $fixture 'local') '1.0.9.0' $payload.Hash
    Copy-NexusLocalPayload $payload $destination
    if ((Get-NexusLocalPayload $destination).Hash -ne $payload.Hash) { throw 'Copy was not complete.' }; $checks++
    Copy-NexusLocalPayload $payload $destination; $checks++
    Set-Content -LiteralPath (Join-Path $destination 'NexusSystem.pri') -Value 'preserve changed resource'
    Assert-LocalRefused { Copy-NexusLocalPayload $payload $destination } 'nothing was overwritten'; $checks++
    if ((Get-Content -LiteralPath (Join-Path $destination 'NexusSystem.pri') -Raw).Trim() -ne 'preserve changed resource') { throw 'An existing release was overwritten.' }; $checks++
    Assert-LocalRefused { Get-NexusLocalDestination $fixture '..\other' $payload.Hash } 'Invalid local version'; $checks++
    Assert-LocalRefused { Get-NexusLocalDestination $fixture '1.0.9.0' '..\other' } 'Invalid local version'; $checks++
    Assert-LocalRefused { Get-NexusLocalDestination 'relative' '1.0.9.0' $payload.Hash } 'absolute directory'; $checks++
    $physical = [NexusLocalNative]::Physical((Join-Path $source 'NexusSystem.exe'))
    if ($physical -ne (Join-Path $source 'NexusSystem.exe')) { throw 'A file handle did not resolve the physical fixture.' }; $checks++
    Set-Content -LiteralPath (Join-Path $source 'foreign.txt') -Value 'extra file'
    if ((Get-NexusLocalPayload $source).Hash -eq $payload.Hash) { throw 'The digest ignored an additional file.' }; $checks++
    $root = Join-Path $fixture 'local'
    $current = Get-NexusLocalDestination $root '1.0.14.0' ('A' * 64)
    $busy = Get-NexusLocalDestination $root '1.0.13.0' ('B' * 64)
    $stale = Get-NexusLocalDestination $root '1.0.14.0' ('C' * 64)
    $flat = Join-Path $root 'NexusApps\local\1.0.8.0'  # older layout: files directly in the version folder
    foreach ($folder in $current, $busy, $stale, $flat, (Join-Path $root 'NexusApps\local\publish-20261030-111026')) { [IO.Directory]::CreateDirectory($folder) | Out-Null }
    Set-Content -LiteralPath (Join-Path $flat 'NexusSystem.exe') -Value 'fixture'
    Remove-NexusLocalStale $current @((Join-Path $busy 'NexusSystem.exe'))
    $left = @(Get-ChildItem -LiteralPath (Join-Path $root 'NexusApps\local') -Directory -Recurse | ForEach-Object FullName)
    if (($left -notcontains $current) -or ($left -notcontains $busy) -or ($left -contains $stale) -or
        ($left -like '*publish-*') -or ($left -like '*1.0.9.0*') -or ($left -like '*1.0.8.0*')) { throw "Stale local copies were not pruned as expected: $($left -join ', ')" }; $checks++
    Write-Host "Local installer: $checks guards passed (fixtures only)."
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'nexus-local-install-tests-*') { throw 'Unsafe fixture cleanup refused.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
