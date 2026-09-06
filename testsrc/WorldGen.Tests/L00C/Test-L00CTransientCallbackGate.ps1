[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'
foreach ($dependency in @('VintagestoryAPI.dll', 'VintagestoryLib.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath $dependency))
}
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$gateType = $assembly.GetType('ISRWorldGen.WorldgenProbe.TransientLoadCallbackGate', $false)
if ($null -eq $gateType) {
    throw 'The Debug assembly does not expose the production transient callback gate.'
}

$flags = [Reflection.BindingFlags]'Instance, NonPublic'
$begin = $gateType.GetMethod('Begin', $flags)
$accept = $gateType.GetMethod('Accept', $flags)
$complete = $gateType.GetMethod('Complete', $flags)
$reject = $gateType.GetMethod('Reject', $flags)
$reset = $gateType.GetMethod('Reset', $flags)
$open = $gateType.GetMethod('Open', $flags)
$pending = $gateType.GetProperty('PendingCount', $flags)
if (@($begin, $accept, $complete, $reject, $reset, $open, $pending) -contains $null) {
    throw 'The production transient callback gate contract is incomplete.'
}

function New-Gate {
    return [Activator]::CreateInstance($gateType, $true)
}

function Invoke-GateMethod {
    param($Method, $Target, [object[]]$Arguments = @())
    try {
        return $Method.Invoke($Target, $Arguments)
    }
    catch [Reflection.TargetInvocationException] {
        throw $_.Exception.InnerException
    }
}

function Get-PendingCount($Gate) {
    return [int]$pending.GetValue($Gate)
}

$syncCount = 0
$syncGate = New-Gate
$syncReservation = Invoke-GateMethod $begin $syncGate (, [Action]{ $script:syncCount++ })
[void](Invoke-GateMethod $complete $syncGate (, $syncReservation))
if ($syncCount -ne 0 -or (Get-PendingCount $syncGate) -ne 1) {
    throw 'A synchronous callback ran before the range request was accepted.'
}
[void](Invoke-GateMethod $accept $syncGate (, $syncReservation))
[void](Invoke-GateMethod $complete $syncGate (, $syncReservation))
if ($syncCount -ne 1 -or (Get-PendingCount $syncGate) -ne 0) {
    throw 'A synchronous callback was not invoked exactly once after acceptance.'
}

$asyncCount = 0
$asyncGate = New-Gate
$asyncReservation = Invoke-GateMethod $begin $asyncGate (, [Action]{ $script:asyncCount++ })
[void](Invoke-GateMethod $accept $asyncGate (, $asyncReservation))
[void](Invoke-GateMethod $complete $asyncGate (, $asyncReservation))
[void](Invoke-GateMethod $complete $asyncGate (, $asyncReservation))
if ($asyncCount -ne 1 -or (Get-PendingCount $asyncGate) -ne 0) {
    throw 'An asynchronous or duplicate callback was not single-shot.'
}

$lateCount = 0
$lateGate = New-Gate
$lateReservation = Invoke-GateMethod $begin $lateGate (, [Action]{ $script:lateCount++ })
[void](Invoke-GateMethod $accept $lateGate (, $lateReservation))
$transitionCancelled = [int](Invoke-GateMethod $reset $lateGate)
[void](Invoke-GateMethod $complete $lateGate (, $lateReservation))
if ($transitionCancelled -ne 1 -or $lateCount -ne 0 -or (Get-PendingCount $lateGate) -ne 0) {
    throw 'A late callback survived a world transition reset.'
}

$errorCount = 0
$errorGate = New-Gate
$errorReservation = Invoke-GateMethod $begin $errorGate (, [Action]{ $script:errorCount++ })
[void](Invoke-GateMethod $reject $errorGate (, $errorReservation))
[void](Invoke-GateMethod $accept $errorGate (, $errorReservation))
[void](Invoke-GateMethod $complete $errorGate (, $errorReservation))
if ($errorCount -ne 0 -or (Get-PendingCount $errorGate) -ne 0) {
    throw 'A rejected range request retained a callable completion.'
}

$disposeCount = 0
$disposeGate = New-Gate
$disposeReservation = Invoke-GateMethod $begin $disposeGate (, [Action]{ $script:disposeCount++ })
[void](Invoke-GateMethod $accept $disposeGate (, $disposeReservation))
$disposeCancelled = [int](Invoke-GateMethod $reset $disposeGate)
[void](Invoke-GateMethod $complete $disposeGate (, $disposeReservation))
if ($disposeCancelled -ne 1 -or $disposeCount -ne 0 -or (Get-PendingCount $disposeGate) -ne 0) {
    throw 'Dispose-style reset did not neutralize its pending callback.'
}

[void](Invoke-GateMethod $open $lateGate)
$nextCount = 0
$nextReservation = Invoke-GateMethod $begin $lateGate (, [Action]{ $script:nextCount++ })
[void](Invoke-GateMethod $accept $lateGate (, $nextReservation))
[void](Invoke-GateMethod $complete $lateGate (, $lateReservation))
[void](Invoke-GateMethod $complete $lateGate (, $nextReservation))
if ($nextCount -ne 1 -or $lateCount -ne 0 -or (Get-PendingCount $lateGate) -ne 0) {
    throw 'A stale reservation interfered with the next world run.'
}

[ordered]@{
    TestId = 'L00-C-TRANSIENT-CALLBACK-GATE'
    Status = 'PASS'
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    SynchronousBeforeAccept = 0
    SynchronousAfterAccept = $syncCount
    AsynchronousInvocationCount = $asyncCount
    DuplicateInvocationCount = $asyncCount
    TransitionCancelled = $transitionCancelled
    LateAfterTransitionCount = $lateCount
    RejectedInvocationCount = $errorCount
    DisposeCancelled = $disposeCancelled
    LateAfterDisposeCount = $disposeCount
    NextRunInvocationCount = $nextCount
    PendingCount = Get-PendingCount $lateGate
} | ConvertTo-Json -Depth 4
