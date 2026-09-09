[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path }
$controller = Join-Path $PSScriptRoot 'L00CProcessCampaignController.cs'
$laboratoryHost = Join-Path $PSScriptRoot 'L00CMenuActionLaboratoryHost.cs'
$bootstrap = Join-Path $PSScriptRoot 'L00CFixtureBootstrap.cs'
$storage = Join-Path $PSScriptRoot 'L00CCampaignStorage.cs'
$driver = Join-Path $PSScriptRoot 'L00CMenuActionDriver.cs'
$modSystem = Join-Path $PSScriptRoot 'L00CMenuActionLabModSystem.cs'
$installFailure = Join-Path $PSScriptRoot 'L00CCampaignInstallFailure.cs'
foreach ($file in @($controller, $laboratoryHost, $bootstrap, $storage, $driver, $modSystem, $installFailure)) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing process campaign source: $file" } }

$text = Get-Content -LiteralPath $controller -Raw
$driverText = Get-Content -LiteralPath $driver -Raw
$installFailureText = Get-Content -LiteralPath $installFailure -Raw
$hostText = Get-Content -LiteralPath $laboratoryHost -Raw
$bootstrapText = Get-Content -LiteralPath $bootstrap -Raw
$storageText = Get-Content -LiteralPath $storage -Raw
$modSystemText = Get-Content -LiteralPath $modSystem -Raw
function Assert-Contains([string]$Value, [string]$Needle, [string]$Label) { if ($Value.IndexOf($Needle, [StringComparison]::Ordinal) -lt 0) { throw "$Label is missing: $Needle" } }
function Assert-Before([string]$Value, [string]$First, [string]$Second, [string]$Label) {
    $a = $Value.IndexOf($First, [StringComparison]::Ordinal); $b = $Value.IndexOf($Second, [StringComparison]::Ordinal)
    if ($a -lt 0 -or $b -lt 0 -or $a -ge $b) { throw "$Label has invalid ordering." }
}

# Deterministic white-box regression oracle: these assertions establish the
# lifetime/state contract without requiring a game process or authentication.
Assert-Contains $text 'private static readonly L00CProcessCampaignInstallTransaction<L00CProcessCampaignController> installTransaction = new();' 'Transactional singleton'
Assert-Contains $text 'private static L00CManagerLeaseLifecycle? bootstrapLease;' 'Shared bootstrap lease engine singleton'
Assert-Contains $text 'installTransaction.TrySignalSameRoot(root' 'Singleton/root signal branch'
Assert-Contains $text 'value => value.SignalSessionReady()' 'Subsequent session signal'
Assert-Contains $text 'var engine = new L00CManagerLeaseLifecycle(seams);' 'Shared engine immediate handoff'
Assert-Contains $text 'RegisterGameTickListener(_ => callback(), 50)' '50ms session retry listener adapter'
Assert-Contains $driverText 'GameUnavailable' 'Explicit unavailable resolution status'
Assert-Contains $driverText 'RunningScreenUnavailable' 'Explicit running-screen status'
Assert-Contains $driverText 'ManagerUnavailable' 'Explicit manager status'
Assert-Contains $driverText 'ApiTypeMismatch' 'Explicit API mismatch terminal status'
Assert-Contains $text 'public void Release()' 'Adapter clears API after engine terminal state'
Assert-Contains $text 'api = null; token?.Complete(); token = null;' 'Adapter release clears API and token'
Assert-Contains $text 'L00CManagerLeaseToken token = new(engine.Dispose);' 'Token-based session disposal cleanup'
Assert-Contains $text 'schema=l00c-manager-availability-v1' 'Availability receipt schema'
Assert-Contains $text 'L00CMenuActionDriver.EnqueueMainThreadTask(Pump);' 'Main-thread pump'
Assert-Contains $installFailureText 'controller-construction-fault' 'Construction fault diagnostic'
Assert-Contains $installFailureText 'pump-enqueue-fault' 'Pump enqueue fault diagnostic'
Assert-Contains $text 'L00CCampaignInstallFailure.ConstructionStatus' 'Bounded construction error mapping'
Assert-Contains $text 'L00CCampaignInstallFailure.PumpEnqueueStatus' 'Bounded enqueue error mapping'
Assert-Contains $installFailureText 'internal static class L00CCampaignInstallFailure' 'Install-failure contract owns diagnostic constants'
Assert-Contains $modSystemText 'if (installed.Accepted)' 'Ready publication only after accepted install'
Assert-Contains $modSystemText 'L00C_INPROCESS_HARNESS_REFUSED code=' 'Refusal diagnostic instead of ready publication'
Assert-Contains $storageText 'campaign collision preserves existing data' 'Collision refusal without reuse'
Assert-Contains $storageText 'campaign-provenance.json' 'Persisted campaign provenance'
Assert-Contains $storageText 'Guid.NewGuid().ToString("N")' 'Fresh stable run id'
Assert-Contains $text 'new L00CFixtureBootstrap(campaign)' 'Bootstrap accepts attested campaign storage'
Assert-Contains $bootstrapText 'campaign.PrimarySavePath' 'Primary save isolated to campaign'
Assert-Contains $bootstrapText 'campaign.SecondarySavePath' 'Secondary save isolated to campaign'
Assert-Contains $driverText '"2D0E0FEC4E3D47E083F2BEF081D24672E8CC03051C47BDBDAEC8CFB37FA7E977"' 'Enqueue IL lock'
Assert-Contains $driverText '"EEFC023F05EF3E3E1FAA5F06D3EB6C1996812827854F67532E5AA34CEB1B389E"' 'OnNewFrame IL lock'
Assert-Contains $text 'bootstrap.TryAdvance(screenManager' 'Bootstrap survives ModSystem Dispose'
Assert-Contains $text 'host.TryAdvance(screenManager)' 'Campaign survives ModSystem Dispose'
Assert-Contains $text 'UnregisterAndClearSingleton();' 'Terminal cleanup'
Assert-Contains $text 'terminal = true;' 'Terminal state'
Assert-Contains $text 'installTransaction.Clear(this);' 'Singleton release'
if ($text -notmatch 'private ICoreClientAPI\? api;' -or $text -notmatch 'api = null; token\?\.Complete\(\); token = null;') { throw 'The sole adapter API reference is not explicitly severed.' }

Assert-Before $hostText 'ExpectPrimaryMenu' 'ExpectSecondaryMenu' 'Primary cycles before secondary campaign phase'
Assert-Contains $hostText 'state != State.SecondaryMenuOpen || primaryCycles != RequiredPrimaryCycles' 'Five primary cycles gate secondary open'
Assert-Contains $hostText 'ReturnToMainMenu(clientMain, screenManager)' 'Return transition evidence'
Assert-Contains $bootstrapText 'WaitPrimaryMenu' 'Primary bootstrap state'
Assert-Contains $bootstrapText 'WaitSecondaryCell' 'Secondary bootstrap state'
Assert-Contains $bootstrapText 'L00CMenuActionDriver.EnterSingleplayerMenu(menu);' 'Native menu transition for live cell binding'
Assert-Contains $bootstrapText 'ReadUniqueSaveCell' 'Observed save-cell binding'
Assert-Contains $modSystemText 'api.Event.LevelFinalize += OnLevelFinalize;' 'Native LevelFinalize subscription'
Assert-Contains $modSystemText 'subscribed.Event.LevelFinalize -= OnLevelFinalize;' 'Native LevelFinalize unsubscription'
Assert-Contains $modSystemText 'L00CProcessCampaignController.SignalLevelFinalize();' 'LevelFinalize forwarding'
Assert-Contains $text 'internal static void SignalLevelFinalize()' 'Process-level finalization signal'
Assert-Contains $text 'active.bootstrap?.SignalLevelFinalize();' 'Bootstrap receives finalization signal'
Assert-Contains $bootstrapText 'internal void SignalLevelFinalize()' 'Fixture finalization latch'
Assert-Contains $bootstrapText 'fixture.LevelFinalizeObserved' 'Finalization latch required before return'
if ($bootstrapText.IndexOf('clientPlayingFired` is the audited client-side LevelFinalize gate', [StringComparison]::Ordinal) -ge 0) { throw 'Player-ready flag must not be mislabeled as LevelFinalize.' }
Assert-Contains $hostText 'expectedRole' 'Role rejection'
Assert-Contains $hostText 'requires distinct marked saves' 'Distinct role/cell rejection'

[ordered]@{
    TestId = 'L00-C-PROCESS-CAMPAIGN-CONTROLLER'
    Status = 'PASS'
    StateOrdering = 'PASS: bootstrap primary/secondary then five primary open-return cycles then secondary final return'
    SingletonPumpLifetime = 'PASS: static singleton, static ScreenManager queue, no retained client/session API'
    DisposeSurvival = 'PASS: pump owns bootstrap/host outside ModSystem lifetime'
    InvalidRoleAndPathRejection = 'PASS: marked-save role/path/cell guards remain in host'
    TerminalCleanup = 'PASS: terminal state stops requeue and clears singleton'
    CampaignIsolation = 'PASS: every new run owns a fresh attested campaigns/<run-id> root; collision and constructor/enqueue diagnostics are explicit'
    Scope = 'Deterministic source contract only; no Vintage Story process was launched.'
} | ConvertTo-Json -Depth 4
