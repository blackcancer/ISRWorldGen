[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldgenProbe\L00CWorldgenProbeModSystem.cs'
$projectPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj'

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "L00-C source is missing: $sourcePath"
}

$source = Get-Content -LiteralPath $sourcePath -Raw
$forbidden = @('WipeAllHandlers', 'Task.Run(', 'GetHashCode(', 'new Random(', 'DateTime.Now', 'DateTime.UtcNow')
foreach ($fragment in $forbidden) {
    if ($source.Contains($fragment)) {
        throw "Forbidden L00-C fragment found: $fragment"
    }
}

$required = @(
    '#if DEBUG',
    'GetRegisteredWorldGenHandlers',
    'OnChunkColumnGen',
    'SetBlockUnsafe',
    'SetFluid',
    'WorldGenTerrainHeightMap',
    'RainHeightMap',
    'TopRockIdMap',
    'EnumWorldGenPass.PreDone',
    'preDoneHandlers.Add(metadataFinalizerHandler)',
    'for (int y = 0; y < worldHeight; y++)',
    "canonical.Append(solid).Append(',').Append(fluid)",
    'StoreData',
    'ShutDown',
    'IsNew'
)
foreach ($fragment in $required) {
    if (-not $source.Contains($fragment)) {
        throw "Required L00-C fragment is missing: $fragment"
    }
}

& dotnet build $projectPath -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "L00-C $Configuration build failed with exit code $LASTEXITCODE."
}

$assemblyPath = Join-Path $RepositoryRoot "src\WorldGen.VintageStory\bin\$Configuration\Mods\isrworldgen\ISRWorldGen.dll"
$pdbPath = [IO.Path]::ChangeExtension($assemblyPath, '.pdb')
foreach ($path in @($assemblyPath, $pdbPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Build artifact is missing: $path"
    }
}

$stream = [IO.File]::OpenRead($assemblyPath)
try {
    $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
        $typeNames = foreach ($handle in $metadata.TypeDefinitions) {
            $definition = $metadata.GetTypeDefinition($handle)
            $namespace = $metadata.GetString($definition.Namespace)
            $name = $metadata.GetString($definition.Name)
            if ($namespace) { "$namespace.$name" } else { $name }
        }
    }
    finally {
        $peReader.Dispose()
    }
}
finally {
    $stream.Dispose()
}

$probeType = 'ISRWorldGen.WorldgenProbe.L00CWorldgenProbeModSystem'
$probePresent = @($typeNames | Where-Object { $_ -eq $probeType }).Count -eq 1
if ($Configuration -eq 'Debug' -and -not $probePresent) {
    throw 'Debug assembly does not contain the L00-C probe type.'
}
if ($Configuration -eq 'Release' -and $probePresent) {
    throw 'Release assembly must not contain the L00-C probe type.'
}

$result = [ordered]@{
    TestId = 'L00-C-STATIC'
    Status = 'PASS'
    Utc = [DateTime]::UtcNow.ToString('o')
    Configuration = $Configuration
    ProbeTypePresent = $probePresent
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    PdbSha256 = (Get-FileHash -LiteralPath $pdbPath -Algorithm SHA256).Hash
}

$json = $result | ConvertTo-Json -Depth 4
if ($OutputPath) {
    $directory = Split-Path -Parent $OutputPath
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }
    Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
}

$json
