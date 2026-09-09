[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$LaboratoryRoot,
    [Parameter(Mandatory=$true)][string]$GamePathsSaves,
    [Parameter(Mandatory=$true)][ValidateSet('a7290d12e6f54247bae27b71e2e571cf')][string]$RunId,
    [Parameter(Mandatory=$true)][ValidateSet(74920)][int]$RuntimeProcessId,
    [Parameter(Mandatory=$true)][string]$PrimarySavePath,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9A-F]{64}$')][string]$PrimarySaveSha256,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9A-F]{64}$')][string]$ProvenanceSha256,
    [Parameter(Mandatory=$true)][ValidateSet('I-ATTEST-L00C-A7290D12-PRIMARY-RAW-ONLY')][string]$IntegratorAttestation,
    [Parameter(Mandatory=$true)][string]$DebugAssemblyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Integrator-only authority minting.  This command never deletes, renames or
# starts anything.  Every identity and hash is supplied explicitly; there is no
# scan or fallback candidate selection.
if (Get-Process -Id $RuntimeProcessId -ErrorAction SilentlyContinue) {
    throw 'L00-C legacy manifest refused: the pinned runtime PID is currently alive or reused.'
}
if (-not (Test-Path -LiteralPath $DebugAssemblyPath -PathType Leaf)) {
    throw 'L00-C legacy manifest assembly is absent.'
}

try {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DebugAssemblyPath).Path)
    $type = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CCampaignStorage', $true)
    $method = $type.GetMethod('CreateLegacyPreJournalRecoveryManifest', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $method) { throw 'L00-C legacy manifest entrypoint is absent.' }
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
catch [Reflection.TargetInvocationException] {
    [ordered]@{
        Schema = 'l00c-legacy-prejournal-manifest-launch-v1'
        Status = 'REFUSED'
        RunId = $RunId
        RuntimeProcessId = $RuntimeProcessId
        Reason = $_.Exception.InnerException.GetType().Name
    } | ConvertTo-Json -Compress
    exit 2
}
