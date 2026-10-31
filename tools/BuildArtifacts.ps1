# Temporary artifacts are owned explicitly; never infer ownership from old output names.
function Assert-NasDeskArtifactPath([string]$Path, [string]$AllowedRoot, [switch]$Recurse) {
    $absolute = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $boundary = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\')
    if (-not $absolute.StartsWith($boundary + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Generated artifact path must be a child of its allowed root.'
    }
    $ancestor = $absolute
    while ($ancestor) {
        $ancestorItem = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
        if ($null -ne $ancestorItem -and (($ancestorItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'Generated artifact paths must not pass through links.'
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    if ($Recurse -and (Test-Path -LiteralPath $absolute -PathType Container)) {
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($absolute)
        while ($pending.Count -gt 0) {
            foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw 'Generated artifact directories must not contain links.'
                }
                if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            }
        }
    }
    return $absolute
}

function Get-NasDeskArtifactRoot([string]$ProjectRoot) {
    $root = Assert-NasDeskArtifactPath (Join-Path $ProjectRoot 'test-output\.generated') $ProjectRoot
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $locks = Assert-NasDeskArtifactPath (Join-Path $root '.locks') $root
    New-Item -ItemType Directory -Path $locks -Force | Out-Null
    return $root
}

function Enter-NasDeskArtifactLock {
    param([string]$ProjectRoot, [ValidateSet('build','tests','packaging-tests','cleanup')] [string]$Name)
    $root = Get-NasDeskArtifactRoot $ProjectRoot
    $path = Assert-NasDeskArtifactPath (Join-Path $root ('.locks\' + $Name + '.lock')) $root
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        try { return [IO.File]::Open($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
        catch [IO.IOException] { if ($attempt -eq 99) { throw "Another $Name operation is still using the generated artifacts. Retry after it finishes." }; Start-Sleep -Milliseconds 100 }
    }
}

function Test-NasDeskWorkspaceMarker([string]$Path, [string]$Root) {
    $absolute = Assert-NasDeskArtifactPath $Path $Root
    if ([IO.Path]::GetDirectoryName($absolute) -ine $Root -or [IO.Path]::GetFileName($absolute) -notmatch '^(build|tests|packaging-tests|cleanup-tests)-[a-f0-9]{8}$') { return $false }
    $marker = Assert-NasDeskArtifactPath (Join-Path $absolute '.nasdesk-generated') $Root
    return (Test-Path -LiteralPath $marker -PathType Leaf) -and (Get-Item -LiteralPath $marker).Length -lt 128 -and [IO.File]::ReadAllText($marker) -ceq 'nas-desktop-app:generated-workspace:v1'
}

function Remove-NasDeskWorkspaceDirectory([string]$Path, [string]$Root) {
    $absolute = Assert-NasDeskArtifactPath $Path $Root -Recurse
    if (-not (Test-NasDeskWorkspaceMarker $absolute $Root)) { throw 'Refusing to remove a workspace without a valid ownership marker.' }
    # Leave the marker until last so partially locked directories remain recoverable.
    $marker = Join-Path $absolute '.nasdesk-generated'
    foreach ($child in Get-ChildItem -LiteralPath $absolute -Force | Where-Object { $_.Name -cne '.nasdesk-generated' }) {
        $target = Assert-NasDeskArtifactPath $child.FullName $absolute -Recurse
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
    }
    Remove-Item -LiteralPath $marker -Force -ErrorAction Stop
    try { Remove-Item -LiteralPath $absolute -Force -ErrorAction Stop }
    catch {
        if (Test-Path -LiteralPath $absolute -PathType Container) {
            Assert-NasDeskArtifactPath $absolute $Root | Out-Null
            [IO.File]::WriteAllText($marker, 'nas-desktop-app:generated-workspace:v1')
        }
        throw
    }
}

function Invoke-NasDeskWorkspaceCleanup([string]$ProjectRoot) {
    $root = Get-NasDeskArtifactRoot $ProjectRoot
    $cleanupLock = Enter-NasDeskArtifactLock -ProjectRoot $ProjectRoot -Name cleanup
    try {
        foreach ($directory in Get-ChildItem -LiteralPath $root -Directory -Force) {
            if ($directory.Name -notmatch '^(build|tests|packaging-tests|cleanup-tests)-[a-f0-9]{8}$') { continue }
            $lease = $null
            $leasePath = $null
            try {
                if (-not (Test-NasDeskWorkspaceMarker $directory.FullName $root)) { continue }
                $leasePath = Assert-NasDeskArtifactPath (Join-Path $root ('.locks\' + $directory.Name + '.lock')) $root
                try { $lease = [IO.File]::Open($leasePath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
                catch [IO.IOException] { continue } # A live build/test owns this workspace.
                Remove-NasDeskWorkspaceDirectory $directory.FullName $root
            }
            catch { Write-Warning ('Temporary workspace retained for a later cleanup: ' + $directory.Name + '. ' + $_.Exception.Message) }
            finally {
                if ($null -ne $lease) {
                    $lease.Dispose()
                    if (-not (Test-Path -LiteralPath $directory.FullName)) { Remove-Item -LiteralPath $leasePath -Force -ErrorAction SilentlyContinue }
                }
            }
        }
        # A process may have stopped between creating its lease and its directory.
        foreach ($lockFile in Get-ChildItem -LiteralPath (Join-Path $root '.locks') -File -Force) {
            if ($lockFile.Name -notmatch '^(build|tests|packaging-tests|cleanup-tests)-[a-f0-9]{8}\.lock$') { continue }
            $workspacePath = Join-Path $root ([IO.Path]::GetFileNameWithoutExtension($lockFile.Name))
            if (Test-Path -LiteralPath $workspacePath) { continue }
            $orphanLease = $null
            $lockPath = Assert-NasDeskArtifactPath $lockFile.FullName $root
            try { $orphanLease = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
            catch [IO.IOException] { continue }
            finally { if ($null -ne $orphanLease) { $orphanLease.Dispose() } }
            Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
        }
    }
    finally { $cleanupLock.Dispose() }
}

function New-NasDeskWorkspace {
    param([string]$ProjectRoot, [ValidateSet('build','tests','packaging-tests','cleanup-tests')] [string]$Kind)
    Invoke-NasDeskWorkspaceCleanup $ProjectRoot
    $root = Get-NasDeskArtifactRoot $ProjectRoot
    $cleanupLock = Enter-NasDeskArtifactLock -ProjectRoot $ProjectRoot -Name cleanup
    $lease = $null
    try {
        do {
            $name = $Kind + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
            $path = Assert-NasDeskArtifactPath (Join-Path $root $name) $root
            $leasePath = Assert-NasDeskArtifactPath (Join-Path $root ('.locks\' + $name + '.lock')) $root
        } while ((Test-Path -LiteralPath $path) -or (Test-Path -LiteralPath $leasePath))
        $lease = [IO.File]::Open($leasePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        New-Item -ItemType Directory -Path $path | Out-Null
        [IO.File]::WriteAllText((Join-Path $path '.nasdesk-generated'), 'nas-desktop-app:generated-workspace:v1')
        return [pscustomobject]@{ Path = $path; ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot); LeasePath = $leasePath; Lease = $lease }
    }
    catch { if ($null -ne $lease) { $lease.Dispose() }; throw }
    finally { $cleanupLock.Dispose() }
}

function Remove-NasDeskWorkspace($Workspace) {
    if ($null -eq $Workspace) { return }
    $root = Get-NasDeskArtifactRoot $Workspace.ProjectRoot
    $path = Assert-NasDeskArtifactPath $Workspace.Path $root
    if ([IO.Path]::GetDirectoryName($path) -ine $root -or [IO.Path]::GetFileName($path) -notmatch '^(build|tests|packaging-tests|cleanup-tests)-[a-f0-9]{8}$') {
        throw 'Workspace identity does not match the generated subtree.'
    }
    $expectedLease = Join-Path $root ('.locks\' + [IO.Path]::GetFileName($path) + '.lock')
    if ([IO.Path]::GetFullPath($Workspace.LeasePath) -ine $expectedLease) { throw 'Workspace lease does not match its path.' }
    if ($null -ne $Workspace.Lease -and [IO.Path]::GetFullPath($Workspace.Lease.Name) -ine $expectedLease) { throw 'Workspace lease handle does not match its path.' }
    if ($null -ne $Workspace.Lease) { $Workspace.Lease.Dispose(); $Workspace.Lease = $null }
    # Cleanup failures must not hide the build/test's original result.
    try { Invoke-NasDeskWorkspaceCleanup $Workspace.ProjectRoot }
    catch { Write-Warning ('Automatic temporary artifact cleanup was deferred. ' + $_.Exception.Message) }
}

function Clear-NasDeskSdkCache {
    param([string]$ProjectRoot, [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')] [string]$Version)
    $cache = Assert-NasDeskArtifactPath (Join-Path $ProjectRoot '.packages') $ProjectRoot
    if (-not (Test-Path -LiteralPath $cache -PathType Container)) { return }
    foreach ($item in Get-ChildItem -LiteralPath $cache -Force) {
        if ($item.PSIsContainer -and $item.Name -match '^webview2-(\d+\.\d+\.\d+\.\d+)$') {
            $oldVersion = $Matches[1]
            if ($oldVersion -eq $Version) { continue }
            $marker = Assert-NasDeskArtifactPath (Join-Path $item.FullName '.nasdesk-sdk-cache') $cache
            if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Item -LiteralPath $marker).Length -gt 128 -or [IO.File]::ReadAllText($marker) -cne ('nas-desktop-app:sdk-cache:v1:' + $oldVersion)) { continue }
        }
        elseif ($item.PSIsContainer -or ($item.Name -notmatch '^microsoft\.web\.webview2\.\d+\.\d+\.\d+\.\d+\.zip$' -and $item.Name -notin @('webview2.zip','IconGenerator.exe','app.ico'))) { continue }
        $target = Assert-NasDeskArtifactPath $item.FullName $cache -Recurse
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
    }
}
