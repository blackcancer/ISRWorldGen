[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$LaboratoryRoot,
    [Parameter(Mandatory=$true)][string]$GamePathsSaves,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{32}$')][string]$RunId,
    [Parameter(Mandatory=$true)][int]$RuntimeProcessId,
    [Parameter(Mandatory=$true)][string]$DebugAssemblyPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# This is deliberately a post-runtime launcher: it never starts, signals, or
# attaches to Vintage Story. A live PID is an immediate refusal.
if (Get-Process -Id $RuntimeProcessId -ErrorAction SilentlyContinue) { throw 'L00-C cleanup refused: target runtime process is still alive.' }
if (-not (Test-Path -LiteralPath $DebugAssemblyPath -PathType Leaf)) { throw 'L00-C cleanup assembly is absent.' }
try {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DebugAssemblyPath).Path)
    $type = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CCampaignStorage', $true)
    $method = $type.GetMethod('CleanupAfterRuntimeStopped', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $method) { throw 'L00-C cleanup entrypoint is absent.' }
    $proof = [Func[bool]]{ $true }
    $method.Invoke($null, @($LaboratoryRoot, $GamePathsSaves, $RunId, $RuntimeProcessId, $proof))
    [ordered]@{ Schema='l00c-post-runtime-cleanup-v1'; Status='CLEANED'; RunId=$RunId; RuntimeProcessId=$RuntimeProcessId; Utc=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json -Compress
}
catch [Reflection.TargetInvocationException] {
    [ordered]@{ Schema='l00c-post-runtime-cleanup-v1'; Status='REFUSED'; RunId=$RunId; RuntimeProcessId=$RuntimeProcessId; Reason=$_.Exception.InnerException.GetType().Name; Utc=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json -Compress
    exit 2
}
