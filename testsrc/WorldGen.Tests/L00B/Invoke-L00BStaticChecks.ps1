[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj'
$probePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\DebugProbe\L00BDebugProbeModSystem.cs'
$packagePath = Join-Path $RepositoryRoot "src\WorldGen.VintageStory\bin\$Configuration\Mods\isrworldgen"
$assemblyPath = Join-Path $packagePath 'ISRWorldGen.dll'
$symbolsPath = Join-Path $packagePath 'ISRWorldGen.pdb'

if (-not (Test-Path -LiteralPath $probePath -PathType Leaf)) {
    throw "L00-B probe source is missing: $probePath"
}

$source = Get-Content -LiteralPath $probePath -Raw
$requiredSourceFragments = @(
    'override void StartServerSide(ICoreServerAPI api)',
    'api.Event.ChunkColumnGeneration(',
    'EnumWorldGenPass.Terrain',
    '"standard"',
    'bool generateProbeColumn = false;',
    'EnumServerRunPhase.RunGame',
    'LoadChunkColumnPriority(',
    'void OnChunkColumnGeneration(IChunkColumnGenerateRequest request)',
    '#if DEBUG',
    'bool throwRequested = false;',
    'Interlocked.CompareExchange',
    'throw new InvalidOperationException'
)

foreach ($fragment in $requiredSourceFragments) {
    if (-not $source.Contains($fragment)) {
        throw "Required probe fragment was not found: $fragment"
    }
}

$buildOutput = & dotnet build $projectPath -c $Configuration --nologo --no-restore 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Debug probe build failed:`n$($buildOutput -join [Environment]::NewLine)"
}

foreach ($artifact in @($assemblyPath, $symbolsPath)) {
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Expected build artifact is missing: $artifact"
    }
}

$localProps = [xml](Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.local.props') -Raw)
$vintageStoryPath = [string]$localProps.Project.PropertyGroup.VintageStoryPath
$apiPath = Join-Path $vintageStoryPath 'VintagestoryAPI.dll'
$apiAssembly = [Reflection.Assembly]::LoadFrom($apiPath)
$delegateType = $apiAssembly.GetType('Vintagestory.API.Common.ChunkColumnGenerationDelegate', $true)
$invoke = $delegateType.GetMethod('Invoke')
$parameters = @($invoke.GetParameters())

if ($invoke.ReturnType -ne [void] -or $parameters.Count -ne 1 -or
    $parameters[0].ParameterType.FullName -ne 'Vintagestory.API.Server.IChunkColumnGenerateRequest') {
    throw "Unexpected local ChunkColumnGenerationDelegate signature: $invoke"
}

$result = [ordered]@{
    TestId = 'L00B-STATIC'
    Status = 'PASS'
    Utc = [DateTime]::UtcNow.ToString('o')
    Configuration = $Configuration
    ApiAssembly = [IO.Path]::GetFileName($apiPath)
    CallbackSignature = 'void (IChunkColumnGenerateRequest request)'
    WorldGenPass = 'Terrain'
    WorldType = 'standard'
    ExceptionProbe = [ordered]@{
        DebugOnly = $true
        OffByDefault = $true
        Activation = 'debugger-local-mutation-only'
        RequestedColumns = 1
    }
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    PdbSha256 = (Get-FileHash -LiteralPath $symbolsPath -Algorithm SHA256).Hash
    BuildSummary = ($buildOutput | Select-String -Pattern '0 Warning|0 Error' | ForEach-Object { $_.Line.Trim() })
}

$result | ConvertTo-Json -Depth 6
