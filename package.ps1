param(
    [string]$AppDirectory = 'dist\NasDesktop',
    [string]$OutputFile = 'dist\NasDesktop-windows-x64.zip'
)
$ErrorActionPreference = 'Stop'

$appPath = if ([IO.Path]::IsPathRooted($AppDirectory)) { $AppDirectory } else { Join-Path $PSScriptRoot $AppDirectory }
$appPath = [IO.Path]::GetFullPath($appPath)
$archivePath = if ([IO.Path]::IsPathRooted($OutputFile)) { $OutputFile } else { Join-Path $PSScriptRoot $OutputFile }
$archivePath = [IO.Path]::GetFullPath($archivePath)
if ([IO.Path]::GetExtension($archivePath) -ne '.zip') { throw 'OutputFile must have a .zip extension.' }

function Assert-UnlinkedPackagePath([string]$Path) {
    $ancestor = [IO.Path]::GetFullPath($Path)
    while ($ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and (((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'Package paths must not pass through linked files or directories.'
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
}

Assert-UnlinkedPackagePath $appPath
Assert-UnlinkedPackagePath $archivePath

# Copy only the files required to run the app, even from a private development build.
$allowedNames = @(
    'NasDesktop.exe',
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
$checksumPath = Join-Path $archiveDirectory 'SHA256SUMS.txt'
Assert-UnlinkedPackagePath $checksumPath
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
    # Reopen the finished ZIP: reject extra entries and verify each packaged byte stream.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        if ($archive.Entries.Count -ne $allowedNames.Count) { throw 'Public ZIP file count did not match the allowlist.' }
        $seenNames = @{}
        foreach ($entry in $archive.Entries) {
            if (($allowedNames -cnotcontains $entry.FullName) -or $seenNames.ContainsKey($entry.FullName)) { throw 'Public ZIP contains an unapproved or duplicate entry.' }
            $seenNames[$entry.FullName] = $true
            $stream = $entry.Open()
            $algorithm = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
            finally { $algorithm.Dispose(); $stream.Dispose() }
            $expected = (Get-FileHash -LiteralPath (Join-Path $stagingDirectory $entry.FullName) -Algorithm SHA256).Hash
            if ($actual -cne $expected) { throw "Public ZIP content verification failed: $($entry.FullName)." }
        }
    }
    finally { $archive.Dispose() }
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumTemporary = Join-Path $archiveDirectory ('SHA256SUMS.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($checksumTemporary, $archiveHash + '  ' + [IO.Path]::GetFileName($archivePath) + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
        # PowerShell otherwise binds $null as an empty backup path on newer .NET.
        if (Test-Path -LiteralPath $checksumPath) { [IO.File]::Replace($checksumTemporary, $checksumPath, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($checksumTemporary, $checksumPath) }
    }
    finally { if (Test-Path -LiteralPath $checksumTemporary -PathType Leaf) { Remove-Item -LiteralPath $checksumTemporary -Force } }
    Write-Output "Packaged: $archivePath"
    Write-Output "SHA-256: $checksumPath"
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
