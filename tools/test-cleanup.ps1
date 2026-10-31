$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'BuildArtifacts.ps1')

function Assert-CleanupCheck([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "FAIL $Description" }
    Write-Output "PASS $Description"
}

function Invoke-ExpectedCleanupRefusal([scriptblock]$Action) {
    # Refusal may throw or warn: the assertions verify that no protected data moved.
    $previousWarningPreference = $WarningPreference
    $WarningPreference = 'SilentlyContinue'
    try { & $Action | Out-Null }
    catch { }
    finally { $WarningPreference = $previousWarningPreference }
}

function Remove-CleanupFixtureJunction([string]$Path, [string]$AllowedRoot) {
    $absolute = [IO.Path]::GetFullPath($Path)
    $boundary = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\')
    if (-not $absolute.StartsWith($boundary + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture junction escaped its temporary workspace.' }
    if (((Get-Item -LiteralPath $absolute -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) { throw 'Fixture junction identity changed.' }
    # The Windows PowerShell 5.1 provider can fail to unlink junctions. This
    # nonrecursive API removes only the checked junction, never its target.
    [IO.Directory]::Delete($absolute)
}

$outer = New-NasDeskWorkspace -ProjectRoot $projectRoot -Kind 'cleanup-tests'
$fixtureRoot = Join-Path $outer.Path 'fixture'
$outside = Join-Path $outer.Path 'outside'
$owned = @()
$openStreams = @()
$junctions = @()
try {
    New-Item -ItemType Directory -Path $fixtureRoot,$outside -Force | Out-Null
    $outsideSentinel = Join-Path $outside 'keep.txt'
    Set-Content -LiteralPath $outsideSentinel -Value 'outside workspace sentinel' -Encoding Ascii
    $outsideHash = (Get-FileHash -LiteralPath $outsideSentinel).Hash
    $privateSettings = Join-Path $fixtureRoot 'connection.local.json'
    Set-Content -LiteralPath $privateSettings -Value 'private fixture settings' -Encoding Ascii
    $privateHash = (Get-FileHash -LiteralPath $privateSettings).Hash
    $backup = Join-Path $fixtureRoot 'test-output\actual-tray-fix-fixture\restore.reg'
    New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
    Set-Content -LiteralPath $backup -Value 'registry recovery fixture' -Encoding Ascii
    $backupHash = (Get-FileHash -LiteralPath $backup).Hash

    $lock = Enter-NasDeskArtifactLock -ProjectRoot $fixtureRoot -Name 'build'
    try {
        $exclusive = $false
        try {
            $unexpectedLock = [IO.File]::Open($lock.Name, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $unexpectedLock.Dispose()
        }
        catch [IO.IOException] { $exclusive = $true }
        Assert-CleanupCheck $exclusive 'Operation locks prevent concurrent access to the same generated output'
        $otherLock = Enter-NasDeskArtifactLock -ProjectRoot $fixtureRoot -Name 'tests'
        try { Assert-CleanupCheck ($null -ne $otherLock) 'Independent operation locks can coexist' }
        finally { $otherLock.Dispose() }
    }
    finally { $lock.Dispose() }

    $invalidLockRejected = $false
    try {
        $invalidLock = Enter-NasDeskArtifactLock -ProjectRoot $fixtureRoot -Name '..\outside'
        $invalidLock.Dispose()
    }
    catch { $invalidLockRejected = $true }
    Assert-CleanupCheck $invalidLockRejected 'Lock names cannot escape the managed lock directory'

    $first = New-NasDeskWorkspace -ProjectRoot $fixtureRoot -Kind 'tests'
    $owned += $first
    $generatedRoot = Join-Path $fixtureRoot 'test-output\.generated'
    Assert-CleanupCheck ($first.Path.StartsWith($generatedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) 'Generated workspaces stay inside the project temporary subtree'
    Assert-CleanupCheck (([IO.File]::ReadAllText((Join-Path $first.Path '.nasdesk-generated'))).Trim() -ceq 'nas-desktop-app:generated-workspace:v1') 'Generated workspaces carry the required ownership marker'
    Set-Content -LiteralPath (Join-Path $first.Path 'active.txt') -Value 'active' -Encoding Ascii
    $second = New-NasDeskWorkspace -ProjectRoot $fixtureRoot -Kind 'build'
    $owned += $second
    Invoke-NasDeskWorkspaceCleanup -ProjectRoot $fixtureRoot
    Assert-CleanupCheck ((Test-Path -LiteralPath (Join-Path $first.Path 'active.txt')) -and (Test-Path -LiteralPath $second.Path)) 'Cleanup preserves all workspaces with active leases'

    Remove-NasDeskWorkspace -Workspace $second
    Assert-CleanupCheck ((-not (Test-Path -LiteralPath $second.Path)) -and (-not (Test-Path -LiteralPath $second.LeasePath))) 'Completed workspace cleanup removes its files and lease'

    $abandoned = New-NasDeskWorkspace -ProjectRoot $fixtureRoot -Kind 'packaging-tests'
    $owned += $abandoned
    Set-Content -LiteralPath (Join-Path $abandoned.Path 'stale-cache.bin') -Value 'stale' -Encoding Ascii
    $abandoned.Lease.Dispose()
    Invoke-NasDeskWorkspaceCleanup -ProjectRoot $fixtureRoot
    Assert-CleanupCheck ((-not (Test-Path -LiteralPath $abandoned.Path)) -and (-not (Test-Path -LiteralPath $abandoned.LeasePath))) 'The next invocation reclaims an abandoned marked workspace'

    $failedRun = New-NasDeskWorkspace -ProjectRoot $fixtureRoot -Kind 'tests'
    $owned += $failedRun
    $expectedFailure = $false
    try {
        try {
            Set-Content -LiteralPath (Join-Path $failedRun.Path 'temporary-profile.bin') -Value 'fixture profile' -Encoding Ascii
            throw 'intentional fixture failure'
        }
        finally { Remove-NasDeskWorkspace -Workspace $failedRun }
    }
    catch { $expectedFailure = $_.Exception.Message -eq 'intentional fixture failure' }
    Assert-CleanupCheck ($expectedFailure -and (-not (Test-Path -LiteralPath $failedRun.Path))) 'Failure cleanup removes temporary data without replacing the original error'

    $unmarked = Join-Path $generatedRoot ('tests-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $unknown = Join-Path $generatedRoot 'personal-backup'
    New-Item -ItemType Directory -Path $unmarked,$unknown -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $unmarked 'keep.txt') -Value 'unmarked' -Encoding Ascii
    Set-Content -LiteralPath (Join-Path $unknown '.nasdesk-generated') -Value 'nas-desktop-app:generated-workspace:v1' -Encoding Ascii
    Invoke-NasDeskWorkspaceCleanup -ProjectRoot $fixtureRoot
    Assert-CleanupCheck ((Test-Path -LiteralPath (Join-Path $unmarked 'keep.txt')) -and (Test-Path -LiteralPath $unknown)) 'Cleanup preserves unmarked directories and unknown directory names'
    Assert-CleanupCheck (((Get-FileHash -LiteralPath $privateSettings).Hash -ceq $privateHash) -and ((Get-FileHash -LiteralPath $backup).Hash -ceq $backupHash)) 'Project settings and manual recovery backups remain unchanged'

    $tampered = $first.PSObject.Copy()
    $tampered.Path = $outside
    Invoke-ExpectedCleanupRefusal { Remove-NasDeskWorkspace -Workspace $tampered }
    Assert-CleanupCheck (((Get-FileHash -LiteralPath $outsideSentinel).Hash -ceq $outsideHash) -and (Test-Path -LiteralPath $first.Path)) 'A tampered workspace path cannot delete data outside the generated subtree'

    $linked = New-NasDeskWorkspace -ProjectRoot $fixtureRoot -Kind 'tests'
    $owned += $linked
    $junction = Join-Path $linked.Path 'profile-link'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    $junctions += $junction
    Invoke-ExpectedCleanupRefusal { Remove-NasDeskWorkspace -Workspace $linked }
    Assert-CleanupCheck (((Get-FileHash -LiteralPath $outsideSentinel).Hash -ceq $outsideHash) -and (Test-Path -LiteralPath $junction)) 'A linked descendant blocks recursive cleanup before external data can be touched'
    Remove-CleanupFixtureJunction -Path $junction -AllowedRoot $outer.Path
    Remove-NasDeskWorkspace -Workspace $linked

    $locked = New-NasDeskWorkspace -ProjectRoot $fixtureRoot -Kind 'tests'
    $owned += $locked
    $lockedFile = Join-Path $locked.Path 'locked-profile.bin'
    [IO.File]::WriteAllText($lockedFile, 'locked fixture')
    $fileLease = [IO.File]::Open($lockedFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    $openStreams += $fileLease
    Invoke-ExpectedCleanupRefusal { Remove-NasDeskWorkspace -Workspace $locked }
    Assert-CleanupCheck ((Test-Path -LiteralPath $locked.Path) -and (Test-Path -LiteralPath (Join-Path $locked.Path '.nasdesk-generated'))) 'A temporarily locked profile retains its ownership marker for a later retry'
    $fileLease.Dispose()
    Invoke-NasDeskWorkspaceCleanup -ProjectRoot $fixtureRoot
    Assert-CleanupCheck (-not (Test-Path -LiteralPath $locked.Path)) 'Deferred profile cleanup succeeds after the file lock is released'

    $cache = Join-Path $fixtureRoot '.packages'
    $currentVersion = '1.0.4314.35'
    $oldVersion = '1.0.1111.22'
    $currentSdk = Join-Path $cache ('webview2-' + $currentVersion)
    $oldSdk = Join-Path $cache ('webview2-' + $oldVersion)
    $unmarkedSdk = Join-Path $cache 'webview2-1.0.2222.33'
    $unknownCache = Join-Path $cache 'private-cache'
    New-Item -ItemType Directory -Path $currentSdk,$oldSdk,$unmarkedSdk,$unknownCache -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $currentSdk '.nasdesk-sdk-cache'), 'nas-desktop-app:sdk-cache:v1:' + $currentVersion)
    [IO.File]::WriteAllText((Join-Path $oldSdk '.nasdesk-sdk-cache'), 'nas-desktop-app:sdk-cache:v1:' + $oldVersion)
    [IO.File]::WriteAllText((Join-Path $currentSdk 'keep.dll'), 'current pinned SDK fixture')
    [IO.File]::WriteAllText((Join-Path $oldSdk 'old.dll'), 'old SDK fixture')
    Clear-NasDeskSdkCache -ProjectRoot $fixtureRoot -Version $currentVersion
    Assert-CleanupCheck ((-not (Test-Path -LiteralPath $oldSdk)) -and (Test-Path -LiteralPath (Join-Path $currentSdk 'keep.dll'))) 'SDK cleanup removes only marked obsolete versions and retains the current version'
    Assert-CleanupCheck ((Test-Path -LiteralPath $unmarkedSdk) -and (Test-Path -LiteralPath $unknownCache)) 'SDK cleanup preserves unmarked or unrelated cache directories'

    $invalidVersionRejected = $false
    try { Clear-NasDeskSdkCache -ProjectRoot $fixtureRoot -Version '..\outside' }
    catch { $invalidVersionRejected = $true }
    Assert-CleanupCheck $invalidVersionRejected 'An invalid SDK version cannot redirect cache cleanup'

    for ($iteration = 0; $iteration -lt 2; $iteration++) {
        $repeated = New-NasDeskWorkspace -ProjectRoot $fixtureRoot -Kind 'tests'
        $owned += $repeated
        Set-Content -LiteralPath (Join-Path $repeated.Path 'temporary.bin') -Value 'fixture' -Encoding Ascii
        Remove-NasDeskWorkspace -Workspace $repeated
        Assert-CleanupCheck (-not (Test-Path -LiteralPath $repeated.Path)) ('Repeated run ' + ($iteration + 1) + ' leaves no completed workspace behind')
    }
    Assert-CleanupCheck (((Get-FileHash -LiteralPath $outsideSentinel).Hash -ceq $outsideHash) -and ((Get-FileHash -LiteralPath $privateSettings).Hash -ceq $privateHash) -and ((Get-FileHash -LiteralPath $backup).Hash -ceq $backupHash)) 'All cleanup scenarios leave external data, private settings and recovery backups intact'
}
finally {
    foreach ($stream in $openStreams) { $stream.Dispose() }
    foreach ($junction in $junctions) {
        if (Test-Path -LiteralPath $junction) {
            Remove-CleanupFixtureJunction -Path $junction -AllowedRoot $outer.Path
        }
    }
    foreach ($workspace in $owned) {
        if ($null -ne $workspace.Lease) { $workspace.Lease.Dispose() }
        if (Test-Path -LiteralPath $workspace.Path) { Remove-NasDeskWorkspace -Workspace $workspace }
    }
    Remove-NasDeskWorkspace -Workspace $outer
}
