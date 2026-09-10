[CmdletBinding()]
param([string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$sources = @(
    (Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\L00CScenarioModel.cs'),
    (Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLifecycleShutdownBarrier.cs'),
    (Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLevelFinalizeGate.cs'),
    (Join-Path $root 'L00CStrictEvidenceJson.cs'),
    (Join-Path $root 'L00CCampaignStorage.cs'),
    (Join-Path $root 'L00CMenuActionDriver.cs'),
    (Join-Path $root 'L00CNativeOpenControllerCompileStub.cs'),
    (Join-Path $root 'L00CMenuActionLaboratoryHost.cs'),
    (Join-Path $root 'L00CT00LifecycleValidator.cs'),
    (Join-Path $root 'L00CScenarioCompositionOracle.cs')
)
$controller = Join-Path $root 'L00CProcessCampaignController.cs'
$bootstrap = Join-Path $root 'L00CFixtureBootstrap.cs'
$levelGate = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CLevelFinalizeGate.cs'
foreach ($path in @($sources + $controller + $bootstrap + $csc)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "L00-C S4 composition input is missing: $path" }
}

$controllerText = Get-Content -LiteralPath $controller -Raw
$bootstrapText = Get-Content -LiteralPath $bootstrap -Raw
$hostText = Get-Content -LiteralPath (Join-Path $root 'L00CMenuActionLaboratoryHost.cs') -Raw
$gateText = Get-Content -LiteralPath $levelGate -Raw
foreach ($required in @(
    'new L00CNativeScenarioHostAdapter(new L00CProductionScenarioComposition(campaign))',
    'TryGetCompletedEvidence',
    'ConfirmSaveCommitted',
    'RecordSaveCommitted',
    'campaign.BeginCycling();evidence.Complete(state);campaign.SealForExternalCleanup();',
    't00-06-observations.jsonl',
    't00-06-registrations.jsonl',
    't00-06-run.json')) {
    if (-not (($controllerText + $hostText).Contains($required))) { throw "L00-C S4 production composition lost: $required" }
}
foreach ($forbidden in @('two-save', 'two saves', 'RebindCurrentCell', 'OnClickCellLeft', 'currentOwnedSaves')) {
    if ($bootstrapText.Contains($forbidden) -or $hostText.Contains($forbidden)) { throw "L00-C S4 retained legacy bootstrap/cell contract: $forbidden" }
}
if (-not $gateText.Contains('fixtureSequence > 15') -or $gateText.Contains('1..8')) {
    throw 'L00-C LevelFinalize gate was not migrated to fifteen exact sessions.'
}

$out = Join-Path ([IO.Path]::GetTempPath()) ('l00c-s4-composition-' + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    & $csc /nologo /target:library "/define:DEBUG,L00C_STANDALONE_ORACLE" /nullable:enable /warnaserror /langversion:latest "/out:$out" $sources
    if ($LASTEXITCODE -ne 0) { throw 'L00-C S4 composition oracle compilation failed.' }
    $assembly = [Reflection.Assembly]::LoadFrom($out)
    $method = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CScenarioCompositionOracle', $true).GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $method -or [int]$method.Invoke($null, @()) -ne 0) { throw 'L00-C S4 composition oracle failed.' }
}
finally { if (Test-Path -LiteralPath $out) { try { Remove-Item -LiteralPath $out -Force } catch { } } }

[ordered]@{
    TestId = 'L00-C-S4-SCENARIO-COMPOSITION'
    Status = 'PASS'
    Iterations = 5
    Sessions = 15
    DedicatedSaves = 10
    DispatchGate = 'all fifteen next actions withheld until SaveCommitted plus registration release'
    NegativeCases = 'stale equal-value DTO; duplicate Ready/JSONL; event during closing; validator refusal before exact cleanup; interrupted abort cleanup'
    Scope = 'Controlled deterministic scheduler/composition and independent validator over temporary files; no VS, F5, Vintage Story runtime, or T00-06 PASS claim.'
} | ConvertTo-Json -Depth 4
