[CmdletBinding()]
param([switch]$PreflightOnly)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$solution = Join-Path $root 'ISRWorldGen.sln'
$localRoot = Join-Path $root '.local\L03B'
$testAssembly = Join-Path $root 'testsrc\WorldGen.Tests\bin\Release\net10.0\ISRWorldGen.Tests.dll'
$coreAssembly = Join-Path $root 'src\WorldGen.Core\bin\Release\net10.0\ISRWorldGen.Core.dll'

function Protect-EvidenceDiagnostic {
    param([AllowNull()][string]$Text, [string[]]$Secrets = @(), [int]$MaximumCharacters = 2048)
    $safe = if ($null -eq $Text) { '' } else { $Text.Trim() }
    foreach ($secret in $Secrets) {
        if ($secret) { $safe = $safe.Replace($secret, '<redacted>') }
    }
    if ($safe.Length -gt $MaximumCharacters) { return $safe.Substring(0, $MaximumCharacters) + '...<truncated>' }
    return $safe
}
function Format-EvidenceDiagnostic {
    param($Result, [string[]]$Secrets = @())
    $stdout = Protect-EvidenceDiagnostic -Text ([string]$Result.StdOut) -Secrets $Secrets
    $stderr = Protect-EvidenceDiagnostic -Text ([string]$Result.StdErr) -Secrets $Secrets
    return "stdout=[$stdout] stderr=[$stderr]"
}
function Invoke-EvidenceProcess {
    param([string]$FileName, [string[]]$Arguments, [int]$TimeoutSeconds = 600, [string[]]$Secrets = @())
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($FileName)
        $process.StartInfo.WorkingDirectory = $root
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        foreach ($argument in $Arguments) { [void]$process.StartInfo.ArgumentList.Add($argument) }
        if (-not $process.Start()) { throw "Unable to start $FileName." }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $exitTask = $process.WaitForExitAsync()
        $completion = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($exitTask, $stdoutTask, $stderrTask))
        $timeout = [Threading.Tasks.Task]::Delay([TimeSpan]::FromSeconds($TimeoutSeconds))
        $winner = [Threading.Tasks.Task]::WhenAny([Threading.Tasks.Task[]]@($completion, $timeout)).GetAwaiter().GetResult()
        if (-not [object]::ReferenceEquals($winner, $completion)) {
            try { if (-not $process.HasExited) { $process.Kill($true) } } catch [InvalidOperationException] { }
            $drain = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($exitTask, $stdoutTask, $stderrTask))
            $drainTimeout = [Threading.Tasks.Task]::Delay([TimeSpan]::FromSeconds(5))
            [void][Threading.Tasks.Task]::WhenAny([Threading.Tasks.Task[]]@($drain, $drainTimeout)).GetAwaiter().GetResult()
            $partial = [pscustomobject]@{
                StdOut = if ($stdoutTask.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion) { $stdoutTask.Result } else { '<unavailable>' }
                StdErr = if ($stderrTask.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion) { $stderrTask.Result } else { '<unavailable>' }
            }
            throw "$FileName timed out after $TimeoutSeconds seconds. $(Format-EvidenceDiagnostic -Result $partial -Secrets $Secrets)"
        }
        $completion.GetAwaiter().GetResult()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; StdOut = $stdoutTask.Result; StdErr = $stderrTask.Result }
    } finally {
        $process.Dispose()
    }
}
function Invoke-EvidenceGit { param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $result = Invoke-EvidenceProcess -FileName git -Arguments (@('-c', "safe.directory=$($root.Replace('\', '/'))", '-C', $root) + $Arguments) -TimeoutSeconds 30
    if ($result.ExitCode -ne 0) { throw "Git preflight failed: $($Arguments -join ' '): $(Format-EvidenceDiagnostic -Result $result)" }
    return ([string]($result.StdOut)).Trim()
}
function Assert-Provenance { param([string]$ExpectedHead, [string]$ExpectedTree)
    $actualHead = Invoke-EvidenceGit rev-parse HEAD; $actualTree = Invoke-EvidenceGit rev-parse 'HEAD^{tree}'
    $attached = Invoke-EvidenceProcess -FileName git -Arguments @('-c', "safe.directory=$($root.Replace('\', '/'))", '-C', $root, 'symbolic-ref', '-q', 'HEAD') -TimeoutSeconds 30
    $status = Invoke-EvidenceGit status --porcelain --untracked-files=all
    if ($attached.ExitCode -ne 1 -or $status -or ($ExpectedHead -and $actualHead -ne $ExpectedHead) -or ($ExpectedTree -and $actualTree -ne $ExpectedTree)) { throw 'Evidence requires the same detached HEAD/tree and a fully clean worktree including untracked files.' }
    return [pscustomobject]@{ Head = $actualHead; Tree = $actualTree }
}
function Get-EvidenceSha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Write-EvidenceAtomicBytes {
    param([string]$Path, [byte[]]$Content)
    $temporaryPath = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllBytes($temporaryPath, $Content)
        [IO.File]::Move($temporaryPath, $Path, $true)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
    }
}
function Write-SealedManifest {
    param(
        [string]$StagingPath,
        [string]$Head,
        [string]$Tree,
        [string]$FixturesBlob,
        [string]$TestAssemblyHash,
        [string]$CoreAssemblyHash)
    $artifactRelativePaths = @(
        'blind/T03-06-S-manifest.json',
        'sealed/T03-05-06-S.json',
        'sealed/T03-05-06-S.trx',
        'sealed/T03-06-S-progress.json',
        'sealed/T03-06-S-review-key.json')
    $artifacts = @($artifactRelativePaths | Sort-Object | ForEach-Object {
        $artifactPath = Join-Path $StagingPath $_
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) { throw "Required sealed evidence artifact is absent: $_" }
        [ordered]@{ path = $_; sha256 = Get-EvidenceSha256 -Path $artifactPath }
    })
    $payload = [ordered]@{
        schemaVersion = 1
        requirementIds = @('R03-05', 'R03-06')
        automatedStatus = 'PASS'
        qualitativeReviewStatus = 'REVIEW_REQUIRED'
        overallStatus = 'REVIEW_REQUIRED'
        commit = $Head
        tree = $Tree
        fixturesBlob = $FixturesBlob
        configuration = 'Release'
        testAssemblySha256 = $TestAssemblyHash
        coreAssemblySha256 = $CoreAssemblyHash
        signatureScheme = 'sha256-canonical-json-v1'
        artifacts = $artifacts
    }
    $canonicalBytes = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Depth 8 -Compress))
    $manifest = [ordered]@{}
    foreach ($entry in $payload.GetEnumerator()) { $manifest[$entry.Key] = $entry.Value }
    $manifest.bundleSignature = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($canonicalBytes)).ToLowerInvariant()
    $manifestBytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 8))
    Write-EvidenceAtomicBytes -Path (Join-Path $StagingPath 'sealed/T03-05-06-S-manifest.json') -Content $manifestBytes
}

[void][IO.Directory]::CreateDirectory($localRoot)
$lockPath = Join-Path $localRoot 'evidence-s-runner.lock'
$runLock = $null
try {
    $runLock = [IO.FileStream]::new($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None, 1, [IO.FileOptions]::DeleteOnClose)
} catch [IO.IOException] {
    throw 'Another L03-B S evidence runner owns the exclusive lock.'
}
try {
    $provenance = Assert-Provenance; $head = $provenance.Head; $tree = $provenance.Tree; $fixturesBlob = Invoke-EvidenceGit rev-parse 'HEAD:registry/fixtures.json'
    $restore = Invoke-EvidenceProcess -FileName dotnet -Arguments @('restore', $solution, '--locked-mode', '/p:VintageStoryPath=D:\Jeux\Vintagestory') -TimeoutSeconds 600
    if ($restore.ExitCode -ne 0) { throw "Restore failed: $(Format-EvidenceDiagnostic -Result $restore)" }
    $build = Invoke-EvidenceProcess -FileName dotnet -Arguments @('build', $solution, '--configuration', 'Release', '--no-restore', '/p:VintageStoryPath=D:\Jeux\Vintagestory', "/p:InformationalVersion=1.0.0+$head", '/p:IncludeSourceRevisionInInformationalVersion=false') -TimeoutSeconds 600
    if ($build.ExitCode -ne 0) { throw "Build failed: $(Format-EvidenceDiagnostic -Result $build)" }
    if ($PreflightOnly) { [void](Assert-Provenance $head $tree); return }
    $terminalPath = Join-Path $localRoot "evidence-s-terminal-$head"; if (Test-Path -LiteralPath $terminalPath) { throw 'Evidence terminal directory already exists.' }
    $stagingName = "evidence-s-staging-$head-$([guid]::NewGuid().ToString('N'))"; $stagingPath = Join-Path $localRoot $stagingName
    $trxStagingPath = Join-Path $localRoot "$stagingName-trx"
    $trxName = 'T03-05-06-S.trx'
    $nonceBytes = [byte[]]::new(32); [Security.Cryptography.RandomNumberGenerator]::Fill($nonceBytes); $nonce = [Convert]::ToHexString($nonceBytes).ToLowerInvariant()
    try {
        $env:ISR_L03B_EVIDENCE_COMMIT = $head; $env:ISR_L03B_EVIDENCE_TREE = $tree; $env:ISR_L03B_EVIDENCE_FIXTURES_BLOB = $fixturesBlob; $env:ISR_L03B_EVIDENCE_NONCE = $nonce; $env:ISR_L03B_EVIDENCE_CONFIGURATION = 'Release'; $env:ISR_L03B_EVIDENCE_RUN = $stagingName
        $env:ISR_L03B_EVIDENCE_TEST_ASSEMBLY_SHA256 = Get-EvidenceSha256 -Path $testAssembly; $env:ISR_L03B_EVIDENCE_CORE_ASSEMBLY_SHA256 = Get-EvidenceSha256 -Path $coreAssembly
        $evidenceFilter = 'FullyQualifiedName=ISRWorldGen.Tests.L03B.EvidenceArtifactTests.T0305AndT0306PublishAtomicBlindReviewEvidence'
        $testArguments = @('test', (Join-Path $root 'testsrc\WorldGen.Tests\WorldGen.Tests.csproj'), '--configuration', 'Release', '--no-build', '--no-restore', '--filter', $evidenceFilter, '--logger', "trx;LogFileName=$trxName", '--results-directory', $trxStagingPath, '/p:VintageStoryPath=D:\Jeux\Vintagestory')
        $test = Invoke-EvidenceProcess -FileName dotnet -Arguments $testArguments -TimeoutSeconds 3600 -Secrets @($nonce)
        if ($test.ExitCode -ne 0) { throw "Evidence test failed: $(Format-EvidenceDiagnostic -Result $test -Secrets @($nonce))" }
        $trxSource = Join-Path $trxStagingPath $trxName
        if (-not (Test-Path -LiteralPath $trxSource -PathType Leaf)) { throw 'Evidence test completed without the required TRX.' }
        [xml]$trx = Get-Content -LiteralPath $trxSource -Raw
        $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
        if ($null -eq $counters -or [int]$counters.total -ne 1 -or [int]$counters.executed -ne 1 -or [int]$counters.passed -ne 1 -or [int]$counters.failed -ne 0) { throw 'Evidence TRX must contain exactly one executed and passing evidence test.' }
        $trxDestination = Join-Path $stagingPath "sealed\$trxName"
        Move-Item -LiteralPath $trxSource -Destination $trxDestination -ErrorAction Stop
        Write-SealedManifest -StagingPath $stagingPath -Head $head -Tree $tree -FixturesBlob $fixturesBlob -TestAssemblyHash $env:ISR_L03B_EVIDENCE_TEST_ASSEMBLY_SHA256 -CoreAssemblyHash $env:ISR_L03B_EVIDENCE_CORE_ASSEMBLY_SHA256
        [void](Assert-Provenance $head $tree)
        Move-Item -LiteralPath $stagingPath -Destination $terminalPath -ErrorAction Stop
    } finally {
        foreach ($name in @('ISR_L03B_EVIDENCE_COMMIT', 'ISR_L03B_EVIDENCE_TREE', 'ISR_L03B_EVIDENCE_FIXTURES_BLOB', 'ISR_L03B_EVIDENCE_NONCE', 'ISR_L03B_EVIDENCE_CONFIGURATION', 'ISR_L03B_EVIDENCE_RUN', 'ISR_L03B_EVIDENCE_TEST_ASSEMBLY_SHA256', 'ISR_L03B_EVIDENCE_CORE_ASSEMBLY_SHA256')) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
        if ($null -ne $nonceBytes) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($nonceBytes) }
        $nonce = $null
        if (Test-Path -LiteralPath $stagingPath) { Remove-Item -LiteralPath $stagingPath -Recurse -Force }
        if (Test-Path -LiteralPath $trxStagingPath) { Remove-Item -LiteralPath $trxStagingPath -Recurse -Force }
    }
} finally {
    if ($null -ne $runLock) { $runLock.Dispose() }
    if (Test-Path -LiteralPath $lockPath) { Remove-Item -LiteralPath $lockPath -Force }
}
