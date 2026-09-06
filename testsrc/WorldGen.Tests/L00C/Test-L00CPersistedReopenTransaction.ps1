[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
$sourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$transactionSourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\PersistedReopenPublicationTransaction.cs'
foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$transactionType = $assembly.GetType('ISRWorldGen.WorldgenProbe.PersistedReopenPublicationTransaction', $false)
$markerGateType = $assembly.GetType('ISRWorldGen.WorldgenProbe.MarkerPublicationGate', $false)
$markerType = $assembly.GetType('ISRWorldGen.WorldgenProbe.ProbeMarker', $false)
foreach ($type in @($transactionType, $markerGateType, $markerType)) {
    if ($null -eq $type) { throw 'The Debug assembly does not expose the production persisted-reopen transaction components.' }
}
$executeDefinition = $transactionType.GetMethod('Execute')
$begin = $markerGateType.GetMethod('Begin')
$commit = $markerGateType.GetMethod('Commit')
$save = $markerGateType.GetMethod('SaveIfCommitted')
$isCommitted = $markerGateType.GetProperty('IsCommitted')
if ($null -eq $executeDefinition -or -not $executeDefinition.IsGenericMethodDefinition) {
    throw 'The production persisted-reopen transaction entry point is unavailable.'
}
$execute = $executeDefinition.MakeGenericMethod([object])

function Invoke-Case {
    param([string]$FaultStage)

    $gate = [Activator]::CreateInstance($markerGateType)
    $source = [Activator]::CreateInstance($markerType)
    $markerType.GetProperty('MarkerId').SetValue($source, 'marker-a')
    $markerType.GetProperty('SavegameIdentifier').SetValue($source, 'save-a')
    $markerType.GetProperty('Version').SetValue($source, 'l00c-flat-v2-map-snapshot')
    $markerType.GetProperty('OpenCount').SetValue($source, 4)
    $candidate = $begin.Invoke($gate, @($source))
    $trace = [Collections.Generic.List[string]]::new()
    $script:storeCalls = 0
    $store = [Action[byte[]]]{
        param([byte[]]$Payload)
        $script:storeCalls++
    }
    $throwAt = {
        param([string]$Stage)
        [void]$trace.Add($Stage)
        if ($FaultStage -eq $Stage) { throw [InvalidOperationException]::new("synthetic-$Stage-failure") }
    }
    $inspect = [Func[object]]{
        & $throwAt 'inspection'
        return [object]'snapshot'
    }
    $prepare = [Action]{
        & $throwAt 'prepare'
        $markerType.GetProperty('OpenCount').SetValue($candidate, 5)
    }
    $inventoryLog = [Action]{ & $throwAt 'inventory-log' }
    $activationLog = [Action]{ & $throwAt 'activation-log' }
    $schedule = [Action[object]]{
        param($Snapshot)
        if ($Snapshot -ne 'snapshot') { throw 'Inspection result was not passed to scheduling.' }
        & $throwAt 'schedule'
    }
    $commitAction = [Action]{
        & $throwAt 'commit'
        [void]$commit.Invoke($gate, @($store))
    }

    $threw = $false
    try {
        [void]$execute.Invoke($null, @($inspect, $prepare, $inventoryLog, $activationLog, $schedule, $commitAction))
    }
    catch {
        $observed = $_.Exception
        while ($null -ne $observed.InnerException) { $observed = $observed.InnerException }
        if ($FaultStage -eq 'none' -or $observed.Message -ne "synthetic-$FaultStage-failure") { throw }
        $threw = $true
    }

    $script:saveCalls = 0
    $saved = [bool]$save.Invoke($gate, @([Action[byte[]]]{ param([byte[]]$Payload) $script:saveCalls++ }))
    return [pscustomobject]@{
        FaultStage = $FaultStage
        Threw = $threw
        StoreCalls = $script:storeCalls
        SaveCalls = $script:saveCalls
        Saved = $saved
        Committed = [bool]$isCommitted.GetValue($gate)
        Trace = @($trace)
    }
}

$faults = @('inspection', 'prepare', 'inventory-log', 'activation-log', 'schedule')
$failedCases = @($faults | ForEach-Object { Invoke-Case $_ })
foreach ($case in $failedCases) {
    if (-not $case.Threw -or $case.StoreCalls -ne 0 -or $case.SaveCalls -ne 0 -or $case.Saved -or $case.Committed) {
        throw "Fault $($case.FaultStage) wrote or exposed a reopen marker before commit."
    }
}
$success = Invoke-Case 'none'
$expectedTrace = 'inspection|prepare|inventory-log|activation-log|schedule|commit'
if ($success.Threw -or $success.StoreCalls -ne 1 -or $success.SaveCalls -ne 1 -or -not $success.Saved -or
    -not $success.Committed -or ($success.Trace -join '|') -ne $expectedTrace -or $success.Trace[-1] -ne 'commit') {
    throw 'Successful production transaction did not make commit the final critical operation.'
}

$source = Get-Content -LiteralPath $sourcePath -Raw
$transactionSource = Get-Content -LiteralPath $transactionSourcePath -Raw
$branchStart = $source.IndexOf('if (!saveGame.IsNew)', [StringComparison]::Ordinal)
$branchEnd = $source.IndexOf('ValidateReplacementPreconditions(handlers)', $branchStart, [StringComparison]::Ordinal)
$branch = $source.Substring($branchStart, $branchEnd - $branchStart)
$executeIndex = $branch.IndexOf('PersistedReopenPublicationTransaction.Execute(', [StringComparison]::Ordinal)
$commitIndex = $branch.IndexOf('() => markerPublication.Commit(', $executeIndex, [StringComparison]::Ordinal)
$returnIndex = $branch.IndexOf('return;', $commitIndex, [StringComparison]::Ordinal)
if ($executeIndex -lt 0 -or $commitIndex -le $executeIndex -or $returnIndex -le $commitIndex -or
    $branch.Substring($commitIndex, $returnIndex - $commitIndex) -match 'Log\(|SchedulePersistedReopen\(') {
    throw 'InitializeWorldCore does not return immediately after the persisted-reopen transaction commit boundary.'
}
$transactionStart = $transactionSource.IndexOf('TInspection inspection = inspect();', [StringComparison]::Ordinal)
$transactionCommit = $transactionSource.IndexOf('commit();', $transactionStart, [StringComparison]::Ordinal)
$transactionAfterCommit = $transactionSource.Substring($transactionCommit + 'commit();'.Length)
if ($transactionStart -lt 0 -or $transactionCommit -le $transactionStart -or
    $transactionAfterCommit -match '(?m)^\s*(?!\}|#endif|//|$)\S') {
    throw 'The production transaction performs a critical operation after commit.'
}

[ordered]@{
    TestId = 'L00-C-PERSISTED-REOPEN-TRANSACTION'
    Status = 'PASS'
    FaultsBeforeCommit = $faults
    WritesAcrossFaults = [int](($failedCases | Measure-Object StoreCalls -Sum).Sum)
    RepublishAcrossFaults = [int](($failedCases | Measure-Object SaveCalls -Sum).Sum)
    SuccessTrace = $success.Trace
    CommitIsFinalCriticalOperation = $true
    ProductionAssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
} | ConvertTo-Json -Depth 5
