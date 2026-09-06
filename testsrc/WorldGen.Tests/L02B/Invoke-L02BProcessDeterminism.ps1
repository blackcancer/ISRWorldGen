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
$priorCommit = [Environment]::GetEnvironmentVariable("ISRW_L02B_COMMIT", "Process")
$reports = @()
$runs = @()
try {
    [Environment]::SetEnvironmentVariable("ISRW_L02B_COMMIT", $commit, "Process")
    for ($index = 1; $index -le 2; $index++) {
        $reportPath = Join-Path $OutputDirectory ("process-{0}.json" -f $index)
        $trxName = "process-{0}.trx" -f $index
        [Environment]::SetEnvironmentVariable("ISRW_L02B_PROBE_OUTPUT", $reportPath, "Process")
        & dotnet test (Join-Path $RepositoryRoot "testsrc\WorldGen.Tests\WorldGen.Tests.csproj") `
            -c Release --no-build --no-restore `
            --filter "FullyQualifiedName~ISRWorldGen.Tests.L02B.SpatialIndexProcessProbeTests.DeterminismAndAllocationProcessProbe" `
            --logger ("trx;LogFileName={0}" -f $trxName) `
            --results-directory $OutputDirectory
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $reportPath)) {
            throw "L02-B process probe $index did not produce a passing report."
        }

        [xml] $trx = Get-Content -LiteralPath (Join-Path $OutputDirectory $trxName) -Raw
        $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
        if ($null -eq $counters -or [int]$counters.total -ne 1 -or [int]$counters.passed -ne 1) {
            throw "L02-B process probe $index did not discover and pass exactly one test."
        }

        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $reports += $report
        $runs += [pscustomobject][ordered]@{
            index = $index
            exitCode = 0
            processId = $report.processId
            trx = Join-Path $OutputDirectory $trxName
            report = $reportPath
            cumulativeAllocatedBytes = $report.cumulativeAllocatedBytes
            liveManagedBytesDelta = $report.liveManagedBytesDelta
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable("ISRW_L02B_PROBE_OUTPUT", $priorOutput, "Process")
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
$distinctProcesses = $reports[0].processId -ne $reports[1].processId
$passed = $sameQualification -and $samePlan -and $sameSnapshot -and $sameParallelSnapshot -and $sameOwnershipSnapshot -and $sameDenseRefusal -and $sameLaboratory64 -and $distinctProcesses
$summary = [pscustomobject][ordered]@{
    schemaVersion = 1
    status = $(if ($passed) { "PASS" } else { "FAIL" })
    commit = $commit
    sameQualification = $sameQualification
    samePlan = $samePlan
    sameSnapshot = $sameSnapshot
    sameParallelSnapshot = $sameParallelSnapshot
    sameOwnershipSnapshot = $sameOwnershipSnapshot
    sameDenseRefusal = $sameDenseRefusal
    sameLaboratory64 = $sameLaboratory64
    distinctProcesses = $distinctProcesses
    contentHash = $reports[0].contentHash
    cumulativeAllocationRange = [pscustomobject][ordered]@{
        minimumBytes = ($reports.cumulativeAllocatedBytes | Measure-Object -Minimum).Minimum
        maximumBytes = ($reports.cumulativeAllocatedBytes | Measure-Object -Maximum).Maximum
        scope = $reports[0].allocationScope
    }
    liveManagedDeltaRange = [pscustomobject][ordered]@{
        minimumBytes = ($reports.liveManagedBytesDelta | Measure-Object -Minimum).Minimum
        maximumBytes = ($reports.liveManagedBytesDelta | Measure-Object -Maximum).Maximum
        scope = $reports[0].liveMemoryScope
    }
    runs = $runs
}
$summaryPath = Join-Path $OutputDirectory "T02-03-04-process.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Output $summaryPath
if (-not $passed) {
    exit 1
}
