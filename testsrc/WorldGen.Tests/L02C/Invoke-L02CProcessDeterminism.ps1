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
    $OutputDirectory = Join-Path $RepositoryRoot ".local\L02C\process"
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$summaryPath = Join-Path $OutputDirectory "T02-05-analytical-06-process.json"
if (Test-Path -LiteralPath $summaryPath) {
    Remove-Item -LiteralPath $summaryPath -Force
}
$commit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch "^[0-9a-f]{40}$") {
    throw "Unable to resolve exact Git commit."
}

$priorOutput = [Environment]::GetEnvironmentVariable("ISRW_L02C_PROBE_OUTPUT", "Process")
$priorCommit = [Environment]::GetEnvironmentVariable("ISRW_L02C_COMMIT", "Process")
$reports = @()
try {
    [Environment]::SetEnvironmentVariable("ISRW_L02C_COMMIT", $commit, "Process")
    for ($index = 1; $index -le 2; $index++) {
        $reportPath = Join-Path $OutputDirectory ("process-{0}.json" -f $index)
        $trxName = "process-{0}.trx" -f $index
        $trxPath = Join-Path $OutputDirectory $trxName
        if (Test-Path -LiteralPath $reportPath) {
            Remove-Item -LiteralPath $reportPath -Force
        }
        if (Test-Path -LiteralPath $trxPath) {
            Remove-Item -LiteralPath $trxPath -Force
        }
        [Environment]::SetEnvironmentVariable("ISRW_L02C_PROBE_OUTPUT", $reportPath, "Process")
        & dotnet test (Join-Path $RepositoryRoot "testsrc\WorldGen.Tests\WorldGen.Tests.csproj") `
            -c Release --no-build --no-restore `
            --filter "FullyQualifiedName=ISRWorldGen.Tests.L02C.ProfileProcessProbeTests.DeterminismProcessProbe" `
            --logger ("trx;LogFileName={0}" -f $trxName) `
            --results-directory $OutputDirectory
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $reportPath)) {
            throw "L02-C process probe $index did not produce a passing report."
        }

        [xml] $trx = Get-Content -LiteralPath (Join-Path $OutputDirectory $trxName) -Raw
        $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
        if ($null -eq $counters -or [int]$counters.total -ne 1 -or [int]$counters.passed -ne 1) {
            throw "L02-C process probe $index did not discover and pass exactly one test."
        }

        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.status -ne "PASS" -or $report.commit -ne $commit -or $report.schemaVersion -ne 1) {
            throw "L02-C process probe $index returned stale or invalid identity evidence."
        }
        $reports += $report
    }
}
finally {
    [Environment]::SetEnvironmentVariable("ISRW_L02C_PROBE_OUTPUT", $priorOutput, "Process")
    [Environment]::SetEnvironmentVariable("ISRW_L02C_COMMIT", $priorCommit, "Process")
}

$sameQualification =
    $reports[0].commit -eq $reports[1].commit -and
    $reports[0].framework -eq $reports[1].framework -and
    $reports[0].architecture -eq $reports[1].architecture
$sameContent =
    $reports[0].profileHash -eq $reports[1].profileHash -and
    $reports[0].profileBytes -eq $reports[1].profileBytes -and
    $reports[0].mapsHash -eq $reports[1].mapsHash -and
    $reports[0].policyHash -eq $reports[1].policyHash -and
    $reports[0].diagnosticHash -eq $reports[1].diagnosticHash -and
    $reports[0].sensitivityHash -eq $reports[1].sensitivityHash
$sameMetrics =
    $reports[0].edgeAlignmentScorePpm -eq $reports[1].edgeAlignmentScorePpm -and
    $reports[0].axisPeriodicityScorePpm -eq $reports[1].axisPeriodicityScorePpm -and
    $reports[0].strongestHorizontalLag -eq $reports[1].strongestHorizontalLag -and
    $reports[0].strongestVerticalLag -eq $reports[1].strongestVerticalLag -and
    $reports[0].truePositiveCount -eq $reports[1].truePositiveCount -and
    $reports[0].trueNegativeCount -eq $reports[1].trueNegativeCount -and
    $reports[0].falsePositiveCount -eq $reports[1].falsePositiveCount -and
    $reports[0].falseNegativeCount -eq $reports[1].falseNegativeCount -and
    $reports[0].EdgeGradientThreshold -eq $reports[1].EdgeGradientThreshold -and
    $reports[0].EdgeAlignmentThresholdPpm -eq $reports[1].EdgeAlignmentThresholdPpm -and
    $reports[0].PeriodicityThresholdPpm -eq $reports[1].PeriodicityThresholdPpm -and
    $reports[0].MinimumPeriodLag -eq $reports[1].MinimumPeriodLag -and
    $reports[0].MaximumPeriodLag -eq $reports[1].MaximumPeriodLag -and
    $reports[0].MaximumAnalysisWorkUnits -eq $reports[1].MaximumAnalysisWorkUnits -and
    $reports[0].MaximumCorpusAnalysisWorkUnits -eq $reports[1].MaximumCorpusAnalysisWorkUnits
$distinctProcesses = $reports[0].processId -ne $reports[1].processId
$passed = $sameQualification -and $sameContent -and $sameMetrics -and $distinctProcesses

$summary = [pscustomobject][ordered]@{
    schemaVersion = 1
    status = $(if ($passed) { "PASS" } else { "FAIL" })
    commit = $commit
    sameQualification = $sameQualification
    sameContent = $sameContent
    sameMetrics = $sameMetrics
    distinctProcesses = $distinctProcesses
    processIds = @($reports[0].processId, $reports[1].processId)
    framework = $reports[0].framework
    architecture = $reports[0].architecture
    profileHash = $reports[0].profileHash
    mapsHash = $reports[0].mapsHash
    policyHash = $reports[0].policyHash
    edgeGradientThreshold = $reports[0].EdgeGradientThreshold
    edgeAlignmentThresholdPpm = $reports[0].EdgeAlignmentThresholdPpm
    periodicityThresholdPpm = $reports[0].PeriodicityThresholdPpm
    minimumPeriodLag = $reports[0].MinimumPeriodLag
    maximumPeriodLag = $reports[0].MaximumPeriodLag
    maximumAnalysisWorkUnits = $reports[0].MaximumAnalysisWorkUnits
    maximumCorpusAnalysisWorkUnits = $reports[0].MaximumCorpusAnalysisWorkUnits
    diagnosticHash = $reports[0].diagnosticHash
    sensitivityHash = $reports[0].sensitivityHash
    edgeAlignmentScorePpm = $reports[0].edgeAlignmentScorePpm
    axisPeriodicityScorePpm = $reports[0].axisPeriodicityScorePpm
    truePositiveCount = $reports[0].truePositiveCount
    trueNegativeCount = $reports[0].trueNegativeCount
    falsePositiveCount = $reports[0].falsePositiveCount
    falseNegativeCount = $reports[0].falseNegativeCount
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
if (-not $passed) {
    throw "L02-C process determinism proof failed. See $summaryPath"
}

Write-Output "L02-C process determinism PASS: $summaryPath"
