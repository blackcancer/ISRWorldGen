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

function Get-TypeShapeFromPortableExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$FullTypeName
    )

    $stream = [IO.File]::OpenRead($Path)
    $peReader = $null
    try {
        $peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)

        foreach ($handle in $metadata.TypeDefinitions) {
            $definition = $metadata.GetTypeDefinition($handle)
            $namespace = $metadata.GetString($definition.Namespace)
            $name = $metadata.GetString($definition.Name)
            $candidate = if ([string]::IsNullOrEmpty($namespace)) { $name } else { "$namespace.$name" }

            if ($candidate -eq $FullTypeName) {
                $methods = @($definition.GetMethods() | ForEach-Object {
                    $metadata.GetString($metadata.GetMethodDefinition($_).Name)
                })
                $fields = @($definition.GetFields() | ForEach-Object {
                    $metadata.GetString($metadata.GetFieldDefinition($_).Name)
                })

                return [PSCustomObject]@{
                    Present = $true
                    Methods = $methods
                    Fields = $fields
                }
            }
        }

        return [PSCustomObject]@{
            Present = $false
            Methods = @()
            Fields = @()
        }
    }
    finally {
        if ($null -ne $peReader) {
            $peReader.Dispose()
        }
        $stream.Dispose()
    }
}

$probeType = Get-TypeShapeFromPortableExecutable -Path $assemblyPath -FullTypeName 'ISRWorldGen.L00BDebugProbeModSystem'
if ($Configuration -eq 'Debug') {
    if (-not $probeType.Present) {
        throw 'The L00-B ModSystem type must be present in the Debug assembly.'
    }

    foreach ($method in @('StartServerSide', 'RequestProbeColumn', 'OnChunkColumnGeneration')) {
        if ($method -notin $probeType.Methods) {
            throw "Debug assembly is missing expected L00-B method: $method"
        }
    }

    if ('exceptionIssued' -notin $probeType.Fields) {
        throw 'Debug assembly is missing the controlled-exception guard field.'
    }
}
elseif ($probeType.Present) {
    throw 'The complete L00-B ModSystem type must be absent from the Release assembly.'
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
    BinaryInspection = [ordered]@{
        ProbeTypePresent = [bool]$probeType.Present
        ExpectedPresent = ($Configuration -eq 'Debug')
        Methods = @($probeType.Methods)
        Fields = @($probeType.Fields)
    }
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
