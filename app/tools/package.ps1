# The app's MSIX package. Without -Store: a test package (identity NexusSystemTest, unsigned), for an install in developer
# mode, x64 only by default. With -Store: the .msixupload for Partner Center, built with the identity Partner Center
# gives once the name "NexusSystem" is reserved (Product identity page: Package/Identity/Name and Publisher), for x64 and
# ARM64 in one .msixbundle, with public-symbol packages for crash analytics; the Store signs it. Nothing is uploaded
# here: the submission is a manual step in Partner
# Center, after review.
# The version has one source: <Version> in Directory.Build.props. It goes to the assemblies, the executable's file
# version and the package manifest; -Version overrides it for one build.
param(
    [switch]$Store,
    [string]$IdentityName = '',
    [string]$Publisher = '',
    [string]$PublisherDisplayName = '',
    [string]$Version = '',
    [ValidateSet('x64', 'ARM64')]
    [string[]]$Platforms = @(),
    # Outside OneDrive: each Store build weighs ~600 MB and would fill the cloud quota.
    [string]$Out = (Join-Path $env:LOCALAPPDATA 'NexusApps\dist\msix')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$manifest = Join-Path $root 'src\Nexus.App\Package.appxmanifest'
if ($Store -and (-not $IdentityName -or -not $Publisher)) { throw '-Store needs -IdentityName and -Publisher from Partner Center (Product identity).' }
if (-not $Version) { $Version = ([xml](Get-Content -Raw (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version '$Version': a package version is four numbers ending in .0 (the Store reserves the last one)." }
if (-not $Platforms) { $Platforms = if ($Store) { 'x64', 'ARM64' } else { 'x64' } }
$outDir = [IO.Path]::GetFullPath($Out) + '\'
$symbolArguments = @()
if ($Store) {
    # dotnet does not set VsInstallRoot, even when the C++ tools are installed. Find the public-symbol tool through
    # Visual Studio's own installation inventory so the upload includes crash analytics instead of silently omitting it.
    $symbolTool = Get-Command mspdbcmf.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not $symbolTool -and (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        $symbolTool = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -find 'VC\Tools\MSVC\**\bin\Hostx64\x64\mspdbcmf.exe' | Select-Object -First 1
    }
    if (-not $symbolTool -or -not (Test-Path -LiteralPath $symbolTool -PathType Leaf)) {
        throw 'Store packaging needs the Visual Studio C++ tools (mspdbcmf.exe) to include public symbols.'
    }
    $symbolArguments = @("-p:MsPdbCmfExeFullpath=$symbolTool")
}
$original = [IO.File]::ReadAllText($manifest)
$packages = @()
$symbolPackages = @()
try {
    $patched = $original -replace 'Version="[0-9.]+" />', "Version=`"$Version`" />"
    if ($Store) { $patched = $patched -replace 'Name="NexusSystemTest" Publisher="CN=NexusSystemTest"', "Name=`"$IdentityName`" Publisher=`"$Publisher`"" }
    if ($PublisherDisplayName) { $patched = $patched -replace '<PublisherDisplayName>[^<]*</PublisherDisplayName>', "<PublisherDisplayName>$PublisherDisplayName</PublisherDisplayName>" }
    [IO.File]::WriteAllText($manifest, $patched)
    # StoreUpload mode needs Visual Studio's MSBuild (its container task fails under the dotnet CLI): each .msix is
    # built in SideloadOnly mode, and the .msixupload contains the .msix, or the bundle of both, plus public symbols.
    foreach ($platform in $Platforms) {
        $platformDir = Join-Path $outDir $platform
        Remove-Item -LiteralPath $platformDir -Recurse -Force -ErrorAction SilentlyContinue
        dotnet publish (Join-Path $root 'src\Nexus.App\Nexus.App.csproj') -c Release "-p:Platform=$platform" -r "win-$($platform.ToLowerInvariant())" `
            "-p:Version=$Version" -p:WindowsPackageType=MSIX -p:EnableMsixTooling=true -p:GenerateAppxPackageOnBuild=true `
            -p:AppxPackageSigningEnabled=false -p:UapAppxPackageBuildMode=SideloadOnly -p:AppxBundle=Never `
            "-p:AppxPackageDir=$platformDir\" @symbolArguments --nologo -v q
        if ($LASTEXITCODE) { throw "dotnet publish failed ($platform)" }
        $packages += Get-ChildItem $platformDir -Recurse -Filter *.msix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($Store) {
            $symbols = Get-ChildItem $platformDir -Recurse -Filter *.appxsym | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if (-not $symbols) { throw "Public symbols were not generated ($platform)." }
            $symbolPackages += $symbols
        }
    }
} finally {
    [IO.File]::WriteAllText($manifest, $original)
}
if ($Store) {
    $content = $packages[0]
    $name = "NexusSystem_${Version}_$($Platforms[0].ToLowerInvariant())"
    if ($packages.Count -gt 1) {
        # makeappx from the Windows SDK build tools the project already restores (Microsoft.Windows.SDK.BuildTools).
        $sdk = (Select-Xml -Path (Join-Path $root 'src\Nexus.App\Nexus.App.csproj') -XPath '//PackageReference[@Include="Microsoft.Windows.SDK.BuildTools"]').Node.Version
        $makeappx = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools\$sdk\bin") -Recurse -Filter makeappx.exe |
            Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
        if (-not $makeappx) { throw "makeappx.exe not found in Microsoft.Windows.SDK.BuildTools $sdk (dotnet restore first)." }
        $staging = Join-Path $outDir 'bundle'
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory $staging | Out-Null
        $packages | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $staging }
        $name = "NexusSystem_${Version}_" + (($Platforms | ForEach-Object { $_.ToLowerInvariant() }) -join '_') + '_bundle'
        $bundle = Join-Path $outDir "$name.msixbundle"
        & $makeappx.FullName bundle /d $staging /p $bundle /bv $Version /o | Out-Null
        if ($LASTEXITCODE) { throw 'makeappx bundle failed' }
        Remove-Item -LiteralPath $staging -Recurse -Force
        $content = Get-Item -LiteralPath $bundle
    }
    $upload = Join-Path $outDir "$name.msixupload"
    Remove-Item -LiteralPath $upload -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($upload, 'Create')
    try {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $content.FullName, $content.Name)
        foreach ($symbols in $symbolPackages) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $symbols.FullName, $symbols.Name)
        }
    } finally { $zip.Dispose() }
}
Get-ChildItem $Out -Recurse -Include *.msix, *.msixbundle, *.msixupload, *.appxsym | Sort-Object LastWriteTime -Descending |
    ForEach-Object { '{0}  {1:N1} MB  SHA256 {2}' -f $_.FullName, ($_.Length / 1MB), (Get-FileHash $_.FullName).Hash }
