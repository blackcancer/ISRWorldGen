[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$project = Join-Path $repoRoot 'testsrc\WorldGen.Tests\WorldGen.Tests.csproj'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts\test-results\L01B\process'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

$reports = @()
$testFilter = 'FullyQualifiedName=ISRWorldGen.Tests.L01B.DeterminismTests.ProcessProbe_ProducesCanonicalHashesForRealRestartComparison'

for ($run = 1; $run -le 2; $run++) {
    $jsonPath = Join-Path $OutputDirectory ("process-run-{0}.json" -f $run)
    $trxName = "process-run-{0}.trx" -f $run
    $env:ISRW_L01B_PROCESS_OUTPUT = $jsonPath
    try {
        & dotnet test $project --configuration $Configuration --no-build --no-restore --filter $testFilter --logger ("trx;LogFileName={0}" -f $trxName) --results-directory $OutputDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "Le processus de test $run a échoué avec le code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item Env:ISRW_L01B_PROCESS_OUTPUT -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path -LiteralPath $jsonPath -PathType Leaf)) {
        throw "Le processus de test $run n'a pas produit $jsonPath."
    }
    $reports += Get-Content -Raw -LiteralPath $jsonPath | ConvertFrom-Json
}

if ($reports[0].processId -eq $reports[1].processId) {
    throw 'Les deux probes ont le meme PID : le redemarrage reel du processus n est pas prouve.'
}

$firstHashes = @($reports[0].seeds | ForEach-Object { "{0}:{1}" -f $_.Seed, $_.CanonicalHash })
$secondHashes = @($reports[1].seeds | ForEach-Object { "{0}:{1}" -f $_.Seed, $_.CanonicalHash })
if ($firstHashes.Count -ne 16 -or $secondHashes.Count -ne 16) {
    throw 'Chaque processus doit produire exactement 16 hashes de seed.'
}
$differences = @(Compare-Object -ReferenceObject $firstHashes -DifferenceObject $secondHashes -SyncWindow 0)
if ($differences.Count -ne 0) {
    throw 'Les hashes canoniques diffèrent après redémarrage du processus.'
}

$proof = [ordered]@{
    schemaVersion = 1
    status = 'PASS'
    configuration = $Configuration
    distinctProcessIds = @($reports[0].processId, $reports[1].processId)
    framework = $reports[0].framework
    architecture = $reports[0].architecture
    processorCount = $reports[0].processorCount
    nWorkerCount = $reports[0].nWorkerCount
    seedCount = 16
    hashes = $firstHashes
}
$proofPath = Join-Path $OutputDirectory 'T01-04-process.json'
[IO.File]::WriteAllText($proofPath, ($proof | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))

Write-Output ("T01-04 process restart PASS: PID {0} -> PID {1}; 16 hashes identical." -f $reports[0].processId, $reports[1].processId)
Write-Output ("Proof: {0}" -f $proofPath)
