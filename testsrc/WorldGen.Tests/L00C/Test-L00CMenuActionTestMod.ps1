[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

$project = Join-Path $PSScriptRoot 'L00CMenuActionLabMod.csproj'
$modSystem = Join-Path $PSScriptRoot 'L00CMenuActionLabModSystem.cs'
$laboratoryHost = Join-Path $PSScriptRoot 'L00CMenuActionLaboratoryHost.cs'
$driver = Join-Path $PSScriptRoot 'L00CMenuActionDriver.cs'
$bootstrap = Join-Path $PSScriptRoot 'L00CFixtureBootstrap.cs'
$product = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj'
$output = Join-Path $RepositoryRoot '.local\L00C\menu-action-testmod\Debug\isrworldgenl00clab'

foreach ($path in @($project, $modSystem, $laboratoryHost, $driver, $bootstrap, (Join-Path $GamePath 'VintagestoryAPI.dll'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required L00-C laboratory input is missing: $path" }
}
if (Select-String -LiteralPath $project -Pattern 'ProjectReference|src\\WorldGen' -Quiet) {
    throw 'Test mod must not reference product projects.'
}
if (-not (Select-String -LiteralPath $project -SimpleMatch 'BeforeTargets="PrepareForBuild"' -Quiet)) {
    throw 'Release rejection must run before PrepareForBuild, before C# compilation.'
}
if (Select-String -LiteralPath @($modSystem, $laboratoryHost, $driver) -Pattern 'SendKeys|mouse_event|keybd_event|WindowsInput|Process\.Start|Start-Process|GetCredential|AuthenticationHeader|Token|Password' -Quiet) {
    throw 'L00-C laboratory host must not synthesize input, start a process, or access authentication material.'
}
foreach ($required in @('public sealed class L00CMenuActionLabModSystem', 'StartClientSide(ICoreClientAPI api)', 'RegisterGameTickListener', 'Debugger.IsAttached', 'ISR_L00C_LAB', 'ISR_L00C_LAB_ROOT', 'TryAdvance(api)')) {
    if (-not (Select-String -LiteralPath $modSystem -SimpleMatch $required -Quiet)) { throw "Missing test-mod contract: $required" }
}
foreach ($required in @('TryAdvance(object api)', 'TryFindMenuLeft', 'TryFindSingleplayerScreen', 'TryFindClientSession', 'stableTicks < 3', 'RequiredPrimaryCycles = 5')) {
    if (-not (Select-String -LiteralPath @($laboratoryHost, $driver) -SimpleMatch $required -Quiet)) { throw "Missing client-cycle contract: $required" }
}
foreach ($required in @('L00CFixtureBootstrap', 'CreateFixtureWorld', 'ConnectToSingleplayer', 'StartServerArgs', 'GuiScreenSingleplayer.entries', 'ClientCellBindingConfirmed', 'File.Exists(fixture.SavePath)', 'FileMode.CreateNew', 'WaitPrimaryMenu', 'WaitSecondaryCell', 'L00CMenuActionLaboratoryHost.Open')) {
    if (-not (Select-String -LiteralPath @($bootstrap, $driver, $modSystem) -SimpleMatch $required -Quiet)) { throw "Missing native bootstrap contract: $required" }
}
if (Select-String -LiteralPath $bootstrap -Pattern 'OnClickCellLeft|\[.*CellIndex.*\]|ClientSaveCellIndex\s*=\s*[0-9]' -Quiet) { throw 'Bootstrap must observe a live save cell; it must not infer or invoke a cell index.' }

& dotnet build $project --configuration Debug --nologo "-p:VintageStoryPath=$GamePath"
if ($LASTEXITCODE -ne 0) { throw "L00-C test mod Debug build failed with exit code $LASTEXITCODE." }
foreach ($packageFile in @('ISRWorldGen.L00C.MenuActionLab.dll', 'modinfo.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $output $packageFile) -PathType Leaf)) { throw "L00-C local package is incomplete: $packageFile" }
}
if (Select-String -LiteralPath $product -SimpleMatch 'L00CMenuActionLab' -Quiet) { throw 'Production package must not reference the L00-C test mod.' }

$releaseOutput = (& dotnet build $project --configuration Release --nologo "-p:VintageStoryPath=$GamePath" 2>&1 | Out-String)
$releaseExitCode = $LASTEXITCODE
if ($releaseExitCode -eq 0) { throw 'L00-C test mod Release build unexpectedly succeeded.' }
if ($releaseOutput -notmatch [regex]::Escape('L00-C menu laboratory mod is Debug-only.')) {
    throw "L00-C test mod Release build did not fail at the explicit Debug-only gate:$([Environment]::NewLine)$releaseOutput"
}
if ($releaseOutput -match 'CS0169') { throw "L00-C test mod Release build reached C# compilation instead of the Debug-only gate:$([Environment]::NewLine)$releaseOutput" }
[ordered]@{
    TestId = 'L00-C-MENU-HOST-TESTMOD-STATIC'
    Status = 'PASS'
    Scope = 'Debug test-mod compilation and isolation only; no client runtime or authentication was exercised.'
    Package = $output
    ModSystem = 'ISRWorldGen.L00C.Laboratory.L00CMenuActionLabModSystem'
    ReleaseRejection = 'PASS: RejectNonDebugLaboratoryBuild before PrepareForBuild'
    Utc = [DateTimeOffset]::UtcNow.ToString('o')
} | ConvertTo-Json -Depth 4
