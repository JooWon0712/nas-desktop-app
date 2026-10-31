$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Path $output -Force | Out-Null
do {
    $runName = 'r-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $runOutput = Join-Path $output $runName
} while (Test-Path -LiteralPath $runOutput)
New-Item -ItemType Directory -Path $runOutput | Out-Null
$resultsPath = Join-Path $output 'results.txt'
$runResultsPath = Join-Path $runOutput 'results.txt'
$runFailure = 'Test run did not complete.'
Set-Content -LiteralPath $resultsPath -Value "FAIL $runFailure [run=$runName]" -Encoding UTF8
Set-Content -LiteralPath (Join-Path $output 'stdout.txt') -Value '' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $output 'stderr.txt') -Value '' -Encoding UTF8
try {
    $appOutput = Join-Path $output 'app'
    & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $appOutput
    $packageRoot = Join-Path $PSScriptRoot '.packages\webview2-1.0.4314.35'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    # Keep the executable and DLL locations stable; only fixture data is unique.
    $arguments = @('/nologo','/target:exe','/platform:x64','/optimize+','/utf8output','/codepage:65001','/main:NasDesk.Tests',"/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')", "/out:$(Join-Path $output 'NasDesk.Tests.exe')")
    $references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Security.dll','System.Windows.Forms.dll','System.Web.Extensions.dll',(Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'),(Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'))
    $arguments += $references | ForEach-Object { "/reference:$_" }
    $arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
    $arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Get-ChildItem -LiteralPath $appOutput -Filter '*.dll' | Copy-Item -Destination $output -Force
    $taskArguments = '"{0}" "{1}"' -f $runOutput,(Join-Path $PSScriptRoot 'tests\fixtures')
    $process = Start-Process -FilePath (Join-Path $output 'NasDesk.Tests.exe') -ArgumentList $taskArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'stdout.txt') -RedirectStandardError (Join-Path $output 'stderr.txt')
    if (-not $process.WaitForExit(60000)) { Stop-Process -Id $process.Id; throw 'Tests timed out.' }
    Get-Content -LiteralPath (Join-Path $output 'stdout.txt')
    if ($process.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $output 'stderr.txt'); throw "Tests failed with exit code $($process.ExitCode)." }
    if (-not (Test-Path -LiteralPath $runResultsPath)) { throw 'Tests did not produce a results file.' }
}
catch {
    $runFailure = $_.Exception.Message
    throw
}
finally {
    if (Test-Path -LiteralPath $runResultsPath) {
        Copy-Item -LiteralPath $runResultsPath -Destination $resultsPath -Force
    }
    else {
        Set-Content -LiteralPath $resultsPath -Value "FAIL $runFailure [run=$runName]" -Encoding UTF8
    }
}
