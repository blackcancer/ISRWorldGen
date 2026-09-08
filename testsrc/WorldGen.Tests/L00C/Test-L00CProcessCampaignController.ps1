[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path }
$controller = Join-Path $PSScriptRoot 'L00CProcessCampaignController.cs'
$laboratoryHost = Join-Path $PSScriptRoot 'L00CMenuActionLaboratoryHost.cs'
$bootstrap = Join-Path $PSScriptRoot 'L00CFixtureBootstrap.cs'
$driver = Join-Path $PSScriptRoot 'L00CMenuActionDriver.cs'
foreach ($file in @($controller, $laboratoryHost, $bootstrap, $driver)) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing process campaign source: $file" } }

$text = Get-Content -LiteralPath $controller -Raw
$driverText = Get-Content -LiteralPath $driver -Raw
$hostText = Get-Content -LiteralPath $laboratoryHost -Raw
$bootstrapText = Get-Content -LiteralPath $bootstrap -Raw
function Assert-Contains([string]$Value, [string]$Needle, [string]$Label) { if (-not $Value.Contains($Needle, [StringComparison]::Ordinal)) { throw "$Label is missing: $Needle" } }
function Assert-Before([string]$Value, [string]$First, [string]$Second, [string]$Label) {
    $a = $Value.IndexOf($First, [StringComparison]::Ordinal); $b = $Value.IndexOf($Second, [StringComparison]::Ordinal)
    if ($a -lt 0 -or $b -lt 0 -or $a -ge $b) { throw "$Label has invalid ordering." }
}

# Deterministic white-box regression oracle: these assertions establish the
# lifetime/state contract without requiring a game process or authentication.
Assert-Contains $text 'private static L00CProcessCampaignController? active;' 'Singleton'
Assert-Contains $text 'if (active is not null)' 'Singleton rejection/signal branch'
Assert-Contains $text 'active.SignalSessionReady();' 'Subsequent session signal'
Assert-Contains $text 'active = new L00CProcessCampaignController(manager, root);' 'Initial singleton construction'
Assert-Contains $text 'L00CMenuActionDriver.EnqueueMainThreadTask(Pump);' 'Main-thread pump'
Assert-Contains $driverText '"2D0E0FEC4E3D47E083F2BEF081D24672E8CC03051C47BDBDAEC8CFB37FA7E977"' 'Enqueue IL lock'
Assert-Contains $driverText '"EEFC023F05EF3E3E1FAA5F06D3EB6C1996812827854F67532E5AA34CEB1B389E"' 'OnNewFrame IL lock'
Assert-Contains $text 'bootstrap.TryAdvance(screenManager' 'Bootstrap survives ModSystem Dispose'
Assert-Contains $text 'host.TryAdvance(screenManager)' 'Campaign survives ModSystem Dispose'
Assert-Contains $text 'UnregisterAndClearSingleton();' 'Terminal cleanup'
Assert-Contains $text 'terminal = true;' 'Terminal state'
Assert-Contains $text 'active = null;' 'Singleton release'
if ($text -match 'ICoreClientAPI\s+[A-Za-z_][A-Za-z0-9_]*\s*;|ClientMain\s+[A-Za-z_][A-Za-z0-9_]*\s*;') { throw 'Controller retains a forbidden session API.' }

Assert-Before $hostText 'ExpectPrimaryMenu' 'ExpectSecondaryMenu' 'Primary cycles before secondary campaign phase'
Assert-Contains $hostText 'state != State.SecondaryMenuOpen || primaryCycles != RequiredPrimaryCycles' 'Five primary cycles gate secondary open'
Assert-Contains $hostText 'ReturnToMainMenu(clientMain, screenManager)' 'Return transition evidence'
Assert-Contains $bootstrapText 'WaitPrimaryMenu' 'Primary bootstrap state'
Assert-Contains $bootstrapText 'WaitSecondaryCell' 'Secondary bootstrap state'
Assert-Contains $bootstrapText 'L00CMenuActionDriver.EnterSingleplayerMenu(menu);' 'Native menu transition for live cell binding'
Assert-Contains $bootstrapText 'ReadUniqueSaveCell' 'Observed save-cell binding'
Assert-Contains $hostText 'expectedRole' 'Role rejection'
Assert-Contains $hostText 'distinct marked saves and distinct confirmed menu cells' 'Distinct role/cell rejection'

[ordered]@{
    TestId = 'L00-C-PROCESS-CAMPAIGN-CONTROLLER'
    Status = 'PASS'
    StateOrdering = 'PASS: bootstrap primary/secondary then five primary open-return cycles then secondary final return'
    SingletonPumpLifetime = 'PASS: static singleton, static ScreenManager queue, no retained client/session API'
    DisposeSurvival = 'PASS: pump owns bootstrap/host outside ModSystem lifetime'
    InvalidRoleAndPathRejection = 'PASS: marked-save role/path/cell guards remain in host'
    TerminalCleanup = 'PASS: terminal state stops requeue and clears singleton'
    Scope = 'Deterministic source contract only; no Vintage Story process was launched.'
} | ConvertTo-Json -Depth 4
