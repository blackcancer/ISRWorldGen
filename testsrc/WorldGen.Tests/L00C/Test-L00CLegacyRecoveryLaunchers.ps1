[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$DebugAssemblyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runId = 'a7290d12e6f54247bae27b71e2e571cf'
$pidValue = 74920
if (Get-Process -Id $pidValue -ErrorAction SilentlyContinue) {
    throw 'L00-C launcher oracle requires the pinned PID to be stopped.'
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('l00c-legacy-launchers-' + [Guid]::NewGuid().ToString('N'))
try {
    $laboratoryRoot = Join-Path $tempRoot 'repo\.local\L00C'
    $saves = Join-Path $tempRoot 'fake-GamePaths-Saves'
    $campaign = Join-Path (Join-Path $laboratoryRoot 'campaigns') $runId
    $evidence = Join-Path $campaign 'evidence'
    [void](New-Item -ItemType Directory -Path $evidence -Force)
    [void](New-Item -ItemType Directory -Path $saves -Force)

    $primary = Join-Path $saves "ISRWorldGen-L00C-$runId-activated-primary.vcdbs"
    $wal = "$primary-wal"
    $shm = "$primary-shm"
    $secondary = Join-Path $saves "ISRWorldGen-L00C-$runId-activated-secondary.vcdbs"
    $provenance = Join-Path $campaign 'campaign-provenance.json'
    $sentinel = Join-Path $saves 'unrelated-user-save.vcdbs'
    [IO.File]::WriteAllBytes($primary, [byte[]]::new(4096))
    [IO.File]::WriteAllBytes($wal, [byte[]]::new(57712))
    [IO.File]::WriteAllBytes($shm, [byte[]]::new(32768))
    [IO.File]::WriteAllText($sentinel, 'never-delete')
    [IO.File]::WriteAllText($provenance, ([ordered]@{
        schema='l00c-appdata-campaign-v1'; runId=$runId
        laboratoryRoot=[IO.Path]::GetFullPath($laboratoryRoot)
        gamePathsSaves=[IO.Path]::GetFullPath($saves)
        primarySave=[IO.Path]::GetFullPath($primary)
        secondarySave=[IO.Path]::GetFullPath($secondary)
        processId=$pidValue
    } | ConvertTo-Json -Compress))

    $manifestOutput = @(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'New-L00CLegacyPreJournalRecoveryManifest.ps1') `
        -LaboratoryRoot $laboratoryRoot -GamePathsSaves $saves -RunId $runId `
        -RuntimeProcessId $pidValue -PrimarySavePath $primary `
        -PrimarySaveSha256 (Get-FileHash -LiteralPath $primary -Algorithm SHA256).Hash `
        -PrimaryWalPath $wal -PrimaryWalSha256 (Get-FileHash -LiteralPath $wal -Algorithm SHA256).Hash `
        -PrimaryShmPath $shm -PrimaryShmSha256 (Get-FileHash -LiteralPath $shm -Algorithm SHA256).Hash `
        -ProvenanceSha256 (Get-FileHash -LiteralPath $provenance -Algorithm SHA256).Hash `
        -IntegratorAttestation 'I-ATTEST-L00C-A7290D12-PRIMARY-DB-WAL-SHM-ONLY' `
        -DebugAssemblyPath $DebugAssemblyPath)
    $manifestExitCode = $LASTEXITCODE
    if ($manifestOutput.Count -ne 1) { throw 'L00-C launcher oracle manifest returned an invalid result count.' }
    $manifestResult = $manifestOutput[0] | ConvertFrom-Json
    if ($manifestExitCode -ne 0 -or $manifestResult.Status -ne 'SEALED') { throw "L00-C launcher oracle manifest was not sealed: $($manifestResult.Reason)." }

    $cleanupOutput = @(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-L00CLegacyPreJournalRecovery.ps1') `
        -LaboratoryRoot $laboratoryRoot -GamePathsSaves $saves -RunId $runId `
        -RuntimeProcessId $pidValue -ManifestSha256 $manifestResult.ManifestSha256 `
        -SealSha256 $manifestResult.SealSha256 -DebugAssemblyPath $DebugAssemblyPath)
    $cleanupExitCode = $LASTEXITCODE
    if ($cleanupOutput.Count -ne 1) { throw 'L00-C launcher oracle cleanup returned an invalid result count.' }
    $cleanupResult = $cleanupOutput[0] | ConvertFrom-Json
    if ($cleanupExitCode -ne 0 -or $cleanupResult.Status -ne 'CLEANED' -or
        (Test-Path -LiteralPath $primary) -or (Test-Path -LiteralPath $wal) -or (Test-Path -LiteralPath $shm) -or
        -not (Test-Path -LiteralPath $sentinel -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $campaign 'legacy-prejournal-recovery-cleaned.json') -PathType Leaf)) {
        throw 'L00-C launcher oracle cleanup shape is invalid.'
    }
    [ordered]@{ TestId='L00-C-LEGACY-LAUNCHERS'; Status='PASS'; Shape='4096-byte db, 57712-byte wal, 32768-byte shm'; Isolation='temporary paths only' } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
