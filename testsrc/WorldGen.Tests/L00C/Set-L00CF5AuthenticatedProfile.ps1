[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare', 'Arm', 'BeginLaunch', 'BlockLaunch', 'Acquire', 'Restore', 'Recover')][string]$Action,
    [string]$BackupDirectory,
    [int]$VisualStudioProcessId,
    [string]$ExpectedSolutionPath,
    [string]$ExpectedGameExecutablePath,
    [string]$VisualStudioAttestationPath,
    [string]$SyntheticFixtureRoot,
    [scriptblock]$TestProcessQuery,
    [scriptblock]$TestProcessListQuery,
    [scriptblock]$TestHook
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (($null -ne $TestProcessQuery -or $null -ne $TestProcessListQuery -or $null -ne $TestHook) -and -not $SyntheticFixtureRoot) {
    throw 'Test seams are permitted only with -SyntheticFixtureRoot.'
}

$Protocol = 'l00c-f5-debug-transaction-v2'
$ExpectedProfileName = 'ISRWorldGen Client (authenticated user data)'
$ExpectedExecutableTemplate = '$(VintageStoryPath)\Vintagestory.exe'
$OriginalArguments = '--tracelog --addModPath "$(ProjectDir)bin\$(Configuration)\Mods" --addOrigin "$(ProjectDir)assets"'
$BootstrapWorldName = 'ISRWorldGen-L00C-Client'

function Get-CanonicalPath([string]$Path) {
    return [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path)
}

function Assert-ChildPath([string]$Child, [string]$Parent, [string]$Label) {
    $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $Child.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "$Label must stay below $Parent." }
}

function Assert-PhysicalDirectory([string]$Path, [string]$Label) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label must be a physical directory."
    }
}

function Assert-PhysicalFile([string]$Path, [string]$Label) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label must be a physical file."
    }
}

function Get-Sha256([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '') }
    finally { $algorithm.Dispose() }
}

function Get-FileSha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

function Get-Utf8Text([byte[]]$Bytes) {
    $offset = if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) { 3 } else { 0 }
    return [Text.Encoding]::UTF8.GetString($Bytes, $offset, $Bytes.Length - $offset)
}

function Read-AllBytes([IO.FileStream]$Stream) {
    $Stream.Position = 0
    $bytes = New-Object byte[] $Stream.Length
    $offset = 0
    while ($offset -lt $bytes.Length) {
        $read = $Stream.Read($bytes, $offset, $bytes.Length - $offset)
        if ($read -le 0) { throw 'A locked settings file ended before its declared length.' }
        $offset += $read
    }
    return $bytes
}

function Read-PathBytes([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try { return Read-AllBytes $stream }
    finally { $stream.Dispose() }
}

function Write-AtomicBytes([string]$Path, [byte[]]$Bytes, [string]$ExpectedCurrentHash, [string]$TransactionDirectory, [string]$SwapStem, [string]$HookPoint) {
    $replacement = Join-Path $TransactionDirectory ($SwapStem + '.new')
    $previous = Join-Path $TransactionDirectory ($SwapStem + '.previous')
    if ((Test-Path -LiteralPath $replacement) -or (Test-Path -LiteralPath $previous)) { throw "Atomic swap residue for $SwapStem requires recovery." }
    Write-DurableNewBytes $replacement $Bytes
    try {
        if ((Get-Sha256 (Read-PathBytes $Path)) -cne $ExpectedCurrentHash) { throw 'Atomic settings source changed before replacement.' }
        Invoke-TestHook $HookPoint
        [IO.File]::Replace($replacement, $Path, $previous, $true)
        if ((Get-FileSha256 $previous) -cne $ExpectedCurrentHash) {
            $rejected = Join-Path $TransactionDirectory ($SwapStem + '.rejected')
            [IO.File]::Replace($previous, $Path, $rejected, $true)
            if (Test-Path -LiteralPath $rejected) { [IO.File]::Delete($rejected) }
            throw 'Atomic settings source changed during replacement; external bytes were reinstated.'
        }
        if ((Get-FileSha256 $Path) -cne (Get-Sha256 $Bytes)) { throw 'Atomic settings replacement hash verification failed.' }
        [IO.File]::Delete($previous)
    }
    catch {
        if (Test-Path -LiteralPath $replacement) { [IO.File]::Delete($replacement) }
        throw
    }
}

function Write-DurableNewBytes([string]$Path, [byte[]]$Bytes) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
}

function Write-NewBytes([string]$Path, [byte[]]$Bytes) {
    if (Test-Path -LiteralPath $Path) { throw "Authoritative artifact already exists: $Path" }
    $publishing = $Path + '.publishing'
    if (Test-Path -LiteralPath $publishing) { throw "Interrupted publication requires recovery: $publishing" }
    Write-DurableNewBytes $publishing $Bytes
    # A hard stop at this boundary leaves only a non-authoritative .publishing
    # file. The final authoritative name is created by the same-volume rename.
    Invoke-TestHook ('PublishAfterFlush:' + [IO.Path]::GetFileName($Path))
    [IO.File]::Move($publishing, $Path)
}

function Write-NewJson([string]$Path, [object]$Value) {
    Write-NewBytes $Path ([Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 12)))
}

function Write-NewPublishingJson([string]$FinalPath, [object]$Value) {
    if ((Test-Path -LiteralPath $FinalPath) -or (Test-Path -LiteralPath ($FinalPath + '.publishing'))) { throw "Refusal terminal staging already exists: $FinalPath" }
    Write-DurableNewBytes ($FinalPath + '.publishing') ([Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 12)))
    Invoke-TestHook ('StageAfterFlush:' + [IO.Path]::GetFileName($FinalPath))
}

function Clear-PublishingResidue([string]$Path) {
    $publishing = $Path + '.publishing'
    if (-not (Test-Path -LiteralPath $publishing)) { return }
    if (-not (Test-Path -LiteralPath $publishing -PathType Leaf)) { throw "Publication residue is not a file: $publishing" }
    Assert-PhysicalFile $publishing 'Publication residue'
    [IO.File]::Delete($publishing)
}

function Invoke-TestHook([string]$Point) { if ($null -ne $TestHook) { & $TestHook $Point } }

function Test-SensitiveProfile([object]$Profile) {
    return (($Profile | ConvertTo-Json -Depth 16 -Compress) -match '(?i)(login|token|credential|password)')
}

function Get-BootstrapSaveAttestation([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required L00-C bootstrap save is absent: $Path" }
    $canonical = Get-CanonicalPath $Path
    Assert-PhysicalFile $canonical 'L00-C bootstrap save'
    $stream = [IO.File]::Open($canonical, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $bytes = Read-AllBytes $stream
        $item = Get-Item -LiteralPath $canonical -Force
        return [ordered]@{ Path = $canonical; Size = [Int64]$bytes.Length; LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o'); Sha256 = Get-Sha256 $bytes }
    }
    finally { $stream.Dispose() }
}

function Assert-BootstrapSave([object]$Expected) {
    $actual = Get-BootstrapSaveAttestation ([string]$Expected.Path)
    foreach ($property in @('Path', 'Size', 'LastWriteTimeUtc', 'Sha256')) {
        if ([string]$actual[$property] -cne [string]$Expected.$property) { throw "Bootstrap save drifted at $property." }
    }
}

function Get-PreparedUserSettings([byte[]]$Bytes, [string]$Profile) {
    $document = [Xml.XmlDocument]::new()
    $document.PreserveWhitespace = $true
    try { $document.LoadXml((Get-Utf8Text $Bytes)) }
    catch { throw 'WorldGen.VintageStory.csproj.user is not valid XML.' }
    $nodes = @($document.SelectNodes("//*[local-name()='ActiveDebugProfile']"))
    if ($nodes.Count -ne 1) { throw 'WorldGen.VintageStory.csproj.user must contain exactly one ActiveDebugProfile setting.' }
    $nodes[0].InnerText = $Profile
    return [Text.Encoding]::UTF8.GetBytes($document.OuterXml)
}

function Get-ProcessRecord([int]$ProcessId) {
    if ($null -ne $TestProcessQuery) { return (& $TestProcessQuery $ProcessId) }
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    $native = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
    if ($null -eq $native) { return $null }
    $executable = if ([string]::IsNullOrWhiteSpace([string]$native.ExecutablePath)) { $null } else { [IO.Path]::GetFullPath([string]$native.ExecutablePath) }
    return [pscustomobject]@{
        ProcessId = [int]$native.ProcessId
        ParentProcessId = [int]$native.ParentProcessId
        Name = [string]$native.Name
        ExecutablePath = $executable
        CommandLine = [string]$native.CommandLine
        StartTimeUtc = $process.StartTime.ToUniversalTime().ToString('o')
        IsRunning = $true
    }
}

function Get-ProcessRecords {
    if ($null -ne $TestProcessListQuery) { return @(& $TestProcessListQuery) }
    $records = New-Object 'System.Collections.Generic.List[object]'
    foreach ($native in @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)) {
        $process = Get-Process -Id ([int]$native.ProcessId) -ErrorAction SilentlyContinue
        if ($null -eq $process) { continue }
        try { $started = $process.StartTime.ToUniversalTime().ToString('o') } catch { continue }
        $executable = if ([string]::IsNullOrWhiteSpace([string]$native.ExecutablePath)) { $null } else { [IO.Path]::GetFullPath([string]$native.ExecutablePath) }
        [void]$records.Add([pscustomobject]@{
            ProcessId=[int]$native.ProcessId; ParentProcessId=[int]$native.ParentProcessId; Name=[string]$native.Name
            ExecutablePath=$executable; StartTimeUtc=$started; IsRunning=$true
        })
    }
    return @($records)
}

function Get-BoundProcessStatus([object]$Transaction) {
    $attestation = Read-RequiredJson (Join-Path $Transaction.Directory 'visual-studio-consumed.json') 'Visual Studio DTE/MCP attestation'
    $vs = Get-ProcessRecord ([int]$Transaction.Metadata.VisualStudio.ProcessId)
    $mcp = Get-ProcessRecord ([int]$attestation.McpProcessId)
    $vsRunning = $null -ne $vs -and [bool]$vs.IsRunning
    $mcpRunning = $null -ne $mcp -and [bool]$mcp.IsRunning
    if ($vsRunning -and (([DateTimeOffset]$vs.StartTimeUtc).UtcTicks -ne ([DateTimeOffset]$Transaction.Metadata.VisualStudio.StartTimeUtc).UtcTicks -or [string]$vs.Name -cne 'devenv.exe')) {
        throw 'Bound Visual Studio process identity drifted before launch disposition.'
    }
    if ($mcpRunning -and ([int]$mcp.ProcessId -ne [int]$attestation.McpProcessId -or [int]$mcp.ParentProcessId -ne [int]$Transaction.Metadata.VisualStudio.ProcessId -or [string]$mcp.Name -cne 'CodingWithCalvin.MCPServer.Server.exe')) {
        throw 'Bound MCP process identity drifted before launch disposition.'
    }
    return [ordered]@{
        VisualStudioProcessId=[int]$Transaction.Metadata.VisualStudio.ProcessId
        VisualStudioStatus=if($vsRunning){'RUNNING'}else{'STOPPED'}
        McpProcessId=[int]$attestation.McpProcessId
        McpStatus=if($mcpRunning){'RUNNING'}else{'STOPPED'}
        DebuggerStatus='Design'
    }
}

function Get-LaunchDescendantSummary([object]$Transaction) {
    $records = @(Get-ProcessRecords | Where-Object { $null -ne $_ -and [bool]$_.IsRunning })
    $byId = @{}
    foreach ($record in $records) { $byId[[int]$record.ProcessId] = $record }
    $identities = New-Object 'System.Collections.Generic.List[string]'
    foreach ($record in $records) {
        $name = [IO.Path]::GetFileName([string]$record.Name)
        if ($name -cne 'Vintagestory.exe' -and $name -cne 'VintagestoryServer.exe') { continue }
        $cursor = $record
        $descendant = $false
        $seen = @{}
        for ($depth=0; $depth -lt 8; $depth++) {
            $parentId = [int]$cursor.ParentProcessId
            if ($parentId -eq [int]$Transaction.Metadata.VisualStudio.ProcessId) { $descendant=$true; break }
            if ($parentId -le 0 -or $seen.ContainsKey($parentId) -or -not $byId.ContainsKey($parentId)) { break }
            $seen[$parentId]=$true; $cursor=$byId[$parentId]
        }
        if ($descendant) { [void]$identities.Add(('{0}:{1}:{2}' -f [int]$record.ProcessId,[string]$record.StartTimeUtc,$name)) }
    }
    $ordered = @($identities | Sort-Object -CaseSensitive)
    $fingerprint = Get-Sha256 ([Text.Encoding]::UTF8.GetBytes(($ordered -join "`n")))
    return [ordered]@{ Count=[int]$ordered.Count; Fingerprint=$fingerprint }
}

function Assert-VisualStudio([int]$ProcessId, [string]$ExpectedStartUtc = '') {
    if ($ProcessId -le 0) { throw 'An exact Visual Studio process id is required.' }
    $record = Get-ProcessRecord $ProcessId
    if ($null -eq $record -or -not [bool]$record.IsRunning) { throw "Visual Studio process $ProcessId is not running." }
    if ([int]$record.ProcessId -ne $ProcessId -or [IO.Path]::GetFileName([string]$record.Name) -cne 'devenv.exe') {
        throw "Process $ProcessId is not the bound Visual Studio instance."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedStartUtc)) {
        $actualStart = [DateTimeOffset]::Parse([string]$record.StartTimeUtc, [Globalization.CultureInfo]::InvariantCulture)
        $expectedStart = [DateTimeOffset]::Parse($ExpectedStartUtc, [Globalization.CultureInfo]::InvariantCulture)
        if ($actualStart.UtcTicks -ne $expectedStart.UtcTicks) { throw 'Visual Studio PID was reused or its process identity drifted.' }
    }
    return $record
}

function Assert-ChildOfVisualStudio([object]$Child, [object]$Metadata) {
    $cursor = $Child
    $seen = @{}
    for ($depth = 0; $depth -lt 8; $depth++) {
        $parentId = [int]$cursor.ParentProcessId
        if ($parentId -eq [int]$Metadata.VisualStudio.ProcessId) { return }
        if ($parentId -le 0 -or $seen.ContainsKey($parentId)) { break }
        $seen[$parentId] = $true
        $cursor = Get-ProcessRecord $parentId
        if ($null -eq $cursor -or -not [bool]$cursor.IsRunning) { break }
    }
    throw 'Acquired process is not a child of the exact bound Visual Studio instance.'
}

function Assert-ArrayEqual([object[]]$Actual, [object[]]$Expected, [string]$Label) {
    if ($Actual.Count -ne $Expected.Count) { throw "$Label count differs from the armed profile." }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ([string]$Actual[$index] -cne [string]$Expected[$index]) { throw "$Label differs at index $index from the armed profile." }
    }
}

# The raw Win32_Process command line is the process-creation provenance.  The
# managed host reports Environment.CommandLine independently: with the Vintage
# Story .NET host its argv[0] is the managed Vintagestory.dll entry point even
# though ProcessPath/MainModule and Win32_Process identify Vintagestory.exe.
# Both sources use Windows argv parsing, but they have distinct authorities.
function ConvertFrom-WindowsCommandLine([string]$CommandLine, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($CommandLine)) { throw "$Label is absent or empty." }
    if ($null -eq ('ISRWorldGen.L00C.NativeCommandLine' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ISRWorldGen.L00C {
    public static class NativeCommandLine {
        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CommandLineToArgvW(string commandLine, out int argc);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LocalFree(IntPtr hMem);
    }
}
'@ -ErrorAction Stop
    }
    $argc = 0
    $buffer = [ISRWorldGen.L00C.NativeCommandLine]::CommandLineToArgvW($CommandLine, [ref]$argc)
    if ($buffer -eq [IntPtr]::Zero -or $argc -le 0) { throw "$Label cannot be parsed by CommandLineToArgvW." }
    try {
        $arguments = New-Object 'System.Collections.Generic.List[string]'
        for ($index = 0; $index -lt $argc; $index++) {
            $pointer = [Runtime.InteropServices.Marshal]::ReadIntPtr($buffer, $index * [IntPtr]::Size)
            if ($pointer -eq [IntPtr]::Zero) { throw "$Label contains a null argv element." }
            $value = [Runtime.InteropServices.Marshal]::PtrToStringUni($pointer)
            if ($null -eq $value) { throw "$Label contains an unreadable argv element." }
            [void]$arguments.Add($value)
        }
        return @($arguments)
    }
    finally {
        [void][ISRWorldGen.L00C.NativeCommandLine]::LocalFree($buffer)
    }
}

function Assert-ArgumentTail([object[]]$Actual, [object[]]$Expected, [string]$Label) {
    if ($Actual.Count -ne ($Expected.Count + 1)) { throw "$Label argument vector count differs from the armed profile." }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ([string]$Actual[$index + 1] -cne [string]$Expected[$index]) { throw "$Label argument vector differs at index $index from the armed profile." }
    }
}

function Assert-RawProcessCommandLineProvenance([string]$CommandLine, [object]$Metadata) {
    $label = 'Live process command line'
    $actual = @(ConvertFrom-WindowsCommandLine $CommandLine $label)
    $expectedArguments = @($Metadata.ExpectedArguments | ForEach-Object { [string]$_ })
    Assert-ArgumentTail $actual $expectedArguments $label
    $actualExecutable = Get-CanonicalPath ([string]$actual[0])
    if ($actualExecutable -cne [string]$Metadata.ExpectedGameExecutablePath) { throw "$label executable differs from the armed profile." }
}

function Assert-InProcessCommandLineProvenance([string]$CommandLine, [object]$Metadata) {
    $label = 'Child in-process command line'
    $actual = @(ConvertFrom-WindowsCommandLine $CommandLine $label)
    $expectedArguments = @($Metadata.ExpectedArguments | ForEach-Object { [string]$_ })
    Assert-ArgumentTail $actual $expectedArguments $label

    # Environment.CommandLine may lead with the executable or with the exact
    # managed entry point selected by the known Vintage Story host.  Do not
    # treat that token as an executable identity; that identity is separately
    # bound to ProcessPath/MainModule and the raw CreateProcess command line.
    # The closed two-token set nevertheless catches spoofed/wrong hosts.
    $expectedExecutable = [string]$Metadata.ExpectedGameExecutablePath
    $expectedManagedEntrypoint = Join-Path (Split-Path $expectedExecutable -Parent) (([IO.Path]::GetFileNameWithoutExtension($expectedExecutable)) + '.dll')
    $actualHost = Get-CanonicalPath ([string]$actual[0])
    if ($actualHost -cne $expectedExecutable -and $actualHost -cne (Get-CanonicalPath $expectedManagedEntrypoint)) {
        throw "$label host token is neither the expected executable nor its expected managed entry point."
    }
}

function Read-RequiredJson([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label is absent." }
    Assert-PhysicalFile $Path $Label
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -DateKind String }
    catch { throw "$Label is unreadable; an authoritative receipt is never repaired in place." }
}

function Get-TerminalPaths([string]$Directory) {
    $paths = @()
    foreach ($name in @('restored.json','recovered.json','prepare-failed-recovered.json','pre-intent-recovered.json','pre-launch-blocked.json')) {
        $path = Join-Path $Directory $name
        if (Test-Path -LiteralPath $path) { $paths += $path }
    }
    return $paths
}

function Assert-NoTerminal([string]$Directory) {
    if (@(Get-TerminalPaths $Directory).Count -ne 0) { throw 'Terminal F5 transaction refuses every replay action.' }
}

function Read-ValidatedEnvelope([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'This action requires -BackupDirectory returned by Prepare.' }
    $backup = Get-CanonicalPath $Path
    Assert-ChildPath $backup $transactionsRoot 'Transaction directory'
    Assert-PhysicalDirectory $backup 'Transaction directory'
    $envelopePath = Join-Path $backup 'envelope.json'
    if (-not (Test-Path -LiteralPath $envelopePath -PathType Leaf)) {
        $legacyMetadata = Join-Path $backup 'metadata.json'
        if (Test-Path -LiteralPath $legacyMetadata -PathType Leaf) {
            try { $legacy = Get-Content -LiteralPath $legacyMetadata -Raw | ConvertFrom-Json -DateKind String } catch { $legacy = $null }
            if ($null -ne $legacy -and ($null -ne $legacy.PSObject.Properties['Phase'] -or $null -eq $legacy.PSObject.Properties['SchemaVersion'])) {
                throw 'Stale PREPARED receipt is not a current F5 transaction.'
            }
        }
    }
    $envelope = Read-RequiredJson $envelopePath 'F5 transaction envelope'
    if ([int]$envelope.SchemaVersion -ne 2 -or [string]$envelope.Protocol -cne $Protocol -or [string]$envelope.Status -cne 'DIRECTORY_RESERVED') {
        throw 'F5 transaction envelope is stale or malformed.'
    }
    if ([string]$envelope.Owner -cne (Get-Acl -LiteralPath $backup).Owner -or [string]$envelope.TransactionDirectory -cne $backup -or
        [string]$envelope.LaunchSettingsPath -cne $launchSettings -or [string]$envelope.ProjectPath -cne $project -or [string]$envelope.ProjectUserSettingsPath -cne $projectUserSettings) {
        throw 'F5 transaction envelope identity or ownership is not trusted.'
    }
    if ([string]$envelope.TransactionId -notmatch '^[0-9a-f]{32}$' -or [IO.Path]::GetFileName($backup) -cne ('f5-' + [string]$envelope.TransactionId) -or [string]$envelope.Nonce -notmatch '^[A-F0-9]{64}$') {
        throw 'F5 transaction envelope id does not bind its directory.'
    }
    if ([string]$envelope.SolutionPath -cne $solution -or [string]$envelope.LaboratoryRoot -cne $laboratory) { throw 'F5 transaction envelope belongs to another solution.' }
    $abandoned = $null -ne $envelope.PSObject.Properties['AbandonedBeforeIntent'] -and [bool]$envelope.AbandonedBeforeIntent
    if ($abandoned) {
        if ([int]$envelope.VisualStudio.ProcessId -ne 0 -or [string]$envelope.VisualStudio.Name -cne 'UNBOUND' -or ([DateTimeOffset]$envelope.VisualStudio.StartTimeUtc).UtcTicks -ne ([DateTimeOffset]'1970-01-01T00:00:00Z').UtcTicks) {
            throw 'Abandoned pre-intent envelope has an invalid unbound process sentinel.'
        }
    }
    elseif ([int]$envelope.VisualStudio.ProcessId -le 0 -or [IO.Path]::GetFileName([string]$envelope.VisualStudio.Name) -cne 'devenv.exe') {
        throw 'Normal pre-intent envelope has no exact Visual Studio process identity.'
    }
    return [pscustomobject]@{ Directory=$backup; EnvelopePath=$envelopePath; Envelope=$envelope }
}

function Read-ValidatedTransaction([string]$Path) {
    $envelopeTransaction = Read-ValidatedEnvelope $Path
    $backup = $envelopeTransaction.Directory
    $metadataPath = Join-Path $backup 'metadata.json'
    foreach ($required in @($metadataPath, (Join-Path $backup 'launchSettings.original.bin'), (Join-Path $backup 'launchSettings.intended.bin'), (Join-Path $backup 'project.user.original.bin'), (Join-Path $backup 'project.user.intended.bin'))) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw 'F5 transaction is incomplete; use Recover only after inspecting its exact directory.' }
    }
    $metadata = Read-RequiredJson $metadataPath 'F5 transaction metadata'
    foreach ($property in @('SchemaVersion','Protocol','State')) {
        if ($null -eq $metadata.PSObject.Properties[$property]) { throw 'Stale PREPARED receipt is not a current F5 transaction.' }
    }
    if ([int]$metadata.SchemaVersion -ne 2 -or [string]$metadata.Protocol -cne $Protocol -or [string]$metadata.State -cne 'PREPARE_INTENT') {
        throw 'Stale PREPARED receipt is not a current F5 transaction.'
    }
    if ([string]$metadata.Owner -cne (Get-Acl -LiteralPath $backup).Owner -or [string]$metadata.TransactionDirectory -cne $backup -or [string]$metadata.LaunchSettingsPath -cne $launchSettings -or [string]$metadata.ProjectPath -cne $project -or [string]$metadata.ProjectUserSettingsPath -cne $projectUserSettings) {
        throw 'F5 transaction identity or ownership is not trusted.'
    }
    if ([string]$metadata.TransactionId -notmatch '^[0-9a-f]{32}$' -or [IO.Path]::GetFileName($backup) -cne ('f5-' + [string]$metadata.TransactionId)) {
        throw 'F5 transaction id does not bind its directory.'
    }
    if ([string]$metadata.Nonce -notmatch '^[A-F0-9]{64}$') { throw 'F5 transaction nonce is malformed.' }
    if ([string]$metadata.Nonce -cne [string]$envelopeTransaction.Envelope.Nonce -or [string]$metadata.TransactionId -cne [string]$envelopeTransaction.Envelope.TransactionId -or
        [string]$metadata.OriginalSha256 -cne [string]$envelopeTransaction.Envelope.OriginalSha256 -or [string]$metadata.IntendedSha256 -cne [string]$envelopeTransaction.Envelope.IntendedSha256 -or
        [string]$metadata.ProjectUserOriginalSha256 -cne [string]$envelopeTransaction.Envelope.ProjectUserOriginalSha256 -or [string]$metadata.ProjectUserIntendedSha256 -cne [string]$envelopeTransaction.Envelope.ProjectUserIntendedSha256) {
        throw 'F5 transaction metadata is detached from its pre-intent envelope.'
    }
    if ([string]$metadata.SolutionPath -cne $solution -or [string]$metadata.LaboratoryRoot -cne $laboratory) { throw 'F5 transaction belongs to another solution or laboratory root.' }
    $files = @(
        @('launchSettings.original.bin', 'OriginalSha256'), @('launchSettings.intended.bin', 'IntendedSha256'),
        @('project.user.original.bin', 'ProjectUserOriginalSha256'), @('project.user.intended.bin', 'ProjectUserIntendedSha256')
    )
    foreach ($pair in $files) {
        $artifactPath = Join-Path $backup $pair[0]
        Assert-PhysicalFile $artifactPath "F5 transaction backup $($pair[0])"
        if ((Get-FileSha256 $artifactPath) -cne [string]$metadata.($pair[1])) { throw "F5 transaction backup hash drifted for $($pair[0])." }
    }
    return [pscustomobject]@{ Directory = $backup; EnvelopePath=$envelopeTransaction.EnvelopePath; MetadataPath = $metadataPath; Metadata = $metadata }
}

function Get-AttestationProjectGuid([string]$SolutionPath, [string]$ProjectPath) {
    $solutionDirectory = [IO.Path]::GetDirectoryName($SolutionPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $solutionDirectory + [IO.Path]::DirectorySeparatorChar
    if (-not $ProjectPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Visual Studio attestation project escaped the solution directory.' }
    $relative = $ProjectPath.Substring($prefix.Length).Replace('/', '\')
    $matches = [regex]::Matches([IO.File]::ReadAllText($SolutionPath), '(?im)^Project\("\{[^}]+\}"\)\s*=\s*"[^"]+",\s*"' + [regex]::Escape($relative) + '",\s*"\{(?<guid>[0-9a-f-]{36})\}"\s*$')
    if ($matches.Count -ne 1) { throw 'Visual Studio attestation project GUID is not uniquely bound by the solution.' }
    return ([Guid]$matches[0].Groups['guid'].Value).ToString('D').ToUpperInvariant()
}

function Get-ExpectedAttestedEnvironment([object]$Transaction) {
    $intendedPath = Join-Path $Transaction.Directory 'launchSettings.intended.bin'
    try { $document = Get-Utf8Text (Read-PathBytes $intendedPath) | ConvertFrom-Json }
    catch { throw 'Intended launch settings backup is not valid JSON.' }
    $profile = $document.profiles.PSObject.Properties[[string]$Transaction.Metadata.ProfileName]
    if ($null -eq $profile -or $null -eq $profile.Value.environmentVariables) { throw 'Intended launch profile has no environment to attest.' }
    $result = [ordered]@{}
    foreach ($entry in @($profile.Value.environmentVariables.PSObject.Properties)) {
        if ($result.Contains([string]$entry.Name)) { throw 'Intended launch environment contains a duplicate key.' }
        $result[[string]$entry.Name] = [string]$entry.Value
    }
    return $result
}

function Test-AttestedStringArray([object]$Expected, [object]$Actual) {
    $left = @($Expected | ForEach-Object { [string]$_ })
    $right = @($Actual | ForEach-Object { [string]$_ })
    if ($left.Count -ne $right.Count) { return $false }
    for ($index = 0; $index -lt $left.Count; $index++) { if ($left[$index] -cne $right[$index]) { return $false } }
    return $true
}

function Test-AttestedEnvironment([Collections.IDictionary]$Expected, [object]$Actual) {
    if ($null -eq $Actual) { return $false }
    $properties = @($Actual.PSObject.Properties)
    if ($properties.Count -ne $Expected.Count) { return $false }
    foreach ($name in $Expected.Keys) {
        $property = $Actual.PSObject.Properties[[string]$name]
        if ($null -eq $property -or [string]$property.Value -cne [string]$Expected[$name]) { return $false }
    }
    return $true
}

function Assert-VisualStudioAttestation([object]$Transaction, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Arm requires a Visual Studio DTE/MCP attestation path.' }
    $expectedPath = Join-Path $Transaction.Directory 'visual-studio-consumed.json'
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Visual Studio DTE/MCP attestation is absent; a .publishing residue is not authoritative.' }
    $actualPath = Get-CanonicalPath $Path
    if ($actualPath -cne $expectedPath) { throw 'Visual Studio attestation must use the fixed transaction receipt path.' }
    $receipt = Read-RequiredJson $actualPath 'Visual Studio DTE/MCP attestation'
    $metadata = $Transaction.Metadata
    $expectedProjectGuid = Get-AttestationProjectGuid $solution ([string]$metadata.ProjectPath)
    foreach ($property in @('DebuggerMode','UnsavedDocumentCount','SolutionIsDirty','ProjectIsDirty','ProjectSaved')) {
        if ($null -eq $receipt.PSObject.Properties[$property]) { throw 'Visual Studio DTE/MCP attestation omitted a reload safety precondition.' }
    }
    if ([string]$receipt.DebuggerMode -cne 'Design' -or [int]$receipt.UnsavedDocumentCount -ne 0 -or [bool]$receipt.SolutionIsDirty -or [bool]$receipt.ProjectIsDirty -or -not [bool]$receipt.ProjectSaved) {
        throw 'Visual Studio DTE/MCP attestation failed a reload safety precondition.'
    }
    if ([int]$receipt.SchemaVersion -ne 2 -or [string]$receipt.Protocol -cne $Protocol -or [string]$receipt.Status -cne 'VISUAL_STUDIO_PROFILE_CONSUMED' -or
        [string]$receipt.Source -cne 'VISUAL_STUDIO_DTE_MCP' -or [string]$receipt.AttestationMethod -cne 'ROT_DTE_IVS_QUERY_DEBUG_TARGETS' -or
        [string]$receipt.TransactionId -cne [string]$metadata.TransactionId -or [string]$receipt.Nonce -cne [string]$metadata.Nonce -or
        [int]$receipt.ProcessId -ne [int]$metadata.VisualStudio.ProcessId -or ([DateTimeOffset]$receipt.ProcessStartUtc).UtcTicks -ne ([DateTimeOffset]$metadata.VisualStudio.StartTimeUtc).UtcTicks -or
        [string]$receipt.SolutionPath -cne $solution -or [string]$receipt.ProjectPath -cne [string]$metadata.ProjectPath -or
        [string]$receipt.StartupProjectPath -cne [string]$metadata.ProjectPath -or [string]$receipt.ProjectGuid -cne $expectedProjectGuid -or
        [string]$receipt.ActiveConfiguration -cne 'Debug' -or [string]$receipt.ActivePlatform -cne 'Any CPU' -or
        [string]$receipt.ActiveDebugProfile -cne [string]$metadata.ProfileName -or [string]$receipt.EvaluatedExecutablePath -cne [string]$metadata.ExpectedGameExecutablePath -or
        [string]$receipt.EvaluatedWorkingDirectory -cne [IO.Path]::GetDirectoryName([string]$metadata.ExpectedGameExecutablePath) -or
        [string]$receipt.ConsumedLaunchSettingsSha256 -cne [string]$metadata.IntendedSha256 -or [string]$receipt.ConsumedProjectUserSettingsSha256 -cne [string]$metadata.ProjectUserIntendedSha256) {
        throw 'Visual Studio DTE/MCP attestation is stale, cached, or belongs to another process, solution, startup project, GUID, configuration, profile, executable, working directory, or settings revision.'
    }
    if (-not (Test-AttestedStringArray $metadata.ExpectedArguments $receipt.EvaluatedArguments)) {
        throw 'Visual Studio DTE/MCP attestation did not consume the exact argument vector.'
    }
    $expectedEnvironment = Get-ExpectedAttestedEnvironment $Transaction
    if (-not (Test-AttestedEnvironment $expectedEnvironment $receipt.EvaluatedEnvironment)) {
        throw 'Visual Studio DTE/MCP attestation did not consume the exact launch environment.'
    }
    $intentPath = Join-Path $Transaction.Directory 'visual-studio-reload-intent.json'
    $intent = Read-RequiredJson $intentPath 'Visual Studio reload intent'
    foreach ($property in @('DebuggerMode','UnsavedDocumentCount','SolutionIsDirty','ProjectIsDirty','ProjectSaved')) {
        if ($null -eq $intent.PSObject.Properties[$property]) { throw 'Visual Studio reload intent omitted a safety precondition.' }
    }
    if ([string]$intent.DebuggerMode -cne 'Design' -or [int]$intent.UnsavedDocumentCount -ne 0 -or [bool]$intent.SolutionIsDirty -or [bool]$intent.ProjectIsDirty -or -not [bool]$intent.ProjectSaved) {
        throw 'Visual Studio reload intent failed a safety precondition.'
    }
    if ([int]$intent.SchemaVersion -ne 2 -or [string]$intent.Protocol -cne $Protocol -or [string]$intent.Status -cne 'VISUAL_STUDIO_RELOAD_INTENT' -or
        [string]$intent.TransactionId -cne [string]$metadata.TransactionId -or [int]$intent.ProcessId -ne [int]$metadata.VisualStudio.ProcessId -or
        [string]$intent.ProjectGuid -cne $expectedProjectGuid -or [string]$intent.MetadataSha256 -cne (Get-FileSha256 $Transaction.MetadataPath) -or
        [string]$receipt.ReloadIntentSha256 -cne (Get-FileSha256 $intentPath)) {
        throw 'Visual Studio DTE/MCP attestation is detached from its durable reload intent.'
    }
    $mcp = Get-ProcessRecord ([int]$receipt.McpProcessId)
    if ([int]$receipt.McpParentProcessId -ne [int]$metadata.VisualStudio.ProcessId -or $null -eq $mcp -or -not $mcp.IsRunning -or
        [string]$mcp.Name -cne 'CodingWithCalvin.MCPServer.Server.exe' -or [int]$mcp.ParentProcessId -ne [int]$metadata.VisualStudio.ProcessId) {
        throw 'Visual Studio DTE/MCP attestation is not bound to the live MCP child of the exact devenv process.'
    }
    if ([DateTimeOffset]$receipt.ObservedUtc -lt [DateTimeOffset]$metadata.PreparedUtc -or [DateTimeOffset]$receipt.ObservedUtc -lt [DateTimeOffset]$intent.PreparedUtc -or [DateTimeOffset]$receipt.ObservedUtc -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        throw 'Visual Studio DTE/MCP attestation time is outside this prepare interval.'
    }
    return $receipt
}

function Assert-Armed([object]$Transaction) {
    $path = Join-Path $Transaction.Directory 'armed.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'F5 transaction is not durably ARMED_FOR_F5.' }
    $receipt = Read-RequiredJson $path 'ARMED_FOR_F5 receipt'
    if ([int]$receipt.SchemaVersion -ne 2 -or [string]$receipt.Protocol -cne $Protocol -or [string]$receipt.Status -cne 'ARMED_FOR_F5' -or
        [string]$receipt.TransactionId -cne [string]$Transaction.Metadata.TransactionId -or [string]$receipt.MetadataSha256 -cne (Get-FileSha256 $Transaction.MetadataPath) -or
        [string]$receipt.LaunchSettingsSha256 -cne [string]$Transaction.Metadata.IntendedSha256 -or [string]$receipt.ProjectUserSettingsSha256 -cne [string]$Transaction.Metadata.ProjectUserIntendedSha256 -or
        [string]$receipt.VisualStudioAttestationSha256 -cne (Get-FileSha256 (Join-Path $Transaction.Directory 'visual-studio-consumed.json'))) {
        throw 'ARMED_FOR_F5 receipt is stale, malformed, or detached from current intended bytes.'
    }
    return $receipt
}

function Assert-ExactProperties([object]$Value, [string[]]$Expected, [string]$Label) {
    $actual = @($Value.PSObject.Properties.Name | Sort-Object -CaseSensitive)
    $wanted = @($Expected | Sort-Object -CaseSensitive)
    if ($actual.Count -ne $wanted.Count) { throw "$Label has an unexpected field set." }
    for ($index=0; $index -lt $wanted.Count; $index++) {
        if ([string]$actual[$index] -cne [string]$wanted[$index]) { throw "$Label has an unexpected field set." }
    }
}

function Get-ReceiptSeal([object]$Value) {
    $canonical = [ordered]@{}
    foreach ($property in @($Value.PSObject.Properties)) {
        if ([string]$property.Name -cne 'SealSha256') { $canonical[[string]$property.Name] = $property.Value }
    }
    return Get-Sha256 ([Text.Encoding]::UTF8.GetBytes(($canonical | ConvertTo-Json -Depth 12 -Compress)))
}

function Add-ReceiptSeal([Collections.IDictionary]$Value) {
    $Value['SealSha256'] = Get-Sha256 ([Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 12 -Compress)))
    return $Value
}

function Get-ValidatedUtc([object]$Value, [string]$Label) {
    if ($Value -isnot [string]) { throw "$Label must be an ISO UTC string." }
    try { $parsed = [DateTimeOffset]::ParseExact([string]$Value, 'o', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind) }
    catch { throw "$Label must be an exact round-trip timestamp." }
    if ($parsed.Offset -ne [TimeSpan]::Zero) { throw "$Label must be UTC." }
    return $parsed
}

function Assert-LaunchIntent([object]$Transaction) {
    $path = Join-Path $Transaction.Directory 'pre-launch-intent.json'
    $receipt = Read-RequiredJson $path 'PRE_LAUNCH_INTENT receipt'
    Assert-ExactProperties $receipt @(
        'SchemaVersion','Protocol','Status','TransactionId','MetadataSha256','ArmedReceiptSha256','VisualStudioAttestationSha256','Tool',
        'DebuggerStatus','VisualStudioProcessId','VisualStudioStatus','McpProcessId','McpStatus','LaunchSettingsPreSha256',
        'ProjectUserSettingsPreSha256','DescendantCountPre','DescendantFingerprintPre','NoChildProcess','NoCampaignStarted','IntentUtc','SealSha256'
    ) 'PRE_LAUNCH_INTENT receipt'
    if ([int]$receipt.SchemaVersion -ne 2 -or [string]$receipt.Protocol -cne $Protocol -or [string]$receipt.Status -cne 'PRE_LAUNCH_INTENT' -or
        [string]$receipt.TransactionId -cne [string]$Transaction.Metadata.TransactionId -or [string]$receipt.MetadataSha256 -cne (Get-FileSha256 $Transaction.MetadataPath) -or
        [string]$receipt.ArmedReceiptSha256 -cne (Get-FileSha256 (Join-Path $Transaction.Directory 'armed.json')) -or
        [string]$receipt.VisualStudioAttestationSha256 -cne (Get-FileSha256 (Join-Path $Transaction.Directory 'visual-studio-consumed.json')) -or
        [string]$receipt.Tool -cne 'mcp__visualstudio__debugger_launch' -or [string]$receipt.DebuggerStatus -cne 'Design' -or
        [int]$receipt.VisualStudioProcessId -ne [int]$Transaction.Metadata.VisualStudio.ProcessId -or [string]$receipt.VisualStudioStatus -cne 'RUNNING' -or
        [int]$receipt.McpProcessId -ne [int](Read-RequiredJson (Join-Path $Transaction.Directory 'visual-studio-consumed.json') 'Visual Studio DTE/MCP attestation').McpProcessId -or
        [string]$receipt.McpStatus -cne 'RUNNING' -or [string]$receipt.LaunchSettingsPreSha256 -cne [string]$Transaction.Metadata.IntendedSha256 -or
        [string]$receipt.ProjectUserSettingsPreSha256 -cne [string]$Transaction.Metadata.ProjectUserIntendedSha256 -or
        [int]$receipt.DescendantCountPre -ne 0 -or [string]$receipt.DescendantFingerprintPre -notmatch '^[A-F0-9]{64}$' -or
        $receipt.NoChildProcess -isnot [bool] -or -not $receipt.NoChildProcess -or $receipt.NoCampaignStarted -isnot [bool] -or -not $receipt.NoCampaignStarted -or
        [string]$receipt.SealSha256 -cne (Get-ReceiptSeal $receipt)) {
        throw 'PRE_LAUNCH_INTENT receipt is malformed, unsafe, or detached from the armed transaction.'
    }
    $intentUtc = Get-ValidatedUtc $receipt.IntentUtc 'PRE_LAUNCH_INTENT IntentUtc'
    $armed = Read-RequiredJson (Join-Path $Transaction.Directory 'armed.json') 'ARMED_FOR_F5 receipt'
    if ($intentUtc -lt (Get-ValidatedUtc $armed.ArmedUtc 'ARMED_FOR_F5 ArmedUtc') -or $intentUtc -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) { throw 'PRE_LAUNCH_INTENT time is outside the armed interval.' }
    return $receipt
}

function Assert-RefusalObservation([object]$Transaction, [string]$Path = '') {
    $path = if ([string]::IsNullOrWhiteSpace($Path)) { Join-Path $Transaction.Directory 'pre-launch-refusal-observed.json' } else { $Path }
    $receipt = Read-RequiredJson $path 'PRE_LAUNCH_REFUSAL_OBSERVED receipt'
    Assert-ExactProperties $receipt @(
        'SchemaVersion','Protocol','Status','RuntimeStatus','RefusalCategory','Tool','TransactionId','LaunchIntentSha256',
        'VisualStudioProcessId','VisualStudioStatus','McpProcessId','McpStatus','DebuggerStatus','LaunchSettingsPostRefusalSha256',
        'ProjectUserSettingsPostRefusalSha256','DescendantCount','DescendantFingerprint','NoChildProcess','NoCampaignStarted','ObservedUtc','SealSha256'
    ) 'PRE_LAUNCH_REFUSAL_OBSERVED receipt'
    $intent = Assert-LaunchIntent $Transaction
    if ([int]$receipt.SchemaVersion -ne 2 -or [string]$receipt.Protocol -cne $Protocol -or [string]$receipt.Status -cne 'PRE_LAUNCH_REFUSAL_OBSERVED' -or
        [string]$receipt.RuntimeStatus -cne 'NOT_RUN' -or [string]$receipt.RefusalCategory -cne 'MCP_TOOL_REFUSED_BEFORE_ACTION' -or
        [string]$receipt.Tool -cne 'mcp__visualstudio__debugger_launch' -or [string]$receipt.TransactionId -cne [string]$Transaction.Metadata.TransactionId -or
        [string]$receipt.LaunchIntentSha256 -cne (Get-FileSha256 (Join-Path $Transaction.Directory 'pre-launch-intent.json')) -or
        [int]$receipt.VisualStudioProcessId -ne [int]$intent.VisualStudioProcessId -or
        ([string]$receipt.VisualStudioStatus -cne 'RUNNING' -and [string]$receipt.VisualStudioStatus -cne 'STOPPED') -or
        [int]$receipt.McpProcessId -ne [int]$intent.McpProcessId -or ([string]$receipt.McpStatus -cne 'RUNNING' -and [string]$receipt.McpStatus -cne 'STOPPED') -or
        [string]$receipt.DebuggerStatus -cne 'NOT_RUN' -or [string]$receipt.LaunchSettingsPostRefusalSha256 -cne [string]$Transaction.Metadata.IntendedSha256 -or
        [string]$receipt.ProjectUserSettingsPostRefusalSha256 -cne [string]$Transaction.Metadata.ProjectUserIntendedSha256 -or [int]$receipt.DescendantCount -ne 0 -or
        [string]$receipt.DescendantFingerprint -cne [string]$intent.DescendantFingerprintPre -or $receipt.NoChildProcess -isnot [bool] -or -not $receipt.NoChildProcess -or
        $receipt.NoCampaignStarted -isnot [bool] -or -not $receipt.NoCampaignStarted -or [string]$receipt.SealSha256 -cne (Get-ReceiptSeal $receipt)) {
        throw 'PRE_LAUNCH_REFUSAL_OBSERVED receipt is malformed, tampered, or detached.'
    }
    $observedUtc = Get-ValidatedUtc $receipt.ObservedUtc 'PRE_LAUNCH_REFUSAL_OBSERVED ObservedUtc'
    if ($observedUtc -lt (Get-ValidatedUtc $intent.IntentUtc 'PRE_LAUNCH_INTENT IntentUtc') -or $observedUtc -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) { throw 'PRE_LAUNCH_REFUSAL_OBSERVED time is outside the launch interval.' }
    return $receipt
}

function Assert-BlockedReceipt([object]$Transaction, [object]$Receipt) {
    Assert-ExactProperties $Receipt @(
        'SchemaVersion','Protocol','Status','RuntimeStatus','RefusalCategory','Tool','TransactionId','MetadataSha256','ArmedReceiptSha256',
        'VisualStudioAttestationSha256','LaunchIntentSha256','RefusalObservationSha256','DebuggerStatusBefore','DebuggerStatusAfter','VisualStudioProcessId',
        'VisualStudioStatusBefore','VisualStudioStatusAfter','McpProcessId','McpStatusBefore','McpStatusAfter','LaunchSettingsPreSha256',
        'LaunchSettingsPostRefusalSha256','ProjectUserSettingsPreSha256','ProjectUserSettingsPostRefusalSha256','RestoredLaunchSettingsSha256',
        'RestoredProjectUserSettingsSha256','DescendantCountBefore','DescendantFingerprintBefore','DescendantCountAfter','DescendantFingerprintAfter',
        'NoChildProcess','NoCampaignStarted','BlockedUtc','SealSha256'
    ) 'PRE_LAUNCH_BLOCKED terminal receipt'
    $intent = Assert-LaunchIntent $Transaction
    $observation = Assert-RefusalObservation $Transaction
    if ([int]$Receipt.SchemaVersion -ne 2 -or [string]$Receipt.Protocol -cne $Protocol -or [string]$Receipt.Status -cne 'BLOCKED' -or
        [string]$Receipt.RuntimeStatus -cne 'NOT_RUN' -or [string]$Receipt.RefusalCategory -cne 'MCP_TOOL_REFUSED_BEFORE_ACTION' -or
        [string]$Receipt.Tool -cne 'mcp__visualstudio__debugger_launch' -or [string]$Receipt.TransactionId -cne [string]$Transaction.Metadata.TransactionId -or
        [string]$Receipt.MetadataSha256 -cne (Get-FileSha256 $Transaction.MetadataPath) -or [string]$Receipt.ArmedReceiptSha256 -cne (Get-FileSha256 (Join-Path $Transaction.Directory 'armed.json')) -or
        [string]$Receipt.VisualStudioAttestationSha256 -cne (Get-FileSha256 (Join-Path $Transaction.Directory 'visual-studio-consumed.json')) -or
        [string]$Receipt.LaunchIntentSha256 -cne (Get-FileSha256 (Join-Path $Transaction.Directory 'pre-launch-intent.json')) -or
        [string]$Receipt.RefusalObservationSha256 -cne (Get-FileSha256 (Join-Path $Transaction.Directory 'pre-launch-refusal-observed.json')) -or
        [string]$Receipt.DebuggerStatusBefore -cne 'Design' -or [string]$Receipt.DebuggerStatusAfter -cne 'NOT_RUN' -or
        [int]$Receipt.VisualStudioProcessId -ne [int]$intent.VisualStudioProcessId -or [string]$Receipt.VisualStudioStatusBefore -cne [string]$intent.VisualStudioStatus -or
        [string]$Receipt.VisualStudioStatusAfter -cne [string]$observation.VisualStudioStatus -or
        [int]$Receipt.McpProcessId -ne [int]$intent.McpProcessId -or [string]$Receipt.McpStatusBefore -cne [string]$intent.McpStatus -or
        [string]$Receipt.McpStatusAfter -cne [string]$observation.McpStatus -or
        [string]$Receipt.LaunchSettingsPreSha256 -cne [string]$intent.LaunchSettingsPreSha256 -or
        [string]$Receipt.LaunchSettingsPostRefusalSha256 -cne [string]$observation.LaunchSettingsPostRefusalSha256 -or
        [string]$Receipt.ProjectUserSettingsPreSha256 -cne [string]$intent.ProjectUserSettingsPreSha256 -or
        [string]$Receipt.ProjectUserSettingsPostRefusalSha256 -cne [string]$observation.ProjectUserSettingsPostRefusalSha256 -or
        [string]$Receipt.RestoredLaunchSettingsSha256 -cne [string]$Transaction.Metadata.OriginalSha256 -or
        [string]$Receipt.RestoredProjectUserSettingsSha256 -cne [string]$Transaction.Metadata.ProjectUserOriginalSha256 -or
        [int]$Receipt.DescendantCountBefore -ne 0 -or [string]$Receipt.DescendantFingerprintBefore -cne [string]$observation.DescendantFingerprint -or
        [int]$Receipt.DescendantCountAfter -ne 0 -or [string]$Receipt.DescendantFingerprintAfter -cne [string]$intent.DescendantFingerprintPre -or
        $Receipt.NoChildProcess -isnot [bool] -or -not $Receipt.NoChildProcess -or $Receipt.NoCampaignStarted -isnot [bool] -or -not $Receipt.NoCampaignStarted -or
        [string]$Receipt.SealSha256 -cne (Get-ReceiptSeal $Receipt) -or
        (Test-Path -LiteralPath (Join-Path $Transaction.Directory 'child-acquisition.json')) -or (Test-Path -LiteralPath (Join-Path $Transaction.Directory 'acquired.json'))) {
        throw 'Prior pre-launch refusal terminal is malformed, tampered, replayed, or does not prove NOT_RUN.'
    }
    $blockedUtc = Get-ValidatedUtc $Receipt.BlockedUtc 'PRE_LAUNCH_BLOCKED BlockedUtc'
    if ($blockedUtc -lt (Get-ValidatedUtc $observation.ObservedUtc 'PRE_LAUNCH_REFUSAL_OBSERVED ObservedUtc') -or $blockedUtc -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) { throw 'PRE_LAUNCH_BLOCKED time is outside the refusal interval.' }
    return $Receipt
}

function Assert-Acquired([object]$Transaction) {
    $path = Join-Path $Transaction.Directory 'acquired.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Restore is forbidden before confirmed launch acquisition.' }
    $receipt = Read-RequiredJson $path 'LAUNCH_ACQUIRED receipt'
    $childPath = Join-Path $Transaction.Directory 'child-acquisition.json'
    if ([int]$receipt.SchemaVersion -ne 2 -or [string]$receipt.Protocol -cne $Protocol -or [string]$receipt.Status -cne 'LAUNCH_ACQUIRED' -or
        [string]$receipt.TransactionId -cne [string]$Transaction.Metadata.TransactionId -or [string]$receipt.MetadataSha256 -cne (Get-FileSha256 $Transaction.MetadataPath) -or
        [string]$receipt.ArmedReceiptSha256 -cne (Get-FileSha256 (Join-Path $Transaction.Directory 'armed.json')) -or [string]$receipt.ChildReceiptSha256 -cne (Get-FileSha256 $childPath) -or
        [int]$receipt.VisualStudioProcessId -ne [int]$Transaction.Metadata.VisualStudio.ProcessId) {
        throw 'Launch acquisition receipt is malformed or detached from its child receipt.'
    }
    return $receipt
}

function Assert-TerminalTransaction([string]$Directory) {
    $envelopeTransaction = Read-ValidatedEnvelope $Directory
    $found = @(Get-TerminalPaths $envelopeTransaction.Directory)
    if ($found.Count -ne 1) { throw 'Prior F5 transaction must contain exactly one terminal receipt.' }
    $receipt = Read-RequiredJson $found[0] 'Prior F5 transaction terminal receipt'
    $isPreIntent = [IO.Path]::GetFileName($found[0]) -ceq 'pre-intent-recovered.json'
    if ($isPreIntent) {
        foreach ($name in @('metadata.json','settings-prepared.json','visual-studio-reload-intent.json','visual-studio-consumed.json','armed.json','pre-launch-intent.json','pre-launch-refusal-observed.json','child-acquisition.json','acquired.json')) {
            if (Test-Path -LiteralPath (Join-Path $envelopeTransaction.Directory $name)) { throw 'Pre-intent terminal cannot coexist with a later transaction state.' }
        }
        if ([int]$receipt.SchemaVersion -ne 2 -or [string]$receipt.Protocol -cne $Protocol -or [string]$receipt.Status -cne 'PRE_INTENT_RECOVERED' -or
            [string]$receipt.TransactionId -cne [string]$envelopeTransaction.Envelope.TransactionId -or [string]$receipt.EnvelopeSha256 -cne (Get-FileSha256 $envelopeTransaction.EnvelopePath) -or
            [string]$receipt.OriginalSha256 -cne [string]$envelopeTransaction.Envelope.OriginalSha256 -or [string]$receipt.ProjectUserOriginalSha256 -cne [string]$envelopeTransaction.Envelope.ProjectUserOriginalSha256) {
            throw 'Prior pre-intent terminal receipt is malformed or detached.'
        }
        return
    }
    $transaction = Read-ValidatedTransaction $Directory
    $terminalName = [IO.Path]::GetFileName($found[0])
    if ($terminalName -ceq 'pre-launch-blocked.json') {
        [void](Assert-BlockedReceipt $transaction $receipt)
        return
    }
    $expected = @{
        'restored.json'='RESTORED_AFTER_ACQUISITION'; 'recovered.json'='RECOVERED_WITH_BOUND_VS_STOPPED'; 'prepare-failed-recovered.json'='PREPARE_FAILED_RECOVERED'
    }[[IO.Path]::GetFileName($found[0])]
    if ([int]$receipt.SchemaVersion -ne 2 -or [string]$receipt.Protocol -cne $Protocol -or [string]$receipt.Status -cne $expected -or
        [string]$receipt.TransactionId -cne [string]$transaction.Metadata.TransactionId -or [string]$receipt.MetadataSha256 -cne (Get-FileSha256 $transaction.MetadataPath) -or
        [string]$receipt.OriginalSha256 -cne [string]$transaction.Metadata.OriginalSha256 -or [string]$receipt.ProjectUserOriginalSha256 -cne [string]$transaction.Metadata.ProjectUserOriginalSha256) {
        throw 'Prior F5 transaction terminal receipt is malformed or detached.'
    }
    if ($terminalName -ceq 'restored.json') {
        [void](Assert-Acquired $transaction)
    }
    elseif ($terminalName -ceq 'recovered.json') {
        if (Test-Path -LiteralPath (Join-Path $transaction.Directory 'acquired.json')) { throw 'Recovered terminal cannot coexist with launch acquisition.' }
    }
    else {
        foreach ($name in @('visual-studio-reload-intent.json','visual-studio-consumed.json','armed.json','pre-launch-intent.json','pre-launch-refusal-observed.json','child-acquisition.json','acquired.json')) {
            if (Test-Path -LiteralPath (Join-Path $transaction.Directory $name)) { throw 'Prepare-failure terminal cannot coexist with a later transaction state.' }
        }
    }
}

function Get-CurrentState([string]$Path, [string]$OriginalHash, [string]$IntendedHash, [string]$Label) {
    $hash = Get-Sha256 (Read-PathBytes $Path)
    if ($hash -ceq $OriginalHash) { return 'ORIGINAL' }
    if ($hash -ceq $IntendedHash) { return 'INTENDED' }
    throw "$Label contains neither byte-exact original nor intended transaction bytes."
}

function Clear-AtomicResidues([object]$Transaction) {
    foreach ($entry in @(
        @('launch.swap.new', [string]$Transaction.Metadata.OriginalSha256, [string]$Transaction.Metadata.IntendedSha256),
        @('launch.swap.previous', [string]$Transaction.Metadata.OriginalSha256, [string]$Transaction.Metadata.IntendedSha256),
        @('user.swap.new', [string]$Transaction.Metadata.ProjectUserOriginalSha256, [string]$Transaction.Metadata.ProjectUserIntendedSha256),
        @('user.swap.previous', [string]$Transaction.Metadata.ProjectUserOriginalSha256, [string]$Transaction.Metadata.ProjectUserIntendedSha256)
    )) {
        $path = Join-Path $Transaction.Directory $entry[0]
        if (-not (Test-Path -LiteralPath $path)) { continue }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Atomic residue $($entry[0]) is not a file." }
        Assert-PhysicalFile $path "Atomic residue $($entry[0])"
        $hash = Get-FileSha256 $path
        if ($hash -cne $entry[1] -and $hash -cne $entry[2]) { throw "Atomic residue $($entry[0]) has unknown bytes." }
        [IO.File]::Delete($path)
    }
}

function Restore-TransactionBytes([object]$Transaction, [string]$UserHook, [string]$LaunchHook) {
    $metadata = $Transaction.Metadata
    $launchOriginal = [IO.File]::ReadAllBytes((Join-Path $Transaction.Directory 'launchSettings.original.bin'))
    $userOriginal = [IO.File]::ReadAllBytes((Join-Path $Transaction.Directory 'project.user.original.bin'))
    $launchState = Get-CurrentState $launchSettings ([string]$metadata.OriginalSha256) ([string]$metadata.IntendedSha256) 'launchSettings.json'
    $userState = Get-CurrentState $projectUserSettings ([string]$metadata.ProjectUserOriginalSha256) ([string]$metadata.ProjectUserIntendedSha256) 'WorldGen.VintageStory.csproj.user'
    Clear-AtomicResidues $Transaction
    try {
        if ($userState -eq 'INTENDED') { Write-AtomicBytes $projectUserSettings $userOriginal ([string]$metadata.ProjectUserIntendedSha256) $Transaction.Directory 'user.swap' $UserHook }
        if ($launchState -eq 'INTENDED') { Write-AtomicBytes $launchSettings $launchOriginal ([string]$metadata.IntendedSha256) $Transaction.Directory 'launch.swap' $LaunchHook }
    }
    catch {
        # Best-effort rollback to the entry state keeps the pair coherent for a
        # normal exception. A hard process stop is recovered from either prefix.
        if ($userState -eq 'INTENDED' -and (Get-FileSha256 $projectUserSettings) -ceq [string]$metadata.ProjectUserOriginalSha256) {
            $userIntended = [IO.File]::ReadAllBytes((Join-Path $Transaction.Directory 'project.user.intended.bin'))
            Write-AtomicBytes $projectUserSettings $userIntended ([string]$metadata.ProjectUserOriginalSha256) $Transaction.Directory 'user.swap' 'RestoreRollbackUserAfterReplace'
        }
        if ($launchState -eq 'INTENDED' -and (Get-FileSha256 $launchSettings) -ceq [string]$metadata.OriginalSha256) {
            $launchIntended = [IO.File]::ReadAllBytes((Join-Path $Transaction.Directory 'launchSettings.intended.bin'))
            Write-AtomicBytes $launchSettings $launchIntended ([string]$metadata.OriginalSha256) $Transaction.Directory 'launch.swap' 'RestoreRollbackLaunchAfterReplace'
        }
        throw
    }
    if ((Get-FileSha256 $launchSettings) -cne [string]$metadata.OriginalSha256 -or (Get-FileSha256 $projectUserSettings) -cne [string]$metadata.ProjectUserOriginalSha256) {
        throw 'Byte-exact settings restoration verification failed.'
    }
    Clear-AtomicResidues $Transaction
}

function Recover-PreIntent([string]$Directory) {
    $transaction = Read-ValidatedEnvelope $Directory
    Assert-NoTerminal $transaction.Directory
    if (Test-Path -LiteralPath (Join-Path $transaction.Directory 'metadata.json')) { throw 'Published metadata requires normal transaction recovery.' }
    $envelope = $transaction.Envelope
    $abandoned = $null -ne $envelope.PSObject.Properties['AbandonedBeforeIntent'] -and [bool]$envelope.AbandonedBeforeIntent
    if (-not $abandoned) {
        $bound = Get-ProcessRecord ([int]$envelope.VisualStudio.ProcessId)
        if ($null -ne $bound -and [bool]$bound.IsRunning -and ([DateTimeOffset]$bound.StartTimeUtc).UtcTicks -eq ([DateTimeOffset]$envelope.VisualStudio.StartTimeUtc).UtcTicks) {
            throw 'Pre-intent transaction cannot be recovered while its bound Visual Studio instance is running.'
        }
    }
    if ((Get-FileSha256 $launchSettings) -cne [string]$envelope.OriginalSha256 -or (Get-FileSha256 $projectUserSettings) -cne [string]$envelope.ProjectUserOriginalSha256) {
        throw 'Pre-intent recovery refuses because settings no longer have their recorded original bytes.'
    }
    $known = @('envelope.json','envelope.recovery','launchSettings.original.bin','launchSettings.intended.bin','project.user.original.bin','project.user.intended.bin','metadata.json','pre-intent-recovered.json')
    foreach ($entry in @(Get-ChildItem -LiteralPath $transaction.Directory -Force)) {
        if ($entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Pre-intent transaction contains a redirected or nested entry.' }
        $baseName = if ($entry.Name.EndsWith('.publishing',[StringComparison]::Ordinal)) { $entry.Name.Substring(0,$entry.Name.Length-11) } else { $entry.Name }
        if ($known -notcontains $baseName) { throw "Pre-intent transaction contains an unexpected entry: $($entry.Name)" }
    }
    foreach ($pair in @(
        @('launchSettings.original.bin','OriginalSha256'), @('launchSettings.intended.bin','IntendedSha256'),
        @('project.user.original.bin','ProjectUserOriginalSha256'), @('project.user.intended.bin','ProjectUserIntendedSha256')
    )) {
        $path = Join-Path $transaction.Directory $pair[0]
        if (Test-Path -LiteralPath $path) {
            Assert-PhysicalFile $path "Pre-intent artifact $($pair[0])"
            if ((Get-FileSha256 $path) -cne [string]$envelope.($pair[1])) { throw "Pre-intent artifact hash drifted for $($pair[0])." }
        }
        Clear-PublishingResidue $path
    }
    Clear-PublishingResidue (Join-Path $transaction.Directory 'envelope.json')
    Clear-PublishingResidue (Join-Path $transaction.Directory 'envelope.recovery')
    Clear-PublishingResidue (Join-Path $transaction.Directory 'metadata.json')
    $terminalPath = Join-Path $transaction.Directory 'pre-intent-recovered.json'
    Clear-PublishingResidue $terminalPath
    Write-NewJson $terminalPath ([ordered]@{ SchemaVersion=2; Protocol=$Protocol; Status='PRE_INTENT_RECOVERED'; TransactionId=[string]$envelope.TransactionId; EnvelopeSha256=Get-FileSha256 $transaction.EnvelopePath; OriginalSha256=[string]$envelope.OriginalSha256; ProjectUserOriginalSha256=[string]$envelope.ProjectUserOriginalSha256; RecoveredUtc=[DateTimeOffset]::UtcNow.ToString('o') })
    [ordered]@{ Status='PRE_INTENT_RECOVERED'; TransactionId=[string]$envelope.TransactionId; BackupDirectory=$transaction.Directory } | ConvertTo-Json -Compress
}

function Complete-AbandonedEnvelope([string]$Directory) {
    $backup = Get-CanonicalPath $Directory
    Assert-ChildPath $backup $transactionsRoot 'Transaction directory'
    Assert-PhysicalDirectory $backup 'Transaction directory'
    $transactionId = [IO.Path]::GetFileName($backup)
    if ($transactionId -notmatch '^f5-[0-9a-f]{32}$') { throw 'Unpublished envelope directory id is malformed.' }
    $entries = @(Get-ChildItem -LiteralPath $backup -Force)
    if ($entries.Count -gt 2) {
        throw 'Unpublished envelope recovery refuses unexpected transaction entries.'
    }
    foreach ($entry in $entries) {
        if (($entry.Name -cne 'envelope.json.publishing' -and $entry.Name -cne 'envelope.recovery.publishing') -or $entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Unpublished envelope recovery refuses unexpected transaction entries.'
        }
    }
    # No configuration write is reachable before envelope publication. A torn
    # first publication can therefore be abandoned against the current bytes.
    $now = [DateTimeOffset]::UtcNow.ToString('o')
    $launchHash = Get-FileSha256 $launchSettings
    $userHash = Get-FileSha256 $projectUserSettings
    $recoveryPublishing = Join-Path $backup 'envelope.recovery.publishing'
    if (Test-Path -LiteralPath $recoveryPublishing) { [IO.File]::Delete($recoveryPublishing) }
    $recoveredEnvelope = [ordered]@{
        SchemaVersion=2; Protocol=$Protocol; Status='DIRECTORY_RESERVED'; TransactionId=$transactionId.Substring(3); Nonce=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        Owner=(Get-Acl -LiteralPath $backup).Owner; TransactionDirectory=$backup; LaboratoryRoot=$laboratory; SolutionPath=$solution
        LaunchSettingsPath=$launchSettings; ProjectPath=$project; ProjectUserSettingsPath=$projectUserSettings
        VisualStudio=[ordered]@{ ProcessId=0; StartTimeUtc='1970-01-01T00:00:00.0000000+00:00'; Name='UNBOUND'; ExecutablePath='' }
        OriginalSha256=$launchHash; IntendedSha256=$launchHash; ProjectUserOriginalSha256=$userHash; ProjectUserIntendedSha256=$userHash; ReservedUtc=$now; AbandonedBeforeIntent=$true
    }
    Write-DurableNewBytes $recoveryPublishing ([Text.Encoding]::UTF8.GetBytes(($recoveredEnvelope | ConvertTo-Json -Depth 12)))
    Invoke-TestHook 'RecoverAbandonedEnvelopeAfterFlush'
    [IO.File]::Move($recoveryPublishing, (Join-Path $backup 'envelope.json'))
}

if ($SyntheticFixtureRoot) {
    $repository = Get-CanonicalPath $SyntheticFixtureRoot
    Assert-ChildPath $repository ([IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)) 'Synthetic fixture root'
}
else { $repository = Get-CanonicalPath (Join-Path $PSScriptRoot '..\..\..') }

$solution = Get-CanonicalPath (Join-Path $repository 'ISRWorldGen.sln')
$project = Get-CanonicalPath (Join-Path $repository 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj')
$launchSettings = Get-CanonicalPath (Join-Path $repository 'src\WorldGen.VintageStory\Properties\launchSettings.json')
$projectUserSettings = Get-CanonicalPath (Join-Path $repository 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj.user')
$laboratory = Get-CanonicalPath (Join-Path $repository '.local\L00C')
$transactionsRoot = Join-Path $laboratory 'f5-profile-transactions'
$save = if ($SyntheticFixtureRoot) { Join-Path $repository 'AppData\VintagestoryData\Saves\ISRWorldGen-L00C-Client.vcdbs' } else { Join-Path $env:APPDATA 'VintagestoryData\Saves\ISRWorldGen-L00C-Client.vcdbs' }
$actionLock = $null
try {
    Assert-PhysicalFile $solution 'Solution'
    Assert-PhysicalFile $project 'WorldGen.VintageStory.csproj'
    Assert-PhysicalFile $launchSettings 'launchSettings.json'
    Assert-PhysicalFile $projectUserSettings 'WorldGen.VintageStory.csproj.user'
    Assert-PhysicalDirectory $laboratory 'L00-C laboratory'
    $actionLockPath = Join-Path $laboratory 'f5-profile-transaction.lock'
    if (Test-Path -LiteralPath $actionLockPath) { Assert-PhysicalFile $actionLockPath 'F5 transaction action lock' }
    try { $actionLock = [IO.File]::Open($actionLockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch [IO.IOException] { throw 'Another F5 transaction action owns the exclusive action lock.' }
    Assert-PhysicalFile $actionLockPath 'F5 transaction action lock'
    if ($Action -eq 'Prepare') {
        if ([string]::IsNullOrWhiteSpace($ExpectedSolutionPath) -or (Get-CanonicalPath $ExpectedSolutionPath) -cne $solution) { throw 'Expected solution must be this worktree ISRWorldGen.sln.' }
        if ([string]::IsNullOrWhiteSpace($ExpectedGameExecutablePath)) { throw 'Prepare requires the exact expected Vintagestory.exe path.' }
        $gameExecutable = Get-CanonicalPath $ExpectedGameExecutablePath
        if ([IO.Path]::GetFileName($gameExecutable) -cne 'Vintagestory.exe') { throw 'Expected game executable is not Vintagestory.exe.' }
        Assert-PhysicalFile $gameExecutable 'Expected game executable'
        $visualStudio = Assert-VisualStudio $VisualStudioProcessId
        foreach ($required in @($laboratory, (Join-Path $laboratory '.isrworldgen-lab'))) {
            if (-not (Test-Path -LiteralPath $required)) { throw "Required L00-C laboratory input is missing: $required" }
        }
        if (-not (Test-Path -LiteralPath $transactionsRoot)) { [void](New-Item -ItemType Directory -Path $transactionsRoot) }
        Assert-PhysicalDirectory $transactionsRoot 'F5 transactions root'
        foreach ($entry in @(Get-ChildItem -LiteralPath $transactionsRoot -Force)) {
            if (-not $entry.PSIsContainer) { throw 'F5 transactions root contains an unexpected file.' }
        }
        foreach ($existing in @(Get-ChildItem -LiteralPath $transactionsRoot -Directory -Force)) {
            if (($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'F5 transactions root contains a reparse-point transaction.' }
            $terminal = @(Get-TerminalPaths $existing.FullName).Count -ne 0
            if (-not $terminal) { throw "Unresolved F5 transaction must be recovered before Prepare: $($existing.FullName)" }
            Assert-TerminalTransaction $existing.FullName
        }

        $original = Read-PathBytes $launchSettings
        $originalUser = Read-PathBytes $projectUserSettings
        try { $document = Get-Utf8Text $original | ConvertFrom-Json }
        catch { throw 'launchSettings.json is not valid JSON.' }
        $profiles = @($document.profiles.PSObject.Properties)
        if ($profiles.Count -lt 1) { throw 'launchSettings.json contains no profile.' }
        $first = $profiles[0]
        if ($first.Name -cne $ExpectedProfileName -or [string]$first.Value.commandName -cne 'Executable' -or [string]$first.Value.executablePath -cne $ExpectedExecutableTemplate -or
            [string]$first.Value.commandLineArgs -cne $OriginalArguments -or [regex]::Matches([string]$first.Value.commandLineArgs, '(?i)(?:^|\s)--openWorld(?:\s|=|$)').Count -ne 0 -or
            [string]$first.Value.commandLineArgs -match '(?i)--dataPath(?:\s|=|$)' -or (Test-SensitiveProfile $first.Value)) {
            throw 'First F5 profile is not the exact non-sensitive authenticated Debug client profile.'
        }

        $attestation = Get-BootstrapSaveAttestation $save
        $transactionId = [Guid]::NewGuid().ToString('N')
        $nonce = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $transaction = Join-Path $transactionsRoot ('f5-' + $transactionId)
        [void](New-Item -ItemType Directory -Path $transaction)
        $transaction = Get-CanonicalPath $transaction
        $preparedUtc = [DateTimeOffset]::UtcNow
        $first.Value.commandLineArgs = '--openWorld "' + $BootstrapWorldName + '" ' + $OriginalArguments
        if ($null -eq $first.Value.PSObject.Properties['environmentVariables']) { $first.Value | Add-Member -NotePropertyName environmentVariables -NotePropertyValue ([pscustomobject]@{}) }
        $environment = $first.Value.environmentVariables
        $environment | Add-Member -NotePropertyName ISR_L00C_LAB -NotePropertyValue '1' -Force
        $environment | Add-Member -NotePropertyName ISR_L00C_AUTOSHUTDOWN -NotePropertyValue '1' -Force
        $environment | Add-Member -NotePropertyName ISR_L00C_AUTOSHUTDOWN_DELAY_MS -NotePropertyValue '15000' -Force
        $environment | Add-Member -NotePropertyName ISR_L00C_LAB_ROOT -NotePropertyValue $laboratory -Force
        $environment | Add-Member -NotePropertyName ISR_L00C_F5_TRANSACTION_ID -NotePropertyValue $transactionId -Force
        $environment | Add-Member -NotePropertyName ISR_L00C_F5_LAUNCH_NONCE -NotePropertyValue $nonce -Force
        $environment | Add-Member -NotePropertyName ISR_L00C_F5_TRANSACTION_DIRECTORY -NotePropertyValue $transaction -Force
        $environment | Add-Member -NotePropertyName ISR_L00C_F5_VISUAL_STUDIO_PID -NotePropertyValue ([string]$VisualStudioProcessId) -Force
        $environment | Add-Member -NotePropertyName ISR_L00C_F5_SOLUTION_PATH -NotePropertyValue $solution -Force
        $intended = [Text.Encoding]::UTF8.GetBytes(($document | ConvertTo-Json -Depth 16))
        $intendedUser = Get-PreparedUserSettings $originalUser $ExpectedProfileName
        $expectedArguments = @('--openWorld', $BootstrapWorldName, '--tracelog', '--addModPath', (Join-Path $repository 'src\WorldGen.VintageStory\bin\Debug\Mods'), '--addOrigin', (Join-Path $repository 'src\WorldGen.VintageStory\assets'))
        $metadata = [ordered]@{
            SchemaVersion = 2; Protocol = $Protocol; State = 'PREPARE_INTENT'; TransactionId = $transactionId; Nonce = $nonce
            Owner = (Get-Acl -LiteralPath $transaction).Owner; TransactionDirectory = $transaction; LaboratoryRoot = $laboratory
            SolutionPath = $solution; ProjectPath=$project; LaunchSettingsPath = $launchSettings; ProjectUserSettingsPath = $projectUserSettings
            ProfileName = $ExpectedProfileName; ExpectedGameExecutablePath = $gameExecutable; ExpectedArguments = $expectedArguments
            VisualStudio = [ordered]@{ ProcessId = $VisualStudioProcessId; StartTimeUtc = ([DateTimeOffset]$visualStudio.StartTimeUtc).ToString('o'); Name = [string]$visualStudio.Name; ExecutablePath = [string]$visualStudio.ExecutablePath }
            BootstrapSave = $attestation; OriginalSha256 = Get-Sha256 $original; IntendedSha256 = Get-Sha256 $intended
            ProjectUserOriginalSha256 = Get-Sha256 $originalUser; ProjectUserIntendedSha256 = Get-Sha256 $intendedUser; PreparedUtc = $preparedUtc.ToString('o')
        }
        $envelope = [ordered]@{
            SchemaVersion=2; Protocol=$Protocol; Status='DIRECTORY_RESERVED'; TransactionId=$transactionId; Nonce=$nonce
            Owner=(Get-Acl -LiteralPath $transaction).Owner; TransactionDirectory=$transaction; LaboratoryRoot=$laboratory; SolutionPath=$solution
            LaunchSettingsPath=$launchSettings; ProjectPath=$project; ProjectUserSettingsPath=$projectUserSettings
            VisualStudio=$metadata.VisualStudio; OriginalSha256=$metadata.OriginalSha256; IntendedSha256=$metadata.IntendedSha256
            ProjectUserOriginalSha256=$metadata.ProjectUserOriginalSha256; ProjectUserIntendedSha256=$metadata.ProjectUserIntendedSha256; ReservedUtc=$preparedUtc.ToString('o')
        }
        Write-NewJson (Join-Path $transaction 'envelope.json') $envelope
        Write-NewBytes (Join-Path $transaction 'launchSettings.original.bin') $original
        Write-NewBytes (Join-Path $transaction 'launchSettings.intended.bin') $intended
        Write-NewBytes (Join-Path $transaction 'project.user.original.bin') $originalUser
        Write-NewBytes (Join-Path $transaction 'project.user.intended.bin') $intendedUser
        Write-NewJson (Join-Path $transaction 'metadata.json') $metadata
        try {
            Invoke-TestHook 'PrepareBeforeFirstSettingsWrite'
            Assert-BootstrapSave $attestation
            if ((Get-FileSha256 $launchSettings) -cne $metadata.OriginalSha256 -or (Get-FileSha256 $projectUserSettings) -cne $metadata.ProjectUserOriginalSha256) { throw 'Settings changed before the F5 prepare commit.' }
            $prepareTransaction = [pscustomobject]@{ Directory = $transaction; Metadata = [pscustomobject]$metadata }
            Clear-AtomicResidues $prepareTransaction
            Write-AtomicBytes $launchSettings $intended ([string]$metadata.OriginalSha256) $transaction 'launch.swap' 'PrepareLaunchBeforeReplace'
            Invoke-TestHook 'PrepareBetweenSettingsWrites'
            Write-AtomicBytes $projectUserSettings $intendedUser ([string]$metadata.ProjectUserOriginalSha256) $transaction 'user.swap' 'PrepareUserBeforeReplace'
            if ((Get-FileSha256 $launchSettings) -cne $metadata.IntendedSha256 -or (Get-FileSha256 $projectUserSettings) -cne $metadata.ProjectUserIntendedSha256) { throw 'Durable intended settings verification failed.' }
            $prepared = [ordered]@{ SchemaVersion = 2; Protocol = $Protocol; Status = 'SETTINGS_PREPARED_FOR_VS'; TransactionId = $transactionId; MetadataSha256 = Get-FileSha256 (Join-Path $transaction 'metadata.json'); LaunchSettingsSha256 = $metadata.IntendedSha256; ProjectUserSettingsSha256 = $metadata.ProjectUserIntendedSha256; PreparedUtc = [DateTimeOffset]::UtcNow.ToString('o') }
            Write-NewJson (Join-Path $transaction 'settings-prepared.json') $prepared
            Invoke-TestHook 'PrepareAfterPreparedReceipt'
            [ordered]@{ Status = 'SETTINGS_PREPARED_FOR_VS'; TransactionId = $transactionId; BackupDirectory = $transaction; VisualStudioProcessId = $VisualStudioProcessId; VisualStudioAttestationPath=(Join-Path $transaction 'visual-studio-consumed.json'); LaunchSettingsSha256 = $metadata.IntendedSha256; ProjectUserSettingsSha256 = $metadata.ProjectUserIntendedSha256 } | ConvertTo-Json -Compress
            return
        }
        catch {
            try {
                $synthetic = [pscustomobject]@{ Directory = $transaction; Metadata = [pscustomobject]$metadata }
                Restore-TransactionBytes $synthetic 'PrepareFailureUserAfterTruncate' 'PrepareFailureLaunchAfterTruncate'
                Clear-PublishingResidue (Join-Path $transaction 'settings-prepared.json')
                Write-NewJson (Join-Path $transaction 'prepare-failed-recovered.json') ([ordered]@{ SchemaVersion = 2; Protocol = $Protocol; Status = 'PREPARE_FAILED_RECOVERED'; TransactionId = $transactionId; MetadataSha256 = Get-FileSha256 (Join-Path $transaction 'metadata.json'); OriginalSha256 = [string]$metadata.OriginalSha256; ProjectUserOriginalSha256 = [string]$metadata.ProjectUserOriginalSha256; RecoveredUtc = [DateTimeOffset]::UtcNow.ToString('o') })
            }
            catch { throw 'Prepare failed and byte-exact in-action recovery could not be completed; use Recover after stopping the bound Visual Studio instance.' }
            throw
        }
    }

    if ($Action -eq 'Recover' -and -not [string]::IsNullOrWhiteSpace($BackupDirectory)) {
        $candidateDirectory = [IO.Path]::GetFullPath($BackupDirectory)
        $candidateMetadata = Join-Path $candidateDirectory 'metadata.json'
        if (-not (Test-Path -LiteralPath $candidateMetadata -PathType Leaf)) {
            if (-not (Test-Path -LiteralPath (Join-Path $candidateDirectory 'envelope.json') -PathType Leaf)) { Complete-AbandonedEnvelope $candidateDirectory }
            Recover-PreIntent $BackupDirectory
            return
        }
    }

    $currentTransaction = Read-ValidatedTransaction $BackupDirectory
    $metadata = $currentTransaction.Metadata
    Assert-NoTerminal $currentTransaction.Directory

    if ($Action -eq 'Arm') {
        $preparedPath = Join-Path $currentTransaction.Directory 'settings-prepared.json'
        $prepared = Read-RequiredJson $preparedPath 'SETTINGS_PREPARED_FOR_VS receipt'
        if ([int]$prepared.SchemaVersion -ne 2 -or [string]$prepared.Protocol -cne $Protocol -or [string]$prepared.Status -cne 'SETTINGS_PREPARED_FOR_VS' -or
            [string]$prepared.TransactionId -cne [string]$metadata.TransactionId -or [string]$prepared.MetadataSha256 -cne (Get-FileSha256 $currentTransaction.MetadataPath) -or
            [string]$prepared.LaunchSettingsSha256 -cne [string]$metadata.IntendedSha256 -or [string]$prepared.ProjectUserSettingsSha256 -cne [string]$metadata.ProjectUserIntendedSha256) {
            throw 'SETTINGS_PREPARED_FOR_VS receipt is malformed or detached.'
        }
        if ((Test-Path -LiteralPath (Join-Path $currentTransaction.Directory 'armed.json')) -or (Test-Path -LiteralPath (Join-Path $currentTransaction.Directory 'pre-launch-intent.json')) -or (Test-Path -LiteralPath (Join-Path $currentTransaction.Directory 'child-acquisition.json')) -or (Test-Path -LiteralPath (Join-Path $currentTransaction.Directory 'acquired.json'))) {
            throw 'Arm refuses a transaction that is already armed or has launch evidence.'
        }
        if ((Get-CurrentState $launchSettings ([string]$metadata.OriginalSha256) ([string]$metadata.IntendedSha256) 'launchSettings.json') -ne 'INTENDED' -or
            (Get-CurrentState $projectUserSettings ([string]$metadata.ProjectUserOriginalSha256) ([string]$metadata.ProjectUserIntendedSha256) 'WorldGen.VintageStory.csproj.user') -ne 'INTENDED') {
            throw 'Visual Studio cannot be armed without both exact intended settings.'
        }
        [void](Assert-VisualStudio ([int]$metadata.VisualStudio.ProcessId) ([string]$metadata.VisualStudio.StartTimeUtc))
        Assert-BootstrapSave $metadata.BootstrapSave
        $vsAttestation = Assert-VisualStudioAttestation $currentTransaction $VisualStudioAttestationPath
        if ((Get-FileSha256 $launchSettings) -cne [string]$metadata.IntendedSha256 -or (Get-FileSha256 $projectUserSettings) -cne [string]$metadata.ProjectUserIntendedSha256) {
            throw 'Settings changed after Visual Studio attestation and before arm publication.'
        }
        Invoke-TestHook 'ArmBeforePublish'
        $armedPath = Join-Path $currentTransaction.Directory 'armed.json'
        Clear-PublishingResidue $armedPath
        $armed = [ordered]@{ SchemaVersion=2; Protocol=$Protocol; Status='ARMED_FOR_F5'; TransactionId=[string]$metadata.TransactionId; MetadataSha256=Get-FileSha256 $currentTransaction.MetadataPath; VisualStudioAttestationSha256=Get-FileSha256 $VisualStudioAttestationPath; LaunchSettingsSha256=[string]$metadata.IntendedSha256; ProjectUserSettingsSha256=[string]$metadata.ProjectUserIntendedSha256; ArmedUtc=[DateTimeOffset]::UtcNow.ToString('o') }
        Write-NewJson $armedPath $armed
        [ordered]@{ Status='ARMED_FOR_F5'; TransactionId=[string]$metadata.TransactionId; BackupDirectory=$currentTransaction.Directory; VisualStudioProcessId=[int]$metadata.VisualStudio.ProcessId } | ConvertTo-Json -Compress
        return
    }

    if ($Action -eq 'BeginLaunch') {
        [void](Assert-Armed $currentTransaction)
        foreach ($name in @('pre-launch-intent.json','child-acquisition.json','acquired.json')) {
            if (Test-Path -LiteralPath (Join-Path $currentTransaction.Directory $name)) { throw 'BeginLaunch refuses replay or existing launch evidence.' }
        }
        if ((Get-CurrentState $launchSettings ([string]$metadata.OriginalSha256) ([string]$metadata.IntendedSha256) 'launchSettings.json') -ne 'INTENDED' -or
            (Get-CurrentState $projectUserSettings ([string]$metadata.ProjectUserOriginalSha256) ([string]$metadata.ProjectUserIntendedSha256) 'WorldGen.VintageStory.csproj.user') -ne 'INTENDED') {
            throw 'BeginLaunch requires both exact intended settings.'
        }
        $status = Get-BoundProcessStatus $currentTransaction
        if ([string]$status.VisualStudioStatus -cne 'RUNNING' -or [string]$status.McpStatus -cne 'RUNNING') { throw 'BeginLaunch requires the attested Visual Studio and MCP processes to be running.' }
        $descendants = Get-LaunchDescendantSummary $currentTransaction
        if ([int]$descendants.Count -ne 0) { throw 'BeginLaunch detected an existing Vintage Story descendant of the bound Visual Studio process.' }
        $intentPath = Join-Path $currentTransaction.Directory 'pre-launch-intent.json'
        Clear-PublishingResidue $intentPath
        Invoke-TestHook 'BeginLaunchBeforePublish'
        $intentReceipt = [ordered]@{
            SchemaVersion=2; Protocol=$Protocol; Status='PRE_LAUNCH_INTENT'; TransactionId=[string]$metadata.TransactionId
            MetadataSha256=Get-FileSha256 $currentTransaction.MetadataPath; ArmedReceiptSha256=Get-FileSha256 (Join-Path $currentTransaction.Directory 'armed.json')
            VisualStudioAttestationSha256=Get-FileSha256 (Join-Path $currentTransaction.Directory 'visual-studio-consumed.json')
            Tool='mcp__visualstudio__debugger_launch'; DebuggerStatus='Design'; VisualStudioProcessId=[int]$status.VisualStudioProcessId
            VisualStudioStatus=[string]$status.VisualStudioStatus; McpProcessId=[int]$status.McpProcessId; McpStatus=[string]$status.McpStatus
            LaunchSettingsPreSha256=[string]$metadata.IntendedSha256; ProjectUserSettingsPreSha256=[string]$metadata.ProjectUserIntendedSha256
            DescendantCountPre=[int]$descendants.Count; DescendantFingerprintPre=[string]$descendants.Fingerprint
            NoChildProcess=$true; NoCampaignStarted=$true; IntentUtc=[DateTimeOffset]::UtcNow.ToString('o')
        }
        Write-NewJson $intentPath (Add-ReceiptSeal $intentReceipt)
        [ordered]@{ Status='PRE_LAUNCH_INTENT'; RuntimeStatus='NOT_RUN'; TransactionId=[string]$metadata.TransactionId; BackupDirectory=$currentTransaction.Directory; Tool='mcp__visualstudio__debugger_launch' } | ConvertTo-Json -Compress
        return
    }

    if ($Action -eq 'BlockLaunch') {
        $intent = Assert-LaunchIntent $currentTransaction
        foreach ($name in @('child-acquisition.json','child-acquisition.json.publishing','acquired.json','acquired.json.publishing')) {
            if (Test-Path -LiteralPath (Join-Path $currentTransaction.Directory $name)) { throw 'BlockLaunch refuses because child or acquisition evidence exists.' }
        }
        $launchState = Get-CurrentState $launchSettings ([string]$metadata.OriginalSha256) ([string]$metadata.IntendedSha256) 'launchSettings.json'
        $userState = Get-CurrentState $projectUserSettings ([string]$metadata.ProjectUserOriginalSha256) ([string]$metadata.ProjectUserIntendedSha256) 'WorldGen.VintageStory.csproj.user'
        $blockedPath = Join-Path $currentTransaction.Directory 'pre-launch-blocked.json'
        $blockedPublishingPath = $blockedPath + '.publishing'
        $observationPath = Join-Path $currentTransaction.Directory 'pre-launch-refusal-observed.json'
        $observationPublishingPath = $observationPath + '.publishing'
        if (Test-Path -LiteralPath $observationPath -PathType Leaf) {
            $observation = Assert-RefusalObservation $currentTransaction
        }
        elseif (Test-Path -LiteralPath $observationPublishingPath -PathType Leaf) {
            Assert-PhysicalFile $observationPublishingPath 'PRE_LAUNCH_REFUSAL_OBSERVED publication residue'
            [void](Assert-RefusalObservation $currentTransaction $observationPublishingPath)
            [IO.File]::Move($observationPublishingPath, $observationPath)
            $observation = Assert-RefusalObservation $currentTransaction
        }
        else {
            if ($launchState -cne 'INTENDED' -or $userState -cne 'INTENDED') { throw 'BlockLaunch refuses restored or split settings without a durable refusal observation.' }
            $statusAfterRefusal = Get-BoundProcessStatus $currentTransaction
            $beforeRestore = Get-LaunchDescendantSummary $currentTransaction
            if ([int]$beforeRestore.Count -ne 0) { throw 'BlockLaunch detected a launched Vintage Story descendant.' }
            $launchPostRefusal = Get-FileSha256 $launchSettings
            $userPostRefusal = Get-FileSha256 $projectUserSettings
            $observationReceipt = [ordered]@{
                SchemaVersion=2; Protocol=$Protocol; Status='PRE_LAUNCH_REFUSAL_OBSERVED'; RuntimeStatus='NOT_RUN'; RefusalCategory='MCP_TOOL_REFUSED_BEFORE_ACTION'
                Tool='mcp__visualstudio__debugger_launch'; TransactionId=[string]$metadata.TransactionId; LaunchIntentSha256=Get-FileSha256 (Join-Path $currentTransaction.Directory 'pre-launch-intent.json')
                VisualStudioProcessId=[int]$intent.VisualStudioProcessId; VisualStudioStatus=[string]$statusAfterRefusal.VisualStudioStatus
                McpProcessId=[int]$intent.McpProcessId; McpStatus=[string]$statusAfterRefusal.McpStatus; DebuggerStatus='NOT_RUN'
                LaunchSettingsPostRefusalSha256=$launchPostRefusal; ProjectUserSettingsPostRefusalSha256=$userPostRefusal
                DescendantCount=[int]$beforeRestore.Count; DescendantFingerprint=[string]$beforeRestore.Fingerprint
                NoChildProcess=$true; NoCampaignStarted=$true; ObservedUtc=[DateTimeOffset]::UtcNow.ToString('o')
            }
            Invoke-TestHook 'BlockLaunchBeforeObservationPublish'
            Write-NewJson $observationPath (Add-ReceiptSeal $observationReceipt)
            $observation = Assert-RefusalObservation $currentTransaction
        }
        if (Test-Path -LiteralPath $blockedPublishingPath -PathType Leaf) {
            Assert-PhysicalFile $blockedPublishingPath 'PRE_LAUNCH_BLOCKED publication residue'
            [void](Assert-BlockedReceipt $currentTransaction (Read-RequiredJson $blockedPublishingPath 'Flushed PRE_LAUNCH_BLOCKED receipt'))
        }
        else {
            if ($launchState -cne 'INTENDED' -or $userState -cne 'INTENDED') { throw 'BlockLaunch refuses restored or split settings without the previously flushed refusal terminal.' }
            $blockedReceipt = [ordered]@{
                SchemaVersion=2; Protocol=$Protocol; Status='BLOCKED'; RuntimeStatus='NOT_RUN'; RefusalCategory='MCP_TOOL_REFUSED_BEFORE_ACTION'
                Tool='mcp__visualstudio__debugger_launch'; TransactionId=[string]$metadata.TransactionId; MetadataSha256=Get-FileSha256 $currentTransaction.MetadataPath
                ArmedReceiptSha256=Get-FileSha256 (Join-Path $currentTransaction.Directory 'armed.json'); VisualStudioAttestationSha256=Get-FileSha256 (Join-Path $currentTransaction.Directory 'visual-studio-consumed.json')
                LaunchIntentSha256=Get-FileSha256 (Join-Path $currentTransaction.Directory 'pre-launch-intent.json'); RefusalObservationSha256=Get-FileSha256 $observationPath
                DebuggerStatusBefore='Design'; DebuggerStatusAfter='NOT_RUN'
                VisualStudioProcessId=[int]$intent.VisualStudioProcessId; VisualStudioStatusBefore=[string]$intent.VisualStudioStatus; VisualStudioStatusAfter=[string]$observation.VisualStudioStatus
                McpProcessId=[int]$intent.McpProcessId; McpStatusBefore=[string]$intent.McpStatus; McpStatusAfter=[string]$observation.McpStatus
                LaunchSettingsPreSha256=[string]$intent.LaunchSettingsPreSha256; LaunchSettingsPostRefusalSha256=[string]$observation.LaunchSettingsPostRefusalSha256
                ProjectUserSettingsPreSha256=[string]$intent.ProjectUserSettingsPreSha256; ProjectUserSettingsPostRefusalSha256=[string]$observation.ProjectUserSettingsPostRefusalSha256
                RestoredLaunchSettingsSha256=[string]$metadata.OriginalSha256; RestoredProjectUserSettingsSha256=[string]$metadata.ProjectUserOriginalSha256
                DescendantCountBefore=[int]$observation.DescendantCount; DescendantFingerprintBefore=[string]$observation.DescendantFingerprint
                DescendantCountAfter=0; DescendantFingerprintAfter=[string]$intent.DescendantFingerprintPre
                NoChildProcess=$true; NoCampaignStarted=$true; BlockedUtc=[DateTimeOffset]::UtcNow.ToString('o')
            }
            Write-NewPublishingJson $blockedPath (Add-ReceiptSeal $blockedReceipt)
            [void](Assert-BlockedReceipt $currentTransaction (Read-RequiredJson $blockedPublishingPath 'Flushed PRE_LAUNCH_BLOCKED receipt'))
        }
        Invoke-TestHook 'BlockLaunchBeforeRestore'
        Restore-TransactionBytes $currentTransaction 'BlockLaunchUserAfterTruncate' 'BlockLaunchSettingsAfterTruncate'
        $afterRestore = Get-LaunchDescendantSummary $currentTransaction
        if ([int]$afterRestore.Count -ne 0 -or [string]$afterRestore.Fingerprint -cne [string]$intent.DescendantFingerprintPre) { throw 'BlockLaunch detected a Vintage Story descendant during restoration.' }
        Invoke-TestHook 'BlockLaunchAfterRestore'
        [void](Assert-BlockedReceipt $currentTransaction (Read-RequiredJson $blockedPublishingPath 'Flushed PRE_LAUNCH_BLOCKED receipt'))
        [IO.File]::Move($blockedPublishingPath, $blockedPath)
        [ordered]@{ Status='BLOCKED'; RuntimeStatus='NOT_RUN'; RefusalCategory='MCP_TOOL_REFUSED_BEFORE_ACTION'; TransactionId=[string]$metadata.TransactionId; BackupDirectory=$currentTransaction.Directory; NoChildProcess=$true; NoCampaignStarted=$true } | ConvertTo-Json -Compress
        return
    }

    if ($Action -eq 'Acquire') {
        $armed = Assert-Armed $currentTransaction
        [void](Assert-LaunchIntent $currentTransaction)
        if ((Test-Path -LiteralPath (Join-Path $currentTransaction.Directory 'pre-launch-refusal-observed.json')) -or (Test-Path -LiteralPath (Join-Path $currentTransaction.Directory 'pre-launch-refusal-observed.json.publishing'))) {
            throw 'Acquire refuses after a pre-launch refusal observation.'
        }
        $launchState = Get-CurrentState $launchSettings ([string]$metadata.OriginalSha256) ([string]$metadata.IntendedSha256) 'launchSettings.json'
        $userState = Get-CurrentState $projectUserSettings ([string]$metadata.ProjectUserOriginalSha256) ([string]$metadata.ProjectUserIntendedSha256) 'WorldGen.VintageStory.csproj.user'
        if ($launchState -ne 'INTENDED' -or $userState -ne 'INTENDED') { throw 'F5 settings are not both intended at launch acquisition.' }
        [void](Assert-VisualStudio ([int]$metadata.VisualStudio.ProcessId) ([string]$metadata.VisualStudio.StartTimeUtc))
        $childPath = Join-Path $currentTransaction.Directory 'child-acquisition.json'
        if (-not (Test-Path -LiteralPath $childPath -PathType Leaf)) { throw 'Child process has not published an acquisition receipt; settings remain armed.' }
        $child = Read-RequiredJson $childPath 'Child acquisition receipt'
        if ([int]$child.SchemaVersion -ne 2 -or [string]$child.Protocol -cne $Protocol -or [string]$child.Status -cne 'CHILD_ACQUIRED' -or
            [string]$child.TransactionId -cne [string]$metadata.TransactionId -or [string]$child.Nonce -cne [string]$metadata.Nonce -or
            [string]$child.TransactionDirectory -cne $currentTransaction.Directory -or [string]$child.LaboratoryRoot -cne $laboratory -or
            [int]$child.VisualStudioProcessId -ne [int]$metadata.VisualStudio.ProcessId -or [string]$child.SolutionPath -cne $solution -or -not [bool]$child.DebuggerAttached) {
            throw 'Child acquisition receipt did not inherit the exact armed transaction.'
        }
        Assert-ArrayEqual @($child.Arguments) @($metadata.ExpectedArguments) 'Child argument vector'
        $childProcess = Get-ProcessRecord ([int]$child.ProcessId)
        if ($null -eq $childProcess -or -not [bool]$childProcess.IsRunning -or [int]$childProcess.ProcessId -ne [int]$child.ProcessId) { throw 'Acquired child process is not running.' }
        if ((Get-CanonicalPath ([string]$child.ExecutablePath)) -cne [string]$metadata.ExpectedGameExecutablePath -or (Get-CanonicalPath ([string]$childProcess.ExecutablePath)) -cne [string]$metadata.ExpectedGameExecutablePath) { throw 'Acquired child executable is not the expected Vintagestory.exe.' }
        if ([DateTimeOffset]$childProcess.StartTimeUtc -ne [DateTimeOffset]$child.ProcessStartUtc -or [DateTimeOffset]$child.ProcessStartUtc -lt [DateTimeOffset]$armed.ArmedUtc -or [DateTimeOffset]$child.RecordedUtc -lt [DateTimeOffset]$child.ProcessStartUtc) { throw 'Child process time does not belong to this armed interval.' }
        Assert-InProcessCommandLineProvenance ([string]$child.CommandLine) $metadata
        Assert-RawProcessCommandLineProvenance ([string]$childProcess.CommandLine) $metadata
        Assert-ChildOfVisualStudio $childProcess $metadata
        if ((Get-FileSha256 $launchSettings) -cne [string]$metadata.IntendedSha256 -or (Get-FileSha256 $projectUserSettings) -cne [string]$metadata.ProjectUserIntendedSha256) { throw 'F5 settings changed before durable launch acquisition.' }
        $acquired = [ordered]@{ SchemaVersion = 2; Protocol = $Protocol; Status = 'LAUNCH_ACQUIRED'; TransactionId = [string]$metadata.TransactionId; MetadataSha256 = Get-FileSha256 $currentTransaction.MetadataPath; ArmedReceiptSha256 = Get-FileSha256 (Join-Path $currentTransaction.Directory 'armed.json'); ChildReceiptSha256 = Get-FileSha256 $childPath; ChildProcessId = [int]$child.ProcessId; ChildProcessStartUtc = [string]$child.ProcessStartUtc; VisualStudioProcessId = [int]$metadata.VisualStudio.ProcessId; AcquiredUtc = [DateTimeOffset]::UtcNow.ToString('o') }
        Invoke-TestHook 'AcquireBeforePublish'
        $acquiredPath = Join-Path $currentTransaction.Directory 'acquired.json'
        Clear-PublishingResidue $acquiredPath
        Write-NewJson $acquiredPath $acquired
        [ordered]@{ Status = 'LAUNCH_ACQUIRED'; TransactionId = [string]$metadata.TransactionId; ChildProcessId = [int]$child.ProcessId; BackupDirectory = $currentTransaction.Directory } | ConvertTo-Json -Compress
        return
    }

    if ($Action -eq 'Restore') {
        [void](Assert-Acquired $currentTransaction)
        foreach ($name in @('visual-studio-reload-intent.json','visual-studio-consumed.json','armed.json','pre-launch-intent.json','pre-launch-refusal-observed.json','child-acquisition.json','acquired.json')) {
            Clear-PublishingResidue (Join-Path $currentTransaction.Directory $name)
        }
        Restore-TransactionBytes $currentTransaction 'RestoreUserAfterTruncate' 'RestoreLaunchAfterTruncate'
        $restoredPath = Join-Path $currentTransaction.Directory 'restored.json'
        Clear-PublishingResidue $restoredPath
        Write-NewJson $restoredPath ([ordered]@{ SchemaVersion = 2; Protocol = $Protocol; Status = 'RESTORED_AFTER_ACQUISITION'; TransactionId = [string]$metadata.TransactionId; MetadataSha256 = Get-FileSha256 $currentTransaction.MetadataPath; AcquiredReceiptSha256 = Get-FileSha256 (Join-Path $currentTransaction.Directory 'acquired.json'); OriginalSha256 = [string]$metadata.OriginalSha256; ProjectUserOriginalSha256 = [string]$metadata.ProjectUserOriginalSha256; RestoredUtc = [DateTimeOffset]::UtcNow.ToString('o') })
        [ordered]@{ Status = 'RESTORED_AFTER_ACQUISITION'; TransactionId = [string]$metadata.TransactionId; BackupDirectory = $currentTransaction.Directory } | ConvertTo-Json -Compress
        return
    }

    if ($Action -eq 'Recover') {
        if (Test-Path -LiteralPath (Join-Path $currentTransaction.Directory 'acquired.json')) { throw 'Acquired transaction must use Restore so its launch proof remains explicit.' }
        if ((Test-Path -LiteralPath (Join-Path $currentTransaction.Directory 'pre-launch-refusal-observed.json')) -or
            (Test-Path -LiteralPath (Join-Path $currentTransaction.Directory 'pre-launch-refusal-observed.json.publishing')) -or
            (Test-Path -LiteralPath (Join-Path $currentTransaction.Directory 'pre-launch-blocked.json.publishing'))) {
            throw 'A durable pre-launch refusal must complete BlockLaunch; Recover cannot replace BLOCKED/NOT_RUN.'
        }
        $boundVisualStudio = Get-ProcessRecord ([int]$metadata.VisualStudio.ProcessId)
        if ($null -ne $boundVisualStudio -and [bool]$boundVisualStudio.IsRunning -and [DateTimeOffset]$boundVisualStudio.StartTimeUtc -eq [DateTimeOffset]$metadata.VisualStudio.StartTimeUtc) {
            throw 'Unacquired transaction cannot be recovered while its bound Visual Studio instance is running.'
        }
        foreach ($name in @('settings-prepared.json','visual-studio-reload-intent.json','visual-studio-consumed.json','armed.json','pre-launch-intent.json','pre-launch-refusal-observed.json','child-acquisition.json','acquired.json','prepare-failed-recovered.json','pre-launch-blocked.json')) {
            Clear-PublishingResidue (Join-Path $currentTransaction.Directory $name)
        }
        Restore-TransactionBytes $currentTransaction 'RecoverUserAfterTruncate' 'RecoverLaunchAfterTruncate'
        $recoveredPath = Join-Path $currentTransaction.Directory 'recovered.json'
        Clear-PublishingResidue $recoveredPath
        Write-NewJson $recoveredPath ([ordered]@{ SchemaVersion = 2; Protocol = $Protocol; Status = 'RECOVERED_WITH_BOUND_VS_STOPPED'; TransactionId = [string]$metadata.TransactionId; MetadataSha256 = Get-FileSha256 $currentTransaction.MetadataPath; OriginalSha256 = [string]$metadata.OriginalSha256; ProjectUserOriginalSha256 = [string]$metadata.ProjectUserOriginalSha256; RecoveredUtc = [DateTimeOffset]::UtcNow.ToString('o') })
        [ordered]@{ Status = 'RECOVERED_WITH_BOUND_VS_STOPPED'; TransactionId = [string]$metadata.TransactionId; BackupDirectory = $currentTransaction.Directory } | ConvertTo-Json -Compress
        return
    }
}
finally { if ($null -ne $actionLock) { $actionLock.Dispose() } }
