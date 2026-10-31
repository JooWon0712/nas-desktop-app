param(
    [string]$AppDirectory = 'dist\SynologyNasDesktop',
    [string]$OutputFile = 'dist\SynologyNasDesktop-windows-x64.zip'
)
$ErrorActionPreference = 'Stop'

$appPath = if ([IO.Path]::IsPathRooted($AppDirectory)) { $AppDirectory } else { Join-Path $PSScriptRoot $AppDirectory }
$appPath = [IO.Path]::GetFullPath($appPath)
$archivePath = if ([IO.Path]::IsPathRooted($OutputFile)) { $OutputFile } else { Join-Path $PSScriptRoot $OutputFile }
$archivePath = [IO.Path]::GetFullPath($archivePath)
if ([IO.Path]::GetExtension($archivePath) -ne '.zip') { throw 'OutputFile must have a .zip extension.' }

# Copy only the files required to run the app, even from a private development build.
$allowedNames = @(
    'SynologyNasDesktop.exe',
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.WinForms.dll',
    'WebView2Loader.dll'
)
$sourceFiles = @($allowedNames | ForEach-Object {
    $sourcePath = Join-Path $appPath $_
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Required app file is missing: $_" }
    $sourceItem = Get-Item -LiteralPath $sourcePath
    if (($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "App files must not be links: $_" }
    $sourceItem.FullName
})

$archiveDirectory = [IO.Path]::GetDirectoryName($archivePath)
New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null
$stagingName = '.package-staging-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N')
$stagingDirectory = [IO.Path]::GetFullPath((Join-Path $archiveDirectory $stagingName))
if (-not [IO.Path]::GetDirectoryName($stagingDirectory).Equals($archiveDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package staging path escaped the archive directory.'
}
New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
try {
    Copy-Item -LiteralPath $sourceFiles -Destination $stagingDirectory
    $stagedFiles = @($allowedNames | ForEach-Object { Join-Path $stagingDirectory $_ })
    Compress-Archive -LiteralPath $stagedFiles -DestinationPath $archivePath -Force
    Write-Output "Packaged: $archivePath"
}
finally {
    # Remove only this invocation's newly created staging directory.
    $resolvedStaging = [IO.Path]::GetFullPath($stagingDirectory)
    if (-not [IO.Path]::GetDirectoryName($resolvedStaging).Equals($archiveDirectory, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedStaging) -ne $stagingName) {
        throw 'Refusing to remove an unexpected staging path.'
    }
    if (Test-Path -LiteralPath $resolvedStaging) {
        if (((Get-Item -LiteralPath $resolvedStaging).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Refusing to remove linked package staging.'
        }
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
}
