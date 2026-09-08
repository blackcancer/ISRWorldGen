[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare', 'Restore')][string]$Action,
    [string]$BackupDirectory,
    # Test-only escape hatch: must be below the process temporary directory and
    # still uses the same fixed repository-relative layout as the real helper.
    [string]$SyntheticFixtureRoot,
    # Synthetic-fixture seam used only to prove that every pending write
    # revalidates its source hash. It is rejected for the real repository.
    [scriptblock]$TestHook
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($null -ne $TestHook -and -not $SyntheticFixtureRoot) { throw 'TestHook is permitted only with -SyntheticFixtureRoot.' }

function Get-CanonicalPath([string]$Path) { [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path) }
function Assert-ChildPath([string]$Child, [string]$Parent, [string]$Label) {
    $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $Child.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "$Label must stay below $Parent." }
}
function Get-Sha256([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '') }
    finally { $algorithm.Dispose() }
}
function Write-NewBytes([string]$Path, [byte[]]$Bytes) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}
function Open-ExclusiveWriteHandle([string]$Path) { [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read) }
function Read-LockedBytes([IO.FileStream]$Stream) {
    $Stream.Position = 0; $bytes = New-Object byte[] $Stream.Length; [void]$Stream.Read($bytes, 0, $bytes.Length); return $bytes
}
function Write-LockedBytes([IO.FileStream]$Stream, [byte[]]$Bytes, [byte[]]$RecoveryBytes, [string]$AfterTruncateHook) {
    try {
        $Stream.Position = 0; $Stream.SetLength(0)
        Invoke-TransactionTestHook $AfterTruncateHook
        $Stream.Write($Bytes, 0, $Bytes.Length); $Stream.Flush($true)
    }
    catch {
        # The target cannot have been externally replaced while this handle is
        # held. Restore only the byte sequence snapshotted for this transaction.
        try { $Stream.Position = 0; $Stream.SetLength(0); $Stream.Write($RecoveryBytes, 0, $RecoveryBytes.Length); $Stream.Flush($true) }
        catch { throw 'A locked settings write failed and its durable in-transaction recovery also failed.' }
        throw
    }
}
function Get-PreparedUserSettingsBytes([byte[]]$Original, [string]$ExpectedProfile) {
    $document = [Xml.XmlDocument]::new(); $document.PreserveWhitespace = $true
    try { $document.LoadXml([Text.Encoding]::UTF8.GetString($Original)) } catch { throw 'WorldGen.VintageStory.csproj.user is not valid XML.' }
    $nodes = @($document.SelectNodes("//*[local-name()='ActiveDebugProfile']"))
    if ($nodes.Count -ne 1) { throw 'WorldGen.VintageStory.csproj.user must contain exactly one ActiveDebugProfile setting.' }
    $nodes[0].InnerText = $ExpectedProfile
    return [Text.Encoding]::UTF8.GetBytes($document.OuterXml)
}
function Assert-CurrentSha256([IO.FileStream]$Stream, [string]$Expected, [string]$Label) {
    if ((Get-Sha256 (Read-LockedBytes $Stream)) -ne $Expected) { throw "Refusing ${Label}: file changed since this transaction was read." }
}
function Invoke-TransactionTestHook([string]$Point) {
    if ($null -ne $TestHook) { & $TestHook $Point }
}
function Test-SensitiveProfile([object]$Profile) { ($Profile | ConvertTo-Json -Depth 16 -Compress) -match '(?i)(login|token|credential|password)' }

if ($SyntheticFixtureRoot) {
    $repository = Get-CanonicalPath $SyntheticFixtureRoot
    $temporaryDirectory = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    Assert-ChildPath $repository $temporaryDirectory 'Synthetic fixture root'
}
else {
    $repository = Get-CanonicalPath (Join-Path $PSScriptRoot '..\..\..')
}
$launchSettings = Get-CanonicalPath (Join-Path $repository 'src\WorldGen.VintageStory\Properties\launchSettings.json')
$projectUserSettings = Get-CanonicalPath (Join-Path $repository 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj.user')
$laboratory = Get-CanonicalPath (Join-Path $repository '.local\L00C')
$mountHelper = Join-Path $PSScriptRoot 'Set-L00CTestModMount.ps1'
$launchSettingsLock = $null; $projectUserSettingsLock = $null
try {
    # These are operating-system write locks, not advisory lock files. The
    # handles remain held until this action has either completed or refused.
    $launchSettingsLock = Open-ExclusiveWriteHandle $launchSettings
    $projectUserSettingsLock = Open-ExclusiveWriteHandle $projectUserSettings

if ($Action -eq 'Restore') {
    if ([string]::IsNullOrWhiteSpace($BackupDirectory)) { throw 'Restore requires -BackupDirectory returned by Prepare.' }
    $backup = Get-CanonicalPath $BackupDirectory; Assert-ChildPath $backup $laboratory 'Backup directory'
    $metadataPath = Join-Path $backup 'metadata.json'; $originalPath = Join-Path $backup 'launchSettings.original.json'; $originalUserSettingsPath = Join-Path $backup 'WorldGen.VintageStory.csproj.user.original'
    if (-not (Test-Path $metadataPath -PathType Leaf) -or -not (Test-Path $originalPath -PathType Leaf) -or -not (Test-Path $originalUserSettingsPath -PathType Leaf)) { throw 'Backup directory is incomplete.' }
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    if ($metadata.Owner -ne (Get-Acl -LiteralPath $backup).Owner) { throw 'Backup ownership no longer matches its preparation receipt.' }
    if ($metadata.LaunchSettingsPath -ne $launchSettings) { throw 'Backup does not belong to this launchSettings.json.' }
    if ($metadata.ProjectUserSettingsPath -ne $projectUserSettings) { throw 'Backup does not belong to this WorldGen.VintageStory.csproj.user.' }
    $current = Read-LockedBytes $launchSettingsLock
    if ((Get-Sha256 $current) -ne $metadata.ModifiedSha256) { throw 'Refusing restore: launchSettings.json changed after Prepare.' }
    $currentUserSettings = Read-LockedBytes $projectUserSettingsLock
    if ((Get-Sha256 $currentUserSettings) -ne $metadata.ProjectUserSettingsModifiedSha256) { throw 'Refusing restore: WorldGen.VintageStory.csproj.user changed after Prepare.' }
    $original = [IO.File]::ReadAllBytes($originalPath)
    if ((Get-Sha256 $original) -ne $metadata.OriginalSha256) { throw 'Backup hash does not match its preparation receipt.' }
    $originalUserSettings = [IO.File]::ReadAllBytes($originalUserSettingsPath)
    if ((Get-Sha256 $originalUserSettings) -ne $metadata.ProjectUserSettingsOriginalSha256) { throw 'User-settings backup hash does not match its preparation receipt.' }
    Invoke-TransactionTestHook 'RestoreBeforeMount'
    Assert-CurrentSha256 $launchSettingsLock $metadata.ModifiedSha256 'restore launchSettings.json'
    Assert-CurrentSha256 $projectUserSettingsLock $metadata.ProjectUserSettingsModifiedSha256 'restore WorldGen.VintageStory.csproj.user'
    Invoke-TransactionTestHook 'RestoreBeforeUserSettingsWrite'
    Assert-CurrentSha256 $launchSettingsLock $metadata.ModifiedSha256 'restore launchSettings.json'
    Assert-CurrentSha256 $projectUserSettingsLock $metadata.ProjectUserSettingsModifiedSha256 'restore WorldGen.VintageStory.csproj.user'
    Write-LockedBytes $projectUserSettingsLock $originalUserSettings $currentUserSettings 'RestoreAfterUserSettingsTruncate'
    if ((Get-Sha256 (Read-LockedBytes $projectUserSettingsLock)) -ne $metadata.ProjectUserSettingsOriginalSha256) { throw 'Durable user-settings restore verification failed.' }
    try {
        Invoke-TransactionTestHook 'RestoreBeforeLaunchSettingsWrite'
        Assert-CurrentSha256 $launchSettingsLock $metadata.ModifiedSha256 'restore launchSettings.json'
        Write-LockedBytes $launchSettingsLock $original $current 'RestoreAfterLaunchSettingsTruncate'
        if ((Get-Sha256 (Read-LockedBytes $launchSettingsLock)) -ne $metadata.OriginalSha256) { throw 'Durable launchSettings restore verification failed.' }
    }
    catch {
        if ((Get-Sha256 (Read-LockedBytes $projectUserSettingsLock)) -eq $metadata.ProjectUserSettingsOriginalSha256 -and (Get-Sha256 (Read-LockedBytes $launchSettingsLock)) -eq $metadata.ModifiedSha256) { Write-LockedBytes $projectUserSettingsLock $currentUserSettings $currentUserSettings 'RestoreAfterUserSettingsRollbackTruncate' }
        throw
    }
    Invoke-TransactionTestHook 'RestoreBeforeMount'
    & $mountHelper -Action Restore -SyntheticFixtureRoot $SyntheticFixtureRoot -BackupDirectory $metadata.MountBackupDirectory | Out-Null
    [ordered]@{ Status = 'RESTORED'; BackupDirectory = $backup; Sha256 = $metadata.OriginalSha256 } | ConvertTo-Json -Compress; return
}

foreach ($required in @($laboratory, (Join-Path $laboratory '.isrworldgen-lab'))) { if (-not (Test-Path -LiteralPath $required)) { throw "Required L00-C laboratory root input is missing: $required" } }
$modsProfileArgument = '--addModPath "$(ProjectDir)bin\$(Configuration)\Mods"'

$original = Read-LockedBytes $launchSettingsLock
$originalUserSettings = Read-LockedBytes $projectUserSettingsLock
$originalHash = Get-Sha256 $original; $document = ([Text.Encoding]::UTF8.GetString($original) | ConvertFrom-Json)
$profiles = @($document.profiles.PSObject.Properties)
if ($profiles.Count -lt 1) { throw 'launchSettings.json contains no profile.' }
$first = $profiles[0]
$addModPathCount = [regex]::Matches([string]$first.Value.commandLineArgs, '(?i)(?:^|\s)--addModPath(?:\s|=)').Count
if ($first.Name -ne 'ISRWorldGen Client (authenticated user data)' -or $first.Value.commandName -ne 'Executable' -or [IO.Path]::GetFileName([string]$first.Value.executablePath) -ne 'Vintagestory.exe' -or $addModPathCount -ne 1 -or [string]$first.Value.commandLineArgs -notmatch [regex]::Escape($modsProfileArgument) -or [string]$first.Value.commandLineArgs -match '(?i)--dataPath(?:\s|=|$)' -or (Test-SensitiveProfile $first.Value)) { throw 'First F5 profile is not the conforming authenticated client profile with exactly its existing production Mods path, or it requests credential material.' }
$modifiedUserSettings = Get-PreparedUserSettingsBytes $originalUserSettings $first.Name
$mountReceipt = & $mountHelper -Action Prepare -SyntheticFixtureRoot $SyntheticFixtureRoot | ConvertFrom-Json
$modifiedHash = $null; $modifiedUserSettingsHash = $null
try {
    $profile = $first.Value
    if ($null -eq $profile.PSObject.Properties['environmentVariables']) { $profile | Add-Member -NotePropertyName environmentVariables -NotePropertyValue ([pscustomobject]@{}) }
    $profile.environmentVariables | Add-Member -NotePropertyName ISR_L00C_LAB -NotePropertyValue '1' -Force
    $profile.environmentVariables | Add-Member -NotePropertyName ISR_L00C_LAB_ROOT -NotePropertyValue $laboratory -Force
    $modified = [Text.Encoding]::UTF8.GetBytes(($document | ConvertTo-Json -Depth 16)); $modifiedHash = Get-Sha256 $modified; $modifiedUserSettingsHash = Get-Sha256 $modifiedUserSettings
    $backupParent = Join-Path $laboratory 'f5-profile-backups'; [void](New-Item -ItemType Directory -Path $backupParent -Force)
    $backup = Join-Path $backupParent ('f5-' + [Guid]::NewGuid().ToString('N')); [void](New-Item -ItemType Directory -Path $backup -ErrorAction Stop)
    $owner = (Get-Acl -LiteralPath $backup).Owner; Write-NewBytes (Join-Path $backup 'launchSettings.original.json') $original; Write-NewBytes (Join-Path $backup 'WorldGen.VintageStory.csproj.user.original') $originalUserSettings
    $metadata = [ordered]@{ Owner = $owner; LaunchSettingsPath = $launchSettings; ProjectUserSettingsPath = $projectUserSettings; FirstProfile = $first.Name; OriginalSha256 = $originalHash; ModifiedSha256 = $modifiedHash; ProjectUserSettingsOriginalSha256 = (Get-Sha256 $originalUserSettings); ProjectUserSettingsModifiedSha256 = $modifiedUserSettingsHash; MountBackupDirectory = $mountReceipt.BackupDirectory; MountedPackage = $mountReceipt.MountedPackage; PreparedUtc = [DateTimeOffset]::UtcNow.ToString('o') }
    Write-NewBytes (Join-Path $backup 'metadata.json') ([Text.Encoding]::UTF8.GetBytes(($metadata | ConvertTo-Json -Depth 4)))
    Invoke-TransactionTestHook 'PrepareBeforeLaunchSettingsWrite'
    Assert-CurrentSha256 $launchSettingsLock $originalHash 'prepare launchSettings.json'
    Assert-CurrentSha256 $projectUserSettingsLock (Get-Sha256 $originalUserSettings) 'prepare WorldGen.VintageStory.csproj.user'
    Write-LockedBytes $launchSettingsLock $modified $original 'PrepareAfterLaunchSettingsTruncate'
    if ((Get-Sha256 (Read-LockedBytes $launchSettingsLock)) -ne $modifiedHash) { throw 'Durable launchSettings preparation verification failed.' }
    Invoke-TransactionTestHook 'PrepareBeforeUserSettingsWrite'
    Assert-CurrentSha256 $launchSettingsLock $modifiedHash 'prepare launchSettings.json'
    Assert-CurrentSha256 $projectUserSettingsLock (Get-Sha256 $originalUserSettings) 'prepare WorldGen.VintageStory.csproj.user'
    Write-LockedBytes $projectUserSettingsLock $modifiedUserSettings $originalUserSettings 'PrepareAfterUserSettingsTruncate'
    if ((Get-Sha256 (Read-LockedBytes $projectUserSettingsLock)) -ne $modifiedUserSettingsHash) { throw 'Durable user-settings preparation verification failed.' }
    [ordered]@{ Status = 'PREPARED'; BackupDirectory = $backup; FirstProfile = $first.Name; Sha256 = $modifiedHash } | ConvertTo-Json -Compress
}
catch {
    if ($null -ne $modifiedHash -and (Get-Sha256 (Read-LockedBytes $launchSettingsLock)) -eq $modifiedHash) { Write-LockedBytes $launchSettingsLock $original $modified 'PrepareAfterLaunchSettingsRollbackTruncate' }
    if ($null -ne $modifiedUserSettingsHash -and (Get-Sha256 (Read-LockedBytes $projectUserSettingsLock)) -eq $modifiedUserSettingsHash) { Write-LockedBytes $projectUserSettingsLock $originalUserSettings $modifiedUserSettings 'PrepareAfterUserSettingsRollbackTruncate' }
    & $mountHelper -Action Restore -SyntheticFixtureRoot $SyntheticFixtureRoot -BackupDirectory $mountReceipt.BackupDirectory | Out-Null
    throw
}
}
finally {
    if ($null -ne $projectUserSettingsLock) { $projectUserSettingsLock.Dispose() }
    if ($null -ne $launchSettingsLock) { $launchSettingsLock.Dispose() }
}
