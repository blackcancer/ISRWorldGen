[CmdletBinding()]
param(
    [string] $RepositoryRoot = "",
    [string] $OutputDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $RepositoryRoot "artifacts\test-results\L02B\process"
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$commit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch "^[0-9a-f]{40}$") {
    throw "Unable to resolve exact Git commit."
}

$priorOutput = [Environment]::GetEnvironmentVariable("ISRW_L02B_PROBE_OUTPUT", "Process")
$priorAllocationOutput = [Environment]::GetEnvironmentVariable("ISRW_L02B_ALLOCATION_OUTPUT", "Process")
$priorCommit = [Environment]::GetEnvironmentVariable("ISRW_L02B_COMMIT", "Process")
$reports = @()
$allocationReports = @()
$runs = @()
try {
    [Environment]::SetEnvironmentVariable("ISRW_L02B_COMMIT", $commit, "Process")
    for ($index = 1; $index -le 2; $index++) {
        $reportPath = Join-Path $OutputDirectory ("process-{0}.json" -f $index)
        $trxName = "process-{0}.trx" -f $index
        [Environment]::SetEnvironmentVariable("ISRW_L02B_PROBE_OUTPUT", $reportPath, "Process")
        [Environment]::SetEnvironmentVariable("ISRW_L02B_ALLOCATION_OUTPUT", $null, "Process")
        & dotnet test (Join-Path $RepositoryRoot "testsrc\WorldGen.Tests\WorldGen.Tests.csproj") `
            -c Release --no-build --no-restore `
            --filter "FullyQualifiedName=ISRWorldGen.Tests.L02B.SpatialIndexProcessProbeTests.DeterminismProcessProbe" `
            --logger ("trx;LogFileName={0}" -f $trxName) `
            --results-directory $OutputDirectory
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $reportPath)) {
            throw "L02-B determinism process probe $index did not produce a passing report."
        }

        [xml] $trx = Get-Content -LiteralPath (Join-Path $OutputDirectory $trxName) -Raw
        $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
        if ($null -eq $counters -or [int]$counters.total -ne 1 -or [int]$counters.passed -ne 1) {
            throw "L02-B determinism process probe $index did not discover and pass exactly one test."
        }

        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $reports += $report

        $allocationReportPath = Join-Path $OutputDirectory ("allocation-{0}.json" -f $index)
        $allocationTrxName = "allocation-{0}.trx" -f $index
        [Environment]::SetEnvironmentVariable("ISRW_L02B_PROBE_OUTPUT", $null, "Process")
        [Environment]::SetEnvironmentVariable("ISRW_L02B_ALLOCATION_OUTPUT", $allocationReportPath, "Process")
        & dotnet test (Join-Path $RepositoryRoot "testsrc\WorldGen.Tests\WorldGen.Tests.csproj") `
            -c Release --no-build --no-restore `
            --filter "FullyQualifiedName=ISRWorldGen.Tests.L02B.SpatialIndexProcessProbeTests.DedicatedAllocationProcessProbe" `
            --logger ("trx;LogFileName={0}" -f $allocationTrxName) `
            --results-directory $OutputDirectory
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $allocationReportPath)) {
            throw "L02-B dedicated allocation process probe $index did not produce a passing report."
        }

        [xml] $allocationTrx = Get-Content -LiteralPath (Join-Path $OutputDirectory $allocationTrxName) -Raw
        $allocationCounters = $allocationTrx.SelectSingleNode("//*[local-name()='Counters']")
        if ($null -eq $allocationCounters -or [int]$allocationCounters.total -ne 1 -or [int]$allocationCounters.passed -ne 1) {
            throw "L02-B dedicated allocation process probe $index did not discover and pass exactly one test."
        }

        $allocationReport = Get-Content -LiteralPath $allocationReportPath -Raw | ConvertFrom-Json
        $allocationReports += $allocationReport
        $runs += [pscustomobject][ordered]@{
            index = $index
            determinismProcessId = $report.processId
            determinismTrx = Join-Path $OutputDirectory $trxName
            determinismReport = $reportPath
            allocationProcessId = $allocationReport.processId
            allocationTrx = Join-Path $OutputDirectory $allocationTrxName
            allocationReport = $allocationReportPath
            cumulativeAllocatedBytes = $allocationReport.cumulativeAllocatedBytes
            liveManagedBytesDelta = $allocationReport.liveManagedBytesDelta
            denseRefusalAllocatedBytes = $allocationReport.rejectedDenseRefusalAllocatedBytes
            largeInputRefusalAllocatedBytes = $allocationReport.largeInputRefusalAllocatedBytes
            manyPrimitiveEstimateAllocatedBytes = $allocationReport.manyPrimitiveEstimateAllocatedBytes
            manyPrimitiveBuildAllocatedBytes = $allocationReport.manyPrimitiveBuildAllocatedBytes
            maximumPrimitiveEstimateAllocatedBytes = $allocationReport.maximumPrimitiveEstimateAllocatedBytes
            maximumPrimitiveBuildAllocatedBytes = $allocationReport.maximumPrimitiveBuildAllocatedBytes
            precomputedGeometryEstimateAllocatedBytes = $allocationReport.precomputedGeometryEstimateAllocatedBytes
            precomputedGeometryBuildAllocatedBytes = $allocationReport.precomputedGeometryBuildAllocatedBytes
            coldGeometryEstimateAllocatedBytes = $allocationReport.coldGeometryEstimateAllocatedBytes
            coldGeometryBuildAllocatedBytes = $allocationReport.coldGeometryBuildAllocatedBytes
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable("ISRW_L02B_PROBE_OUTPUT", $priorOutput, "Process")
    [Environment]::SetEnvironmentVariable("ISRW_L02B_ALLOCATION_OUTPUT", $priorAllocationOutput, "Process")
    [Environment]::SetEnvironmentVariable("ISRW_L02B_COMMIT", $priorCommit, "Process")
}

$sameQualification =
    $reports[0].commit -eq $reports[1].commit -and
    $reports[0].framework -eq $reports[1].framework -and
    $reports[0].architecture -eq $reports[1].architecture -and
    $reports[0].algorithmVersion -eq $reports[1].algorithmVersion -and
    $reports[0].snapshotSchemaVersion -eq $reports[1].snapshotSchemaVersion -and
    $reports[0].configHash -eq $reports[1].configHash
$samePlan =
    $reports[0].estimatedCanonicalCaptureBytes -eq $reports[1].estimatedCanonicalCaptureBytes -and
    $reports[0].estimatedSnapshotBytes -eq $reports[1].estimatedSnapshotBytes -and
    $reports[0].estimatedPeakBuildBytes -eq $reports[1].estimatedPeakBuildBytes -and
    $reports[0].estimatedGeometryCacheBytes -eq 0 -and
    $reports[1].estimatedGeometryCacheBytes -eq 0 -and
    $reports[0].parallelEstimatedGeometryCacheBytes -gt 0 -and
    $reports[0].parallelEstimatedGeometryCacheBytes -eq $reports[1].parallelEstimatedGeometryCacheBytes
$sameSnapshot = $reports[0].contentHash -eq $reports[1].contentHash
$sameParallelSnapshot = $reports[0].parallelContentHash -eq $reports[1].parallelContentHash
$sameOwnershipSnapshot =
    $reports[0].ownershipContentHash -eq $reports[1].ownershipContentHash -and
    $reports[0].ownerId -eq $reports[1].ownerId -and
    ($reports[0].ownerIds -join ",") -eq ($reports[1].ownerIds -join ",") -and
    $reports[0].publishedStableIdCount -eq $reports[0].uniquePublishedStableIdCount -and
    $reports[1].publishedStableIdCount -eq $reports[1].uniquePublishedStableIdCount -and
    $reports[0].ownerTile.X -eq $reports[1].ownerTile.X -and
    $reports[0].ownerTile.Z -eq $reports[1].ownerTile.Z -and
    $reports[0].northeastQueryCount -eq 2 -and
    $reports[1].northeastQueryCount -eq 2
$sameDenseRefusal =
    $reports[0].rejectedDenseProbe.requestedSites -eq 256 -and
    $reports[1].rejectedDenseProbe.requestedSites -eq 256 -and
    $reports[0].rejectedDenseProbe.estimatedPeakBuildBytes -eq $reports[1].rejectedDenseProbe.estimatedPeakBuildBytes -and
    $reports[0].rejectedDenseProbe.failureCode -eq "BudgetExceeded" -and
    $reports[1].rejectedDenseProbe.failureCode -eq "BudgetExceeded" -and
    $reports[0].rejectedDenseProbe.failureStage -eq "atlas.spatial-index.budget" -and
    $reports[1].rejectedDenseProbe.failureStage -eq "atlas.spatial-index.budget" -and
    -not $reports[0].rejectedDenseProbe.snapshotVisible -and
    -not $reports[1].rejectedDenseProbe.snapshotVisible
$sameLaboratory64 =
    $reports[0].laboratory64.requestedSites -eq 64 -and
    $reports[1].laboratory64.requestedSites -eq 64 -and
    $reports[0].laboratory64.estimatedPeakBuildBytes -eq $reports[1].laboratory64.estimatedPeakBuildBytes -and
    $reports[0].laboratory64.contentHash -eq $reports[1].laboratory64.contentHash
$sameAllocationQualification =
    $allocationReports[0].commit -eq $allocationReports[1].commit -and
    $allocationReports[0].framework -eq $allocationReports[1].framework -and
    $allocationReports[0].architecture -eq $allocationReports[1].architecture -and
    $allocationReports[0].algorithmVersion -eq $allocationReports[1].algorithmVersion -and
    $allocationReports[0].snapshotSchemaVersion -eq $allocationReports[1].snapshotSchemaVersion -and
    $allocationReports[0].configHash -eq $allocationReports[1].configHash -and
    $allocationReports[0].commit -eq $reports[0].commit -and
    $allocationReports[0].framework -eq $reports[0].framework -and
    $allocationReports[0].architecture -eq $reports[0].architecture
$sameAllocationSnapshot =
    $allocationReports[0].estimatedPeakBuildBytes -eq $allocationReports[1].estimatedPeakBuildBytes -and
    $allocationReports[0].contentHash -eq $allocationReports[1].contentHash -and
    $allocationReports[0].contentHash -eq $reports[0].contentHash
$isolatedAllocation =
    $allocationReports[0].isolation -eq $allocationReports[1].isolation -and
    $allocationReports[0].workers -eq 1 -and
    $allocationReports[1].workers -eq 1 -and
    $allocationReports[0].rejectedDenseRefusalAllocatedBytes -lt 65536 -and
    $allocationReports[1].rejectedDenseRefusalAllocatedBytes -lt 65536 -and
    $allocationReports[0].largeInputPointCount -eq 200000 -and
    $allocationReports[1].largeInputPointCount -eq 200000 -and
    $allocationReports[0].largeInputMemoryBudgetBytes -eq 1048576 -and
    $allocationReports[1].largeInputMemoryBudgetBytes -eq 1048576 -and
    $allocationReports[0].largeInputRefusalAllocatedBytes -lt 262144 -and
    $allocationReports[1].largeInputRefusalAllocatedBytes -lt 262144 -and
    $allocationReports[0].largeInputFailureCode -eq "BudgetExceeded" -and
    $allocationReports[1].largeInputFailureCode -eq "BudgetExceeded" -and
    $allocationReports[0].largeInputFailureStage -eq "atlas.spatial-index.budget" -and
    $allocationReports[1].largeInputFailureStage -eq "atlas.spatial-index.budget" -and
    $allocationReports[0].manyPrimitiveCount -eq 500000 -and
    $allocationReports[1].manyPrimitiveCount -eq 500000 -and
    $allocationReports[0].manyPrimitiveMemoryBudgetBytes -eq 25165824 -and
    $allocationReports[1].manyPrimitiveMemoryBudgetBytes -eq 25165824 -and
    $allocationReports[0].manyPrimitiveEstimatedPeakBuildBytes -eq $allocationReports[1].manyPrimitiveEstimatedPeakBuildBytes -and
    $allocationReports[0].manyPrimitiveEstimateAllocatedBytes -lt 8388608 -and
    $allocationReports[1].manyPrimitiveEstimateAllocatedBytes -lt 8388608 -and
    $allocationReports[0].manyPrimitiveBuildAllocatedBytes -lt 8388608 -and
    $allocationReports[1].manyPrimitiveBuildAllocatedBytes -lt 8388608 -and
    $allocationReports[0].manyPrimitiveFailureCode -eq "BudgetExceeded" -and
    $allocationReports[1].manyPrimitiveFailureCode -eq "BudgetExceeded" -and
    $allocationReports[0].manyPrimitiveFailureStage -eq "atlas.spatial-index.budget" -and
    $allocationReports[1].manyPrimitiveFailureStage -eq "atlas.spatial-index.budget" -and
    $allocationReports[0].maximumPrimitiveCount -eq 2147483647 -and
    $allocationReports[1].maximumPrimitiveCount -eq 2147483647 -and
    $allocationReports[0].maximumPrimitiveMemoryBudgetBytes -eq [long]::MaxValue -and
    $allocationReports[1].maximumPrimitiveMemoryBudgetBytes -eq [long]::MaxValue -and
    $allocationReports[0].maximumPrimitiveEstimateAllocatedBytes -lt 65536 -and
    $allocationReports[1].maximumPrimitiveEstimateAllocatedBytes -lt 65536 -and
    $allocationReports[0].maximumPrimitiveBuildAllocatedBytes -lt 65536 -and
    $allocationReports[1].maximumPrimitiveBuildAllocatedBytes -lt 65536 -and
    $allocationReports[0].maximumPrimitiveEstimateFailureCode -eq "InvalidInput" -and
    $allocationReports[1].maximumPrimitiveEstimateFailureCode -eq "InvalidInput" -and
    $allocationReports[0].maximumPrimitiveBuildFailureCode -eq "InvalidInput" -and
    $allocationReports[1].maximumPrimitiveBuildFailureCode -eq "InvalidInput" -and
    $allocationReports[0].maximumPrimitiveEstimateFailureStage -eq "atlas.spatial-index.array-capacity" -and
    $allocationReports[1].maximumPrimitiveEstimateFailureStage -eq "atlas.spatial-index.array-capacity" -and
    $allocationReports[0].maximumPrimitiveBuildFailureStage -eq "atlas.spatial-index.array-capacity" -and
    $allocationReports[1].maximumPrimitiveBuildFailureStage -eq "atlas.spatial-index.array-capacity" -and
    $allocationReports[0].maximumPrimitiveEnumerationCount -eq 0 -and
    $allocationReports[1].maximumPrimitiveEnumerationCount -eq 0 -and
    $allocationReports[0].geometryCapacitySiteCount -eq 50000 -and
    $allocationReports[1].geometryCapacitySiteCount -eq 50000 -and
    $allocationReports[0].geometryCapacityMemoryBudgetBytes -eq [long]::MaxValue -and
    $allocationReports[1].geometryCapacityMemoryBudgetBytes -eq [long]::MaxValue -and
    $allocationReports[0].precomputedGeometryEstimateAllocatedBytes -lt 65536 -and
    $allocationReports[1].precomputedGeometryEstimateAllocatedBytes -lt 65536 -and
    $allocationReports[0].precomputedGeometryBuildAllocatedBytes -lt 65536 -and
    $allocationReports[1].precomputedGeometryBuildAllocatedBytes -lt 65536 -and
    $allocationReports[0].coldGeometryEstimateAllocatedBytes -lt 65536 -and
    $allocationReports[1].coldGeometryEstimateAllocatedBytes -lt 65536 -and
    $allocationReports[0].coldGeometryBuildAllocatedBytes -lt 65536 -and
    $allocationReports[1].coldGeometryBuildAllocatedBytes -lt 65536 -and
    $allocationReports[0].precomputedGeometryEstimateFailureCode -eq "InvalidInput" -and
    $allocationReports[1].precomputedGeometryEstimateFailureCode -eq "InvalidInput" -and
    $allocationReports[0].precomputedGeometryBuildFailureCode -eq "InvalidInput" -and
    $allocationReports[1].precomputedGeometryBuildFailureCode -eq "InvalidInput" -and
    $allocationReports[0].precomputedGeometryEstimateFailureStage -eq "atlas.spatial-index.array-capacity" -and
    $allocationReports[1].precomputedGeometryEstimateFailureStage -eq "atlas.spatial-index.array-capacity" -and
    $allocationReports[0].precomputedGeometryBuildFailureStage -eq "atlas.spatial-index.array-capacity" -and
    $allocationReports[1].precomputedGeometryBuildFailureStage -eq "atlas.spatial-index.array-capacity" -and
    $allocationReports[0].coldGeometryEstimateFailureCode -eq "BudgetExceeded" -and
    $allocationReports[1].coldGeometryEstimateFailureCode -eq "BudgetExceeded" -and
    $allocationReports[0].coldGeometryBuildFailureCode -eq "BudgetExceeded" -and
    $allocationReports[1].coldGeometryBuildFailureCode -eq "BudgetExceeded" -and
    $allocationReports[0].coldGeometryEstimateFailureStage -eq "atlas.spatial-index.geometry-work-capacity" -and
    $allocationReports[1].coldGeometryEstimateFailureStage -eq "atlas.spatial-index.geometry-work-capacity" -and
    $allocationReports[0].coldGeometryBuildFailureStage -eq "atlas.spatial-index.geometry-work-capacity" -and
    $allocationReports[1].coldGeometryBuildFailureStage -eq "atlas.spatial-index.geometry-work-capacity" -and
    $allocationReports[0].geometryCapacityEnumerationCount -eq 0 -and
    $allocationReports[1].geometryCapacityEnumerationCount -eq 0 -and
    -not $allocationReports[0].rejectedDenseSnapshotVisible -and
    -not $allocationReports[1].rejectedDenseSnapshotVisible -and
    -not $allocationReports[0].largeInputSnapshotVisible -and
    -not $allocationReports[1].largeInputSnapshotVisible -and
    -not $allocationReports[0].manyPrimitiveSnapshotVisible -and
    -not $allocationReports[1].manyPrimitiveSnapshotVisible -and
    -not $allocationReports[0].maximumPrimitiveSnapshotVisible -and
    -not $allocationReports[1].maximumPrimitiveSnapshotVisible -and
    -not $allocationReports[0].geometryCapacitySnapshotVisible -and
    -not $allocationReports[1].geometryCapacitySnapshotVisible
$processIds = @(
    $reports[0].processId,
    $reports[1].processId,
    $allocationReports[0].processId,
    $allocationReports[1].processId)
$distinctProcesses = ($processIds | Select-Object -Unique).Count -eq 4
$passed =
    $sameQualification -and
    $samePlan -and
    $sameSnapshot -and
    $sameParallelSnapshot -and
    $sameOwnershipSnapshot -and
    $sameDenseRefusal -and
    $sameLaboratory64 -and
    $sameAllocationQualification -and
    $sameAllocationSnapshot -and
    $isolatedAllocation -and
    $distinctProcesses
$summary = [pscustomobject][ordered]@{
    schemaVersion = 2
    status = $(if ($passed) { "PASS" } else { "FAIL" })
    commit = $commit
    sameQualification = $sameQualification
    samePlan = $samePlan
    sameSnapshot = $sameSnapshot
    sameParallelSnapshot = $sameParallelSnapshot
    sameOwnershipSnapshot = $sameOwnershipSnapshot
    sameDenseRefusal = $sameDenseRefusal
    sameLaboratory64 = $sameLaboratory64
    sameAllocationQualification = $sameAllocationQualification
    sameAllocationSnapshot = $sameAllocationSnapshot
    isolatedAllocation = $isolatedAllocation
    distinctProcesses = $distinctProcesses
    contentHash = $reports[0].contentHash
    cumulativeAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.cumulativeAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.cumulativeAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = $allocationReports[0].allocationScope
    }
    liveManagedDeltaRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.liveManagedBytesDelta | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.liveManagedBytesDelta | Measure-Object -Maximum).Maximum
        scope = $allocationReports[0].liveMemoryScope
    }
    denseRefusalAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.rejectedDenseRefusalAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.rejectedDenseRefusalAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = "same dedicated single-test processes, measured after the qualified build window"
    }
    largeInputRefusalAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.largeInputRefusalAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.largeInputRefusalAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = "200,000-point immutable input already constructed; one MiB profile; measured dedicated refusal only"
    }
    manyPrimitiveEstimateAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.manyPrimitiveEstimateAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.manyPrimitiveEstimateAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = "500,000 immutable two-point primitives already constructed; 24 MiB profile; Estimate only"
    }
    manyPrimitiveBuildAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.manyPrimitiveBuildAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.manyPrimitiveBuildAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = "500,000 immutable two-point primitives already constructed; 24 MiB profile; Build refusal only"
    }
    maximumPrimitiveEstimateAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.maximumPrimitiveEstimateAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.maximumPrimitiveEstimateAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = "reported Count=int.MaxValue; budget=long.MaxValue; Estimate structural refusal before enumeration"
    }
    maximumPrimitiveBuildAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.maximumPrimitiveBuildAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.maximumPrimitiveBuildAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = "reported Count=int.MaxValue; budget=long.MaxValue; Build structural refusal before enumeration"
    }
    precomputedGeometryEstimateAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.precomputedGeometryEstimateAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.precomputedGeometryEstimateAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = "50,000 sites; budget=long.MaxValue; Precomputed Estimate refusal before primitive enumeration"
    }
    precomputedGeometryBuildAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.precomputedGeometryBuildAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.precomputedGeometryBuildAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = "50,000 sites; budget=long.MaxValue; Precomputed Build refusal before site generation"
    }
    coldGeometryEstimateAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.coldGeometryEstimateAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.coldGeometryEstimateAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = "50,000 sites; budget=long.MaxValue; Cold Estimate work-capacity refusal"
    }
    coldGeometryBuildAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($allocationReports.coldGeometryBuildAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($allocationReports.coldGeometryBuildAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = "50,000 sites; budget=long.MaxValue; Cold Build refusal before quadratic work"
    }
    runs = $runs
}
$summaryPath = Join-Path $OutputDirectory "T02-03-04-process.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Output $summaryPath
if (-not $passed) {
    exit 1
}
