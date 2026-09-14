# Run only on a disposable Windows test machine with permission to create SMB shares.
# All data, ACL changes and shares below belong exclusively to this test.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = Split-Path $PSScriptRoot -Parent
$work = Join-Path $repo ('artifacts/storage-' + [Guid]::NewGuid().ToString('N'))
$share = 'MiniconTest' + [Guid]::NewGuid().ToString('N')
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$data = Join-Path $work 'share/cleanup'
$journal = Join-Path $work 'journal'
$app = Join-Path $work 'app'
$outside = Join-Path $work 'outside'
$lock = $null
$originalDirectoryAcl = $null
$originalFileAcl = $null
function OldFile([string] $path) {
    [IO.File]::WriteAllText($path, 'test data')
    [IO.File]::SetLastWriteTimeUtc($path, [DateTime]::Parse('2020-01-01T00:00:00Z').ToUniversalTime())
}
function Execute([int] $expected) {
    & dotnet (Join-Path $app 'Minicon.FileCleanUp.Console.dll')
    if ($LASTEXITCODE -ne $expected) { throw "Expected exit $expected, got $LASTEXITCODE" }
}
try {
    New-Item -ItemType Directory -Force $data, $journal, $outside | Out-Null
    & dotnet publish (Join-Path $repo 'samples/Minicon.FileCleanUp.Console') -c Release -o $app
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    $settings = @{
        CleanupHost = @{ StateDirectory = (Join-Path $work 'state') }
        FileCleanUp = @{
            DryRun = $false
            Audit = @{ Mode = 'Required'; JournalDirectory = $journal }
            Resilience = @{ MaxAttempts = 1 }
            Rules = @(@{ Name = 'SMB'; RetentionDays = 90; Recursive = $true; RemoveEmptyDirectories = $true; Directories = @(@{ Root = "\\localhost\$share\cleanup" }) })
        }
    }
    $settings | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $app 'appsettings.json')
    New-SmbShare -Name $share -Path (Join-Path $work 'share') -FullAccess $identity | Out-Null
    $old = Join-Path $data 'old.csv'
    OldFile $old
    Execute 0
    if (Test-Path $old) { throw 'Online SMB deletion failed' }
    OldFile $old
    Remove-SmbShare -Name $share -Force
    Execute 1
    if (!(Test-Path $old)) { throw 'Unavailable share must not delete data' }
    New-SmbShare -Name $share -Path (Join-Path $work 'share') -FullAccess $identity | Out-Null
    Execute 0
    if (Test-Path $old) { throw 'Restart after share recovery did not clean up' }

    # Local Windows deletion semantics: a locked file must survive and work on restart.
    $settings.FileCleanUp.Rules[0].Directories[0].Root = $data
    $settings | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $app 'appsettings.json')
    OldFile $old
    $lock = [IO.File]::Open($old, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    Execute 1
    if (!(Test-Path $old)) { throw 'Locked file was deleted' }
    $lock.Dispose(); $lock = $null
    Execute 0
    if (Test-Path $old) { throw 'Unlocked file was not deleted on restart' }

    # Explicit deny permissions must produce a partial result without changing ACLs.
    OldFile $old
    $originalDirectoryAcl = Get-Acl $data
    $originalFileAcl = Get-Acl $old
    $directoryAcl = Get-Acl $data
    $directoryAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles, [Security.AccessControl.AccessControlType]::Deny))
    Set-Acl -Path $data -AclObject $directoryAcl
    $fileAcl = Get-Acl $old
    $fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, [Security.AccessControl.FileSystemRights]::Delete, [Security.AccessControl.AccessControlType]::Deny))
    Set-Acl -Path $old -AclObject $fileAcl
    Execute 1
    if (!(Test-Path $old)) { throw 'ACL-protected file was deleted' }
    Set-Acl -Path $old -AclObject $originalFileAcl; $originalFileAcl = $null
    Set-Acl -Path $data -AclObject $originalDirectoryAcl; $originalDirectoryAcl = $null
    Execute 0
    if (Test-Path $old) { throw 'File was not removed after restoring permissions' }

    $protectedFile = Join-Path $outside 'protected.csv'
    OldFile $protectedFile
    New-Item -ItemType Junction -Path (Join-Path $data 'junction') -Target $outside | Out-Null
    Execute 0
    if (!(Test-Path $protectedFile)) { throw 'Junction target was traversed' }
    # Remove the link explicitly; never recursively traverse it during test cleanup.
    [IO.Directory]::Delete((Join-Path $data 'junction'))
    Write-Host 'PASS: SMB unavailable/recovered, real Windows sharing lock, ACL denial, junction protection'
}
finally {
    if ($lock) { $lock.Dispose() }
    if ($originalFileAcl -and (Test-Path $old)) { Set-Acl -Path $old -AclObject $originalFileAcl }
    if ($originalDirectoryAcl) { Set-Acl -Path $data -AclObject $originalDirectoryAcl }
    if (Get-SmbShare -Name $share -ErrorAction SilentlyContinue) { Remove-SmbShare -Name $share -Force }
    $link = Join-Path $data 'junction'
    if (Test-Path $link) { [IO.Directory]::Delete($link) }
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
}
