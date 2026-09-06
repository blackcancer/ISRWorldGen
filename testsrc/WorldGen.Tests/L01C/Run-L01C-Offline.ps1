[CmdletBinding()]
param(
    [string] $RepositoryRoot = "",
    [string] $ResultsDirectory = "",
    [string] $TestFilter = "FullyQualifiedName~ISRWorldGen.Tests.L01"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
}

if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $RepositoryRoot "artifacts\test-results\L01C\offline"
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$ResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory)
$launchWorkingDirectory = (Get-Location).Path
$invalidGamePath = "Z:\ISRWorldGen-NoGame"
$priorVintageStory = [Environment]::GetEnvironmentVariable("VINTAGE_STORY", "Process")
$priorVintageStoryPath = [Environment]::GetEnvironmentVariable("VintageStoryPath", "Process")
$steps = [System.Collections.Generic.List[object]]::new()
$finalStatus = "FAIL"
$finalExitCode = 1
$message = $null
$testEvidence = $null
$dependencyEvidence = $null
$smokeEvidence = $null
$resolvedCommit = "unknown"
$locationPushed = $false

function Invoke-DotNetStep {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string[]] $Arguments
    )

    $logPath = Join-Path $ResultsDirectory ($Name + ".log")
    $lines = @(& dotnet @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $lines | Set-Content -LiteralPath $logPath -Encoding UTF8
    $step = [pscustomobject][ordered]@{
        name = $Name
        command = "dotnet " + ($Arguments -join " ")
        exitCode = $exitCode
        log = $logPath
    }
    $steps.Add($step)
    return $step
}

function Write-StructuredProof {
    param(
        [Parameter(Mandatory = $true)] [string] $Commit
    )

    $report = [pscustomobject][ordered]@{
        schemaVersion = 1
        status = $finalStatus
        exitCode = $finalExitCode
        message = $message
        commit = $Commit
        testFilter = $TestFilter
        environment = [pscustomobject][ordered]@{
            launchWorkingDirectory = $launchWorkingDirectory
            repositoryRoot = $RepositoryRoot
            vintageStoryEnvironment = "absent"
            vintageStoryPath = $invalidGamePath
            gameMcpGpuRequired = $false
        }
        steps = $steps.ToArray()
        tests = $testEvidence
        dependencyAudit = $dependencyEvidence
        smoke = $smokeEvidence
    }
    $proofPath = Join-Path $ResultsDirectory "offline-proof.json"
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $proofPath -Encoding UTF8
    Write-Output $proofPath
}

[Environment]::SetEnvironmentVariable("VINTAGE_STORY", $null, "Process")
[Environment]::SetEnvironmentVariable("VintageStoryPath", $invalidGamePath, "Process")

try {
    New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
    Push-Location -LiteralPath $RepositoryRoot
    $locationPushed = $true
    try {
        $resolvedCommit = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or $resolvedCommit -notmatch "^[0-9a-f]{40}$") {
            throw "Unable to resolve the exact Git commit."
        }

        do {
        $coreBuild = Invoke-DotNetStep -Name "build-core-release" -Arguments @(
            "build", "src/WorldGen.Core/WorldGen.Core.csproj", "-c", "Release", "--no-restore"
        )
        $toolsBuild = Invoke-DotNetStep -Name "build-tools-release" -Arguments @(
            "build", "src/WorldGen.Tools/WorldGen.Tools.csproj", "-c", "Release", "--no-restore"
        )
        $testsBuild = Invoke-DotNetStep -Name "build-tests-release" -Arguments @(
            "build", "testsrc/WorldGen.Tests/WorldGen.Tests.csproj", "-c", "Release", "--no-restore"
        )
        if (($coreBuild.exitCode -ne 0) -or ($toolsBuild.exitCode -ne 0) -or ($testsBuild.exitCode -ne 0)) {
            $message = "At least one offline project build failed."
            break
        }

        $trxPath = Join-Path $ResultsDirectory "L01C-offline.trx"
        if (Test-Path -LiteralPath $trxPath) {
            Remove-Item -LiteralPath $trxPath -Force
        }
        $testStep = Invoke-DotNetStep -Name "test-release" -Arguments @(
            "test", "testsrc/WorldGen.Tests/WorldGen.Tests.csproj",
            "-c", "Release", "--no-build", "--no-restore",
            "--filter", $TestFilter,
            "--logger", "trx;LogFileName=L01C-offline.trx",
            "--results-directory", $ResultsDirectory
        )

        $total = 0
        $passed = 0
        $failed = 0
        if (Test-Path -LiteralPath $trxPath) {
            [xml] $trx = Get-Content -LiteralPath $trxPath -Raw
            $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
            if ($null -ne $counters) {
                $total = [int] $counters.total
                $passed = [int] $counters.passed
                $failed = [int] $counters.failed
            }
        }
        $testEvidence = [pscustomobject][ordered]@{
            trx = $trxPath
            total = $total
            passed = $passed
            failed = $failed
        }
        if ($total -eq 0) {
            $finalStatus = "TEST_ABSENT"
            $finalExitCode = 3
            $message = "The requested filter discovered zero tests."
            break
        }
        if (($testStep.exitCode -ne 0) -or ($failed -ne 0) -or ($passed -ne $total)) {
            $message = "The discovered offline test suite did not pass completely."
            break
        }

        $toolsDll = Join-Path $RepositoryRoot "src\WorldGen.Tools\bin\Release\net10.0\ISRWorldGen.Tools.dll"
        $auditPath = Join-Path $ResultsDirectory "dependency-audit.json"
        $auditStep = Invoke-DotNetStep -Name "audit-dependencies" -Arguments @(
            $toolsDll,
            "audit-dependencies",
            "--assembly", $toolsDll,
            "--commit", $resolvedCommit,
            "--output", $auditPath
        )
        if (Test-Path -LiteralPath $auditPath) {
            $dependencyEvidence = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
        }
        if ($auditStep.exitCode -ne 0 -or $null -eq $dependencyEvidence -or $dependencyEvidence.status -ne "PASS") {
            $message = "Compiled dependency inspection failed."
            break
        }

        $smokePath = Join-Path $ResultsDirectory "smoke.json"
        $smokeStep = Invoke-DotNetStep -Name "harness-smoke" -Arguments @(
            $toolsDll,
            "run",
            "--fixture", "plane-x",
            "--seed", "73",
            "--config-hash", "9e12605ff5e0e94ccccf28eac0ded526da1e687a1fd3a0650b782da7484906ed",
            "--commit", $resolvedCommit,
            "--order", "permuted",
            "--workers", "2",
            "--cache", "hot",
            "--fault", "none",
            "--budget", "100000",
            "--output", $smokePath
        )
        if (Test-Path -LiteralPath $smokePath) {
            $smokeEvidence = Get-Content -LiteralPath $smokePath -Raw | ConvertFrom-Json
        }
        if ($smokeStep.exitCode -ne 0 -or $null -eq $smokeEvidence -or $smokeEvidence.status -ne "SUCCESS") {
            $message = "Analytical harness smoke run failed."
            break
        }

        $finalStatus = "PASS"
        $finalExitCode = 0
        $message = "Offline Core/Tools builds, discovered tests, compiled dependency audit, and analytical smoke run passed."
        } while ($false)
    }
    finally {
        if ($locationPushed) {
            Pop-Location
            $locationPushed = $false
        }
    }
}
catch {
    $message = $_.Exception.Message
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
    [Environment]::SetEnvironmentVariable("VINTAGE_STORY", $priorVintageStory, "Process")
    [Environment]::SetEnvironmentVariable("VintageStoryPath", $priorVintageStoryPath, "Process")
}

Write-StructuredProof -Commit $resolvedCommit
exit $finalExitCode
