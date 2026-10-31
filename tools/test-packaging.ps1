$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'BuildArtifacts.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot

function Assert-Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "FAIL $Description" }
    Write-Output "PASS $Description"
}

$testLock = $null
$workspace = $null
try {
    $testLock = Enter-NasDeskArtifactLock -ProjectRoot $projectRoot -Name 'packaging-tests'
    $workspace = New-NasDeskWorkspace -ProjectRoot $projectRoot -Kind 'packaging-tests'
    $testRoot = $workspace.Path
    $fixture = Join-Path $testRoot 'fixture'
    $fixtureOutput = Join-Path $fixture 'output'
    $fixturePrivateSettings = Join-Path $fixture 'connection.local.json'
    $version = '1.0.4314.35'

    # A minimal independent project avoids the running app and real private settings.
    New-Item -ItemType Directory -Path (Join-Path $fixture 'src'),(Join-Path $fixture 'tools'),(Join-Path $fixture ".packages\webview2-$version\lib\net462"),(Join-Path $fixture ".packages\webview2-$version\runtimes\win-x64\native") -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'build.ps1'),(Join-Path $projectRoot 'app.manifest') -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Theme.cs') -Destination (Join-Path $fixture 'src')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\IconGenerator.cs') -Destination (Join-Path $fixture 'tools')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\BuildArtifacts.ps1') -Destination (Join-Path $fixture 'tools')
    Set-Content -LiteralPath (Join-Path $fixture 'src\Program.cs') -Encoding Ascii -Value 'internal static class PackagingFixture { private static void Main() { } }'
    Set-Content -LiteralPath $fixturePrivateSettings -Encoding Ascii -Value '{"Url":"https://private-packaging-fixture.invalid:5001/"}'
    $originalSettingsHash = (Get-FileHash -LiteralPath $fixturePrivateSettings -Algorithm SHA256).Hash
    $packageRoot = Join-Path $projectRoot ".packages\webview2-$version"
    Copy-Item -LiteralPath (Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'),(Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.WinForms.dll') -Destination (Join-Path $fixture ".packages\webview2-$version\lib\net462")
    Copy-Item -LiteralPath (Join-Path $packageRoot 'runtimes\win-x64\native\WebView2Loader.dll') -Destination (Join-Path $fixture ".packages\webview2-$version\runtimes\win-x64\native")
    # Verified DLLs make this redundant download unnecessary; it must be discarded even on a warm build.
    $redundantPackage = Join-Path $fixture ".packages\microsoft.web.webview2.$version.zip"
    Set-Content -LiteralPath $redundantPackage -Encoding Ascii -Value 'unused package cache fixture'

    & (Join-Path $fixture 'build.ps1') -OutputDirectory $fixtureOutput -IncludeLocalSettings
    Assert-Check (Test-Path -LiteralPath (Join-Path $fixtureOutput 'connection.local.json')) 'Private build includes local settings only with explicit opt-in'
    & (Join-Path $fixture 'build.ps1') -OutputDirectory $fixtureOutput
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $fixtureOutput 'connection.local.json'))) 'Default public build removes stale bundled settings'
    Assert-Check (((Get-FileHash -LiteralPath $fixturePrivateSettings -Algorithm SHA256).Hash) -eq $originalSettingsHash) 'Original private settings remain unchanged'
    Assert-Check (-not (Test-Path -LiteralPath $redundantPackage)) 'Successful warm builds discard redundant SDK package downloads'
    $sdkRelativeFiles = @('lib\net462\Microsoft.Web.WebView2.Core.dll','lib\net462\Microsoft.Web.WebView2.WinForms.dll','runtimes\win-x64\native\WebView2Loader.dll')
    $sdkCachePreserved = $true
    foreach ($relative in $sdkRelativeFiles) {
        $cachedFile = Join-Path (Join-Path $fixture ".packages\webview2-$version") $relative
        if (-not (Test-Path -LiteralPath $cachedFile -PathType Leaf)) { $sdkCachePreserved = $false; continue }
        if ((Get-FileHash -LiteralPath $cachedFile -Algorithm SHA256).Hash -cne (Get-FileHash -LiteralPath (Join-Path $packageRoot $relative) -Algorithm SHA256).Hash) { $sdkCachePreserved = $false }
    }
    Assert-Check $sdkCachePreserved 'Successful builds retain the current three verified SDK DLLs for reuse'

    $fixtureGenerated = Join-Path $fixture 'test-output\.generated'
    $successfulWorkspaces = @(Get-ChildItem -LiteralPath $fixtureGenerated -Directory -Force | Where-Object { $_.Name -ne '.locks' })
    Assert-Check ($successfulWorkspaces.Count -eq 0) 'Repeated successful builds remove their managed temporary workspaces'

    # Exercise the real build finally block after its workspace and SDK have initialized.
    $fixtureProgram = Join-Path $fixture 'src\Program.cs'
    $originalProgramBytes = [IO.File]::ReadAllBytes($fixtureProgram)
    $validOutputHashes = @{}
    foreach ($name in @('NasDesktop.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) {
        $validOutputHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $fixtureOutput $name) -Algorithm SHA256).Hash
    }
    $failedOutput = Join-Path $fixture 'failure-output'
    $reachedCompilerFailure = $false
    $nativeExitCodeBeforeFailure = $global:LASTEXITCODE
    try {
        Set-Content -LiteralPath $fixtureProgram -Encoding Ascii -Value 'internal static class PackagingFixture { private static void Main() { intentionally invalid C#; } }'
        try { & (Join-Path $fixture 'build.ps1') -OutputDirectory $failedOutput | Out-Null }
        catch { $reachedCompilerFailure = $_.Exception.Message -eq 'Application compilation failed.' }
    }
    finally {
        [IO.File]::WriteAllBytes($fixtureProgram, $originalProgramBytes)
        # The expected native compiler failure has been handled by this test.
        $global:LASTEXITCODE = $nativeExitCodeBeforeFailure
    }
    Assert-Check ($reachedCompilerFailure -and (Test-Path -LiteralPath $failedOutput -PathType Container)) 'Invalid fixture source fails at application compilation after build initialization'
    $failedWorkspaces = @(Get-ChildItem -LiteralPath $fixtureGenerated -Directory -Force | Where-Object { $_.Name -ne '.locks' })
    $validOutputPreserved = $true
    foreach ($name in $validOutputHashes.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $fixtureOutput $name) -Algorithm SHA256).Hash -cne $validOutputHashes[$name]) { $validOutputPreserved = $false }
    }
    Assert-Check (($failedWorkspaces.Count -eq 0) -and $validOutputPreserved -and ((Get-FileHash -LiteralPath $fixturePrivateSettings -Algorithm SHA256).Hash -ceq $originalSettingsHash)) 'Failed builds remove managed temporary workspaces and preserve existing binaries and private settings'

    $rootOutputRejected = $false
    try { & (Join-Path $fixture 'build.ps1') -OutputDirectory $fixture }
    catch { $rootOutputRejected = $_.Exception.Message -like 'OutputDirectory must not be the project root*' }
    Assert-Check $rootOutputRejected 'Build rejects output equal to the project root'
    Assert-Check (((Get-FileHash -LiteralPath $fixturePrivateSettings -Algorithm SHA256).Hash) -eq $originalSettingsHash) 'Rejected output cannot delete original settings'

    $linkedOutput = Join-Path $fixture 'linked-output'
    New-Item -ItemType Junction -Path $linkedOutput -Target $fixture | Out-Null
    try {
        $linkedOutputRejected = $false
        try { & (Join-Path $fixture 'build.ps1') -OutputDirectory $linkedOutput }
        catch { $linkedOutputRejected = $_.Exception.Message -like 'OutputDirectory must not pass through a linked directory*' }
        Assert-Check $linkedOutputRejected 'Build rejects a junction alias of the project root'
        Assert-Check (((Get-FileHash -LiteralPath $fixturePrivateSettings -Algorithm SHA256).Hash) -eq $originalSettingsHash) 'Linked output cannot delete original settings'
    }
    finally {
        if (((Get-Item -LiteralPath $linkedOutput -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) { throw 'Fixture junction identity changed.' }
        [IO.Directory]::Delete($linkedOutput)
    }

    # Deliberately contaminate the folder. Public packaging must still include only four files.
    Copy-Item -LiteralPath $fixturePrivateSettings -Destination $fixtureOutput
    Set-Content -LiteralPath (Join-Path $fixtureOutput 'address-history.json') -Encoding Ascii -Value '{"Entries":[{"Url":"https://private-history-fixture.invalid/","Type":"https"}]}'
    Set-Content -LiteralPath (Join-Path $fixtureOutput 'extra.dll') -Value 'not part of the app'
    New-Item -ItemType Directory -Path (Join-Path $fixtureOutput 'WebView2\Default') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fixtureOutput 'WebView2\Default\Cookies') -Value 'fake cookie data'
    $archivePath = Join-Path $testRoot 'public.zip'
    & (Join-Path $projectRoot 'package.ps1') -AppDirectory $fixtureOutput -OutputFile $archivePath
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $actualNames = @($archive.Entries | ForEach-Object FullName | Sort-Object)
        $expectedNames = @('NasDesktop.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll' | Sort-Object)
        Assert-Check (($actualNames.Count -eq 4) -and (($actualNames -join '|') -eq ($expectedNames -join '|'))) 'Public ZIP contains only the executable and required three DLLs'
        Assert-Check (-not ($actualNames | Where-Object { $_ -match 'connection|history|cookie|profile|extra' })) 'Private settings, address history, browser data and unapproved files are excluded'
    }
    finally { $archive.Dispose() }

    # Repackage into the same ZIP and checksum paths, as normal repeated builds do.
    $firstArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    [IO.File]::AppendAllText((Join-Path $fixtureOutput 'NasDesktop.exe'), 'updated packaging fixture')
    & (Join-Path $projectRoot 'package.ps1') -AppDirectory $fixtureOutput -OutputFile $archivePath
    $updatedArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksum = (Get-Content -LiteralPath (Join-Path $testRoot 'SHA256SUMS.txt') -Raw).Trim()
    Assert-Check (($firstArchiveHash.ToLowerInvariant() -cne $updatedArchiveHash) -and ($checksum -ceq ($updatedArchiveHash + '  public.zip'))) 'Repeated packaging atomically replaces an existing checksum with the updated ZIP digest'
    Assert-Check (-not (Get-ChildItem -LiteralPath $testRoot -Filter 'SHA256SUMS.*.tmp')) 'Repeated packaging leaves no temporary checksum file'
    Assert-Check (-not (Get-ChildItem -LiteralPath $testRoot -Directory -Filter '.package-staging-*')) 'Temporary package staging is removed'
    Assert-Check ((Test-Path -LiteralPath (Join-Path $fixtureOutput 'connection.local.json')) -and (Test-Path -LiteralPath (Join-Path $fixtureOutput 'address-history.json'))) 'Packaging leaves private source settings and address history untouched'
}
finally {
    try {
        if ($null -ne $workspace) { Remove-NasDeskWorkspace -Workspace $workspace }
    }
    finally {
        if ($null -ne $testLock) { $testLock.Dispose() }
    }
}
