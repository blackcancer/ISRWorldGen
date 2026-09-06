[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
    throw "L00-C evidence is missing: $EvidencePath"
}

$evidencePathResolved = (Resolve-Path -LiteralPath $EvidencePath).Path
$evidenceRoot = Split-Path -Parent $evidencePathResolved
$evidence = Get-Content -LiteralPath $evidencePathResolved -Raw | ConvertFrom-Json

function Assert-Equal {
    param($Actual, $Expected, [string]$Label)
    if ($Actual -ne $Expected) {
        throw "$Label expected '$Expected' but was '$Actual'."
    }
}

function Resolve-EvidenceFile {
    param([string]$RelativePath, [string]$Label)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or $RelativePath -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "$Label must be a safe relative evidence path."
    }

    $root = [IO.Path]::GetFullPath($evidenceRoot).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath((Join-Path $evidenceRoot $RelativePath))
    if (-not $resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label escapes the evidence root."
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "$Label is missing: $resolved"
    }

    return $resolved
}

function Assert-Artifact {
    param($Entry, [string]$Label)

    $path = Resolve-EvidenceFile ([string]$Entry.Path) $Label
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $length = (Get-Item -LiteralPath $path).Length
    Assert-Equal $hash ([string]$Entry.Sha256) "$Label SHA256"
    Assert-Equal $length ([long]$Entry.Length) "$Label length"
    return $path
}

if ([string]$evidence.TestedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'TestedCommit must be a full lowercase Git commit id.'
}
& git -C $RepositoryRoot cat-file -e "$($evidence.TestedCommit)^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "TestedCommit is unavailable locally: $($evidence.TestedCommit)"
}

foreach ($testName in @('T00-04', 'T00-05')) {
    $test = $evidence.Tests.$testName
    if ($null -eq $test) { throw "Missing evidence section: $testName" }
    Assert-Equal $test.Status 'PASS' "$testName status"
}

$lifecycle = $evidence.Tests.'T00-06'
if ($null -eq $lifecycle) { throw 'Missing evidence section: T00-06' }
if ($lifecycle.Status -notin @('PASS', 'BLOCKED')) {
    throw "T00-06 status must be PASS or BLOCKED, not '$($lifecycle.Status)'."
}
Assert-Equal $lifecycle.ServerLifecycleStatus 'PASS' 'T00-06 server lifecycle status'
if ($lifecycle.Status -eq 'BLOCKED') {
    Assert-Equal $lifecycle.ClientMenuStatus 'NOT_RUN' 'T00-06 client menu status'
    if ([string]::IsNullOrWhiteSpace([string]$lifecycle.Blocker)) {
        throw 'A blocked T00-06 result must retain its exact client-menu blocker.'
    }
}

Assert-Equal ([int]$evidence.Parameters.RockBlockId) 11165 'Rock block id'
Assert-Equal ([int]$evidence.Parameters.FreshBlockId) 2966 'Fresh-water block id'
Assert-Equal ([int]$evidence.Parameters.SaltBlockId) 2888 'Salt-water block id'
Assert-Equal ([int]$evidence.Parameters.SolidCount) 67022 'Fixture solid count'
Assert-Equal ([int]$evidence.Parameters.FluidCount) 2610 'Fixture fluid count'
Assert-Equal ([int]$evidence.Parameters.FreshCount) 1350 'Fixture fresh-fluid count'
Assert-Equal ([int]$evidence.Parameters.SaltCount) 1260 'Fixture salt-fluid count'
Assert-Equal ([int]$evidence.Parameters.ClientLaunchCount) 0 'Client launch count'

$dll = Assert-Artifact $evidence.Artifacts.Assembly 'Assembly'
$pdb = Assert-Artifact $evidence.Artifacts.Symbols 'Symbols'
$debuggerInspectionPath = Assert-Artifact $evidence.Artifacts.DebuggerInspection 'Debugger inspection'
$artifactDirectoryName = Split-Path -Leaf (Split-Path -Parent $dll)
Assert-Equal $artifactDirectoryName $evidence.TestedCommit 'Assembly artifact directory'
Assert-Equal (Split-Path -Parent $pdb) (Split-Path -Parent $dll) 'Assembly/symbol directory'

$expectedBeforeInventory = '0=[null];1=[0:Vintagestory.ServerMods.GenTerra::OnChunkColumnGen,1:Vintagestory.ServerMods.GenRockStrataNew::GenChunkColumn,2:ISRWorldGen.L00BDebugProbeModSystem::OnChunkColumnGeneration,3:Vintagestory.ServerMods.GenCaves::GenChunkColumn,4:Vintagestory.ServerMods.GenDevastationLayer::OnChunkColumnGeneration,5:Vintagestory.ServerMods.GenBlockLayers::OnChunkColumnGeneration];2=[0:Vintagestory.ServerMods.GenTerraPostProcess::OnChunkColumnGen,1:Vintagestory.ServerMods.GenHotSprings::GenChunkColumn,2:Vintagestory.ServerMods.GenDungeons::onChunkColumnGen,3:Vintagestory.ServerMods.GenDeposits::GenChunkColumn,4:Vintagestory.ServerMods.GenStructures::OnChunkColumnGen,5:Vintagestory.ServerMods.GenPonds::OnChunkColumnGen,6:Vintagestory.ServerMods.GenStructures::OnChunkColumnGenPostPass];3=[0:Vintagestory.GameContent.GenStoryStructures::OnChunkColumnGen,1:Vintagestory.ServerMods.GenVegetationAndPatches::OnChunkColumnGen,2:Vintagestory.ServerMods.GenRivulets::OnChunkColumnGen,3:Vintagestory.ServerMods.GenLightSurvival::OnChunkColumnGeneration];4=[0:Vintagestory.ServerMods.GenSnowLayer::OnChunkColumnGen,1:Vintagestory.ServerMods.GenLightSurvival::OnChunkColumnGenerationFlood];5=[0:Vintagestory.ServerMods.GenCreatures::OnChunkColumnGen]'
$expectedAfterInventory = '0=[null];1=[0:L00CWrapper(original=Vintagestory.ServerMods.GenTerra::OnChunkColumnGen@index=0),1:L00CWrapper(original=Vintagestory.ServerMods.GenRockStrataNew::GenChunkColumn@index=1),2:ISRWorldGen.L00BDebugProbeModSystem::OnChunkColumnGeneration,3:L00CWrapper(original=Vintagestory.ServerMods.GenCaves::GenChunkColumn@index=3),4:L00CWrapper(original=Vintagestory.ServerMods.GenDevastationLayer::OnChunkColumnGeneration@index=4),5:L00CWrapper(original=Vintagestory.ServerMods.GenBlockLayers::OnChunkColumnGeneration@index=5)];2=[0:L00CWrapper(original=Vintagestory.ServerMods.GenTerraPostProcess::OnChunkColumnGen@index=0),1:L00CWrapper(original=Vintagestory.ServerMods.GenHotSprings::GenChunkColumn@index=1),2:L00CWrapper(original=Vintagestory.ServerMods.GenDungeons::onChunkColumnGen@index=2),3:L00CWrapper(original=Vintagestory.ServerMods.GenDeposits::GenChunkColumn@index=3),4:L00CWrapper(original=Vintagestory.ServerMods.GenStructures::OnChunkColumnGen@index=4),5:L00CWrapper(original=Vintagestory.ServerMods.GenPonds::OnChunkColumnGen@index=5),6:L00CWrapper(original=Vintagestory.ServerMods.GenStructures::OnChunkColumnGenPostPass@index=6)];3=[0:L00CWrapper(original=Vintagestory.GameContent.GenStoryStructures::OnChunkColumnGen@index=0),1:L00CWrapper(original=Vintagestory.ServerMods.GenVegetationAndPatches::OnChunkColumnGen@index=1),2:L00CWrapper(original=Vintagestory.ServerMods.GenRivulets::OnChunkColumnGen@index=2),3:L00CFinalizer(before=Vintagestory.ServerMods.GenLightSurvival::OnChunkColumnGeneration@index=3),4:Vintagestory.ServerMods.GenLightSurvival::OnChunkColumnGeneration];4=[0:L00CWrapper(original=Vintagestory.ServerMods.GenSnowLayer::OnChunkColumnGen@index=0),1:Vintagestory.ServerMods.GenLightSurvival::OnChunkColumnGenerationFlood];5=[0:Vintagestory.ServerMods.GenCreatures::OnChunkColumnGen]'

$sessionLogPaths = @()
foreach ($session in @($evidence.Sessions)) {
    if ([int]$session.ProcessId -le 0) { throw 'Every session must record a real process id.' }
    if ([string]$session.InstanceId -notmatch '^[0-9a-f]{32}$') { throw 'Every session must record a 32-character instance id.' }
    if ($session.WorldRole -like 'activated-*' -and [string]$session.MarkerId -notmatch '^[0-9a-f]{32}$') {
        throw 'Every activated session must record a 32-character marker id.'
    }
    if ($session.WorldRole -notlike 'activated-*' -and $session.MarkerId -ne 'none') {
        throw 'Inactive and missing-handler sessions must explicitly record MarkerId=none.'
    }

    $sessionLogPath = Assert-Artifact $session.ServerMainLog "Session $($session.Cycle) server-main.log"
    [void](Assert-Artifact $session.ServerWorldgenLog "Session $($session.Cycle) server-worldgen.log")
    $sessionLogPaths += $sessionLogPath
    $log = Get-Content -LiteralPath $sessionLogPath -Raw
    $instance = [regex]::Escape([string]$session.InstanceId)
    $pidPattern = [regex]::Escape([string]$session.ProcessId)
    $savegame = [regex]::Escape([string]$session.SavegameIdentifier)
    if ($log -notmatch "L00C_PROBE_READY instance=$instance pid=$pidPattern ") {
        throw "Session $($session.Cycle) log does not correlate instance and process id."
    }
    if ($log -notmatch "L00C_HANDLERS phase=before instance=$instance save=$savegame ") {
        throw "Session $($session.Cycle) log does not correlate the savegame identifier."
    }

    if ($session.WorldRole -eq 'disabled-witness') {
        Assert-Equal $session.GracefulShutdown $true "Session $($session.Cycle) graceful shutdown"
        if ($log -notmatch "L00C_INACTIVE instance=$instance " -or $log -notmatch "L00C_WITNESS_LOADED instance=$instance ") {
            throw 'Disabled witness log is missing its inactive or inspection marker.'
        }
        if ($log -match "L00C_FIXTURE_WRITTEN instance=$instance " -or $log -match "L00C_ACTIVATED instance=$instance ") {
            throw 'Disabled witness unexpectedly ran the L00-C fixture writer.'
        }
    }
    elseif ($session.WorldRole -eq 'missing-handler') {
        if ($log -notmatch "L00C_ERROR code=expected-handler-absent instance=$instance ") {
            throw 'Missing-handler session lacks the explicit expected-handler-absent error.'
        }
        if ($log -match "L00C_ACTIVATED instance=$instance " -or $log -match "L00C_FIXTURE_WRITTEN instance=$instance ") {
            throw 'Missing-handler session silently activated or wrote the fixture.'
        }
    }
    elseif ($session.WorldRole -like 'activated-*') {
        Assert-Equal $session.GracefulShutdown $true "Session $($session.Cycle) graceful shutdown"
        $marker = [regex]::Escape([string]$session.MarkerId)
        $open = [regex]::Escape([string]$session.OpenCount)
        $isNew = ([string]$session.IsNew).ToLowerInvariant()
        $requiredPatterns = @(
            "L00C_HANDLERS phase=before instance=$instance ",
            "L00C_HANDLERS phase=after instance=$instance ",
            "L00C_ACTIVATED instance=$instance marker=$marker open=$open isnew=$isNew ",
            "L00C_FIXTURE_INSPECTED instance=$instance marker=$marker phase=loaded ",
            "L00C_FIXTURE_INSPECTED instance=$instance marker=$marker phase=afterticks ",
            "L00C_HALO_VALID instance=$instance marker=$marker phase=loaded radius=1 columns=8 ",
            "L00C_HALO_VALID instance=$instance marker=$marker phase=afterticks radius=1 columns=8 ",
            "L00C_HALO_STABLE instance=$instance marker=$marker ticks=40 columns=8 ",
            "L00C_TICKS_STABLE instance=$instance marker=$marker ticks=40 ",
            "L00C_GRACEFUL_SHUTDOWN_REQUEST instance=$instance marker=$marker ",
            'Forced: Shutdown through Server API',
            'World saved!'
        )
        foreach ($pattern in $requiredPatterns) {
            if ($log -notmatch $pattern) {
                throw "Activated session $($session.Cycle) is missing log pattern: $pattern"
            }
        }
        if ($log -notmatch "L00C_TICKS_STABLE instance=$instance .* unexpected=0") {
            throw "Activated session $($session.Cycle) did not preserve the exact fixture after bounded ticks."
        }

        $beforeLine = [regex]::Match($log, "(?m)^.*L00C_HANDLERS phase=before instance=$instance .*$").Value
        $afterLine = [regex]::Match($log, "(?m)^.*L00C_HANDLERS phase=after instance=$instance .*$").Value
        if ($beforeLine -notmatch 'staleprobe=0' -or $beforeLine -match 'ISRWorldGen\.WorldgenProbe\.L00CWorldgenProbeModSystem') {
            throw "Activated session $($session.Cycle) retained a prior L00-C delegate before installation."
        }
        $beforeInventory = [regex]::Match($beforeLine, ' column=(.*)$').Groups[1].Value.TrimEnd("`r")
        $afterInventory = [regex]::Match($afterLine, ' column=(.*)$').Groups[1].Value.TrimEnd("`r")
        Assert-Equal $beforeInventory $expectedBeforeInventory "Session $($session.Cycle) exact inventory before installation"
        Assert-Equal $afterInventory $expectedAfterInventory "Session $($session.Cycle) exact per-delegate wrapper inventory"

        $loaded = [regex]::Match($log, "L00C_FIXTURE_INSPECTED instance=$instance marker=$marker phase=loaded .* snapshot=([0-9A-F]{64}) .* unexpected=0")
        $afterTicks = [regex]::Match($log, "L00C_FIXTURE_INSPECTED instance=$instance marker=$marker phase=afterticks .* snapshot=([0-9A-F]{64}) .* unexpected=0")
        if (-not $loaded.Success -or -not $afterTicks.Success) {
            throw "Activated session $($session.Cycle) lacks exact loaded/afterticks snapshots."
        }
        Assert-Equal $loaded.Groups[1].Value $afterTicks.Groups[1].Value "Session $($session.Cycle) tick-stable full snapshot"
        Assert-Equal $loaded.Groups[1].Value ([string]$evidence.Parameters.FixtureSnapshotSha256) "Session $($session.Cycle) canonical full snapshot"
        $haloLoaded = [regex]::Match($log, "L00C_HALO_VALID instance=$instance marker=$marker phase=loaded radius=1 columns=8 snapshot=([0-9A-F]{64})")
        $haloAfterTicks = [regex]::Match($log, "L00C_HALO_VALID instance=$instance marker=$marker phase=afterticks radius=1 columns=8 snapshot=([0-9A-F]{64})")
        Assert-Equal $haloLoaded.Groups[1].Value $haloAfterTicks.Groups[1].Value "Session $($session.Cycle) valid/stable first-ring snapshot"
        if ($log -notmatch "L00C_FIXTURE_INSPECTED instance=$instance .* solids=67022 fluids=2610 fresh=1350 salt=1260 .* ymax=67 unexpected=0") {
            throw "Activated session $($session.Cycle) does not retain the expected distinct block IDs/counts and height metadata."
        }

        $writeCount = [regex]::Matches($log, "L00C_FIXTURE_WRITTEN instance=$instance marker=$marker ").Count
        $expectedWrites = if ([bool]$session.IsNew) { 2 } else { 0 }
        Assert-Equal $writeCount $expectedWrites "Session $($session.Cycle) fixture write count"
        $expectedCallbacks = if ([bool]$session.IsNew) { 1 } else { 0 }
        if ($log -notmatch "L00C_DISPOSED instance=$instance removedowned=17 restorednative=16 exact=True callbacks=$expectedCallbacks ") {
            throw "Activated session $($session.Cycle) disposal did not prove exact delegate cleanup/callback ownership."
        }
        $restoreLine = [regex]::Match($log, "(?m)^.*L00C_RESTORE_RESULT reason=dispose instance=$instance removedowned=17 restorednative=16 exact=True inventory=(.*)$")
        if (-not $restoreLine.Success) {
            throw "Activated session $($session.Cycle) lacks its exact post-restoration inventory."
        }
        Assert-Equal $restoreLine.Groups[1].Value.TrimEnd("`r") $expectedBeforeInventory "Session $($session.Cycle) inventory after restoration"

        if ([bool]$session.IsNew) {
            $preLighting = [regex]::Match($log, "L00C_FIXTURE_INSPECTED instance=$instance marker=$marker phase=prelighting .* snapshot=([0-9A-F]{64}) .* unexpected=0")
            $postLighting = [regex]::Match($log, "L00C_LIGHTING_STABLE instance=$instance marker=$marker prelighting=([0-9A-F]{64}) postlighting=([0-9A-F]{64})")
            if (-not $preLighting.Success -or -not $postLighting.Success) {
                throw "New-world session $($session.Cycle) lacks its pre/post-lighting proof."
            }
            Assert-Equal $preLighting.Groups[1].Value $loaded.Groups[1].Value "Session $($session.Cycle) pre-lighting/full loaded snapshot"
            Assert-Equal $postLighting.Groups[1].Value $postLighting.Groups[2].Value "Session $($session.Cycle) lighting-stable snapshot"
            if ($log -notmatch "L00C_HANDLER_SUPPRESSED instance=$instance marker=$marker scope=halo reason=cross-column-write-guard .* target=Vintagestory\.ServerMods\.GenVegetationAndPatches method=OnChunkColumnGen ") {
                throw "New-world session $($session.Cycle) did not guard the observed cross-column loose-stone writer."
            }
            if ($log -notmatch "L00C_HALO_NATIVE_FORWARD instance=$instance marker=$marker reason=local-only-handler .* target=Vintagestory\.ServerMods\.GenTerra method=OnChunkColumnGen ") {
                throw "New-world session $($session.Cycle) did not preserve native base-terrain generation in the halo."
            }
        }
    }
    else {
        throw "Unknown WorldRole: $($session.WorldRole)"
    }

    Assert-Equal $session.DebuggerFinalMode 'Design' "Session $($session.Cycle) debugger final mode"
    Assert-Equal $session.ProcessAbsent $true "Session $($session.Cycle) process absent"
}

$allSessions = @($evidence.Sessions)
for ($index = 0; $index -lt $allSessions.Count; $index++) {
    $currentLog = Get-Content -LiteralPath $sessionLogPaths[$index] -Raw
    for ($otherIndex = 0; $otherIndex -lt $allSessions.Count; $otherIndex++) {
        if ($otherIndex -eq $index) { continue }
        $foreignInstance = [regex]::Escape([string]$allSessions[$otherIndex].InstanceId)
        if ($currentLog -match "instance=$foreignInstance") {
            throw "Session $($allSessions[$index].Cycle) log retained foreign delegate instance $($allSessions[$otherIndex].InstanceId)."
        }
    }
}

$debuggerInspection = Get-Content -LiteralPath $debuggerInspectionPath -Raw | ConvertFrom-Json
Assert-Equal $debuggerInspection.TestedCommit $evidence.TestedCommit 'Debugger inspection commit'
Assert-Equal ([int]$debuggerInspection.ProcessId) 55992 'Debugger inspection process'
Assert-Equal $debuggerInspection.InstanceId 'a292390ee34844a7b8f040ee30874303' 'Debugger inspection instance'
Assert-Equal $debuggerInspection.ModuleSha256 $evidence.Artifacts.Assembly.Sha256 'Debugger module SHA256'
Assert-Equal $debuggerInspection.PdbSha256 $evidence.Artifacts.Symbols.Sha256 'Debugger PDB SHA256'
Assert-Equal $debuggerInspection.Loaded.Phase 'loaded' 'Debugger loaded phase'
Assert-Equal $debuggerInspection.AfterTicks.Phase 'afterticks' 'Debugger afterticks phase'
Assert-Equal $debuggerInspection.Loaded.UnexpectedCount 0 'Debugger loaded unexpected count'
Assert-Equal $debuggerInspection.AfterTicks.UnexpectedCount 0 'Debugger afterticks unexpected count'
Assert-Equal $debuggerInspection.Loaded.SolidCount 67022 'Debugger loaded solid count'
Assert-Equal $debuggerInspection.Loaded.FluidCount 2610 'Debugger loaded fluid count'
Assert-Equal $debuggerInspection.Loaded.FreshCount 1350 'Debugger loaded fresh count'
Assert-Equal $debuggerInspection.Loaded.SaltCount 1260 'Debugger loaded salt count'
Assert-Equal $debuggerInspection.Coordinates.FreshWater.ActualBlockId 2966 'Debugger fresh-water block id'
Assert-Equal $debuggerInspection.Coordinates.SaltWater.ActualBlockId 2888 'Debugger salt-water block id'
Assert-Equal $debuggerInspection.Coordinates.Rock.ActualBlockId 11165 'Debugger rock block id'

$witnessSessions = @($evidence.Sessions | Where-Object { $_.WorldRole -eq 'disabled-witness' })
if ($witnessSessions.Count -ne 1) { throw "Expected one disabled witness session, found $($witnessSessions.Count)." }
$missingSessions = @($evidence.Sessions | Where-Object { $_.WorldRole -eq 'missing-handler' })
if ($missingSessions.Count -ne 1) { throw "Expected one missing-handler session, found $($missingSessions.Count)." }

$cycleSessions = @($evidence.Sessions | Where-Object { $_.WorldRole -eq 'activated-primary' })
if ($cycleSessions.Count -ne 5) {
    throw "T00-06 requires exactly five activated-primary cycles, found $($cycleSessions.Count)."
}

$orderedPrimary = @($cycleSessions | Sort-Object {[int]$_.Cycle})
for ($index = 0; $index -lt $orderedPrimary.Count; $index++) {
    Assert-Equal ([int]$orderedPrimary[$index].OpenCount) ($index + 1) "Primary cycle $($index + 1) open count"
    Assert-Equal ([bool]$orderedPrimary[$index].IsNew) ($index -eq 0) "Primary cycle $($index + 1) IsNew"
}

$primaryMarkers = @($cycleSessions | Select-Object -ExpandProperty MarkerId -Unique)
if ($primaryMarkers.Count -ne 1) {
    throw 'The five primary cycles must reload the same persistent marker.'
}

$processIds = @($evidence.Sessions | Select-Object -ExpandProperty ProcessId -Unique)
if ($processIds.Count -ne @($evidence.Sessions).Count) {
    throw 'Each lifecycle session must have a distinct process id.'
}

$second = @($evidence.Sessions | Where-Object { $_.WorldRole -eq 'activated-secondary' })
if ($second.Count -ne 1 -or $second[0].MarkerId -eq $primaryMarkers[0]) {
    throw 'The second world must have one distinct persistent marker.'
}
Assert-Equal ([int]$second[0].OpenCount) 1 'Second-world open count'
Assert-Equal ([bool]$second[0].IsNew) $true 'Second-world IsNew'

$assemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
Assert-Equal $assemblyVersion "1.0.0+$($evidence.TestedCommit)" 'Assembly ProductVersion'
if ((Get-Content -LiteralPath $pdb -AsByteStream -TotalCount 4 | ForEach-Object { $_.ToString('X2') }) -join '' -ne '42534A42') {
    throw 'Symbols do not contain the expected portable PDB header.'
}

$result = [ordered]@{
    TestId = 'L00-C-EVIDENCE-CHECK'
    Status = 'PASS'
    Utc = [DateTime]::UtcNow.ToString('o')
    TestedCommit = $evidence.TestedCommit
    SessionCount = @($evidence.Sessions).Count
    DistinctProcessCount = $processIds.Count
    PrimaryMarker = $primaryMarkers[0]
    SecondaryMarker = $second[0].MarkerId
    Assembly = $dll
    Symbols = $pdb
}

$result | ConvertTo-Json -Depth 5
