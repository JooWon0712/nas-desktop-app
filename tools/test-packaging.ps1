$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $projectRoot ('test-output\packaging-check-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $testRoot 'fixture'
$fixtureOutput = Join-Path $fixture 'output'
$fixturePrivateSettings = Join-Path $fixture 'connection.local.json'
$version = '1.0.4314.35'

function Assert-Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "FAIL $Description" }
    Write-Output "PASS $Description"
}

# A minimal independent project avoids the running app and real private settings.
New-Item -ItemType Directory -Path (Join-Path $fixture 'src'),(Join-Path $fixture 'tools'),(Join-Path $fixture ".packages\webview2-$version\lib\net462"),(Join-Path $fixture ".packages\webview2-$version\runtimes\win-x64\native") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'build.ps1'),(Join-Path $projectRoot 'app.manifest') -Destination $fixture
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Theme.cs') -Destination (Join-Path $fixture 'src')
Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\IconGenerator.cs') -Destination (Join-Path $fixture 'tools')
Set-Content -LiteralPath (Join-Path $fixture 'src\Program.cs') -Encoding Ascii -Value 'internal static class PackagingFixture { private static void Main() { } }'
Set-Content -LiteralPath $fixturePrivateSettings -Encoding Ascii -Value '{"Url":"https://private-packaging-fixture.invalid:5001/"}'
$originalSettingsHash = (Get-FileHash -LiteralPath $fixturePrivateSettings -Algorithm SHA256).Hash
$packageRoot = Join-Path $projectRoot ".packages\webview2-$version"
Copy-Item -LiteralPath (Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'),(Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.WinForms.dll') -Destination (Join-Path $fixture ".packages\webview2-$version\lib\net462")
Copy-Item -LiteralPath (Join-Path $packageRoot 'runtimes\win-x64\native\WebView2Loader.dll') -Destination (Join-Path $fixture ".packages\webview2-$version\runtimes\win-x64\native")

& (Join-Path $fixture 'build.ps1') -OutputDirectory $fixtureOutput -IncludeLocalSettings
Assert-Check (Test-Path -LiteralPath (Join-Path $fixtureOutput 'connection.local.json')) 'Private build includes local settings only with explicit opt-in'
& (Join-Path $fixture 'build.ps1') -OutputDirectory $fixtureOutput
Assert-Check (-not (Test-Path -LiteralPath (Join-Path $fixtureOutput 'connection.local.json'))) 'Default public build removes stale bundled settings'
Assert-Check (((Get-FileHash -LiteralPath $fixturePrivateSettings -Algorithm SHA256).Hash) -eq $originalSettingsHash) 'Original private settings remain unchanged'

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
    Remove-Item -LiteralPath $linkedOutput -Force
}

# Deliberately contaminate the folder. Public packaging must still include only four files.
Copy-Item -LiteralPath $fixturePrivateSettings -Destination $fixtureOutput
Set-Content -LiteralPath (Join-Path $fixtureOutput 'extra.dll') -Value 'not part of the app'
New-Item -ItemType Directory -Path (Join-Path $fixtureOutput 'WebView2\Default') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $fixtureOutput 'WebView2\Default\Cookies') -Value 'fake cookie data'
$archivePath = Join-Path $testRoot 'public.zip'
& (Join-Path $projectRoot 'package.ps1') -AppDirectory $fixtureOutput -OutputFile $archivePath
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $actualNames = @($archive.Entries | ForEach-Object FullName | Sort-Object)
    $expectedNames = @('SynologyNasDesktop.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll' | Sort-Object)
    Assert-Check (($actualNames.Count -eq 4) -and (($actualNames -join '|') -eq ($expectedNames -join '|'))) 'Public ZIP contains only the executable and required three DLLs'
    Assert-Check (-not ($actualNames | Where-Object { $_ -match 'connection|cookie|profile|extra' })) 'Private settings, browser data and unapproved files are excluded'
}
finally { $archive.Dispose() }
Assert-Check (-not (Get-ChildItem -LiteralPath $testRoot -Directory -Filter '.package-staging-*')) 'Temporary package staging is removed'
Assert-Check (Test-Path -LiteralPath (Join-Path $fixtureOutput 'connection.local.json')) 'Packaging leaves the private source folder untouched'
