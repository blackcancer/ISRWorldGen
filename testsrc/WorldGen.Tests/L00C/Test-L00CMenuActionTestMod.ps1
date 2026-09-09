[CmdletBinding()]
param([string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path, [string]$GamePath = 'D:\Jeux\Vintagestory')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj'
$testProject = Join-Path $RepositoryRoot 'testsrc\WorldGen.Tests\WorldGen.Tests.csproj'
$sources = @('L00CMenuActionLabModSystem.cs','L00CMenuActionLaboratoryHost.cs','L00CMenuActionDriver.cs','L00CFixtureBootstrap.cs','L00CCampaignStorage.cs','L00CCampaignInstallFailure.cs','L00CProcessCampaignController.cs','L00CNativeFixtureOracle.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
foreach ($file in @($project, $testProject) + $sources) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required in-process L00-C input is missing: $file" } }
$projectXml = Get-Content -LiteralPath $project -Raw; $testXml = Get-Content -LiteralPath $testProject -Raw
foreach ($source in $sources) {
    $leaf = Split-Path $source -Leaf
    if (-not $projectXml.Contains($leaf) -or $projectXml -notmatch 'Condition=.*Configuration.*Debug') { throw "Product Debug allowlist is missing $leaf." }
    if (-not $testXml.Contains('<Compile Remove="L00C\' + $leaf + '"')) { throw "Test project must exclude linked harness source $leaf." }
}
foreach ($removed in @('L00CMenuActionLabMod.csproj','modinfo.json','Set-L00CTestModMount.ps1')) { if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $removed)) { throw "Retired parallel-mod artifact remains: $removed" } }
$system = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'L00CMenuActionLabModSystem.cs') -Raw
foreach ($required in @('public sealed class L00CMenuActionLabModSystem : ModSystem','if (!string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal)) return;','Debugger.IsAttached','ISR_L00C_LAB_ROOT','L00CProcessCampaignController.InstallOrSignal')) { if (-not $system.Contains($required)) { throw "Missing in-process harness guard: $required" } }
if ($system.IndexOf('if (!string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal)) return;', [StringComparison]::Ordinal) -gt $system.IndexOf('RequireLaboratoryRoot', [StringComparison]::Ordinal)) { throw 'The laboratory switch must be evaluated before root or debugger validation.' }
foreach ($source in $sources) { if (Select-String -LiteralPath $source -Pattern 'SendKeys|mouse_event|keybd_event|WindowsInput|Process\.Start|Start-Process|GetCredential|AuthenticationHeader|Password' -Quiet) { throw "Harness source must not synthesize input, start a process, or access authentication material: $source" } }
& dotnet build $project -c Debug --nologo "-p:VintageStoryPath=$GamePath"; if ($LASTEXITCODE -ne 0) { throw "Debug ISRWorldGen build failed with exit code $LASTEXITCODE." }
& dotnet build $project -c Release --nologo "-p:VintageStoryPath=$GamePath"; if ($LASTEXITCODE -ne 0) { throw "Release ISRWorldGen build failed with exit code $LASTEXITCODE." }
$debugDll = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Debug\Mods\isrworldgen\ISRWorldGen.dll'; $releaseDll = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\bin\Release\Mods\isrworldgen\ISRWorldGen.dll'
foreach ($path in @($debugDll,$releaseDll)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "ISRWorldGen binary is missing: $path" } }
Add-Type -Path (Join-Path $GamePath 'Lib\Mono.Cecil.dll')
function Get-Types([string]$Path) { $module = [Mono.Cecil.ModuleDefinition]::ReadModule($Path); try { @($module.Types | ForEach-Object FullName) } finally { $module.Dispose() } }
$debugTypes = Get-Types $debugDll; $releaseTypes = Get-Types $releaseDll; $harnessType = 'ISRWorldGen.L00C.Laboratory.L00CMenuActionLabModSystem'
if ($debugTypes -notcontains $harnessType) { throw 'Debug ISRWorldGen.dll does not contain the linked L00-C harness.' }
if ($releaseTypes -contains $harnessType -or (@($releaseTypes | Where-Object { $_ -like 'ISRWorldGen.L00C.Laboratory.*' })).Count -ne 0) { throw 'Release ISRWorldGen.dll contains laboratory harness code.' }
$output = Split-Path $debugDll -Parent
if (Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Name -match 'MenuActionLab|isrworldgenl00clab' -or $_.Name -eq 'modinfo.json' -and $_.DirectoryName -match 'isrworldgenl00clab' }) { throw 'Debug output contains a retired parallel laboratory package.' }
[ordered]@{ TestId='L00-C-INPROCESS-HARNESS-BINARY'; Status='PASS'; Scope='Debug/Release ISRWorldGen binary composition only; no client runtime or authentication was exercised.'; DebugHarness=$harnessType; ReleaseHarnessAbsent=$true; Utc=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json -Compress
