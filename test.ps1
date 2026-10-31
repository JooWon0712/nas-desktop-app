param([ValidateRange(30,300)] [int]$TimeoutSeconds = 120)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\BuildArtifacts.ps1')
$testLock = Enter-NasDeskArtifactLock -ProjectRoot $PSScriptRoot -Name tests
$output = Join-Path $PSScriptRoot 'test-output'
$resultsPath = Join-Path $output 'results.txt'
$workspace = $null
$process = $null
$runResultsPath = $null
$runStdout = $null
$runStderr = $null
$succeeded = $false
$runFailure = 'Test run did not complete.'
try {
    $workspace = New-NasDeskWorkspace -ProjectRoot $PSScriptRoot -Kind tests
    $runOutput = Join-Path $workspace.Path 'data'
    New-Item -ItemType Directory -Path $runOutput | Out-Null
    $runResultsPath = Join-Path $runOutput 'results.txt'
    $runStdout = Join-Path $workspace.Path 'stdout.txt'
    $runStderr = Join-Path $workspace.Path 'stderr.txt'
    Set-Content -LiteralPath $resultsPath -Value "FAIL $runFailure" -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $output 'stdout.txt') -Value '' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $output 'stderr.txt') -Value '' -Encoding UTF8
    $appOutput = Join-Path $workspace.Path 'app'
    & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $appOutput
    $packageRoot = Join-Path $PSScriptRoot '.packages\webview2-1.0.4314.35'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $arguments = @('/nologo','/target:exe','/platform:x64','/optimize+','/utf8output','/codepage:65001','/main:NasDesk.Tests',"/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')", "/out:$(Join-Path $appOutput 'NasDesk.Tests.exe')")
    $references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Security.dll','System.Windows.Forms.dll','System.Web.Extensions.dll',(Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'),(Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'))
    $arguments += $references | ForEach-Object { "/reference:$_" }
    $arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
    $arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    $taskArguments = '"{0}" "{1}"' -f $runOutput,(Join-Path $PSScriptRoot 'tests\fixtures')
    $process = Start-Process -FilePath (Join-Path $appOutput 'NasDesk.Tests.exe') -ArgumentList $taskArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $runStdout -RedirectStandardError $runStderr
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -ge $deadline) { Stop-Process -Id $process.Id; $process.WaitForExit(); throw 'Tests timed out.' }
    }
    $process.WaitForExit() # Finish redirected stream callbacks before copying/removing logs.
    Get-Content -LiteralPath $runStdout
    if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $runStderr; throw "Tests failed with exit code $($process.ExitCode)." }
    if (-not (Test-Path -LiteralPath $runResultsPath)) { throw 'Tests did not produce a results file.' }
    $succeeded = $true
}
catch {
    $runFailure = $_.Exception.Message
    throw
}
finally {
    try {
        if ($null -ne $process) {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue; $process.WaitForExit() }
            $process.Dispose()
        }
        if ($runResultsPath -and (Test-Path -LiteralPath $runResultsPath)) {
            Copy-Item -LiteralPath $runResultsPath -Destination $resultsPath -Force
            if (-not $succeeded) { Add-Content -LiteralPath $resultsPath -Value "FAIL $runFailure" -Encoding UTF8 }
        }
        else { Set-Content -LiteralPath $resultsPath -Value "FAIL $runFailure" -Encoding UTF8 }
        if ($runStdout -and (Test-Path -LiteralPath $runStdout)) { Copy-Item -LiteralPath $runStdout -Destination (Join-Path $output 'stdout.txt') -Force }
        if ($runStderr -and (Test-Path -LiteralPath $runStderr)) { Copy-Item -LiteralPath $runStderr -Destination (Join-Path $output 'stderr.txt') -Force }
    }
    finally {
        try { Remove-NasDeskWorkspace -Workspace $workspace }
        finally { $testLock.Dispose() }
    }
}
