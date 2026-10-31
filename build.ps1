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
$packageRoot = Join-Path $PSScriptRoot ".packages\webview2-$version"
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'))) {
    New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot '.packages') -Force | Out-Null
    $zip = Join-Path $PSScriptRoot '.packages\webview2.zip'
    Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/microsoft.web.webview2.$version.nupkg" -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $packageRoot -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$iconTool = Join-Path $PSScriptRoot '.packages\IconGenerator.exe'
$icon = Join-Path $PSScriptRoot '.packages\app.ico'
& $compiler /nologo /target:exe "/out:$iconTool" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'src\Theme.cs') (Join-Path $PSScriptRoot 'tools\IconGenerator.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon tool compilation failed.' }
& $iconTool $icon
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll')
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
