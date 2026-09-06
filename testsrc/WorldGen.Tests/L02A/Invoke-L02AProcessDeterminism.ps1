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
    $OutputDirectory = Join-Path $RepositoryRoot "artifacts\test-results\L02A\process"
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$commit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch "^[0-9a-f]{40}$") {
    throw "Unable to resolve exact Git commit."
}

$priorOutput = [Environment]::GetEnvironmentVariable("ISRW_L02A_PROBE_OUTPUT", "Process")
$priorCommit = [Environment]::GetEnvironmentVariable("ISRW_L02A_COMMIT", "Process")
$reports = @()
$testRuns = @()

try {
    [Environment]::SetEnvironmentVariable("ISRW_L02A_COMMIT", $commit, "Process")
    for ($index = 1; $index -le 2; $index++) {
        $probePath = Join-Path $OutputDirectory ("process-{0}.json" -f $index)
        $trxName = "process-{0}.trx" -f $index
        [Environment]::SetEnvironmentVariable("ISRW_L02A_PROBE_OUTPUT", $probePath, "Process")
        & dotnet test (Join-Path $RepositoryRoot "testsrc\WorldGen.Tests\WorldGen.Tests.csproj") `
            -c Release --no-build --no-restore `
            --filter "FullyQualifiedName~ISRWorldGen.Tests.L02A.GeometryProcessProbeTests.DeterminismProcessProbe" `
            --logger ("trx;LogFileName={0}" -f $trxName) `
            --results-directory $OutputDirectory
        $testExitCode = $LASTEXITCODE
        if ($testExitCode -ne 0 -or -not (Test-Path -LiteralPath $probePath)) {
            throw "Process probe $index did not produce a passing report."
        }

        [xml] $trx = Get-Content -LiteralPath (Join-Path $OutputDirectory $trxName) -Raw
        $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
        if ($null -eq $counters -or [int]$counters.total -ne 1 -or [int]$counters.passed -ne 1) {
            throw "Process probe $index did not discover and pass exactly one test."
        }

        $report = Get-Content -LiteralPath $probePath -Raw | ConvertFrom-Json
        $reports += $report
        $testRuns += [pscustomobject][ordered]@{
            index = $index
            exitCode = $testExitCode
            trx = Join-Path $OutputDirectory $trxName
            report = $probePath
            processId = $report.processId
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable("ISRW_L02A_PROBE_OUTPUT", $priorOutput, "Process")
    [Environment]::SetEnvironmentVariable("ISRW_L02A_COMMIT", $priorCommit, "Process")
}

$sameQualification =
    $reports[0].commit -eq $reports[1].commit -and
    $reports[0].framework -eq $reports[1].framework -and
    $reports[0].architecture -eq $reports[1].architecture -and
    $reports[0].algorithmVersion -eq $reports[1].algorithmVersion -and
    $reports[0].snapshotSchemaVersion -eq $reports[1].snapshotSchemaVersion -and
    $reports[0].configHash -eq $reports[1].configHash
$sameGeometry = $reports[0].geometryHash -eq $reports[1].geometryHash
$sameBoundaryField = $reports[0].boundaryFieldHash -eq $reports[1].boundaryFieldHash
$distinctProcesses = $reports[0].processId -ne $reports[1].processId
$passed = $sameQualification -and $sameGeometry -and $sameBoundaryField -and $distinctProcesses
$summary = [pscustomobject][ordered]@{
    schemaVersion = 1
    status = $(if ($passed) { "PASS" } else { "FAIL" })
    commit = $commit
    sameQualification = $sameQualification
    sameGeometry = $sameGeometry
    sameBoundaryField = $sameBoundaryField
    distinctProcesses = $distinctProcesses
    geometryHash = $reports[0].geometryHash
    boundaryFieldHash = $reports[0].boundaryFieldHash
    runs = $testRuns
}
$summaryPath = Join-Path $OutputDirectory "T02-01-02-process.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Output $summaryPath
if (-not $passed) {
    exit 1
}
