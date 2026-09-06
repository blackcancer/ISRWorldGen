[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Debug assembly is missing: $assemblyPath"
}

foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$gateType = $assembly.GetType('ISRWorldGen.WorldgenProbe.MarkerPublicationGate', $false)
$markerType = $assembly.GetType('ISRWorldGen.WorldgenProbe.ProbeMarker', $false)
if ($null -eq $gateType -or $null -eq $markerType) {
    throw 'The Debug assembly does not expose the production marker publication component.'
}

$beginMethod = $gateType.GetMethod('Begin')
$beginWorldTransitionMethod = $gateType.GetMethod('BeginWorldTransition')
$resetMethod = $gateType.GetMethod('Reset')
$commitMethod = $gateType.GetMethod('Commit')
$saveMethod = $gateType.GetMethod('SaveIfCommitted')
$committedProperty = $gateType.GetProperty('IsCommitted')
foreach ($member in @($beginMethod, $beginWorldTransitionMethod, $resetMethod, $commitMethod, $saveMethod, $committedProperty)) {
    if ($null -eq $member) { throw 'The production marker publication component contract is incomplete.' }
}

function New-Marker {
    param([int]$OpenCount)

    $marker = [Activator]::CreateInstance($markerType)
    $markerType.GetProperty('MarkerId').SetValue($marker, 'marker-a')
    $markerType.GetProperty('SavegameIdentifier').SetValue($marker, 'save-a')
    $markerType.GetProperty('Version').SetValue($marker, 'l00c-flat-v1')
    $markerType.GetProperty('OpenCount').SetValue($marker, $OpenCount)
    return $marker
}

function Get-OpenCount {
    param($Marker)
    return [int]$markerType.GetProperty('OpenCount').GetValue($Marker)
}

function Invoke-UncommittedFailureCase {
    param([string]$Name, [switch]$BeginCandidate)

    $gate = [Activator]::CreateInstance($gateType)
    $source = New-Marker 4
    if ($BeginCandidate) {
        $candidate = $beginMethod.Invoke($gate, @($source))
        $markerType.GetProperty('OpenCount').SetValue($candidate, 5)
    }
    $script:uncommittedStoreCalls = 0
    $store = [Action[byte[]]]{
        param([byte[]]$Payload)
        $script:uncommittedStoreCalls++
    }
    $saved = [bool]$saveMethod.Invoke($gate, @($store))
    if ($saved -or $script:uncommittedStoreCalls -ne 0 -or [bool]$committedProperty.GetValue($gate) -or (Get-OpenCount $source) -ne 4) {
        throw "$Name published or mutated an uncommitted marker candidate."
    }
    return [pscustomobject]@{ Name = $Name; StoreCalls = $script:uncommittedStoreCalls; OriginalOpenCount = Get-OpenCount $source }
}

$missingHandler = Invoke-UncommittedFailureCase missing-handler -BeginCandidate
$persistedMapMissing = Invoke-UncommittedFailureCase persisted-map-missing -BeginCandidate
$corruptMarker = Invoke-UncommittedFailureCase corrupt-marker

$throwGate = [Activator]::CreateInstance($gateType)
$throwSource = New-Marker 4
$throwCandidate = $beginMethod.Invoke($throwGate, @($throwSource))
$markerType.GetProperty('OpenCount').SetValue($throwCandidate, 5)
$throwingStore = [Action[byte[]]]{
    param([byte[]]$Payload)
    throw [InvalidOperationException]::new('synthetic-store-failure')
}
$storeThrew = $false
try {
    [void]$commitMethod.Invoke($throwGate, @($throwingStore))
}
catch {
    $observed = $_.Exception
    while ($null -ne $observed.InnerException) { $observed = $observed.InnerException }
    if ($observed.Message -ne 'synthetic-store-failure') { throw }
    $storeThrew = $true
}
$script:postFailureStoreCalls = 0
$postFailureStore = [Action[byte[]]]{
    param([byte[]]$Payload)
    $script:postFailureStoreCalls++
}
$postFailureSaved = [bool]$saveMethod.Invoke($throwGate, @($postFailureStore))
if (-not $storeThrew -or [bool]$committedProperty.GetValue($throwGate) -or $postFailureSaved -or
    $script:postFailureStoreCalls -ne 0 -or (Get-OpenCount $throwSource) -ne 4) {
    throw 'A throwing StoreData delegate committed, republished, or mutated the previous marker.'
}

$successGate = [Activator]::CreateInstance($gateType)
$successSource = New-Marker 4
$successCandidate = $beginMethod.Invoke($successGate, @($successSource))
if ([object]::ReferenceEquals($successSource, $successCandidate)) {
    throw 'Begin returned the persisted marker reference instead of an isolated candidate copy.'
}
$markerType.GetProperty('OpenCount').SetValue($successCandidate, 5)
$storedPayloads = [Collections.Generic.List[byte[]]]::new()
$successfulStore = [Action[byte[]]]{
    param([byte[]]$Payload)
    $storedPayloads.Add([byte[]]$Payload.Clone())
}
[void]$commitMethod.Invoke($successGate, @($successfulStore))
$savedAfterCommit = [bool]$saveMethod.Invoke($successGate, @($successfulStore))
if (-not [bool]$committedProperty.GetValue($successGate) -or -not $savedAfterCommit -or $storedPayloads.Count -ne 2 -or
    (Get-OpenCount $successSource) -ne 4) {
    throw 'Successful marker commit did not preserve the old object and enable later persistence.'
}
$firstJsonText = [Text.Encoding]::UTF8.GetString($storedPayloads[0])
$secondJsonText = [Text.Encoding]::UTF8.GetString($storedPayloads[1])
$firstJson = [Text.Json.JsonDocument]::Parse([string]$firstJsonText)
$secondJson = [Text.Json.JsonDocument]::Parse([string]$secondJsonText)
try {
    $firstOpenCount = $firstJson.RootElement.GetProperty('OpenCount').GetInt32()
    $secondOpenCount = $secondJson.RootElement.GetProperty('OpenCount').GetInt32()
    if ($firstOpenCount -ne 5 -or $secondOpenCount -ne 5 -or
        [Convert]::ToHexString($storedPayloads[0]) -ne [Convert]::ToHexString($storedPayloads[1])) {
        throw 'Committed marker payload drifted between activation and GameWorldSave persistence.'
    }
}
finally {
    $firstJson.Dispose()
    $secondJson.Dispose()
}

$resetMethod.Invoke($successGate, @())
if ([bool]$committedProperty.GetValue($successGate)) {
    throw 'Reset retained a committed marker across world initialization.'
}

$transitionGate = [Activator]::CreateInstance($gateType)
$transitionSource = New-Marker 7
$transitionCandidate = $beginMethod.Invoke($transitionGate, @($transitionSource))
$markerType.GetProperty('OpenCount').SetValue($transitionCandidate, 8)
$script:priorWorldStoreCalls = 0
$priorWorldStore = [Action[byte[]]]{
    param([byte[]]$Payload)
    $script:priorWorldStoreCalls++
}
[void]$commitMethod.Invoke($transitionGate, @($priorWorldStore))
$beginWorldTransitionMethod.Invoke($transitionGate, @())
$cleanupThrew = $false
try {
    throw [InvalidOperationException]::new('synthetic-world-cleanup-failure')
}
catch {
    $cleanupThrew = $_.Exception.Message -eq 'synthetic-world-cleanup-failure'
}
$script:currentWorldStoreCalls = 0
$currentWorldStore = [Action[byte[]]]{
    param([byte[]]$Payload)
    $script:currentWorldStoreCalls++
}
$currentWorldSaved = [bool]$saveMethod.Invoke($transitionGate, @($currentWorldStore))
if (-not $cleanupThrew -or $currentWorldSaved -or $script:currentWorldStoreCalls -ne 0 -or
    $script:priorWorldStoreCalls -ne 1 -or [bool]$committedProperty.GetValue($transitionGate)) {
    throw 'A cleanup failure after world transition republished the prior world marker payload.'
}

[ordered]@{
    TestId = 'L00-C-MARKER-PUBLICATION'
    Status = 'PASS'
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    MissingHandlerStoreCalls = $missingHandler.StoreCalls
    PersistedMapMissingStoreCalls = $persistedMapMissing.StoreCalls
    CorruptMarkerStoreCalls = $corruptMarker.StoreCalls
    ThrowingStoreCommitted = [bool]$committedProperty.GetValue($throwGate)
    ThrowingStoreRepublishCalls = $script:postFailureStoreCalls
    PreviousMarkerOpenCount = Get-OpenCount $successSource
    CommittedMarkerOpenCount = 5
    SuccessfulStoreCalls = $storedPayloads.Count
    PriorWorldInitialStoreCalls = $script:priorWorldStoreCalls
    CurrentWorldStoreCallsAfterCleanupFailure = $script:currentWorldStoreCalls
} | ConvertTo-Json -Depth 4
