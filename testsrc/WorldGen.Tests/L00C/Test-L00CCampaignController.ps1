[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$controllerPath = Join-Path $PSScriptRoot 'Invoke-L00CCampaignController.ps1'
$evidenceValidatorPath = Join-Path $PSScriptRoot 'Test-L00CEvidence.ps1'
$probeSourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
foreach ($path in @($controllerPath, $evidenceValidatorPath, $probeSourcePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Campaign controller test input is missing: $path" }
}
$probeSourceLines = @(Get-Content -LiteralPath $probeSourcePath)
$activatedSourceLine = @($probeSourceLines | Where-Object { $_.Contains('L00C_ACTIVATED') -and $_.Contains('isnew=False') })
$stableSourceLine = @($probeSourceLines | Where-Object { $_.Contains('L00C_PERSISTED_REOPEN_STABLE') })
if ($activatedSourceLine.Count -ne 1 -or $stableSourceLine.Count -ne 1) {
    throw 'Production log format extraction requires one persisted activated line and one persisted stable line.'
}
function Get-ProductionTemplate([string]$SourceLine, [string]$Marker) {
    $match = [regex]::Match($SourceLine, 'Log\(\$"(?<template>L00C_[^"]+)"\);')
    if (-not $match.Success -or -not $match.Groups['template'].Value.StartsWith($Marker, [StringComparison]::Ordinal)) {
        throw "Unable to extract production template for $Marker."
    }
    return $match.Groups['template'].Value
}
function Get-TemplateTokens([string]$Template) {
    return @([regex]::Matches($Template, '\{(?<token>[^}]+)\}') | ForEach-Object { $_.Groups['token'].Value })
}
function Expand-ProductionTemplate([string]$Template, [hashtable]$Values) {
    return [regex]::Replace($Template, '\{(?<token>[^}]+)\}', {
        param($match)
        $token = $match.Groups['token'].Value
        if (-not $Values.ContainsKey($token)) { throw "Production log fixture lacks token value: $token" }
        return [string]$Values[$token]
    })
}
$activatedTemplate = Get-ProductionTemplate $activatedSourceLine[0] 'L00C_ACTIVATED'
$stableTemplate = Get-ProductionTemplate $stableSourceLine[0] 'L00C_PERSISTED_REOPEN_STABLE'
$expectedActivatedTokens = @('instanceId', 'marker!.MarkerId', 'runId', 'marker.OpenCount', 'saveGame.SavegameIdentifier', 'config.FixtureChunkX', 'config.FixtureChunkZ')
$expectedStableTokens = @('instanceId', 'marker!.MarkerId', 'runId', 'priorityLoads', 'transientRequests', 'refreshPasses', 'refreshedMapChunks', 'fixtureWrites', 'mapSnapshotWrites', 'fixtureCallbackCount', 'persistedSnapshot.Fixture.Hash', 'persistedSnapshot.Halo.Hash')
if (((Get-TemplateTokens $activatedTemplate) -join '|') -ne ($expectedActivatedTokens -join '|') -or
    ((Get-TemplateTokens $stableTemplate) -join '|') -ne ($expectedStableTokens -join '|') -or
    $stableTemplate.Contains(' open=', [StringComparison]::Ordinal)) {
    throw 'Campaign controller test fixture is not aligned with the production persisted-reopen log schema.'
}
$controllerSource = Get-Content -LiteralPath $controllerPath -Raw
$validatorSource = Get-Content -LiteralPath $evidenceValidatorPath -Raw
foreach ($fragment in @('Initialize', 'RecordOpen1', 'AuthorizeOpen2', 'Finalize', 'Test-L00CPersistedDatabase.ps1', 'CreateNew', 'ControllerPhaseBefore', 'ControllerPhaseAfter')) {
    if (-not $controllerSource.Contains($fragment)) { throw "Campaign controller is missing required production wiring: $fragment" }
}
foreach ($fragment in @('CampaignControl', 'RecordOpen1', 'AuthorizeOpen2', 'ControllerPhaseBefore', 'ControllerPhaseAfter')) {
    if (-not $validatorSource.Contains($fragment)) { throw "Final evidence validator is missing campaign phase wiring: $fragment" }
}

$head = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve repository HEAD.' }
$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) { throw 'Debug candidate assembly is missing.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion -ne "1.0.0+$head") {
    throw 'Debug candidate must be rebuilt from the exact clean HEAD before testing the campaign controller.'
}

$selfTestRoot = Join-Path $RepositoryRoot ('.local\L00C\campaign-controller-selftest\' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $selfTestRoot)

$sqliteDirectory = 'D:\Jeux\Vintagestory\Lib'
[void][Runtime.InteropServices.NativeLibrary]::Load((Join-Path $sqliteDirectory 'e_sqlite3.dll'))
foreach ($assembly in @('SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll', 'SQLitePCLRaw.batteries_v2.dll', 'Microsoft.Data.Sqlite.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $sqliteDirectory $assembly))
}
[SQLitePCL.Batteries_V2]::Init()

function Write-NewJson([string]$Path, $Value) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { [void](New-Item -ItemType Directory -Path $parent) }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        try { $writer.Write(($Value | ConvertTo-Json -Depth 6)) } finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }
}

function New-CompleteDatabase([string]$Path) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { [void](New-Item -ItemType Directory -Path $parent) }
    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$Path;Mode=ReadWriteCreate;Pooling=False")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = 'CREATE TABLE mapchunk(position INTEGER PRIMARY KEY, data BLOB NOT NULL); CREATE TABLE chunk(position INTEGER PRIMARY KEY, data BLOB NOT NULL);'
        [void]$command.ExecuteNonQuery()
        $transaction = $connection.BeginTransaction()
        try {
            $command.Transaction = $transaction
            $position = $command.CreateParameter()
            $position.ParameterName = '$position'
            [void]$command.Parameters.Add($position)
            for ($z = 31989; $z -le 31991; $z++) {
                for ($x = 31989; $x -le 31991; $x++) {
                    $command.CommandText = 'INSERT INTO mapchunk(position, data) VALUES($position, zeroblob(64))'
                    $position.Value = ([int64]$z -shl 27) -bor [int64]$x
                    [void]$command.ExecuteNonQuery()
                    $command.CommandText = 'INSERT INTO chunk(position, data) VALUES($position, zeroblob(64))'
                    for ($y = 0; $y -lt 8; $y++) {
                        $position.Value = ([int64]$y -shl 54) -bor ([int64]$z -shl 27) -bor [int64]$x
                        [void]$command.ExecuteNonQuery()
                    }
                }
            }
            $transaction.Commit()
        }
        finally { $transaction.Dispose() }
    }
    finally {
        $connection.Close()
        $connection.Dispose()
    }
}

function New-Campaign([string]$Name) {
    $root = Join-Path $selfTestRoot $Name
    [void](New-Item -ItemType Directory -Path $root)
    $evidence = Join-Path $root 'evidence'
    $fixture = [ordered]@{
        Root = $root
        Evidence = $evidence
        Database = Join-Path $root 'data\Saves\fresh.vcdbs'
        Snapshot = Join-Path $evidence 'artifacts\open1.vcdbs'
        Report = Join-Path $evidence 'artifacts\open1-database.json'
        Open1Session = Join-Path $evidence 'sessions\open1\session.json'
        Open1Log = Join-Path $evidence 'sessions\open1\server-main.log'
        Open2Session = Join-Path $evidence 'sessions\open2\session.json'
        Open2Log = Join-Path $evidence 'sessions\open2\server-main.log'
    }
    $parameters = @{
        Phase = 'Initialize'
        EvidenceDirectory = $fixture.Evidence
        TestedCommit = $head
        AssemblyPath = $assemblyPath
        SaveDatabasePath = $fixture.Database
        SnapshotDatabasePath = $fixture.Snapshot
        PersistenceReportPath = $fixture.Report
        Open1SessionPath = $fixture.Open1Session
        Open1LogPath = $fixture.Open1Log
        Open2SessionPath = $fixture.Open2Session
        Open2LogPath = $fixture.Open2Log
        RepositoryRoot = $RepositoryRoot
    }
    $json = (& $controllerPath @parameters) -join [Environment]::NewLine
    $fixture.Add('Initialize', ($json | ConvertFrom-Json))
    return $fixture
}

function Complete-Open1($Fixture) {
    $initialized = [DateTimeOffset]::Parse([string]$Fixture.Initialize.InitializedUtc).ToUniversalTime()
    $initializeReceiptPath = Join-Path $Fixture.Evidence 'campaign-control\01-initialize.json'
    $initializeWritten = [DateTimeOffset](Get-Item -LiteralPath $initializeReceiptPath).LastWriteTimeUtc
    $started = if ($initialized -gt $initializeWritten) { $initialized.AddTicks(1) } else { $initializeWritten.AddTicks(1) }
    New-CompleteDatabase $Fixture.Database
    Start-Sleep -Milliseconds 2
    $completed = [DateTimeOffset]::UtcNow
    [IO.File]::SetCreationTimeUtc($Fixture.Database, $started.AddTicks(1).UtcDateTime)
    [IO.File]::SetLastWriteTimeUtc($Fixture.Database, $completed.AddTicks(-1).UtcDateTime)
    $save = '33333333-3333-3333-3333-333333333333'
    $marker = '44444444444444444444444444444444'
    $instance = '55555555555555555555555555555555'
    $log = @(
        "L00C_ACTIVATED instance=$instance marker=$marker run=1 open=1 isnew=True save=$save fixture=31990,31990"
        "L00C_TICKS_STABLE instance=$instance marker=$marker run=1 ticks=40 snapshot=$('A' * 64)"
        'World saved!'
    ) -join [Environment]::NewLine
    $logParent = Split-Path -Parent $Fixture.Open1Log
    if (-not (Test-Path -LiteralPath $logParent -PathType Container)) { [void](New-Item -ItemType Directory -Path $logParent) }
    [IO.File]::WriteAllText($Fixture.Open1Log, $log, [Text.UTF8Encoding]::new($false))
    $session = [ordered]@{
        EvidenceSequence = 3
        StartedUtc = $started.ToString('o')
        CompletedUtc = $completed.ToString('o')
        WorldRole = 'activated-primary'
        SavegameIdentifier = $save
        MarkerId = $marker
        InstanceId = $instance
        WorldRunId = 1
        OpenCount = 1
        IsNew = $true
        ControllerPhaseBefore = 'Initialize'
        ControllerPhaseAfter = 'RecordOpen1'
    }
    Write-NewJson $Fixture.Open1Session $session
    $resultJson = (& $controllerPath -Phase RecordOpen1 -EvidenceDirectory $Fixture.Evidence -RepositoryRoot $RepositoryRoot) -join [Environment]::NewLine
    $Fixture.Add('RecordOpen1', ($resultJson | ConvertFrom-Json))
}

function Authorize-Open2($Fixture) {
    $json = (& $controllerPath -Phase AuthorizeOpen2 -EvidenceDirectory $Fixture.Evidence -RepositoryRoot $RepositoryRoot) -join [Environment]::NewLine
    $Fixture.Add('AuthorizeOpen2', ($json | ConvertFrom-Json))
}

function Complete-Open2($Fixture, [bool]$UseFalseStableFormat = $false) {
    $authorized = [DateTimeOffset]::Parse([string]$Fixture.AuthorizeOpen2.AuthorizedUtc).ToUniversalTime()
    $authorizationReceiptPath = Join-Path $Fixture.Evidence 'campaign-control\03-authorize-open2.json'
    $authorizationWritten = [DateTimeOffset](Get-Item -LiteralPath $authorizationReceiptPath).LastWriteTimeUtc
    $started = if ($authorized -gt $authorizationWritten) { $authorized.AddTicks(1) } else { $authorizationWritten.AddTicks(1) }
    Start-Sleep -Milliseconds 2
    $completed = [DateTimeOffset]::UtcNow
    $session = [ordered]@{
        EvidenceSequence = 4
        StartedUtc = $started.ToString('o')
        CompletedUtc = $completed.ToString('o')
        WorldRole = 'activated-primary'
        SavegameIdentifier = [string]$Fixture.RecordOpen1.SavegameIdentifier
        MarkerId = [string]$Fixture.RecordOpen1.MarkerId
        InstanceId = '66666666666666666666666666666666'
        WorldRunId = 1
        OpenCount = 2
        IsNew = $false
        ControllerPhaseBefore = 'AuthorizeOpen2'
        ControllerPhaseAfter = 'Finalize'
    }
    Write-NewJson $Fixture.Open2Session $session
    $logParent = Split-Path -Parent $Fixture.Open2Log
    if (-not (Test-Path -LiteralPath $logParent -PathType Container)) { [void](New-Item -ItemType Directory -Path $logParent) }
    $activatedLine = Expand-ProductionTemplate $activatedTemplate @{
        'instanceId' = [string]$session.InstanceId
        'marker!.MarkerId' = [string]$session.MarkerId
        'runId' = [string]$session.WorldRunId
        'marker.OpenCount' = '2'
        'saveGame.SavegameIdentifier' = [string]$session.SavegameIdentifier
        'config.FixtureChunkX' = '31990'
        'config.FixtureChunkZ' = '31990'
    }
    $stableLine = if ($UseFalseStableFormat) {
        "L00C_PERSISTED_REOPEN_STABLE instance=$($session.InstanceId) marker=$($session.MarkerId) run=$($session.WorldRunId) open=2 loadpriority=0 transientrequests=0 refreshpasses=0 refreshedmapchunks=0 keeploaded=0 unload=0 fixturewrites=0 callbacks=0 center=$('A' * 64) halo=$('B' * 64)"
    }
    else {
        Expand-ProductionTemplate $stableTemplate @{
            'instanceId' = [string]$session.InstanceId
            'marker!.MarkerId' = [string]$session.MarkerId
            'runId' = [string]$session.WorldRunId
            'priorityLoads' = '0'
            'transientRequests' = '0'
            'refreshPasses' = '0'
            'refreshedMapChunks' = '0'
            'fixtureWrites' = '0'
            'mapSnapshotWrites' = '0'
            'fixtureCallbackCount' = '0'
            'persistedSnapshot.Fixture.Hash' = 'A' * 64
            'persistedSnapshot.Halo.Hash' = 'B' * 64
        }
    }
    $open2Log = @($activatedLine, $stableLine) -join [Environment]::NewLine
    [IO.File]::WriteAllText($Fixture.Open2Log, $open2Log, [Text.UTF8Encoding]::new($false))
    $json = (& $controllerPath -Phase Finalize -EvidenceDirectory $Fixture.Evidence -RepositoryRoot $RepositoryRoot) -join [Environment]::NewLine
    $Fixture.Add('Finalize', ($json | ConvertFrom-Json))
}

function Assert-Rejected([scriptblock]$Action, [string]$Label) {
    try { [void](& $Action) } catch { return }
    throw "$Label was unexpectedly accepted."
}

try {
    $preexistingRoot = Join-Path $selfTestRoot 'preexisting'
    [void](New-Item -ItemType Directory -Path $preexistingRoot)
    $preexistingDatabase = Join-Path $preexistingRoot 'fresh.vcdbs'
    [IO.File]::WriteAllBytes($preexistingDatabase, [byte[]](1..8))
    $preexistingEvidence = Join-Path $preexistingRoot 'evidence'
    Assert-Rejected {
        & $controllerPath -Phase Initialize -EvidenceDirectory $preexistingEvidence -TestedCommit $head -AssemblyPath $assemblyPath `
            -SaveDatabasePath $preexistingDatabase -SnapshotDatabasePath (Join-Path $preexistingEvidence 'snapshot.vcdbs') `
            -PersistenceReportPath (Join-Path $preexistingEvidence 'report.json') -Open1SessionPath (Join-Path $preexistingEvidence 'open1.json') `
            -Open1LogPath (Join-Path $preexistingEvidence 'open1.log') -Open2SessionPath (Join-Path $preexistingEvidence 'open2.json') `
            -Open2LogPath (Join-Path $preexistingEvidence 'open2.log') -RepositoryRoot $RepositoryRoot
    } 'Preexisting save database'

    $reversed = New-Campaign 'reversed'
    Assert-Rejected { & $controllerPath -Phase AuthorizeOpen2 -EvidenceDirectory $reversed.Evidence -RepositoryRoot $RepositoryRoot } 'AuthorizeOpen2 before RecordOpen1'

    $stale = New-Campaign 'stale-renamed'
    $initialized = [DateTimeOffset]::Parse([string]$stale.Initialize.InitializedUtc).ToUniversalTime()
    $started = $initialized.AddTicks(1)
    New-CompleteDatabase $stale.Database
    Start-Sleep -Milliseconds 2
    $completed = [DateTimeOffset]::UtcNow
    [IO.File]::SetCreationTimeUtc($stale.Database, $started.AddTicks(1).UtcDateTime)
    [IO.File]::SetLastWriteTimeUtc($stale.Database, $completed.AddTicks(-1).UtcDateTime)
    $logParent = Split-Path -Parent $stale.Open1Log
    [void](New-Item -ItemType Directory -Path $logParent)
    [IO.File]::WriteAllText($stale.Open1Log, 'stale', [Text.UTF8Encoding]::new($false))
    Write-NewJson $stale.Open1Session ([ordered]@{ EvidenceSequence = 3; StartedUtc = $started.ToString('o'); CompletedUtc = $completed.ToString('o'); WorldRole = 'activated-primary'; SavegameIdentifier = '33333333-3333-3333-3333-333333333333'; MarkerId = '4' * 32; InstanceId = '5' * 32; WorldRunId = 1; OpenCount = 1; IsNew = $true; ControllerPhaseBefore = 'Initialize'; ControllerPhaseAfter = 'RecordOpen1' })
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $stale.Report))
    [IO.File]::WriteAllText($stale.Report, '{"renamed":"stale"}', [Text.UTF8Encoding]::new($false))
    Assert-Rejected { & $controllerPath -Phase RecordOpen1 -EvidenceDirectory $stale.Evidence -RepositoryRoot $RepositoryRoot } 'Stale renamed report'

    $rewritten = New-Campaign 'rewritten-attestation'
    Complete-Open1 $rewritten
    $reportObject = Get-Content -LiteralPath $rewritten.Report -Raw | ConvertFrom-Json
    $reportObject.AttestedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Import-Module (Join-Path $PSScriptRoot 'L00CPersistenceAttestation.psm1') -Force
    $reportObject.AttestationId = Get-L00CAttestationId $reportObject
    $reportObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $rewritten.Report -Encoding UTF8 -NoNewline
    Assert-Rejected { & $controllerPath -Phase AuthorizeOpen2 -EvidenceDirectory $rewritten.Evidence -RepositoryRoot $RepositoryRoot } 'Rewritten attestation'

    $premature = New-Campaign 'premature-open2'
    Complete-Open1 $premature
    Write-NewJson $premature.Open2Session ([ordered]@{ EvidenceSequence = 4 })
    Assert-Rejected { & $controllerPath -Phase AuthorizeOpen2 -EvidenceDirectory $premature.Evidence -RepositoryRoot $RepositoryRoot } 'Open2 already exists'

    $falseStable = New-Campaign 'false-stable-field'
    Complete-Open1 $falseStable
    Authorize-Open2 $falseStable
    Assert-Rejected { Complete-Open2 $falseStable $true } 'Invented open field on persisted stable marker'

    $baseline = New-Campaign 'baseline'
    Complete-Open1 $baseline
    Authorize-Open2 $baseline
    Complete-Open2 $baseline
    if ([string]$baseline.Initialize.Status -ne 'READY_FOR_OPEN1' -or
        [string]$baseline.RecordOpen1.Status -ne 'READY_FOR_OPEN2_AUTHORIZATION' -or
        [string]$baseline.AuthorizeOpen2.Status -ne 'AUTHORIZED_OPEN2' -or
        [string]$baseline.Finalize.Status -ne 'COMPLETE') {
        throw 'Valid controller campaign did not traverse all four production phases.'
    }

    [ordered]@{
        TestId = 'L00-C-CAMPAIGN-CONTROLLER'
        Status = 'PASS'
        ValidPhaseOrder = @('Initialize', 'RecordOpen1', 'AuthorizeOpen2', 'Finalize')
        CampaignId = [string]$baseline.Initialize.CampaignId
        DatabasePreexistingRejected = $true
        StaleRenamedReportRejected = $true
        RewrittenAttestationRejected = $true
        Open2AlreadyExistsRejected = $true
        ReversedOrderRejected = $true
        FalseStableOpenFieldRejected = $true
        ProductionLogSchemaDerived = $true
        OperationalGuarantee = 'fresh tamper-evident chain, not attacker-resistant'
    } | ConvertTo-Json -Depth 5
}
finally {
    [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools()
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    $resolvedSelfTest = [IO.Path]::GetFullPath($selfTestRoot)
    $resolvedAllowed = ([IO.Path]::GetFullPath((Join-Path $RepositoryRoot '.local\L00C\campaign-controller-selftest'))).TrimEnd('\') + '\'
    if ($resolvedSelfTest.StartsWith($resolvedAllowed, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedSelfTest)) {
        Remove-Item -LiteralPath $resolvedSelfTest -Recurse -Force
    }
}
