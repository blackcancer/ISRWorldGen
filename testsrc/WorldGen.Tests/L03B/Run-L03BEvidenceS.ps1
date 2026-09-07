[CmdletBinding()]
param([switch]$PreflightOnly)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$solution = Join-Path $root 'ISRWorldGen.sln'
$localRoot = Join-Path $root '.local\L03B'
$testAssembly = Join-Path $root 'testsrc\WorldGen.Tests\bin\Release\net10.0\ISRWorldGen.Tests.dll'
$coreAssembly = Join-Path $root 'src\WorldGen.Core\bin\Release\net10.0\ISRWorldGen.Core.dll'

function Invoke-EvidenceProcess {
    param([string]$FileName, [string[]]$Arguments, [int]$TimeoutSeconds = 600)
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($FileName)
    $process.StartInfo.WorkingDirectory = $root; $process.StartInfo.UseShellExecute = $false; $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true; $process.StartInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$process.StartInfo.ArgumentList.Add($argument) }
    if (-not $process.Start()) { throw "Unable to start $FileName." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill($true); $process.WaitForExit(); throw "$FileName timed out after $TimeoutSeconds seconds." }
    [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdoutTask, $stderrTask))
    $result = [pscustomobject]@{ ExitCode = $process.ExitCode; StdOut = $stdoutTask.GetAwaiter().GetResult(); StdErr = $stderrTask.GetAwaiter().GetResult() }; $process.Dispose(); return $result
}
function Invoke-EvidenceGit { param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $result = Invoke-EvidenceProcess -FileName git -Arguments (@('-c', "safe.directory=$($root.Replace('\', '/'))", '-C', $root) + $Arguments) -TimeoutSeconds 30
    if ($result.ExitCode -ne 0) { throw "Git preflight failed: $($Arguments -join ' '): $(([string]($result.StdErr)).Trim())" }; return ([string]($result.StdOut)).Trim()
}
function Assert-Provenance { param([string]$ExpectedHead, [string]$ExpectedTree)
    $actualHead = Invoke-EvidenceGit rev-parse HEAD; $actualTree = Invoke-EvidenceGit rev-parse 'HEAD^{tree}'
    $attached = Invoke-EvidenceProcess -FileName git -Arguments @('-c', "safe.directory=$($root.Replace('\', '/'))", '-C', $root, 'symbolic-ref', '-q', 'HEAD') -TimeoutSeconds 30
    $status = Invoke-EvidenceGit status --porcelain --untracked-files=all
    if ($attached.ExitCode -ne 1 -or $status -or ($ExpectedHead -and $actualHead -ne $ExpectedHead) -or ($ExpectedTree -and $actualTree -ne $ExpectedTree)) { throw 'Evidence requires the same detached HEAD/tree and a fully clean worktree including untracked files.' }
    return [pscustomobject]@{ Head = $actualHead; Tree = $actualTree }
}

[void][IO.Directory]::CreateDirectory($localRoot)
$lockPath = Join-Path $localRoot 'evidence-s-runner.lock'
try { $runLock = [IO.File]::Open($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) } catch [IO.IOException] { throw 'Another L03-B S evidence runner owns the exclusive lock.' }
try {
    $provenance = Assert-Provenance; $head = $provenance.Head; $tree = $provenance.Tree; $fixturesBlob = Invoke-EvidenceGit rev-parse 'HEAD:registry/fixtures.json'
    $restore = Invoke-EvidenceProcess -FileName dotnet -Arguments @('restore', $solution, '--locked-mode', '/p:VintageStoryPath=D:\Jeux\Vintagestory') -TimeoutSeconds 600
    if ($restore.ExitCode -ne 0) { throw "Restore failed: $($restore.StdErr.Trim())" }
    $build = Invoke-EvidenceProcess -FileName dotnet -Arguments @('build', $solution, '--configuration', 'Release', '--no-restore', '/p:VintageStoryPath=D:\Jeux\Vintagestory', "/p:InformationalVersion=1.0.0+$head", '/p:IncludeSourceRevisionInInformationalVersion=false') -TimeoutSeconds 600
    if ($build.ExitCode -ne 0) { throw "Build failed: $($build.StdErr.Trim())" }
    if ($PreflightOnly) { return }
    $terminalPath = Join-Path $localRoot "evidence-s-terminal-$head"; if (Test-Path -LiteralPath $terminalPath) { throw 'Evidence terminal directory already exists.' }
    $stagingName = "evidence-s-staging-$head-$([guid]::NewGuid().ToString('N'))"; $stagingPath = Join-Path $localRoot $stagingName
    $nonceBytes = [byte[]]::new(32); [Security.Cryptography.RandomNumberGenerator]::Fill($nonceBytes); $nonce = [Convert]::ToHexString($nonceBytes).ToLowerInvariant()
    try {
        $env:ISR_L03B_EVIDENCE_COMMIT = $head; $env:ISR_L03B_EVIDENCE_TREE = $tree; $env:ISR_L03B_EVIDENCE_FIXTURES_BLOB = $fixturesBlob; $env:ISR_L03B_EVIDENCE_NONCE = $nonce; $env:ISR_L03B_EVIDENCE_CONFIGURATION = 'Release'; $env:ISR_L03B_EVIDENCE_RUN = $stagingName
        $env:ISR_L03B_EVIDENCE_TEST_ASSEMBLY_SHA256 = (Get-FileHash -LiteralPath $testAssembly -Algorithm SHA256).Hash.ToLowerInvariant(); $env:ISR_L03B_EVIDENCE_CORE_ASSEMBLY_SHA256 = (Get-FileHash -LiteralPath $coreAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
        $test = Invoke-EvidenceProcess -FileName dotnet -Arguments @('test', (Join-Path $root 'testsrc\WorldGen.Tests\WorldGen.Tests.csproj'), '--configuration', 'Release', '--no-build', '--filter', 'FullyQualifiedName~EvidenceArtifactTests', '/p:VintageStoryPath=D:\Jeux\Vintagestory') -TimeoutSeconds 3600
        if ($test.ExitCode -ne 0) { throw "Evidence test failed: $($test.StdErr.Trim())" }
        [void](Assert-Provenance $head $tree)
        Move-Item -LiteralPath $stagingPath -Destination $terminalPath -ErrorAction Stop
    } finally { $env:ISR_L03B_EVIDENCE_NONCE = $null; if (Test-Path -LiteralPath $stagingPath) { Remove-Item -LiteralPath $stagingPath -Recurse -Force } }
} finally { if ($null -ne $runLock) { $runLock.Dispose() }; if (Test-Path -LiteralPath $lockPath) { Remove-Item -LiteralPath $lockPath -Force } }
