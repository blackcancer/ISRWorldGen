[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$LaboratoryRoot,
    [Parameter(Mandatory=$true)][string]$GamePathsSaves,
    [Parameter(Mandatory=$true)][ValidateSet('a7290d12e6f54247bae27b71e2e571cf')][string]$RunId,
    [Parameter(Mandatory=$true)][ValidateSet(74920)][int]$RuntimeProcessId,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9A-F]{64}$')][string]$ManifestSha256,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9A-F]{64}$')][string]$SealSha256,
    [Parameter(Mandatory=$true)][string]$DebugAssemblyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Dedicated single-use path.  It is intentionally separate from the generic
# post-runtime cleanup command, which must keep refusing a missing abort journal.
try {
    if (Get-Process -Id $RuntimeProcessId -ErrorAction SilentlyContinue) {
        throw [InvalidOperationException]::new('L00-C legacy cleanup refused: the pinned runtime PID is currently alive or reused.')
    }
    if (-not (Test-Path -LiteralPath $DebugAssemblyPath -PathType Leaf)) {
        throw [InvalidOperationException]::new('L00-C legacy cleanup assembly is absent.')
    }
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DebugAssemblyPath).Path)
    $type = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CCampaignStorage', $true)
    $signature = [Type[]]@([string], [string], [string], [int], [string], [string], [Func[bool]])
    $method = $type.GetMethod(
        'CleanupLegacyPreJournalAfterRuntimeStopped',
        [Reflection.BindingFlags]'Static,NonPublic',
        $null,
        $signature,
        $null)
    if ($null -eq $method -or $method.ReturnType -ne [void]) {
        throw [InvalidOperationException]::new('L00-C legacy cleanup exact sealed-authority entrypoint is absent.')
    }
    $stoppedProof = [Func[bool]]{
        $null -eq (Get-Process -Id $RuntimeProcessId -ErrorAction SilentlyContinue)
    }
    $method.Invoke($null, @($LaboratoryRoot, $GamePathsSaves, $RunId, $RuntimeProcessId, $ManifestSha256, $SealSha256, $stoppedProof))
    [ordered]@{
        Schema = 'l00c-legacy-prejournal-cleanup-launch-v1'
        Status = 'CLEANED'
        RunId = $RunId
        RuntimeProcessId = $RuntimeProcessId
        ManifestSha256 = $ManifestSha256
        SealSha256 = $SealSha256
    } | ConvertTo-Json -Compress
}
catch {
    $failure = $_.Exception
    if ($failure -is [Reflection.TargetInvocationException] -and $null -ne $failure.InnerException) {
        $failure = $failure.InnerException
    }
    [ordered]@{
        Schema = 'l00c-legacy-prejournal-cleanup-launch-v1'
        Status = 'REFUSED'
        RunId = $RunId
        RuntimeProcessId = $RuntimeProcessId
        Reason = $failure.GetType().Name
    } | ConvertTo-Json -Compress
    exit 2
}
