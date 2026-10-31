param(
    [string]$OutputDirectory = 'dist\NasDesktop',
    [switch]$IncludeLocalSettings
)
$ErrorActionPreference = 'Stop'
$projectDirectory = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
$output = [IO.Path]::GetFullPath($output).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ($output.Equals([IO.Path]::GetPathRoot([IO.Path]::GetFullPath($output)).TrimEnd([IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must not be a filesystem root.'
}
if ($output.Equals($projectDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must not be the project root; local settings must remain outside public build output.'
}
$outputAncestor = $output
while ($outputAncestor) {
    if ((Test-Path -LiteralPath $outputAncestor) -and (((Get-Item -LiteralPath $outputAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'OutputDirectory must not pass through a linked directory; it could alias the original settings.'
    }
    $outputAncestor = [IO.Path]::GetDirectoryName($outputAncestor)
}
$localSettingsSource = Join-Path $projectDirectory 'connection.local.json'
$localSettingsOutput = Join-Path $output 'connection.local.json'
$version = '1.0.4314.35'
$cacheDirectory = Join-Path $PSScriptRoot '.packages'
$packageRoot = Join-Path $cacheDirectory "webview2-$version"
# NuGet's SHA-512 for this exact version. Update the package and DLL pins together.
# https://api.nuget.org/v3/catalog0/data/2026.10.23.06.50.10/microsoft.web.webview2.1.0.4314.35.json
$packageSha512 = '70E6A256A3F7066407327C46A59CC034CDB6B545E0C9CAE2B322478C818DCC868CB26F225E3ECAC7E71E620F916D8C09B734F4D3DA9F7054E872F54B63E7A433'
# These file hashes were derived only after verifying that official package hash.
$sdkFiles = @(
    @{ Path = 'lib\net462\Microsoft.Web.WebView2.Core.dll'; SHA512 = 'B5AF2FBCCB3F3780FA9A32C06AB0A6EC375F8E22E278986518F514931BDAE9B2231675830C021FFF791D18DB11B76D6FA28DE9024E06C6990116BDC2E044EF91' },
    @{ Path = 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'; SHA512 = 'F9ED7E2E4388758437F79BE336190E53986CF21803142C074388CE38A96D5F9456C21F9701F67884437FCA261D377825299A811C7029AECE3426FD4BF9BD0744' },
    @{ Path = 'runtimes\win-x64\native\WebView2Loader.dll'; SHA512 = 'D6C8FEFE85E459F68E8FBB7A2FECC0FF4306A3282FFC6874D2395D5750FB77CF0B69C6D1AE83B481325AC97B745CB1D6879B47076688EBAEE69636173E77671B' }
)

function Assert-UnlinkedSdkPath([string]$Path) {
    $ancestor = [IO.Path]::GetFullPath($Path)
    while ($ancestor) {
        $ancestorItem = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
        if ($null -ne $ancestorItem -and (($ancestorItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'WebView2 SDK paths must not pass through linked files or directories.'
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
}

function Test-SdkCache {
    $complete = $true
    foreach ($file in $sdkFiles) {
        $sdkPath = Join-Path $packageRoot $file.Path
        Assert-UnlinkedSdkPath $sdkPath
        if (-not (Test-Path -LiteralPath $sdkPath -PathType Leaf)) { $complete = $false; continue }
        if ((Get-FileHash -LiteralPath $sdkPath -Algorithm SHA512).Hash -cne $file.SHA512) {
            throw "WebView2 SDK cache integrity check failed: $($file.Path). Remove the SDK cache and rebuild."
        }
    }
    return $complete
}

. (Join-Path $PSScriptRoot 'tools\BuildArtifacts.ps1')
$buildLock = Enter-NasDeskArtifactLock -ProjectRoot $PSScriptRoot -Name build
$buildWorkspace = $null
try {
    $buildWorkspace = New-NasDeskWorkspace -ProjectRoot $PSScriptRoot -Kind build
    Assert-UnlinkedSdkPath $packageRoot
    if (-not (Test-SdkCache)) {
        New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
        $zip = Join-Path $buildWorkspace.Path 'webview2.zip'
        Assert-UnlinkedSdkPath $zip
        if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) {
            $download = Join-Path $buildWorkspace.Path 'webview2-download.zip'
            try {
                Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/microsoft.web.webview2.$version.nupkg" -OutFile $download
                if ((Get-FileHash -LiteralPath $download -Algorithm SHA512).Hash -cne $packageSha512) {
                    throw 'Downloaded WebView2 package failed the pinned NuGet SHA-512 check.'
                }
                [IO.File]::Move($download, $zip)
            }
            finally { if (Test-Path -LiteralPath $download -PathType Leaf) { Remove-Item -LiteralPath $download -Force } }
        }
        if ((Get-FileHash -LiteralPath $zip -Algorithm SHA512).Hash -cne $packageSha512) {
            throw 'Cached WebView2 package failed the pinned NuGet SHA-512 check. Remove the package cache and rebuild.'
        }
        # Extract only verified files needed by this build; never overwrite unrelated cache paths.
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($file in $sdkFiles) {
                $destination = Join-Path $packageRoot $file.Path
                Assert-UnlinkedSdkPath $destination
                $entry = $archive.GetEntry($file.Path.Replace('\', '/'))
                if ($null -eq $entry) { throw "Verified WebView2 package is missing $($file.Path)." }
                New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
            }
        }
        finally { $archive.Dispose() }
        if (-not (Test-SdkCache)) { throw 'Verified WebView2 SDK extraction is incomplete.' }
    }
    $sdkMarker = Join-Path $packageRoot '.nasdesk-sdk-cache'
    Assert-UnlinkedSdkPath $sdkMarker
    [IO.File]::WriteAllText($sdkMarker, 'nas-desktop-app:sdk-cache:v1:' + $version)
    Clear-NasDeskSdkCache -ProjectRoot $PSScriptRoot -Version $version
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $iconTool = Join-Path $buildWorkspace.Path 'IconGenerator.exe'
    $icon = Join-Path $buildWorkspace.Path 'app.ico'
    & $compiler /nologo /target:exe "/out:$iconTool" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'src\Theme.cs') (Join-Path $PSScriptRoot 'tools\IconGenerator.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Icon tool compilation failed.' }
    & $iconTool $icon
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
    $references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Security.dll')
    $references += Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'
    $references += Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'
    $arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output','/codepage:65001',"/out:$(Join-Path $output 'NasDesktop.exe')", "/win32icon:$icon", "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')")
    $arguments += $references | ForEach-Object { "/reference:$_" }
    $arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'),(Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'),(Join-Path $packageRoot 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $output -Force
    if ($IncludeLocalSettings) {
        if (Test-Path -LiteralPath $localSettingsSource -PathType Leaf) {
            Copy-Item -LiteralPath $localSettingsSource -Destination $localSettingsOutput -Force
        }
    }
    elseif (Test-Path -LiteralPath $localSettingsOutput) {
        # This is the generated output copy, never the developer's original settings.
        if ([IO.Path]::GetFullPath($localSettingsOutput).Equals([IO.Path]::GetFullPath($localSettingsSource), [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove the original local settings file.'
        }
        if (-not (Test-Path -LiteralPath $localSettingsOutput -PathType Leaf)) {
            throw 'The public build output contains a directory named connection.local.json.'
        }
        Remove-Item -LiteralPath $localSettingsOutput -Force
    }
    Write-Output "Built: $(Join-Path $output 'NasDesktop.exe')"
}
finally {
    try { Remove-NasDeskWorkspace -Workspace $buildWorkspace }
    finally { $buildLock.Dispose() }
}
