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
    $reports[0].estimatedSnapshotBytes -eq $reports[1].estimatedSnapshotBytes -and
    $reports[0].estimatedPeakBuildBytes -eq $reports[1].estimatedPeakBuildBytes
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
    -not $allocationReports[0].rejectedDenseSnapshotVisible -and
    -not $allocationReports[1].rejectedDenseSnapshotVisible
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
    runs = $runs
}
$summaryPath = Join-Path $OutputDirectory "T02-03-04-process.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Output $summaryPath
if (-not $passed) {
    exit 1
}
