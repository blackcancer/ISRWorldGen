[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Inspect', 'ReloadAndAttest', 'ResumeAfterInterruption')][string]$Action,
    [Parameter(Mandatory = $true)][string]$BackupDirectory,
    [int]$VisualStudioMcpProcessId,
    [string]$SyntheticFixtureRoot,
    [scriptblock]$TestProcessQuery,
    [scriptblock]$TestSnapshotQuery,
    [scriptblock]$TestReloadProject,
    [scriptblock]$TestHook
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (($null -ne $TestProcessQuery -or $null -ne $TestSnapshotQuery -or $null -ne $TestReloadProject -or $null -ne $TestHook) -and -not $SyntheticFixtureRoot) {
    throw 'Visual Studio consumption test seams require -SyntheticFixtureRoot.'
}
if ($SyntheticFixtureRoot) {
    $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $synthetic = [IO.Path]::GetFullPath($SyntheticFixtureRoot)
    if (-not $synthetic.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Synthetic fixture root must stay below the temporary directory.' }
}

$Protocol = 'l00c-f5-debug-transaction-v2'
$ExpectedProfile = 'ISRWorldGen Client (authenticated user data)'
$ExpectedConfiguration = 'Debug'
$ExpectedPlatform = 'Any CPU'
$AttestationMethod = 'ROT_DTE_IVS_QUERY_DEBUG_TARGETS'

function Get-CanonicalPath([string]$Path) {
    return [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path)
}

function Assert-PhysicalFile([string]$Path, [string]$Label) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label must be a physical file." }
}

function Assert-PhysicalDirectory([string]$Path, [string]$Label) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label must be a physical directory." }
}

function Assert-DirectChild([string]$Child, [string]$Parent, [string]$Label) {
    $canonicalChild = [IO.Path]::GetFullPath($Child)
    $canonicalParent = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($canonicalChild), $canonicalParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must be a direct child of $canonicalParent."
    }
}

function Get-FileSha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

function Read-RequiredJson([string]$Path, [string]$Label) {
    Assert-PhysicalFile $Path $Label
    try { return [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($Path)) | ConvertFrom-Json }
    catch { throw "$Label is not valid JSON." }
}

function Write-DurableNewJson([string]$Path, [object]$Value) {
    if (Test-Path -LiteralPath $Path) { throw "Authoritative artifact already exists: $Path" }
    $publishing = $Path + '.publishing'
    if (Test-Path -LiteralPath $publishing) { throw "Interrupted publication requires explicit recovery: $publishing" }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 20))
    $stream = [IO.File]::Open($publishing, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
    Invoke-TestHook ('PublishAfterFlush:' + [IO.Path]::GetFileName($Path))
    [IO.File]::Move($publishing, $Path)
}

function Invoke-TestHook([string]$Point) { if ($null -ne $TestHook) { & $TestHook $Point } }

function Get-ProcessRecord([int]$ProcessId) {
    if ($null -ne $TestProcessQuery) { return & $TestProcessQuery $ProcessId }
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    $native = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
    if ($null -eq $native) { return $null }
    return [pscustomobject]@{
        ProcessId = [int]$native.ProcessId
        ParentProcessId = [int]$native.ParentProcessId
        Name = [string]$native.Name
        ExecutablePath = [string]$native.ExecutablePath
        StartTimeUtc = ([DateTimeOffset]$process.StartTime.ToUniversalTime()).ToString('o')
        IsRunning = $true
    }
}

function Assert-BoundProcesses([object]$Metadata) {
    $visualStudio = Get-ProcessRecord ([int]$Metadata.VisualStudio.ProcessId)
    if ($null -eq $visualStudio -or -not $visualStudio.IsRunning -or [string]$visualStudio.Name -cne 'devenv.exe' -or
        ([DateTimeOffset]$visualStudio.StartTimeUtc).UtcTicks -ne ([DateTimeOffset]$Metadata.VisualStudio.StartTimeUtc).UtcTicks) {
        throw 'The bound Visual Studio process identity is no longer live and exact.'
    }
    if ($VisualStudioMcpProcessId -le 0) { throw 'The exact Visual Studio MCP process id is required.' }
    $mcp = Get-ProcessRecord $VisualStudioMcpProcessId
    if ($null -eq $mcp -or -not $mcp.IsRunning -or [string]$mcp.Name -cne 'CodingWithCalvin.MCPServer.Server.exe' -or [int]$mcp.ParentProcessId -ne [int]$Metadata.VisualStudio.ProcessId) {
        throw 'The Visual Studio MCP process is not parented by the bound devenv instance.'
    }
    return [pscustomobject]@{ VisualStudio = $visualStudio; Mcp = $mcp }
}

function Get-ExpectedProjectGuid([string]$SolutionPath, [string]$ProjectPath) {
    $solutionDirectory = [IO.Path]::GetDirectoryName($SolutionPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $solutionDirectory + [IO.Path]::DirectorySeparatorChar
    if (-not $ProjectPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'The target project escaped the solution directory.' }
    $relative = $ProjectPath.Substring($prefix.Length).Replace('/', '\')
    $escaped = [regex]::Escape($relative)
    $matches = [regex]::Matches([IO.File]::ReadAllText($SolutionPath), '(?im)^Project\("\{[^}]+\}"\)\s*=\s*"[^"]+",\s*"' + $escaped + '",\s*"\{(?<guid>[0-9a-f-]{36})\}"\s*$')
    if ($matches.Count -ne 1) { throw 'The solution must bind the exact target project to one project GUID.' }
    return ([Guid]$matches[0].Groups['guid'].Value).ToString('D').ToUpperInvariant()
}

function Get-ExpectedEnvironment([object]$Metadata, [string]$IntendedLaunchSettingsPath) {
    $document = Read-RequiredJson $IntendedLaunchSettingsPath 'Intended launch settings backup'
    $property = $document.profiles.PSObject.Properties[$ExpectedProfile]
    if ($null -eq $property -or $null -eq $property.Value.environmentVariables) { throw 'Intended launch settings contain no expected environment.' }
    $result = [ordered]@{}
    foreach ($entry in @($property.Value.environmentVariables.PSObject.Properties)) {
        if ($result.Contains([string]$entry.Name)) { throw 'Intended launch environment contains a duplicate key.' }
        $result[[string]$entry.Name] = [string]$entry.Value
    }
    $required = [ordered]@{
        ISR_L00C_LAB = '1'
        ISR_L00C_AUTOSHUTDOWN = '1'
        ISR_L00C_AUTOSHUTDOWN_DELAY_MS = '15000'
        ISR_L00C_LAB_ROOT = [string]$Metadata.LaboratoryRoot
        ISR_L00C_F5_TRANSACTION_ID = [string]$Metadata.TransactionId
        ISR_L00C_F5_LAUNCH_NONCE = [string]$Metadata.Nonce
        ISR_L00C_F5_TRANSACTION_DIRECTORY = [string]$Metadata.TransactionDirectory
        ISR_L00C_F5_VISUAL_STUDIO_PID = [string]$Metadata.VisualStudio.ProcessId
        ISR_L00C_F5_SOLUTION_PATH = [string]$Metadata.SolutionPath
    }
    foreach ($name in $required.Keys) {
        if (-not $result.Contains($name) -or [string]$result[$name] -cne [string]$required[$name]) { throw "Intended launch environment does not bind $name exactly." }
    }
    return $result
}

function Get-StringArray([object]$Value) {
    if ($null -eq $Value) { return @() }
    return @($Value | ForEach-Object { [string]$_ })
}

function Compare-StringArrays([object]$Expected, [object]$Actual) {
    $left = @(Get-StringArray $Expected); $right = @(Get-StringArray $Actual)
    if ($left.Count -ne $right.Count) { return $false }
    for ($index = 0; $index -lt $left.Count; $index++) { if ($left[$index] -cne $right[$index]) { return $false } }
    return $true
}

function Compare-Environment([Collections.IDictionary]$Expected, [object]$Actual) {
    if ($null -eq $Actual) { return $false }
    $properties = @($Actual.PSObject.Properties)
    if ($Expected.Count -ne $properties.Count) { return $false }
    foreach ($name in $Expected.Keys) {
        $property = $Actual.PSObject.Properties[[string]$name]
        if ($null -eq $property -or [string]$property.Value -cne [string]$Expected[$name]) { return $false }
    }
    return $true
}

function Assert-SnapshotIdentityAndSafety([object]$Snapshot, [object]$Metadata, [string]$ExpectedProjectGuid) {
    if ([int]$Snapshot.ProcessId -ne [int]$Metadata.VisualStudio.ProcessId -or
        ([DateTimeOffset]$Snapshot.ProcessStartUtc).UtcTicks -ne ([DateTimeOffset]$Metadata.VisualStudio.StartTimeUtc).UtcTicks -or
        [string]$Snapshot.SolutionPath -cne [string]$Metadata.SolutionPath -or
        [string]$Snapshot.ProjectPath -cne [string]$Metadata.ProjectPath -or
        [string]$Snapshot.StartupProjectPath -cne [string]$Metadata.ProjectPath -or
        [string]$Snapshot.ProjectGuid -cne $ExpectedProjectGuid -or
        [string]$Snapshot.ActiveConfiguration -cne $ExpectedConfiguration -or [string]$Snapshot.ActivePlatform -cne $ExpectedPlatform -or
        [string]$Snapshot.ActiveDebugProfile -cne [string]$Metadata.ProfileName) {
        throw 'Visual Studio snapshot belongs to another process, solution, project, startup target, GUID, configuration, platform, or profile.'
    }
    if ([string]$Snapshot.DebuggerMode -cne 'Design' -or [int]$Snapshot.UnsavedDocumentCount -ne 0 -or [bool]$Snapshot.SolutionIsDirty -or [bool]$Snapshot.ProjectIsDirty -or -not [bool]$Snapshot.ProjectSaved) {
        throw 'Visual Studio project reload requires Design mode, every document and the solution saved, and a saved non-dirty target project.'
    }
}

function Get-TargetMismatches([object]$Snapshot, [object]$Metadata, [Collections.IDictionary]$ExpectedEnvironment) {
    $mismatches = New-Object Collections.Generic.List[string]
    $targets = @($Snapshot.Targets)
    if ($targets.Count -ne 1) { [void]$mismatches.Add('TargetCount'); return $mismatches.ToArray() }
    $target = $targets[0]
    if ([string]$target.ExecutablePath -cne [string]$Metadata.ExpectedGameExecutablePath) { [void]$mismatches.Add('EvaluatedExecutablePath') }
    if (-not (Compare-StringArrays $Metadata.ExpectedArguments $target.Arguments)) { [void]$mismatches.Add('EvaluatedArguments') }
    $expectedWorkingDirectory = [IO.Path]::GetDirectoryName([string]$Metadata.ExpectedGameExecutablePath)
    if ([string]$target.WorkingDirectory -cne $expectedWorkingDirectory) { [void]$mismatches.Add('EvaluatedWorkingDirectory') }
    if (-not (Compare-Environment $ExpectedEnvironment $target.Environment)) { [void]$mismatches.Add('EvaluatedEnvironment') }
    return $mismatches.ToArray()
}

function Import-LiveBridge {
    if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'The live Visual Studio COM bridge must run under Windows PowerShell Desktop; synthetic tests may run under pwsh.' }
    if ('ISRWorldGen.L00C.VisualStudioConsumption.VisualStudioConsumptionBridge' -as [type]) { return }
    $devenv = Get-ProcessRecord ([int]$metadata.VisualStudio.ProcessId)
    if ($null -eq $devenv -or [string]::IsNullOrWhiteSpace([string]$devenv.ExecutablePath)) { throw 'Cannot locate the bound Visual Studio installation.' }
    $ide = [IO.Path]::GetDirectoryName([string]$devenv.ExecutablePath)
    $interop = Join-Path $ide 'PublicAssemblies\Microsoft.VisualStudio.Interop.dll'
    Assert-PhysicalFile $interop 'Visual Studio interop assembly'
    [void][Reflection.Assembly]::LoadFrom($interop)
    Add-Type -Path (Join-Path $PSScriptRoot 'L00CVisualStudioConsumptionBridge.cs') -ReferencedAssemblies $interop
}

function Get-LiveSnapshot {
    Import-LiveBridge
    $bridge = [ISRWorldGen.L00C.VisualStudioConsumption.VisualStudioConsumptionBridge]
    $dte = $bridge::GetDte([int]$metadata.VisualStudio.ProcessId)
    if ($null -eq $dte) { throw 'The exact Visual Studio DTE ROT moniker is absent.' }
    $solution = $bridge::GetProperty($dte, 'Solution')
    $solutionPath = [string]$bridge::GetProperty($solution, 'FullName')
    $project = $bridge::FindProject($dte, [string]$metadata.ProjectPath)
    if ($null -eq $project) { throw 'The exact target project is absent from the bound DTE solution.' }
    $solutionBuild = $bridge::GetProperty($solution, 'SolutionBuild')
    $startup = @($bridge::GetProperty($solutionBuild, 'StartupProjects'))
    if ($startup.Count -ne 1) { throw 'Visual Studio must expose exactly one startup project.' }
    $startupPath = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($solutionPath)) ([string]$startup[0])))
    $configurationManager = $bridge::GetProperty($project, 'ConfigurationManager')
    $activeConfiguration = $bridge::GetProperty($configurationManager, 'ActiveConfiguration')
    $configurationName = [string]$bridge::GetProperty($activeConfiguration, 'ConfigurationName')
    $platformName = [string]$bridge::GetProperty($activeConfiguration, 'PlatformName')
    $properties = $bridge::GetProperty($project, 'Properties')
    $activeProfile = $null
    $propertyCount = [int]$bridge::GetProperty($properties, 'Count')
    for ($index = 1; $index -le $propertyCount; $index++) {
        $property = $bridge::GetItem($properties, $index)
        if ([string]$bridge::GetProperty($property, 'Name') -ceq 'ActiveDebugProfile') { $activeProfile = [string]$bridge::GetProperty($property, 'Value'); break }
    }
    $documents = $bridge::GetProperty($dte, 'Documents')
    $documentCount = [int]$bridge::GetProperty($documents, 'Count')
    $unsaved = 0
    for ($index = 1; $index -le $documentCount; $index++) {
        $document = $bridge::GetItem($documents, $index)
        if (-not [bool]$bridge::GetProperty($document, 'Saved')) { $unsaved++ }
    }
    $debugger = $bridge::GetProperty($dte, 'Debugger')
    $debuggerModeValue = [int]$bridge::GetProperty($debugger, 'CurrentMode')
    $debuggerMode = if ($debuggerModeValue -eq 1) { 'Design' } elseif ($debuggerModeValue -eq 2) { 'Break' } elseif ($debuggerModeValue -eq 3) { 'Run' } else { 'Unknown' }
    $service = $bridge::GetSolutionService($dte)
    $solutionIsDirty = $bridge::GetSolutionIsDirty($service)
    $uniqueName = [string]$bridge::GetProperty($project, 'UniqueName')
    $hierarchy = $bridge::GetProjectHierarchy($service, $uniqueName)
    $projectGuid = $bridge::GetProjectGuid($service, $hierarchy).ToString('D').ToUpperInvariant()
    $configuration = $bridge::GetProjectConfiguration($hierarchy, $configurationName, $platformName)
    $targets = @($bridge::QueryDebugTargets($configuration, 0))
    $targetSnapshots = @()
    foreach ($target in $targets) {
        $environment = [ordered]@{}
        foreach ($entry in ([string]$target.bstrEnv -split "`0")) {
            if ([string]::IsNullOrEmpty($entry)) { continue }
            $separator = $entry.IndexOf('=')
            if ($separator -le 0) { throw 'Visual Studio returned a malformed environment entry.' }
            $name = $entry.Substring(0, $separator)
            if ($environment.Contains($name)) { throw 'Visual Studio returned a duplicate environment key.' }
            $environment[$name] = $entry.Substring($separator + 1)
        }
        $targetSnapshots += [pscustomobject]@{
            ExecutablePath = [string]$target.bstrExe
            Arguments = @($bridge::SplitCommandLine([string]$target.bstrArg))
            WorkingDirectory = [string]$target.bstrCurDir
            Environment = [pscustomobject]$environment
        }
    }
    return [pscustomobject]@{
        ProcessId = [int]$metadata.VisualStudio.ProcessId
        ProcessStartUtc = [string]$metadata.VisualStudio.StartTimeUtc
        SolutionPath = $solutionPath
        ProjectPath = [string]$bridge::GetProperty($project, 'FullName')
        StartupProjectPath = $startupPath
        ProjectGuid = $projectGuid
        ActiveConfiguration = $configurationName
        ActivePlatform = $platformName
        ActiveDebugProfile = $activeProfile
        DebuggerMode = $debuggerMode
        UnsavedDocumentCount = $unsaved
        SolutionIsDirty = $solutionIsDirty
        ProjectIsDirty = [bool]$bridge::GetProperty($project, 'IsDirty')
        ProjectSaved = [bool]$bridge::GetProperty($project, 'Saved')
        Targets = $targetSnapshots
        ObservedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
}

function Get-Snapshot {
    if ($null -ne $TestSnapshotQuery) { return & $TestSnapshotQuery }
    return Get-LiveSnapshot
}

function Invoke-LiveReload([string]$ExpectedProjectGuid) {
    Import-LiveBridge
    $bridge = [ISRWorldGen.L00C.VisualStudioConsumption.VisualStudioConsumptionBridge]
    $dte = $bridge::GetDte([int]$metadata.VisualStudio.ProcessId)
    if ($null -eq $dte) { throw 'The exact Visual Studio DTE ROT moniker disappeared before reload.' }
    $service = $bridge::GetSolutionService($dte)
    $project = $bridge::FindProject($dte, [string]$metadata.ProjectPath)
    if ($null -eq $project) { throw 'The exact Visual Studio project disappeared before reload.' }
    $hierarchy = $bridge::GetProjectHierarchy($service, [string]$bridge::GetProperty($project, 'UniqueName'))
    $actualGuid = $bridge::GetProjectGuid($service, $hierarchy).ToString('D').ToUpperInvariant()
    if ($actualGuid -cne $ExpectedProjectGuid) { throw 'The project GUID changed before ReloadProject.' }
    $result = $bridge::ReloadProject($service, [Guid]$ExpectedProjectGuid)
    if ($result -lt 0) { [Runtime.InteropServices.Marshal]::ThrowExceptionForHR($result) }
    return $result
}

function Invoke-Reload([string]$ExpectedProjectGuid) {
    if ($null -ne $TestReloadProject) { return & $TestReloadProject $ExpectedProjectGuid }
    return Invoke-LiveReload $ExpectedProjectGuid
}

function New-ConsumptionReceipt([object]$Snapshot, [object]$Metadata, [string]$IntentPath, [string]$RecoveryMode) {
    $target = @($Snapshot.Targets)[0]
    return [ordered]@{
        SchemaVersion = 2
        Protocol = $Protocol
        Status = 'VISUAL_STUDIO_PROFILE_CONSUMED'
        Source = 'VISUAL_STUDIO_DTE_MCP'
        AttestationMethod = $AttestationMethod
        TransactionId = [string]$Metadata.TransactionId
        Nonce = [string]$Metadata.Nonce
        ProcessId = [int]$Metadata.VisualStudio.ProcessId
        ProcessStartUtc = [string]$Metadata.VisualStudio.StartTimeUtc
        McpProcessId = $VisualStudioMcpProcessId
        McpParentProcessId = [int]$Metadata.VisualStudio.ProcessId
        SolutionPath = [string]$Snapshot.SolutionPath
        ProjectPath = [string]$Snapshot.ProjectPath
        StartupProjectPath = [string]$Snapshot.StartupProjectPath
        ProjectGuid = [string]$Snapshot.ProjectGuid
        ActiveConfiguration = [string]$Snapshot.ActiveConfiguration
        ActivePlatform = [string]$Snapshot.ActivePlatform
        ActiveDebugProfile = [string]$Snapshot.ActiveDebugProfile
        EvaluatedExecutablePath = [string]$target.ExecutablePath
        EvaluatedArguments = @(Get-StringArray $target.Arguments)
        EvaluatedWorkingDirectory = [string]$target.WorkingDirectory
        EvaluatedEnvironment = $target.Environment
        DebuggerMode = [string]$Snapshot.DebuggerMode
        UnsavedDocumentCount = [int]$Snapshot.UnsavedDocumentCount
        SolutionIsDirty = [bool]$Snapshot.SolutionIsDirty
        ProjectIsDirty = [bool]$Snapshot.ProjectIsDirty
        ProjectSaved = [bool]$Snapshot.ProjectSaved
        ConsumedLaunchSettingsSha256 = [string]$Metadata.IntendedSha256
        ConsumedProjectUserSettingsSha256 = [string]$Metadata.ProjectUserIntendedSha256
        ReloadIntentSha256 = Get-FileSha256 $IntentPath
        RecoveryMode = $RecoveryMode
        ObservedUtc = [string]$Snapshot.ObservedUtc
    }
}

$transaction = Get-CanonicalPath $BackupDirectory
Assert-PhysicalDirectory $transaction 'F5 transaction directory'
$metadataPath = Join-Path $transaction 'metadata.json'
$preparedPath = Join-Path $transaction 'settings-prepared.json'
$metadata = Read-RequiredJson $metadataPath 'F5 transaction metadata'
$prepared = Read-RequiredJson $preparedPath 'SETTINGS_PREPARED_FOR_VS receipt'
if ([int]$metadata.SchemaVersion -ne 2 -or [string]$metadata.Protocol -cne $Protocol -or [string]$metadata.State -cne 'PREPARE_INTENT' -or
    [string]$prepared.Status -cne 'SETTINGS_PREPARED_FOR_VS' -or [string]$prepared.TransactionId -cne [string]$metadata.TransactionId) {
    throw 'Visual Studio consumption requires the exact prepared v2 transaction.'
}
Assert-DirectChild $transaction (Join-Path ([string]$metadata.LaboratoryRoot) 'f5-profile-transactions') 'F5 transaction directory'
if ([string]$metadata.TransactionDirectory -cne $transaction -or [string]$metadata.ProfileName -cne $ExpectedProfile) { throw 'F5 transaction metadata is detached from its directory or profile.' }
foreach ($path in @([string]$metadata.SolutionPath, [string]$metadata.ProjectPath, [string]$metadata.LaunchSettingsPath, [string]$metadata.ProjectUserSettingsPath, (Join-Path $transaction 'launchSettings.intended.bin'), (Join-Path $transaction 'project.user.intended.bin'))) {
    Assert-PhysicalFile $path 'Visual Studio consumption input'
}
if ((Get-FileSha256 ([string]$metadata.LaunchSettingsPath)) -cne [string]$metadata.IntendedSha256 -or
    (Get-FileSha256 ([string]$metadata.ProjectUserSettingsPath)) -cne [string]$metadata.ProjectUserIntendedSha256 -or
    (Get-FileSha256 (Join-Path $transaction 'launchSettings.intended.bin')) -cne [string]$metadata.IntendedSha256 -or
    (Get-FileSha256 (Join-Path $transaction 'project.user.intended.bin')) -cne [string]$metadata.ProjectUserIntendedSha256) {
    throw 'Visual Studio consumption requires both exact intended settings revisions.'
}
$expectedEnvironment = Get-ExpectedEnvironment $metadata (Join-Path $transaction 'launchSettings.intended.bin')
$expectedProjectGuid = Get-ExpectedProjectGuid ([string]$metadata.SolutionPath) ([string]$metadata.ProjectPath)
$intentPath = Join-Path $transaction 'visual-studio-reload-intent.json'
$receiptPath = Join-Path $transaction 'visual-studio-consumed.json'
$actionLock = $null
try {
    $actionLockPath = Join-Path ([string]$metadata.LaboratoryRoot) 'f5-profile-transaction.lock'
    if (Test-Path -LiteralPath $actionLockPath) { Assert-PhysicalFile $actionLockPath 'F5 transaction action lock' }
    try { $actionLock = [IO.File]::Open($actionLockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch [IO.IOException] { throw 'Another F5 transaction action owns the exclusive action lock.' }
    [void](Assert-BoundProcesses $metadata)
    $snapshot = Get-Snapshot
    Assert-SnapshotIdentityAndSafety $snapshot $metadata $expectedProjectGuid
    $mismatches = @(Get-TargetMismatches $snapshot $metadata $expectedEnvironment)

    if ($Action -eq 'Inspect') {
        [ordered]@{
            Status = if ($mismatches.Count -eq 0) { 'VISUAL_STUDIO_PROFILE_CONSUMED_IN_MEMORY' } else { 'VISUAL_STUDIO_CONFIGURATION_STALE' }
            TransactionId = [string]$metadata.TransactionId
            ProcessId = [int]$metadata.VisualStudio.ProcessId
            ProjectGuid = $expectedProjectGuid
            Mismatches = $mismatches
            Snapshot = $snapshot
        } | ConvertTo-Json -Depth 20 -Compress
        return
    }

    if (Test-Path -LiteralPath $receiptPath) { throw 'Visual Studio consumption receipt already exists; replay is forbidden.' }
    if ($Action -eq 'ReloadAndAttest') {
        if (Test-Path -LiteralPath $intentPath) { throw 'A prior Visual Studio reload intent requires ResumeAfterInterruption or transaction recovery.' }
        $intent = [ordered]@{
            SchemaVersion = 2
            Protocol = $Protocol
            Status = 'VISUAL_STUDIO_RELOAD_INTENT'
            TransactionId = [string]$metadata.TransactionId
            ProcessId = [int]$metadata.VisualStudio.ProcessId
            ProcessStartUtc = [string]$metadata.VisualStudio.StartTimeUtc
            McpProcessId = $VisualStudioMcpProcessId
            ProjectPath = [string]$metadata.ProjectPath
            ProjectGuid = $expectedProjectGuid
            MetadataSha256 = Get-FileSha256 $metadataPath
            LaunchSettingsSha256 = [string]$metadata.IntendedSha256
            ProjectUserSettingsSha256 = [string]$metadata.ProjectUserIntendedSha256
            PreReloadMismatches = $mismatches
            DebuggerMode = [string]$snapshot.DebuggerMode
            UnsavedDocumentCount = [int]$snapshot.UnsavedDocumentCount
            SolutionIsDirty = [bool]$snapshot.SolutionIsDirty
            ProjectIsDirty = [bool]$snapshot.ProjectIsDirty
            ProjectSaved = [bool]$snapshot.ProjectSaved
            PreparedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        }
        Write-DurableNewJson $intentPath $intent
        Invoke-TestHook 'AfterReloadIntent'

        # Re-read every mutable precondition after the durable intent and
        # immediately before the single bounded ReloadProject call.
        if ((Get-FileSha256 ([string]$metadata.LaunchSettingsPath)) -cne [string]$metadata.IntendedSha256 -or (Get-FileSha256 ([string]$metadata.ProjectUserSettingsPath)) -cne [string]$metadata.ProjectUserIntendedSha256) {
            throw 'Settings changed after reload intent publication.'
        }
        [void](Assert-BoundProcesses $metadata)
        $commitSnapshot = Get-Snapshot
        Assert-SnapshotIdentityAndSafety $commitSnapshot $metadata $expectedProjectGuid
        [void](Invoke-Reload $expectedProjectGuid)
        Invoke-TestHook 'AfterReloadBeforeObservation'
        $recoveryMode = 'RELOADPROJECT_RETURNED'
    }
    else {
        if (-not (Test-Path -LiteralPath $intentPath -PathType Leaf)) { throw 'ResumeAfterInterruption requires the durable Visual Studio reload intent.' }
        $intent = Read-RequiredJson $intentPath 'Visual Studio reload intent'
        if ([string]$intent.Status -cne 'VISUAL_STUDIO_RELOAD_INTENT' -or [string]$intent.TransactionId -cne [string]$metadata.TransactionId -or
            [string]$intent.ProjectGuid -cne $expectedProjectGuid -or [string]$intent.MetadataSha256 -cne (Get-FileSha256 $metadataPath)) {
            throw 'Visual Studio reload intent is malformed or detached.'
        }
        $recoveryMode = 'POST_INTERRUPTION_READ_ONLY_REOBSERVATION'
    }

    if ((Get-FileSha256 ([string]$metadata.LaunchSettingsPath)) -cne [string]$metadata.IntendedSha256 -or (Get-FileSha256 ([string]$metadata.ProjectUserSettingsPath)) -cne [string]$metadata.ProjectUserIntendedSha256) {
        throw 'Settings changed before post-reload Visual Studio observation.'
    }
    [void](Assert-BoundProcesses $metadata)
    $postSnapshot = Get-Snapshot
    Assert-SnapshotIdentityAndSafety $postSnapshot $metadata $expectedProjectGuid
    $postMismatches = @(Get-TargetMismatches $postSnapshot $metadata $expectedEnvironment)
    if ($postMismatches.Count -ne 0) {
        throw ('Visual Studio reload did not consume the exact launch target: ' + ($postMismatches -join ', ') + '. Stop the bound VS and use transaction Recover; do not Arm or repeat reload without new authority.')
    }
    $receipt = New-ConsumptionReceipt $postSnapshot $metadata $intentPath $recoveryMode
    Write-DurableNewJson $receiptPath $receipt
    [ordered]@{
        Status = 'VISUAL_STUDIO_PROFILE_CONSUMED'
        TransactionId = [string]$metadata.TransactionId
        VisualStudioAttestationPath = $receiptPath
        ProjectGuid = $expectedProjectGuid
        RecoveryMode = $recoveryMode
    } | ConvertTo-Json -Compress
}
finally {
    if ($null -ne $actionLock) { $actionLock.Dispose() }
}
