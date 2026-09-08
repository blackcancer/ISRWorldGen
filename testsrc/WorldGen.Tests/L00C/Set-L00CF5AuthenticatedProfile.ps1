[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare', 'Restore')][string]$Action,
    [string]$BackupDirectory,
    [string]$SyntheticFixtureRoot,
    [scriptblock]$TestHook
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($null -ne $TestHook -and -not $SyntheticFixtureRoot) { throw 'TestHook is permitted only with -SyntheticFixtureRoot.' }
function Get-CanonicalPath([string]$Path) { [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path) }
function Assert-ChildPath([string]$Child, [string]$Parent, [string]$Label) { $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar; if (-not $Child.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "$Label must stay below $Parent." } }
function Get-Sha256([byte[]]$Bytes) { $sha = [Security.Cryptography.SHA256]::Create(); try { ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '') } finally { $sha.Dispose() } }
function Read-LockedBytes([IO.FileStream]$Stream) { $Stream.Position = 0; $bytes = New-Object byte[] $Stream.Length; [void]$Stream.Read($bytes, 0, $bytes.Length); return $bytes }
function Write-LockedBytes([IO.FileStream]$Stream, [byte[]]$Bytes, [byte[]]$Recovery, [string]$HookPoint) { try { $Stream.Position = 0; $Stream.SetLength(0); if ($null -ne $TestHook) { & $TestHook $HookPoint }; $Stream.Write($Bytes, 0, $Bytes.Length); $Stream.Flush($true) } catch { $Stream.Position = 0; $Stream.SetLength(0); $Stream.Write($Recovery, 0, $Recovery.Length); $Stream.Flush($true); throw } }
function Open-Lock([string]$Path) { [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read) }
function Write-NewBytes([string]$Path, [byte[]]$Bytes) { $s = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None); try { $s.Write($Bytes, 0, $Bytes.Length); $s.Flush($true) } finally { $s.Dispose() } }
function Test-SensitiveProfile([object]$Profile) { ($Profile | ConvertTo-Json -Depth 16 -Compress) -match '(?i)(login|token|credential|password)' }
function Get-BootstrapSaveAttestation([string]$Path) { if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required L00-C bootstrap save is absent: $Path" }; $canonical = Get-CanonicalPath $Path; $bytes = [IO.File]::ReadAllBytes($canonical); $item = Get-Item -LiteralPath $canonical -Force; [ordered]@{ Path=$canonical; Size=[Int64]$bytes.Length; LastWriteTimeUtc=$item.LastWriteTimeUtc.ToString('o'); Sha256=Get-Sha256 $bytes } }
function Assert-BootstrapSave([object]$Expected) { $actual = Get-BootstrapSaveAttestation ([string]$Expected.Path); foreach ($name in @('Path','Size','LastWriteTimeUtc','Sha256')) { if ([string]$actual[$name] -cne [string]$Expected.$name) { throw "Refusing prepare: bootstrap save drifted at $name." } } }
function Prepared-UserSettings([byte[]]$Bytes, [string]$Profile) { $xml = [Xml.XmlDocument]::new(); $xml.PreserveWhitespace = $true; $xml.LoadXml([Text.Encoding]::UTF8.GetString($Bytes)); $nodes = @($xml.SelectNodes("//*[local-name()='ActiveDebugProfile']")); if ($nodes.Count -ne 1) { throw 'WorldGen.VintageStory.csproj.user must contain exactly one ActiveDebugProfile setting.' }; $nodes[0].InnerText = $Profile; [Text.Encoding]::UTF8.GetBytes($xml.OuterXml) }
if ($SyntheticFixtureRoot) { $repository = Get-CanonicalPath $SyntheticFixtureRoot; Assert-ChildPath $repository ([IO.Path]::GetTempPath()) 'Synthetic fixture root' } else { $repository = Get-CanonicalPath (Join-Path $PSScriptRoot '..\..\..') }
$launch = Get-CanonicalPath (Join-Path $repository 'src\WorldGen.VintageStory\Properties\launchSettings.json'); $user = Get-CanonicalPath (Join-Path $repository 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj.user'); $lab = Get-CanonicalPath (Join-Path $repository '.local\L00C'); $modsParent = Get-CanonicalPath (Join-Path $repository 'src\WorldGen.VintageStory\bin\Debug\Mods'); $retiredPackage = Join-Path $modsParent 'isrworldgenl00clab'
$save = if ($SyntheticFixtureRoot) { [IO.Path]::GetFullPath((Join-Path $repository 'AppData\VintagestoryData\Saves\ISRWorldGen-L00C-Client.vcdbs')) } else { [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'VintagestoryData\Saves\ISRWorldGen-L00C-Client.vcdbs')) }
$launchLock = $null; $userLock = $null
try {
    $launchLock = Open-Lock $launch; $userLock = Open-Lock $user
    if ($Action -eq 'Restore') {
        if ([string]::IsNullOrWhiteSpace($BackupDirectory)) { throw 'Restore requires -BackupDirectory returned by Prepare.' }; $backup = Get-CanonicalPath $BackupDirectory; Assert-ChildPath $backup $lab 'Backup directory'; $metadataPath = Join-Path $backup 'metadata.json'; $originalPath = Join-Path $backup 'launchSettings.original.json'; $originalUserPath = Join-Path $backup 'WorldGen.VintageStory.csproj.user.original'
        if (-not (Test-Path $metadataPath) -or -not (Test-Path $originalPath) -or -not (Test-Path $originalUserPath)) { throw 'Backup directory is incomplete.' }; $meta = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
        if ($meta.Owner -ne (Get-Acl -LiteralPath $backup).Owner -or $meta.LaunchSettingsPath -ne $launch -or $meta.ProjectUserSettingsPath -ne $user) { throw 'Backup identity is not trusted.' }
        $current = Read-LockedBytes $launchLock; $currentUser = Read-LockedBytes $userLock; if ((Get-Sha256 $current) -ne $meta.ModifiedSha256 -or (Get-Sha256 $currentUser) -ne $meta.ProjectUserSettingsModifiedSha256) { throw 'Refusing restore: selected F5 settings changed after Prepare.' }
        $original = [IO.File]::ReadAllBytes($originalPath); $originalUser = [IO.File]::ReadAllBytes($originalUserPath); if ((Get-Sha256 $original) -ne $meta.OriginalSha256 -or (Get-Sha256 $originalUser) -ne $meta.ProjectUserSettingsOriginalSha256) { throw 'Backup hash does not match its preparation receipt.' }
        try {
            Write-LockedBytes $userLock $originalUser $currentUser 'RestoreUserAfterTruncate'
            Write-LockedBytes $launchLock $original $current 'RestoreLaunchAfterTruncate'
        }
        catch {
            # The two files are one selected-profile transaction.  If the
            # second durable write fails, restore the first to its prepared
            # snapshot while both exclusive handles are still held.
            if ((Get-Sha256 (Read-LockedBytes $userLock)) -eq $meta.ProjectUserSettingsOriginalSha256 -and (Get-Sha256 (Read-LockedBytes $launchLock)) -eq $meta.ModifiedSha256) {
                Write-LockedBytes $userLock $currentUser $originalUser 'RestoreUserRollbackAfterTruncate'
            }
            throw
        }
        if ((Get-Sha256 (Read-LockedBytes $launchLock)) -ne $meta.OriginalSha256 -or (Get-Sha256 (Read-LockedBytes $userLock)) -ne $meta.ProjectUserSettingsOriginalSha256) { throw 'Durable settings restoration failed.' }
        [ordered]@{ Status='RESTORED'; BackupDirectory=$backup; Sha256=$meta.OriginalSha256 } | ConvertTo-Json -Compress; return
    }
    foreach ($needed in @($lab, (Join-Path $lab '.isrworldgen-lab'), $modsParent)) { if (-not (Test-Path -LiteralPath $needed)) { throw "Required L00-C laboratory input is missing: $needed" } }
    # The retired package is never deleted automatically: only its former
    # provenance-aware helper could unmount it.  A residue would load a second
    # code mod beside ISRWorldGen and is therefore a hard pre-F5 refusal.
    if (Test-Path -LiteralPath $retiredPackage) { throw "Retired L00-C laboratory package is present beside ISRWorldGen: $retiredPackage. Refuse F5; remove it only through provenance-validated cleanup." }
    $original = Read-LockedBytes $launchLock; $originalUser = Read-LockedBytes $userLock; $document = [Text.Encoding]::UTF8.GetString($original) | ConvertFrom-Json; $profiles = @($document.profiles.PSObject.Properties); if ($profiles.Count -lt 1) { throw 'launchSettings.json contains no profile.' }; $first = $profiles[0]; $args = [string]$first.Value.commandLineArgs; $mods = '--addModPath "$(ProjectDir)bin\$(Configuration)\Mods"'
    if ($first.Name -ne 'ISRWorldGen Client (authenticated user data)' -or $first.Value.commandName -ne 'Executable' -or [IO.Path]::GetFileName([string]$first.Value.executablePath) -ne 'Vintagestory.exe' -or [regex]::Matches($args, '(?i)(?:^|\s)--addModPath(?:\s|=)').Count -ne 1 -or [regex]::Matches($args, '(?i)(?:^|\s)--openWorld(?:\s|=|$)').Count -ne 0 -or $args -notmatch [regex]::Escape($mods) -or $args -match '(?i)--dataPath(?:\s|=|$)' -or (Test-SensitiveProfile $first.Value)) { throw 'First F5 profile is not the conforming authenticated client profile.' }
    $attestation = Get-BootstrapSaveAttestation $save; $first.Value.commandLineArgs = '--openWorld "ISRWorldGen-L00C-Client" ' + $args; if ($null -eq $first.Value.PSObject.Properties['environmentVariables']) { $first.Value | Add-Member -NotePropertyName environmentVariables -NotePropertyValue ([pscustomobject]@{}) }; $first.Value.environmentVariables | Add-Member -NotePropertyName ISR_L00C_LAB -NotePropertyValue '1' -Force; $first.Value.environmentVariables | Add-Member -NotePropertyName ISR_L00C_LAB_ROOT -NotePropertyValue $lab -Force
    $modified = [Text.Encoding]::UTF8.GetBytes(($document | ConvertTo-Json -Depth 16)); $modifiedUser = Prepared-UserSettings $originalUser $first.Name; $backupParent = Join-Path $lab 'f5-profile-backups'; [void](New-Item -ItemType Directory -Path $backupParent -Force); $backup = Join-Path $backupParent ('f5-' + [Guid]::NewGuid().ToString('N')); [void](New-Item -ItemType Directory -Path $backup)
    Write-NewBytes (Join-Path $backup 'launchSettings.original.json') $original; Write-NewBytes (Join-Path $backup 'WorldGen.VintageStory.csproj.user.original') $originalUser; $meta = [ordered]@{ Owner=(Get-Acl -LiteralPath $backup).Owner; LaunchSettingsPath=$launch; ProjectUserSettingsPath=$user; FirstProfile=$first.Name; BootstrapSave=$attestation; OriginalSha256=(Get-Sha256 $original); ModifiedSha256=(Get-Sha256 $modified); ProjectUserSettingsOriginalSha256=(Get-Sha256 $originalUser); ProjectUserSettingsModifiedSha256=(Get-Sha256 $modifiedUser); Phase='PREPARED'; PreparedUtc=[DateTimeOffset]::UtcNow.ToString('o') }; Write-NewBytes (Join-Path $backup 'metadata.json') ([Text.Encoding]::UTF8.GetBytes(($meta | ConvertTo-Json -Depth 4)))
    try {
        Assert-BootstrapSave $attestation; Write-LockedBytes $launchLock $modified $original 'PrepareLaunchAfterTruncate'; Write-LockedBytes $userLock $modifiedUser $originalUser 'PrepareUserAfterTruncate'
        if ((Get-Sha256 (Read-LockedBytes $launchLock)) -ne $meta.ModifiedSha256 -or (Get-Sha256 (Read-LockedBytes $userLock)) -ne $meta.ProjectUserSettingsModifiedSha256) { throw 'Durable F5 preparation verification failed.' }
        [ordered]@{ Status='PREPARED'; BackupDirectory=$backup; FirstProfile=$first.Name; Sha256=$meta.ModifiedSha256 } | ConvertTo-Json -Compress
    }
    catch {
        if ((Get-Sha256 (Read-LockedBytes $launchLock)) -eq $meta.ModifiedSha256) { Write-LockedBytes $launchLock $original $modified 'PrepareLaunchRollbackAfterTruncate' }
        if ((Get-Sha256 (Read-LockedBytes $userLock)) -eq $meta.ProjectUserSettingsModifiedSha256) { Write-LockedBytes $userLock $originalUser $modifiedUser 'PrepareUserRollbackAfterTruncate' }
        throw
    }
}
finally { if ($null -ne $userLock) { $userLock.Dispose() }; if ($null -ne $launchLock) { $launchLock.Dispose() } }
