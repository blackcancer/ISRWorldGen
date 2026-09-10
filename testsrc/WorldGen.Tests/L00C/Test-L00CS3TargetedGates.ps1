[CmdletBinding()]
param([string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$sources = @(
    (Join-Path $root 'L00CStrictEvidenceJson.cs'),
    (Join-Path $root 'L00CCampaignStorage.cs'),
    (Join-Path $root 'L00CCampaignInstallFailure.cs'),
    (Join-Path $root 'L00CCampaignStorageOracle.cs'),
    (Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLifecycleShutdownBarrier.cs'),
    (Join-Path $root 'L00CLifecycleShutdownOrderingOracle.cs'),
    (Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLifecycleRegistrationLedger.cs'),
    (Join-Path $root 'L00CLifecycleRegistrationLedgerOracle.cs'),
    (Join-Path $root 'L00CT00LifecycleValidator.cs'),
    (Join-Path $root 'L00CT00LifecycleValidatorOracle.cs')
)
foreach ($path in @($sources + $csc)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "L00-C S3 targeted gate is missing $path" }
}

$oracles = @(
    'ISRWorldGen.L00C.Laboratory.L00CCampaignStorageOracle',
    'ISRWorldGen.L00C.Laboratory.L00CLifecycleShutdownOrderingOracle',
    'ISRWorldGen.L00C.Laboratory.L00CLifecycleRegistrationLedgerOracle',
    'ISRWorldGen.L00C.Laboratory.L00CT00LifecycleValidatorOracle'
)
$results = @()
foreach ($configuration in @('Debug', 'Release')) {
    $out = Join-Path ([IO.Path]::GetTempPath()) ('l00c-s3-targeted-' + $configuration.ToLowerInvariant() + '-' + [Guid]::NewGuid().ToString('N') + '.dll')
    try {
        $define = if ($configuration -eq 'Debug') { 'DEBUG,L00C_STANDALONE_ORACLE' } else { 'L00C_STANDALONE_ORACLE' }
        $optimize = if ($configuration -eq 'Debug') { '/optimize-' } else { '/optimize+' }
        & $csc /nologo /target:library "/define:$define" $optimize /nullable:enable /warnaserror /langversion:latest "/out:$out" $sources
        if ($LASTEXITCODE -ne 0) { throw "L00-C S3 $configuration compilation failed." }
        $assembly = [Reflection.Assembly]::LoadFrom($out)
        foreach ($oracleName in $oracles) {
            $method = $assembly.GetType($oracleName, $true).GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
            if ($null -eq $method -or $method.Invoke($null, @()) -ne 0) { throw "L00-C S3 $configuration oracle failed: $oracleName" }
        }
        $results += [ordered]@{ Configuration=$configuration; Compilation='PASS'; Oracles=$oracles.Count }
    }
    finally {
        if (Test-Path -LiteralPath $out) { try { Remove-Item -LiteralPath $out -Force } catch { } }
    }
}

[ordered]@{
    TestId='L00-C-S3-TARGETED-GATES'
    Status='PASS'
    Configurations=$results
    Contract='ten A_i/B_i saves; immutable session observations; native close proof; registration release; external T00-06 evidence rejection and cleanup validation'
    Scope='Debug and Release standalone deterministic compilation/oracles over temporary paths; no runtime, VS, F5, spatial evidence, or T00-06 PASS claim.'
} | ConvertTo-Json -Depth 5 -Compress
