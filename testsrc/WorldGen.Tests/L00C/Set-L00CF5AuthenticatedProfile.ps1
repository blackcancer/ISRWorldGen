[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare', 'Restore')][string]$Action,
    [string]$BackupDirectory,
    # Test-only escape hatch: must be below the process temporary directory and
    # still uses the same fixed repository-relative layout as the real helper.
    [string]$SyntheticFixtureRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CanonicalPath([string]$Path) { [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path) }
function Assert-ChildPath([string]$Child, [string]$Parent, [string]$Label) {
    $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $Child.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "$Label must stay below $Parent." }
}
function Get-Sha256([byte[]]$Bytes) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)) }
function Write-NewBytes([string]$Path, [byte[]]$Bytes) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}
function Replace-Atomically([string]$Destination, [byte[]]$Bytes) {
    $temporary = Join-Path ([IO.Path]::GetDirectoryName($Destination)) ('.l00c-f5-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $replaceBackup = $temporary + '.previous'
    try { Write-NewBytes $temporary $Bytes; [IO.File]::Replace($temporary, $Destination, $replaceBackup, $true) }
    finally { foreach ($path in @($temporary, $replaceBackup)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } } }
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
$laboratory = Get-CanonicalPath (Join-Path $repository '.local\L00C')

if ($Action -eq 'Restore') {
    if ([string]::IsNullOrWhiteSpace($BackupDirectory)) { throw 'Restore requires -BackupDirectory returned by Prepare.' }
    $backup = Get-CanonicalPath $BackupDirectory; Assert-ChildPath $backup $laboratory 'Backup directory'
    $metadataPath = Join-Path $backup 'metadata.json'; $originalPath = Join-Path $backup 'launchSettings.original.json'
    if (-not (Test-Path $metadataPath -PathType Leaf) -or -not (Test-Path $originalPath -PathType Leaf)) { throw 'Backup directory is incomplete.' }
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    if ($metadata.Owner -ne (Get-Acl -LiteralPath $backup).Owner) { throw 'Backup ownership no longer matches its preparation receipt.' }
    if ($metadata.LaunchSettingsPath -ne $launchSettings) { throw 'Backup does not belong to this launchSettings.json.' }
    $current = [IO.File]::ReadAllBytes($launchSettings)
    if ((Get-Sha256 $current) -ne $metadata.ModifiedSha256) { throw 'Refusing restore: launchSettings.json changed after Prepare.' }
    $original = [IO.File]::ReadAllBytes($originalPath)
    if ((Get-Sha256 $original) -ne $metadata.OriginalSha256) { throw 'Backup hash does not match its preparation receipt.' }
    Replace-Atomically $launchSettings $original
    if ((Get-Sha256 ([IO.File]::ReadAllBytes($launchSettings))) -ne $metadata.OriginalSha256) { throw 'Atomic restore verification failed.' }
    [ordered]@{ Status = 'RESTORED'; BackupDirectory = $backup; Sha256 = $metadata.OriginalSha256 } | ConvertTo-Json -Compress; return
}

foreach ($required in @($laboratory, (Join-Path $laboratory '.isrworldgen-lab'))) { if (-not (Test-Path -LiteralPath $required)) { throw "Required L00-C laboratory root input is missing: $required" } }
$package = Get-CanonicalPath (Join-Path $laboratory 'menu-action-testmod\Debug\isrworldgenl00clab')
foreach ($required in @((Join-Path $package 'modinfo.json'), (Join-Path $package 'ISRWorldGen.L00C.MenuActionLab.dll'))) { if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required L00-C test-mod package input is missing: $required" } }
$modPath = Get-CanonicalPath (Join-Path $laboratory 'menu-action-testmod\Debug')
if ([IO.Path]::GetFullPath((Join-Path $modPath 'isrworldgenl00clab')) -cne $package) { throw 'Test-mod package must be the expected direct child of its addModPath container.' }

$lock = [IO.File]::Open($launchSettings, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try { $original = New-Object byte[] $lock.Length; [void]$lock.Read($original, 0, $original.Length) } finally { $lock.Dispose() }
$originalHash = Get-Sha256 $original; $document = ([Text.Encoding]::UTF8.GetString($original) | ConvertFrom-Json)
$profiles = @($document.profiles.PSObject.Properties)
if ($profiles.Count -lt 1) { throw 'launchSettings.json contains no profile.' }
$first = $profiles[0]
if ($first.Name -ne 'ISRWorldGen Client (authenticated user data)' -or $first.Value.commandName -ne 'Executable' -or [IO.Path]::GetFileName([string]$first.Value.executablePath) -ne 'Vintagestory.exe' -or [string]$first.Value.commandLineArgs -match '(?i)--dataPath(?:\s|=|$)' -or (Test-SensitiveProfile $first.Value)) { throw 'First F5 profile is not the conforming authenticated client profile, or it requests credential material.' }
$profile = $first.Value; $profile.commandLineArgs = ([string]$profile.commandLineArgs).TrimEnd() + ' --addModPath "' + $modPath + '"'
if ($null -eq $profile.PSObject.Properties['environmentVariables']) { $profile | Add-Member -NotePropertyName environmentVariables -NotePropertyValue ([pscustomobject]@{}) }
$profile.environmentVariables | Add-Member -NotePropertyName ISR_L00C_LAB -NotePropertyValue '1' -Force
$profile.environmentVariables | Add-Member -NotePropertyName ISR_L00C_LAB_ROOT -NotePropertyValue $laboratory -Force
$modified = [Text.Encoding]::UTF8.GetBytes(($document | ConvertTo-Json -Depth 16)); $modifiedHash = Get-Sha256 $modified
$backupParent = Join-Path $laboratory 'f5-profile-backups'; [void](New-Item -ItemType Directory -Path $backupParent -Force)
$backup = Join-Path $backupParent ('f5-' + [Guid]::NewGuid().ToString('N')); [void](New-Item -ItemType Directory -Path $backup -ErrorAction Stop)
$owner = (Get-Acl -LiteralPath $backup).Owner; Write-NewBytes (Join-Path $backup 'launchSettings.original.json') $original
$metadata = [ordered]@{ Owner = $owner; LaunchSettingsPath = $launchSettings; FirstProfile = $first.Name; OriginalSha256 = $originalHash; ModifiedSha256 = $modifiedHash; PreparedUtc = [DateTimeOffset]::UtcNow.ToString('o') }
Write-NewBytes (Join-Path $backup 'metadata.json') ([Text.Encoding]::UTF8.GetBytes(($metadata | ConvertTo-Json -Depth 4)))
Replace-Atomically $launchSettings $modified
if ((Get-Sha256 ([IO.File]::ReadAllBytes($launchSettings))) -ne $modifiedHash) { throw 'Atomic preparation verification failed.' }
[ordered]@{ Status = 'PREPARED'; BackupDirectory = $backup; FirstProfile = $first.Name; Sha256 = $modifiedHash } | ConvertTo-Json -Compress
