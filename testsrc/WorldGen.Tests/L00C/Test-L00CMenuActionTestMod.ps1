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
$product = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj'
$output = Join-Path $RepositoryRoot '.local\L00C\menu-action-testmod\Debug\isrworldgenl00clab'

foreach ($path in @($project, $modSystem, $laboratoryHost, $driver, (Join-Path $GamePath 'VintagestoryAPI.dll'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required L00-C laboratory input is missing: $path" }
}
if (Select-String -LiteralPath $project -Pattern 'ProjectReference|src\\WorldGen' -Quiet) {
    throw 'Test mod must not reference product projects.'
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

& dotnet build $project --configuration Debug --nologo "-p:VintageStoryPath=$GamePath"
if ($LASTEXITCODE -ne 0) { throw "L00-C test mod Debug build failed with exit code $LASTEXITCODE." }
foreach ($packageFile in @('ISRWorldGen.L00C.MenuActionLab.dll', 'modinfo.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $output $packageFile) -PathType Leaf)) { throw "L00-C local package is incomplete: $packageFile" }
}
if (Select-String -LiteralPath $product -SimpleMatch 'L00CMenuActionLab' -Quiet) { throw 'Production package must not reference the L00-C test mod.' }
[ordered]@{
    TestId = 'L00-C-MENU-HOST-TESTMOD-STATIC'
    Status = 'PASS'
    Scope = 'Debug test-mod compilation and isolation only; no client runtime or authentication was exercised.'
    Package = $output
    ModSystem = 'ISRWorldGen.L00C.Laboratory.L00CMenuActionLabModSystem'
    Utc = [DateTimeOffset]::UtcNow.ToString('o')
} | ConvertTo-Json -Depth 4
