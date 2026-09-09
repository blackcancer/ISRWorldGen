[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$LaboratoryRoot,
    [Parameter(Mandatory=$true)][string]$GamePathsSaves,
    [Parameter(Mandatory=$true)][ValidateSet('a7290d12e6f54247bae27b71e2e571cf')][string]$RunId,
    [Parameter(Mandatory=$true)][ValidateSet(74920)][int]$RuntimeProcessId,
    [Parameter(Mandatory=$true)][string]$PrimarySavePath,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9A-F]{64}$')][string]$PrimarySaveSha256,
    [Parameter(Mandatory=$true)][string]$PrimaryWalPath,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9A-F]{64}$')][string]$PrimaryWalSha256,
    [Parameter(Mandatory=$true)][string]$PrimaryShmPath,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9A-F]{64}$')][string]$PrimaryShmSha256,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9A-F]{64}$')][string]$ProvenanceSha256,
    [Parameter(Mandatory=$true)][ValidateSet('I-ATTEST-L00C-A7290D12-PRIMARY-DB-WAL-SHM-ONLY')][string]$IntegratorAttestation,
    [Parameter(Mandatory=$true)][string]$DebugAssemblyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Integrator-only authority minting.  This command never deletes, renames or
# starts anything.  Every identity and hash is supplied explicitly; there is no
# scan or fallback candidate selection.
try {
    if (Get-Process -Id $RuntimeProcessId -ErrorAction SilentlyContinue) {
        throw [InvalidOperationException]::new('L00-C legacy manifest refused: the pinned runtime PID is currently alive or reused.')
    }
    if (-not (Test-Path -LiteralPath $DebugAssemblyPath -PathType Leaf)) {
        throw [InvalidOperationException]::new('L00-C legacy manifest assembly is absent.')
    }
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DebugAssemblyPath).Path)
    $type = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CCampaignStorage', $true)
    $signature = [Type[]]@(
        [string], [string], [string], [int],
        [string], [string], [string], [string], [string], [string],
        [string], [string], [Func[bool]])
    $method = $type.GetMethod(
        'CreateLegacyPreJournalRecoveryManifest',
        [Reflection.BindingFlags]'Static,NonPublic',
        $null,
        $signature,
        $null)
    if ($null -eq $method -or $method.ReturnType -ne [string]) {
        throw [InvalidOperationException]::new('L00-C legacy manifest exact sidecar-complete entrypoint is absent.')
    }
    $stoppedProof = [Func[bool]]{
        $null -eq (Get-Process -Id $RuntimeProcessId -ErrorAction SilentlyContinue)
    }
    $sealHash = [string]$method.Invoke($null, @(
        $LaboratoryRoot,
        $GamePathsSaves,
        $RunId,
        $RuntimeProcessId,
        $PrimarySavePath,
        $PrimarySaveSha256,
        $PrimaryWalPath,
        $PrimaryWalSha256,
        $PrimaryShmPath,
        $PrimaryShmSha256,
        $ProvenanceSha256,
        $IntegratorAttestation,
        $stoppedProof))
    $campaignRoot = Join-Path (Join-Path $LaboratoryRoot 'campaigns') $RunId
    $manifestPath = Join-Path $campaignRoot 'legacy-prejournal-recovery-manifest.json'
    $sealPath = Join-Path $campaignRoot 'legacy-prejournal-recovery-seal.json'
    [ordered]@{
        Schema = 'l00c-legacy-prejournal-manifest-launch-v1'
        Status = 'SEALED'
        RunId = $RunId
        RuntimeProcessId = $RuntimeProcessId
        ManifestPath = [IO.Path]::GetFullPath($manifestPath)
        ManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        SealPath = [IO.Path]::GetFullPath($sealPath)
        SealSha256 = $sealHash
    } | ConvertTo-Json -Compress
}
catch {
    $failure = $_.Exception
    if ($failure -is [Reflection.TargetInvocationException] -and $null -ne $failure.InnerException) {
        $failure = $failure.InnerException
    }
    [ordered]@{
        Schema = 'l00c-legacy-prejournal-manifest-launch-v1'
        Status = 'REFUSED'
        RunId = $RunId
        RuntimeProcessId = $RuntimeProcessId
        Reason = $failure.GetType().Name
    } | ConvertTo-Json -Compress
    exit 2
}
