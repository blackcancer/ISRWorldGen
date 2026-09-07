[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$VintageStoryPath = $env:VINTAGE_STORY
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'L02CNativeSqliteFixtureSupport.ps1')

function Write-Utf8Fixture {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)

    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)

    if (-not $Condition) { throw $Message }
}

function Assert-Fails {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$ExpectedMessage,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    $failed = $false
    try { & $Action }
    catch {
        $failed = $true
        if (-not $_.Exception.Message.Contains($ExpectedMessage, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Label failed for the wrong reason: $($_.Exception.Message)"
        }
    }
    if (-not $failed) { throw "$Label unexpectedly passed." }
}

function Get-RawSeal {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        Length = $item.Length
        LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o')
        Sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $tempBase ('isrworldgen-l02c-sqlite-extraction-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    Initialize-L02CSqliteFixtureRuntime $VintageStoryPath
    $commitResult = & git -c "safe.directory=$RepositoryRoot" -C $RepositoryRoot rev-parse HEAD 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve extractor self-test HEAD.' }
    $commit = ([string]($commitResult | Select-Object -Last 1)).Trim()
    $extractor = Join-Path $PSScriptRoot 'Invoke-L02CNativeSqliteExtraction.ps1'
    $sessionId = '11111111111111111111111111111111'
    $pidValue = 101
    $logPath = Join-Path $testRoot 'server-main.log'
    Write-Utf8Fixture $logPath @"
1.1.2026 00:00:00 [Notification] [isrworldgen] L00B_DEBUG_PROBE_READY pid=$pidValue module=ISRWorldGen.dll pass=Terrain worldtype=standard
1.1.2026 00:00:01 [Event] Stopped the server!
"@
    $logHash = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash
    $manifestPath = Join-Path $testRoot 'prelaunch-snapshot.json'
    Write-Utf8Fixture $manifestPath '{"fixture":"bounded"}'
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    $envelope = New-L02CCommittedEnvelope
    $sourcePath = Join-Path $testRoot 'wal-source.vcdbs'
    New-L02CSqliteSourceFixture $sourcePath $envelope 0 $true
    $sourcePaths = @($sourcePath, "$sourcePath-wal", "$sourcePath-shm")
    $before = @($sourcePaths | ForEach-Object { Get-RawSeal $_ })
    $reportPath = Join-Path $testRoot 'extraction.json'

    $sealedRoot = Join-Path $testRoot 'sealed-positive'
    & $extractor -SourceDatabasePath $sourcePath -OutputPath $reportPath -SealedSourceDirectory $sealedRoot `
        -TestedCommit $commit `
        -SessionId $sessionId -CaseRole new -ServerPid $pidValue -LogPath $logPath -LogSha256 $logHash `
        -SnapshotManifestPath $manifestPath -SnapshotManifestSha256 $manifestHash `
        -RepositoryRoot $RepositoryRoot -VintageStoryPath $VintageStoryPath | Out-Null

    $after = @($sourcePaths | ForEach-Object { Get-RawSeal $_ })
    for ($index = 0; $index -lt $before.Count; $index++) {
        Assert-True (($before[$index].Values -join '|') -ceq ($after[$index].Values -join '|')) `
            'Extractor changed a source SQLite file while cloning/querying.'
    }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -DateKind String
    Assert-True ($report.Schema -ceq 'isrworldgen.t02-05.sqlite-extraction.v1') `
        'Extractor did not emit its versioned closed schema.'
    Assert-True (@($report.SourceFiles).Count -eq 3) 'Extractor did not seal the exact main/WAL/SHM source set.'
    Assert-True ($report.Clone.Integrity -ceq 'ok' -and $report.Clone.KeyStatus -ceq 'Present' -and
        $report.Clone.EnvelopeState -ceq 'Committed' -and $report.Clone.EnvelopeBytes -eq $envelope.Length) `
        'Extractor did not recover the committed envelope from the clone.'
    Assert-True ($report.Clone.WalEvidence.WalContribution -ceq 'RequiredForObservedState') `
        'Extractor did not prove that WAL was required to reconstruct the observed state.'
    foreach ($file in @($report.SourceFiles)) {
        Assert-True (($file.PreCopy.PSObject.Properties.Value -join '|') -ceq
            ($file.AfterCopy.PSObject.Properties.Value -join '|')) 'After-copy source seal differs from pre-copy.'
        Assert-True (($file.PreCopy.PSObject.Properties.Value -join '|') -ceq
            ($file.PostExtraction.PSObject.Properties.Value -join '|')) 'Post-extraction source seal differs from pre-copy.'
    }

    Assert-Fails 'Output replacement' 'already exists' {
        & $extractor -SourceDatabasePath $sourcePath -OutputPath $reportPath `
            -SealedSourceDirectory (Join-Path $testRoot 'sealed-replacement') -TestedCommit $commit `
            -SessionId $sessionId -CaseRole new -ServerPid $pidValue -LogPath $logPath -LogSha256 $logHash `
            -SnapshotManifestPath $manifestPath -SnapshotManifestSha256 $manifestHash `
            -RepositoryRoot $RepositoryRoot -VintageStoryPath $VintageStoryPath | Out-Null
    }

    $missingWalSource = Join-Path $testRoot 'missing-wal.vcdbs'
    New-L02CSqliteSourceFixture $missingWalSource $null 0 $true
    Remove-Item -LiteralPath "$missingWalSource-wal" -Force
    Assert-Fails 'Missing WAL' 'clone' {
        & $extractor -SourceDatabasePath $missingWalSource -OutputPath (Join-Path $testRoot 'missing-wal.json') `
            -SealedSourceDirectory (Join-Path $testRoot 'sealed-missing-wal') `
            -TestedCommit $commit -SessionId '22222222222222222222222222222222' -CaseRole height `
            -ServerPid $pidValue -LogPath $logPath -LogSha256 $logHash -SnapshotManifestPath $manifestPath `
            -SnapshotManifestSha256 $manifestHash -RepositoryRoot $RepositoryRoot `
            -VintageStoryPath $VintageStoryPath | Out-Null
    }

    $oversizedSource = Join-Path $testRoot 'oversized-source.vcdbs'
    New-L02CSqliteSourceFixture $oversizedSource $null 0 $false
    $oversizedShm = "$oversizedSource-shm"
    $oversizedStream = [IO.File]::Open($oversizedShm, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $oversizedStream.SetLength(1GB + 1) }
    finally { $oversizedStream.Dispose() }
    Assert-Fails 'Oversized source sidecar' 'exceeds maximum byte length' {
        & $extractor -SourceDatabasePath $oversizedSource -OutputPath (Join-Path $testRoot 'oversized.json') `
            -SealedSourceDirectory (Join-Path $testRoot 'sealed-oversized') `
            -TestedCommit $commit -SessionId '33333333333333333333333333333333' -CaseRole height `
            -ServerPid $pidValue -LogPath $logPath -LogSha256 $logHash -SnapshotManifestPath $manifestPath `
            -SnapshotManifestSha256 $manifestHash -RepositoryRoot $RepositoryRoot `
            -VintageStoryPath $VintageStoryPath | Out-Null
    }

    [ordered]@{
        TestId = 'T02-05-SQLITE-CLONE-EXTRACTOR'
        Status = 'PASS'
        SourceFiles = @($report.SourceFiles).Count
        EnvelopeState = $report.Clone.EnvelopeState
        WalContribution = $report.Clone.WalEvidence.WalContribution
        NegativeCases = 3
    } | ConvertTo-Json -Depth 4
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
