# Copies the Nexus pack (get-nexus.ps1) from the repository root (the catalog, pironjulien/NexusSystem) into pack/,
# where the apps embed it.
# The copy is checked byte for byte by tests (PackScriptTests): the apps run the very script of the one-line install.
param([string]$Catalog = (Join-Path $PSScriptRoot '..\..'))
$ErrorActionPreference = 'Stop'
Copy-Item -LiteralPath (Join-Path $Catalog 'get-nexus.ps1') -Destination (Join-Path $PSScriptRoot 'get-nexus.ps1') -Force
Write-Host "pack/get-nexus.ps1 copied from $Catalog"
