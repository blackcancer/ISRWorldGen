[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$VintageStoryPath = $env:VINTAGE_STORY,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'L02CNativeSqliteFixtureSupport.ps1')

function Write-Utf8Fixture {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)

    [IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Write-JsonFixture {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Value)

    Write-Utf8Fixture $Path ($Value | ConvertTo-Json -Depth 12)
}

function Write-OversizedFixture {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][long]$MaximumBytes,
        [Parameter(Mandatory)][string]$Sentinel
    )

    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Sentinel)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.SetLength($MaximumBytes + 1)
    }
    finally {
        $stream.Dispose()
    }

    if ((Get-Item -LiteralPath $Path).Length -le $MaximumBytes) {
        throw "Oversized self-test fixture was not larger than $MaximumBytes bytes."
    }
}

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)

    if (-not $Condition) {
        throw $Message
    }
}

function Get-TextSha256 {
    param([Parameter(Mandatory)][string]$Value)

    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

function Get-LocalLogTimestamp {
    param([Parameter(Mandatory)][DateTimeOffset]$Utc)

    $zone = [TimeZoneInfo]::FindSystemTimeZoneById('Romance Standard Time')
    return [TimeZoneInfo]::ConvertTime($Utc, $zone).ToString('d.M.yyyy HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-Artifact {
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][string]$Name)

    $matches = @($Manifest.Artifacts | Where-Object FileName -ceq $Name)
    if ($matches.Count -ne 1) {
        throw "Self-test snapshot expected one artifact $Name."
    }

    return $matches[0]
}

function Get-SymbolPair {
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][string]$AssemblyName)

    $matches = @($Manifest.SymbolPairs | Where-Object AssemblyFileName -ceq $AssemblyName)
    if ($matches.Count -ne 1) {
        throw "Self-test snapshot expected one symbol pair for $AssemblyName."
    }

    return $matches[0]
}

function New-CaseObservation {
    param(
        [Parameter(Mandatory)][string]$CaseName,
        [Parameter(Mandatory)][int]$ServerPid,
        [Parameter(Mandatory)][string]$LogPath,
        [Parameter(Mandatory)][DateTimeOffset]$Started,
        [Parameter(Mandatory)][DateTimeOffset]$BreakpointHitUtc,
        [Parameter(Mandatory)][DateTimeOffset]$DebuggerContinueUtc,
        [Parameter(Mandatory)][DateTimeOffset]$Completed,
        [Parameter(Mandatory)]$AssemblyArtifact,
        [Parameter(Mandatory)]$PdbArtifact,
        [Parameter(Mandatory)]$SymbolPair
    )

    $breakpointId = if ($CaseName -in @('new', 'reload')) {
        'native-profile-game-ready-frozen'
    }
    else {
        'native-profile-rejected-before-log'
    }
    $frames = if ($breakpointId -ceq 'native-profile-game-ready-frozen') {
        @(
            'native-profile-host.create-frozen-diagnostic',
            'native-profile-bridge.try-log-frozen',
            'native-profile-bridge.on-game-ready'
        )
    }
    else {
        @(
            'native-profile-host.log-rejected',
            'native-profile-bridge.reject-and-stop',
            'native-profile-bridge.on-game-ready'
        )
    }

    return [ordered]@{
        SessionId = (Get-TextSha256 "session-$CaseName-$ServerPid").Substring(0, 32).ToLowerInvariant()
        ServerPid = $ServerPid
        StartedUtc = $Started.ToUniversalTime().ToString('o')
        BreakpointHitUtc = $BreakpointHitUtc.ToUniversalTime().ToString('o')
        DebuggerContinueUtc = $DebuggerContinueUtc.ToUniversalTime().ToString('o')
        CompletedUtc = $Completed.ToUniversalTime().ToString('o')
        LogSha256 = (Get-FileHash -LiteralPath $LogPath -Algorithm SHA256).Hash
        BreakpointId = $breakpointId
        CallstackFrames = $frames
        CallstackSha256 = Get-TextSha256 ($frames -join "`n")
        Module = [ordered]@{
            FileName = 'ISRWorldGen.dll'
            PathSha256 = [string]$AssemblyArtifact.PackagePathSha256
            Sha256 = [string]$AssemblyArtifact.Sha256
            ProductVersion = [string]$AssemblyArtifact.ProductVersion
            PdbFileName = 'ISRWorldGen.pdb'
            PdbSha256 = [string]$PdbArtifact.Sha256
            CodeViewGuid = [string]$SymbolPair.CodeViewGuid
            CodeViewAge = [int]$SymbolPair.CodeViewAge
            CodeViewStamp = [uint32]$SymbolPair.CodeViewStamp
        }
    }
}

function Invoke-Validation {
    param(
        [Parameter(Mandatory)][string]$Oracle,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ObservationPath,
        [Parameter(Mandatory)][hashtable]$LogPaths,
        [hashtable]$ExtractionReports = $script:l02cExtractionReports,
        [hashtable]$SealedSources = $script:l02cSealedSources
    )

    & $Oracle -Phase Validate -EvidenceRoot $Root -RepositoryRoot $RepositoryRoot `
        -VintageStoryPath $VintageStoryPath -Configuration $Configuration `
        -NewLog $LogPaths.new -ReloadLog $LogPaths.reload `
        -HeightRefusalLog $LogPaths.height -RectangleRefusalLog $LogPaths.rectangle `
        -CampaignObservationPath $ObservationPath `
        -NewExtractionReport $ExtractionReports.new -ReloadExtractionReport $ExtractionReports.reload `
        -HeightExtractionReport $ExtractionReports.height -RectangleExtractionReport $ExtractionReports.rectangle `
        -NewSealedSourceDirectory $SealedSources.new -ReloadSealedSourceDirectory $SealedSources.reload `
        -HeightSealedSourceDirectory $SealedSources.height `
        -RectangleSealedSourceDirectory $SealedSources.rectangle | Out-Null
}

function Assert-ValidationFails {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$ExpectedMessage,
        [Parameter(Mandatory)][scriptblock]$Action,
        [Parameter(Mandatory)][string]$ReportPath,
        [string]$ForbiddenMessage
    )

    if (Test-Path -LiteralPath $ReportPath) {
        Remove-Item -LiteralPath $ReportPath -Force
    }

    $failed = $false
    try {
        & $Action
    }
    catch {
        $failed = $true
        if (-not $_.Exception.Message.Contains($ExpectedMessage, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Label failed for the wrong reason: $($_.Exception.Message)"
        }
        if (-not [string]::IsNullOrEmpty($ForbiddenMessage) -and
            $_.Exception.Message.Contains($ForbiddenMessage, [StringComparison]::Ordinal)) {
            throw "$Label leaked oversized input content in its rejection."
        }
    }

    if (-not $failed) {
        throw "$Label unexpectedly passed the runtime evidence oracle."
    }

    if (Test-Path -LiteralPath $ReportPath) {
        Remove-Item -LiteralPath $ReportPath -Force
    }
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $tempBase ("isrworldgen-l02c-runtime-evidence-" + [Guid]::NewGuid().ToString('N'))
$pdbMismatchRoot = $null
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $oracle = Join-Path $PSScriptRoot 'Invoke-L02CNativeRuntimeEvidence.ps1'
    $commitResult = & git -c "safe.directory=$RepositoryRoot" -C $RepositoryRoot rev-parse HEAD 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve test repository HEAD: $($commitResult -join [Environment]::NewLine)"
    }

    $commit = ([string]($commitResult | Select-Object -Last 1)).Trim()
    & $oracle -Phase Snapshot -EvidenceRoot $testRoot -RepositoryRoot $RepositoryRoot `
        -VintageStoryPath $VintageStoryPath -Configuration $Configuration -ExpectedCommit $commit | Out-Null
    $manifestPath = Join-Path $testRoot 'prelaunch-snapshot\prelaunch-snapshot.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -DateKind String
    $originalManifestJson = Get-Content -LiteralPath $manifestPath -Raw
    Assert-True ($manifest.ExpectedAssemblyInformationalVersion -ceq "1.0.0+$commit") `
        'Snapshot did not bind ProductVersion to HEAD/AssemblyInformationalVersion.'
    Assert-True (@($manifest.SymbolPairs).Count -eq 2) 'Snapshot did not attest both DLL/PDB symbol pairs.'

    $assemblyArtifact = Get-Artifact $manifest 'ISRWorldGen.dll'
    $pdbArtifact = Get-Artifact $manifest 'ISRWorldGen.pdb'
    $symbolPair = Get-SymbolPair $manifest 'ISRWorldGen.dll'
    Initialize-L02CSqliteFixtureRuntime $VintageStoryPath
    $envelope = New-L02CCommittedEnvelope
    $envelopeHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($envelope)).ToLowerInvariant()
    $envelopeBytes = $envelope.Length
    $nowUtc = [DateTimeOffset]::UtcNow
    $eventUtc = [DateTimeOffset]::new(
        $nowUtc.UtcDateTime.AddTicks(-($nowUtc.UtcDateTime.Ticks % [TimeSpan]::TicksPerSecond)),
        [TimeSpan]::Zero).AddSeconds(-10)
    $logTimestamp = Get-LocalLogTimestamp $eventUtc
    $logs = [ordered]@{
        new = @"
$logTimestamp [Notification] [isrworldgen] L00A_BOOTSTRAP modid=isrworldgen instance=1 pid=101 side=Server assembly=ISRWorldGen.dll sha256=$($assemblyArtifact.Sha256) runtime=.NET_10.0 architecture=X64
$logTimestamp [Notification] [isrworldgen] L00B_DEBUG_PROBE_READY pid=101 module=ISRWorldGen.dll pass=Terrain worldtype=standard
$logTimestamp [Notification] Entering runphase GameReady
$logTimestamp [Notification] [isrworldgen] L02C_NATIVE_PROFILE_FROZEN profile=laboratory source=new persistencewrites=2 envelopebytes=$envelopeBytes envelopesha256=$envelopeHash gatestate=Frozen gatecangenerate=true gatecallbackregistered=true publishedprofile=true dimensions=4096x256x4096 chunk=32 rules=vintagestory-1.22.7-effective-world-v1:1
$logTimestamp [Notification] Entering runphase WorldReady
$logTimestamp [Notification] L00C_INACTIVE instance=fixture reason=existing-world-without-marker
$logTimestamp [Notification] [isrworldgen] L02C_NATIVE_GATE_FROZEN profile=laboratory
$logTimestamp [Notification] [isrworldgen] L00B_COLUMN_CALLBACK chunk=(1,1)
$logTimestamp [Notification] Entering runphase RunGame
$logTimestamp [Notification] L00C_WITNESS_NO_REQUEST instance=fixture loadrequests=0 transientrequests=0 refreshpasses=0 fixturewrites=0 markers=0
$logTimestamp [Notification] L00C_DELAYED_SHUTDOWN_ARMED instance=fixture run=1 reason=inactive-witness-complete delayms=50 listener=1
$logTimestamp [Notification] L00C_DELAYED_SHUTDOWN_FIRED instance=fixture run=1 reason=inactive-witness-complete
$logTimestamp [Notification] Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API
$logTimestamp [Event] Saved savegamedata...2
$logTimestamp [Event] World saved! Saved 1 chunks, 1 mapchunks, 1 mapregions.
$logTimestamp [Event] Stopped the server!
"@
        reload = @"
$logTimestamp [Notification] [isrworldgen] L00A_BOOTSTRAP modid=isrworldgen instance=1 pid=102 side=Server assembly=ISRWorldGen.dll sha256=$($assemblyArtifact.Sha256) runtime=.NET_10.0 architecture=X64
$logTimestamp [Notification] [isrworldgen] L00B_DEBUG_PROBE_READY pid=102 module=ISRWorldGen.dll pass=Terrain worldtype=standard
$logTimestamp [Notification] Entering runphase GameReady
$logTimestamp [Notification] [isrworldgen] L02C_NATIVE_PROFILE_FROZEN profile=laboratory source=reload persistencewrites=0 envelopebytes=$envelopeBytes envelopesha256=$envelopeHash gatestate=Frozen gatecangenerate=true gatecallbackregistered=false publishedprofile=true dimensions=4096x256x4096 chunk=32 rules=vintagestory-1.22.7-effective-world-v1:1
$logTimestamp [Notification] Entering runphase WorldReady
$logTimestamp [Notification] L00C_INACTIVE instance=fixture reason=existing-world-without-marker
$logTimestamp [Notification] Entering runphase RunGame
$logTimestamp [Notification] L00C_WITNESS_NO_REQUEST instance=fixture loadrequests=0 transientrequests=0 refreshpasses=0 fixturewrites=0 markers=0
$logTimestamp [Notification] L00C_DELAYED_SHUTDOWN_ARMED instance=fixture run=1 reason=inactive-witness-complete delayms=50 listener=1
$logTimestamp [Notification] L00C_DELAYED_SHUTDOWN_FIRED instance=fixture run=1 reason=inactive-witness-complete
$logTimestamp [Notification] Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API
$logTimestamp [Event] Saved savegamedata...2
$logTimestamp [Event] World saved! Saved 1 chunks, 1 mapchunks, 1 mapregions.
$logTimestamp [Event] Stopped the server!
"@
        height = @"
$logTimestamp [Notification] [isrworldgen] L00A_BOOTSTRAP modid=isrworldgen instance=1 pid=103 side=Server assembly=ISRWorldGen.dll sha256=$($assemblyArtifact.Sha256) runtime=.NET_10.0 architecture=X64
$logTimestamp [Notification] [isrworldgen] L00B_DEBUG_PROBE_READY pid=103 module=ISRWorldGen.dll pass=Terrain worldtype=standard
$logTimestamp [Notification] Entering runphase GameReady
$logTimestamp [Error] [isrworldgen] L02C_NATIVE_PROFILE_REJECTED code=InvalidInput stage=atlas.profile.native-height source=new persistencewrites=0 envelopebytes=0 envelopesha256=none gatestate=Rejected gatecangenerate=false gatecallbackregistered=false details=height dimensions=4096x320x4096 chunk=32 rules=vintagestory-1.22.7-effective-world-v1:1
$logTimestamp [Notification] Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API
$logTimestamp [Event] Stopped the server!
"@
        rectangle = @"
$logTimestamp [Notification] [isrworldgen] L00A_BOOTSTRAP modid=isrworldgen instance=1 pid=104 side=Server assembly=ISRWorldGen.dll sha256=$($assemblyArtifact.Sha256) runtime=.NET_10.0 architecture=X64
$logTimestamp [Notification] [isrworldgen] L00B_DEBUG_PROBE_READY pid=104 module=ISRWorldGen.dll pass=Terrain worldtype=standard
$logTimestamp [Notification] Entering runphase GameReady
$logTimestamp [Error] [isrworldgen] L02C_NATIVE_PROFILE_REJECTED code=InvalidInput stage=native-profile.effective-dimensions source=new persistencewrites=0 envelopebytes=0 envelopesha256=none gatestate=Rejected gatecangenerate=false gatecallbackregistered=false details=dimensions dimensions=4096x256x8192 chunk=32 rules=vintagestory-1.22.7-effective-world-v1:1
$logTimestamp [Notification] Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API
$logTimestamp [Event] Stopped the server!
"@
    }

    $logPaths = @{}
    foreach ($caseName in @('new', 'reload', 'height', 'rectangle')) {
        $logPaths[$caseName] = Join-Path $testRoot "$caseName.log"
        Write-Utf8Fixture $logPaths[$caseName] $logs[$caseName]
    }

    $started = $eventUtc.AddSeconds(-30)
    $breakpointHit = $eventUtc.AddSeconds(-20)
    $debuggerContinue = $eventUtc.AddSeconds(-1)
    $completed = $eventUtc.AddSeconds(2)
    $observations = [ordered]@{
        Schema = 'isrworldgen.t02-05.visual-studio-campaign.v3'
        TestedCommit = $commit
        SnapshotManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        VisualStudioProfile = 'ISRWorldGen Server (isolated data)'
        DebuggerTransport = 'visual-studio-debugger'
        Provenance = 'visual-studio-debugger-session-verified-v3'
        Cases = [ordered]@{}
    }
    $pids = [ordered]@{ new = 101; reload = 102; height = 103; rectangle = 104 }
    foreach ($caseName in @('new', 'reload', 'height', 'rectangle')) {
        $observations.Cases[$caseName] = New-CaseObservation $caseName $pids[$caseName] $logPaths[$caseName] `
            $started $breakpointHit $debuggerContinue $completed $assemblyArtifact $pdbArtifact $symbolPair
    }
    $observationPath = Join-Path $testRoot 'campaign-observation.json'
    Write-JsonFixture $observationPath $observations

    $extractor = Join-Path $PSScriptRoot 'Invoke-L02CNativeSqliteExtraction.ps1'
    $databasePaths = @{}
    $extractionReports = @{}
    $sealedSources = @{}
    foreach ($caseName in @('new', 'reload', 'height', 'rectangle')) {
        $databasePaths[$caseName] = Join-Path $testRoot "database-$caseName\source.vcdbs"
        $hasEnvelope = $caseName -in @('new', 'reload')
        $geographyRows = if ($hasEnvelope) { 1 } else { 0 }
        $walDependent = $caseName -in @('height', 'rectangle')
        New-L02CSqliteSourceFixture $databasePaths[$caseName] `
            $(if ($hasEnvelope) { $envelope } else { $null }) $geographyRows $walDependent
        $extractionReports[$caseName] = Join-Path $testRoot "extraction-$caseName.json"
        $sealedSources[$caseName] = Join-Path $testRoot "sealed-$caseName"
        $caseObservation = $observations.Cases.$caseName
        try {
            & $extractor -SourceDatabasePath $databasePaths[$caseName] `
                -OutputPath $extractionReports[$caseName] -SealedSourceDirectory $sealedSources[$caseName] `
                -TestedCommit $commit -SessionId $caseObservation.SessionId -CaseRole $caseName `
                -ServerPid $caseObservation.ServerPid -LogPath $logPaths[$caseName] `
                -LogSha256 $caseObservation.LogSha256 -SnapshotManifestPath $manifestPath `
                -SnapshotManifestSha256 $observations.SnapshotManifestSha256 `
                -RepositoryRoot $RepositoryRoot -VintageStoryPath $VintageStoryPath | Out-Null
        }
        catch {
            throw "$caseName fixture extraction failed: $($_.Exception.Message)"
        }
    }
    $script:l02cExtractionReports = $extractionReports
    $script:l02cSealedSources = $sealedSources

    Invoke-Validation $oracle $testRoot $observationPath $logPaths $extractionReports $sealedSources
    $reportPath = Join-Path $testRoot 'runtime-evidence.json'
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -DateKind String
    Assert-True ($report.Status -ceq 'PASS') 'Runtime evidence self-test report did not pass.'
    Assert-True ($report.EnvelopeSha256 -ceq $envelopeHash) 'Runtime evidence self-test changed the envelope hash.'
    Assert-True ($report.Schema -ceq 'isrworldgen.t02-05.runtime-evidence.v4') `
        'Runtime evidence self-test did not emit the v4 schema.'
    Assert-True ($report.VisualStudio.Provenance -ceq 'visual-studio-debugger-session-verified-v3') `
        'Runtime evidence self-test did not retain verified Visual Studio provenance.'
    Assert-True (@($report.Cases).Count -eq 4) 'Runtime evidence self-test did not report all four cases.'
    Assert-True ($report.Cases[0].InspectionDurationMilliseconds -eq 19000 -and
        $report.Cases[0].PostContinueMarkerUpperBoundMilliseconds -eq 2000) `
        'Runtime evidence self-test did not derive the bounded inspection and post-continue windows.'
    Assert-True ($report.Cases[3].Case -ceq 'rectangle' -and
        $report.Cases[3].BreakpointId -ceq 'native-profile-rejected-before-log') `
        'Runtime evidence self-test did not fully attest the rectangle rejection.'
    Assert-True ($report.Cases[0].SQLiteExtraction.EnvelopeState -ceq 'Committed' -and
        $report.Cases[1].SQLiteExtraction.EnvelopeSha256 -ceq $report.Cases[0].SQLiteExtraction.EnvelopeSha256) `
        'Runtime evidence self-test did not bind identical new/reload SQLite envelopes.'
    Assert-True ($report.Cases[2].SQLiteExtraction.WalContribution -ceq 'RequiredForObservedState' -and
        $report.Cases[2].SQLiteExtraction.ChunkRows -eq 0) `
        'Runtime evidence self-test did not bind fail-closed WAL-backed refusal evidence.'

    $originalObservationJson = Get-Content -LiteralPath $observationPath -Raw
    $originalNewLog = $logs.new
    $originalRectangleLog = $logs.rectangle
    $originalExtractionJson = @{}
    foreach ($caseName in @('new', 'reload', 'height', 'rectangle')) {
        $originalExtractionJson[$caseName] = Get-Content -LiteralPath $extractionReports[$caseName] -Raw
    }

    $observations.Schema = 'isrworldgen.t02-05.visual-studio-campaign.v2'
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Legacy v2 campaign' 'fresh v3 campaign' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.Cases.new.PSObject.Properties.Remove('DebuggerContinueUtc')
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Absent debugger continue timestamp' 'closed schema' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.Cases.new.StartedUtc = $eventUtc.AddSeconds(-402).ToString('o')
    $observations.Cases.new.BreakpointHitUtc = $eventUtc.AddSeconds(-401).ToString('o')
    $observations.Cases.new.DebuggerContinueUtc = $eventUtc.AddSeconds(-1).ToString('o')
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Excessive interactive inspection window' 'inspection window' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.Cases.new.DebuggerContinueUtc = $eventUtc.AddSeconds(-21).ToString('o')
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Inverted breakpoint and continue order' 'timestamp order' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.Cases.new.DebuggerContinueUtc = $eventUtc.AddSeconds(-10).ToString('o')
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Stale marker after debugger continue' 'post-continue' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.Cases.new.ServerPid = 999
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'PID mismatch' 'bootstrap identity' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.Cases.reload.SessionId = $observations.Cases.new.SessionId
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Debugger session mismatch' 'session identifiers must be unique' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.Provenance = 'unverified'
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Unverified provenance' 'provenance' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.PSObject.Properties.Remove('Provenance')
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Absent provenance' 'closed schema' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.VisualStudioProfile = 'arbitrary-profile'
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Visual Studio profile mismatch' 'profile' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations | Add-Member -NotePropertyName OperatorNote -NotePropertyValue '..\..\secret-token'
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Malicious extra field' 'closed schema' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.Cases.new.CallstackFrames[0] = '..\..\arbitrary-frame'
    $observations.Cases.new.CallstackSha256 = Get-TextSha256 ($observations.Cases.new.CallstackFrames -join "`n")
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Malicious callstack field' 'callstack' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.Cases.new.Module.ProductVersion = '1.0.0+0000000000000000000000000000000000000000'
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Module version mismatch' 'ProductVersion' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $observations.Cases.new.Module.PathSha256 = '0' * 64
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Module path mismatch' 'path' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    Write-Utf8Fixture $logPaths.new ($originalNewLog.Replace([string]$assemblyArtifact.Sha256, ('F' * 64)))
    $observations.Cases.new.LogSha256 = (Get-FileHash -LiteralPath $logPaths.new -Algorithm SHA256).Hash
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Bootstrap hash mismatch' 'bootstrap' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $logPaths.new $originalNewLog
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    Write-Utf8Fixture $logPaths.new ($originalNewLog -replace '(?m)^.*L00A_BOOTSTRAP.*\r?\n', '')
    $observations.Cases.new.LogSha256 = (Get-FileHash -LiteralPath $logPaths.new -Algorithm SHA256).Hash
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Bootstrap absence' 'L00A_BOOTSTRAP' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $logPaths.new $originalNewLog
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    Write-Utf8Fixture $logPaths.new ($originalNewLog -replace '(?m)^.*L00B_COLUMN_CALLBACK.*\r?\n', '')
    $observations.Cases.new.LogSha256 = (Get-FileHash -LiteralPath $logPaths.new -Algorithm SHA256).Hash
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'New callback absence' 'L00B_COLUMN_CALLBACK' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $logPaths.new $originalNewLog
    Write-Utf8Fixture $observationPath $originalObservationJson
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String

    $staleTimestamp = '1.1.2000 00:00:00'
    $newRuntimeTimestampPattern = '(?m)^' + [regex]::Escape($logTimestamp) + '(?= .*L02C_NATIVE_PROFILE_FROZEN)'
    Write-Utf8Fixture $logPaths.new ($originalNewLog -replace $newRuntimeTimestampPattern, $staleTimestamp)
    $observations.Cases.new.LogSha256 = (Get-FileHash -LiteralPath $logPaths.new -Algorithm SHA256).Hash
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Stale log' 'timestamp' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $logPaths.new $originalNewLog
    Write-Utf8Fixture $observationPath $originalObservationJson

    $afterSessionTimestamp = Get-LocalLogTimestamp $completed.AddMinutes(1)
    Write-Utf8Fixture $logPaths.new ($originalNewLog -replace $newRuntimeTimestampPattern, $afterSessionTimestamp)
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String
    $observations.Cases.new.LogSha256 = (Get-FileHash -LiteralPath $logPaths.new -Algorithm SHA256).Hash
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Log after debugger session' 'outside the verified debugger session' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $logPaths.new $originalNewLog
    Write-Utf8Fixture $observationPath $originalObservationJson

    Write-Utf8Fixture $logPaths.rectangle ($originalRectangleLog.Replace(
        'dimensions=4096x256x8192',
        'dimensions=4096x256x4096'))
    $observations = $originalObservationJson | ConvertFrom-Json -DateKind String
    $observations.Cases.rectangle.LogSha256 = (Get-FileHash -LiteralPath $logPaths.rectangle -Algorithm SHA256).Hash
    Write-JsonFixture $observationPath $observations
    Assert-ValidationFails 'Rectangle dimensions mismatch' 'dimensions' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $logPaths.rectangle $originalRectangleLog
    Write-Utf8Fixture $observationPath $originalObservationJson

    $newExtraction = $originalExtractionJson.new | ConvertFrom-Json -DateKind String
    $newExtraction.SessionId = 'ffffffffffffffffffffffffffffffff'
    Write-JsonFixture $extractionReports.new $newExtraction
    Assert-ValidationFails 'Cross-session extraction' 'campaign session' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $extractionReports.new $originalExtractionJson.new

    $newExtraction = $originalExtractionJson.new | ConvertFrom-Json -DateKind String
    $newExtraction.SourceSetSha256 = 'F' * 64
    Write-JsonFixture $extractionReports.new $newExtraction
    Assert-ValidationFails 'Extraction hash mismatch' 'source-set hash' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $extractionReports.new $originalExtractionJson.new

    $newExtraction = $originalExtractionJson.new | ConvertFrom-Json -DateKind String
    $newExtraction.SourceMainPathSha256 = 'F' * 64
    Write-JsonFixture $extractionReports.new $newExtraction
    Assert-ValidationFails 'Contradictory main source path hash' 'main source path' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $extractionReports.new $originalExtractionJson.new

    $heightExtraction = $originalExtractionJson.height | ConvertFrom-Json -DateKind String
    $heightExtraction.Clone.WalEvidence.MainOnlyStatus = 'Readable'
    $heightExtraction.Clone.WalEvidence.MainOnlyResultSha256 = $heightExtraction.Clone.ResultSha256
    Write-JsonFixture $extractionReports.height $heightExtraction
    Assert-ValidationFails 'Required WAL with equivalent readable main' 'WAL contribution implication' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $extractionReports.height $originalExtractionJson.height

    $heightExtraction = $originalExtractionJson.height | ConvertFrom-Json -DateKind String
    $heightExtraction.Clone.WalEvidence.WalContribution = 'PresentStateEquivalent'
    $heightExtraction.Clone.WalEvidence.MainOnlyStatus = 'Unreadable'
    $heightExtraction.Clone.WalEvidence.MainOnlyResultSha256 = $null
    Write-JsonFixture $extractionReports.height $heightExtraction
    Assert-ValidationFails 'Equivalent WAL with unreadable main' 'WAL contribution implication' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $extractionReports.height $originalExtractionJson.height

    $heightExtraction = $originalExtractionJson.height | ConvertFrom-Json -DateKind String
    $differentResultHash = if ($heightExtraction.Clone.ResultSha256 -cne ('A' * 64)) { 'A' * 64 } else { 'B' * 64 }
    $heightExtraction.Clone.WalEvidence.WalContribution = 'PresentStateEquivalent'
    $heightExtraction.Clone.WalEvidence.MainOnlyStatus = 'Readable'
    $heightExtraction.Clone.WalEvidence.MainOnlyResultSha256 = $differentResultHash
    Write-JsonFixture $extractionReports.height $heightExtraction
    Assert-ValidationFails 'Equivalent WAL with different readable main' 'WAL contribution implication' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $extractionReports.height $originalExtractionJson.height

    $heightWal = Join-Path $sealedSources.height 'source.vcdbs-wal'
    $heightWalBackup = Join-Path $testRoot 'height-wal.backup'
    [IO.File]::Copy($heightWal, $heightWalBackup, $false)
    Remove-Item -LiteralPath $heightWal -Force
    Assert-ValidationFails 'Missing WAL' 'missing' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    [IO.File]::Copy($heightWalBackup, $heightWal, $false)

    $reloadMain = Join-Path $sealedSources.reload 'source.vcdbs'
    $reloadMainBackup = Join-Path $testRoot 'reload-main.backup'
    [IO.File]::Copy($reloadMain, $reloadMainBackup, $false)
    $mutationStream = [IO.File]::Open($reloadMain, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $mutationStream.WriteByte(1) }
    finally { $mutationStream.Dispose() }
    Assert-ValidationFails 'Source changed after extraction' 'hash or identity mismatch' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Remove-Item -LiteralPath $reloadMain -Force
    [IO.File]::Copy($reloadMainBackup, $reloadMain, $false)

    $oversizedExtractionSentinel = 'EXTRACTION_SECRET_SHOULD_NOT_BE_ECHOED'
    Write-OversizedFixture $extractionReports.new (256KB) $oversizedExtractionSentinel
    Assert-ValidationFails 'Oversized extraction report' 'SQLite extraction report exceeds maximum' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath $oversizedExtractionSentinel
    Write-Utf8Fixture $extractionReports.new $originalExtractionJson.new

    Add-Content -LiteralPath $logPaths.new -Value 'arbitrary stale content'
    Assert-ValidationFails 'Arbitrary log mutation' 'log SHA-256' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath
    Write-Utf8Fixture $logPaths.new $originalNewLog

    $oversizedLogSentinel = 'LOG_SECRET_SHOULD_NOT_BE_ECHOED'
    Write-OversizedFixture $logPaths.new (16MB) $oversizedLogSentinel
    Assert-ValidationFails 'Oversized log' 'new log exceeds maximum' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath $oversizedLogSentinel
    Write-Utf8Fixture $logPaths.new $originalNewLog

    $oversizedManifestSentinel = 'MANIFEST_SECRET_SHOULD_NOT_BE_ECHOED'
    Write-OversizedFixture $manifestPath (64KB) $oversizedManifestSentinel
    Assert-ValidationFails 'Oversized manifest' 'prelaunch snapshot manifest exceeds maximum' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath $oversizedManifestSentinel
    Write-Utf8Fixture $manifestPath $originalManifestJson

    $oversizedCampaignSentinel = 'CAMPAIGN_SECRET_SHOULD_NOT_BE_ECHOED'
    Write-OversizedFixture $observationPath (64KB) $oversizedCampaignSentinel
    Assert-ValidationFails 'Oversized campaign' 'campaign observation exceeds maximum' `
        { Invoke-Validation $oracle $testRoot $observationPath $logPaths } $reportPath $oversizedCampaignSentinel
    Write-Utf8Fixture $observationPath $originalObservationJson

    $duplicateFailed = $false
    try {
        & $oracle -Phase Snapshot -EvidenceRoot $testRoot -RepositoryRoot $RepositoryRoot `
            -VintageStoryPath $VintageStoryPath -Configuration $Configuration -ExpectedCommit $commit | Out-Null
    }
    catch {
        $duplicateFailed = $true
    }
    Assert-True $duplicateFailed 'CreateNew snapshot unexpectedly allowed evidence replacement.'

    $pdbMismatchRoot = Join-Path $tempBase ("isrworldgen-l02c-pdb-mismatch-" + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($pdbMismatchRoot) | Out-Null
    & $oracle -Phase Snapshot -EvidenceRoot $pdbMismatchRoot -RepositoryRoot $RepositoryRoot `
        -VintageStoryPath $VintageStoryPath -Configuration $Configuration -ExpectedCommit $commit | Out-Null
    [IO.File]::Copy(
        (Join-Path $pdbMismatchRoot 'prelaunch-snapshot\ISRWorldGen.Core.pdb'),
        (Join-Path $pdbMismatchRoot 'prelaunch-snapshot\ISRWorldGen.pdb'),
        $true)
    Assert-ValidationFails 'PDB mismatch' 'PDB pairing' `
        { Invoke-Validation $oracle $pdbMismatchRoot $observationPath $logPaths } `
        (Join-Path $pdbMismatchRoot 'runtime-evidence.json')

    [ordered]@{
        TestId = 'T02-05-RUNTIME-EVIDENCE-ORACLE'
        Status = 'PASS'
        Configuration = $Configuration
        CreateNewReplacementRejected = $duplicateFailed
        NegativeCases = 34
        BootstrapModuleBinding = $true
        PortablePdbPairing = $true
        VisualStudioProvenance = 'visual-studio-debugger-session-verified-v3'
        SQLiteExtraction = 'sealed-source-clone-only-v1'
    } | ConvertTo-Json -Depth 4
}
finally {
    foreach ($candidate in @($testRoot, $pdbMismatchRoot)) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $resolved = [IO.Path]::GetFullPath($candidate)
        $leaf = Split-Path -Leaf $resolved
        if (-not $resolved.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -or
            ($leaf -notmatch '^isrworldgen-l02c-(runtime-evidence|pdb-mismatch)-[0-9a-f]{32}$')) {
            throw "Refusing to clean unexpected runtime evidence self-test path: $resolved"
        }

        if (Test-Path -LiteralPath $resolved) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
