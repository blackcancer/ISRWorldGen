[CmdletBinding()]
param([string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ledger = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLifecycleRegistrationLedger.cs'
$oracle = Join-Path $PSScriptRoot 'L00CLifecycleRegistrationLedgerOracle.cs'
$probe = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
foreach ($path in @($ledger,$oracle,$probe,$csc)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "L00-C registration release test is missing $path" } }
$probeText = Get-Content -LiteralPath $probe -Raw
foreach ($required in @('callbacks.Ledger.RecordRegistered(L00CLifecycleRegistrationKind.InitWorldGenerator)',
    'callbacks.Ledger.RecordRegistered(L00CLifecycleRegistrationKind.GameWorldSave)',
    'callbacks.Ledger.RecordRegistered(L00CLifecycleRegistrationKind.Tick)',
    'callbacks?.BeginClosing();','independentlyUnregistered: false','independentlyUnregistered: true',
    'WeakReference<L00CWorldgenProbeModSystem>','serverApi.Event.GameWorldSave -= callbacks.GameWorldSave',
    'serverApi.Event.UnregisterGameTickListener(tickListenerId)')) {
    if (-not $probeText.Contains($required)) { throw "L00-C registration/release wiring lost: $required" }
}
$gameIndex=$probeText.IndexOf('serverApi.Event.GameWorldSave -= callbacks.GameWorldSave',[StringComparison]::Ordinal)
$tickIndex=$probeText.IndexOf('serverApi.Event.UnregisterGameTickListener(tickListenerId)',[StringComparison]::Ordinal)
if($gameIndex -lt 0 -or $tickIndex -lt 0 -or $gameIndex -ge $tickIndex){throw 'L00-C independent unregistration order is not explicit.'}
$out=Join-Path ([IO.Path]::GetTempPath()) ('l00c-registration-release-'+[Guid]::NewGuid().ToString('N')+'.dll')
try {
    & $csc /nologo /target:library "/define:DEBUG,L00C_STANDALONE_ORACLE" /nullable:enable /warnaserror /langversion:latest "/out:$out" $ledger $oracle
    if($LASTEXITCODE -ne 0){throw 'L00-C registration release oracle compilation failed.'}
    $assembly=[Reflection.Assembly]::LoadFrom($out)
    $method=$assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CLifecycleRegistrationLedgerOracle',$true).GetMethod('Run',[Reflection.BindingFlags]'Static,NonPublic')
    if($null -eq $method -or $method.Invoke($null,@()) -ne 0){throw 'L00-C registration release oracle failed.'}
}
finally { if(Test-Path -LiteralPath $out){try{Remove-Item -LiteralPath $out -Force}catch{}} }
[ordered]@{TestId='L00-C-LIFECYCLE-REGISTRATION-RELEASE';Status='PASS';Registrations=3;OwnerRetention='weak trampoline cleared before external unregistration';IndependentRelease='GameWorldSave and tick attempted independently; InitWorldGenerator trampoline releases owner because API has no unregister';Negative='duplicate registration, closing callback, and missing unregistration rejected';Scope='Static wiring plus deterministic ledger oracle; no runtime/VS/F5.'}|ConvertTo-Json -Compress
