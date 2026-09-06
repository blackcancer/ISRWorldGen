[CmdletBinding()]
param(
    [string]$GamePath = 'D:\Jeux\Vintagestory',
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$apiPath = Join-Path $GamePath 'VintagestoryAPI.dll'
$apiXmlPath = Join-Path $GamePath 'VintagestoryAPI.xml'
$essentialsPath = Join-Path $GamePath 'Mods\VSEssentials.dll'
$survivalPath = Join-Path $GamePath 'Mods\VSSurvivalMod.dll'

foreach ($path in @($apiPath, $apiXmlPath, $essentialsPath, $survivalPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required local assembly is missing: $path"
    }
}

$api = [Reflection.Assembly]::LoadFrom($apiPath)
$essentials = [Reflection.Assembly]::LoadFrom($essentialsPath)
$survival = [Reflection.Assembly]::LoadFrom($survivalPath)

function Require-Type {
    param([Reflection.Assembly]$Assembly, [string]$Name)

    $type = $Assembly.GetType($Name, $false)
    if ($null -eq $type) {
        throw "Required type is missing: $Name"
    }

    return $type
}

function Require-Method {
    param([Type]$Type, [string]$Name, [string[]]$ParameterTypeNames)

    $method = $Type.GetMethods() | Where-Object {
        if ($_.Name -ne $Name) { return $false }
        $actual = @($_.GetParameters() | ForEach-Object { $_.ParameterType.FullName })
        return ([string]::Join('|', $actual) -eq [string]::Join('|', $ParameterTypeNames))
    } | Select-Object -First 1

    if ($null -eq $method) {
        throw "Required method signature is missing: $($Type.FullName).$Name($([string]::Join(', ', $ParameterTypeNames)))"
    }

    return $method
}

function Require-InstanceMethodAnyVisibility {
    param([Type]$Type, [string]$Name, [string[]]$ParameterTypeNames)

    $flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
    $method = $Type.GetMethods($flags) | Where-Object {
        if ($_.Name -ne $Name) { return $false }
        $actual = @($_.GetParameters() | ForEach-Object { $_.ParameterType.FullName })
        return ([string]::Join('|', $actual) -eq [string]::Join('|', $ParameterTypeNames))
    } | Select-Object -First 1

    if ($null -eq $method) {
        throw "Required native handler signature is missing: $($Type.FullName).$Name($([string]::Join(', ', $ParameterTypeNames)))"
    }

    return $method
}

$eventApi = Require-Type $api 'Vintagestory.API.Server.IServerEventAPI'
$handler = Require-Type $api 'Vintagestory.API.Server.IWorldGenHandler'
$request = Require-Type $api 'Vintagestory.API.Server.IChunkColumnGenerateRequest'
$chunkBlocks = Require-Type $api 'Vintagestory.API.Common.IChunkBlocks'
$mapChunk = Require-Type $api 'Vintagestory.API.Common.IMapChunk'
$saveGame = Require-Type $api 'Vintagestory.API.Server.ISaveGame'
$server = Require-Type $api 'Vintagestory.API.Server.IServerAPI'
$passType = Require-Type $api 'Vintagestory.API.Server.EnumWorldGenPass'

[void](Require-Method $eventApi 'GetRegisteredWorldGenHandlers' @('System.String'))
[void](Require-Method $eventApi 'InitWorldGenerator' @('System.Action', 'System.String'))
[void](Require-Method $eventApi 'ChunkColumnGeneration' @(
    'Vintagestory.API.Common.ChunkColumnGenerationDelegate',
    'Vintagestory.API.Server.EnumWorldGenPass',
    'System.String'
))
[void](Require-Method $chunkBlocks 'SetBlockUnsafe' @('System.Int32', 'System.Int32'))
[void](Require-Method $chunkBlocks 'SetFluid' @('System.Int32', 'System.Int32'))
[void](Require-Method $saveGame 'GetData' @('System.String'))
[void](Require-Method $saveGame 'StoreData' @('System.String', 'System.Byte[]'))
[void](Require-Method $server 'ShutDown' @())
$apiXml = Get-Content -LiteralPath $apiXmlPath -Raw
$columnOwnershipSignatures = @(
    'M:Vintagestory.API.Server.IWorldManagerAPI.GetMapChunk(System.Int32,System.Int32)',
    'M:Vintagestory.API.Server.IWorldManagerAPI.GetChunk(System.Int32,System.Int32,System.Int32)',
    'M:Vintagestory.API.Server.IWorldManagerAPI.LoadChunkColumnPriority(System.Int32,System.Int32,Vintagestory.API.Server.ChunkLoadOptions)',
    'M:Vintagestory.API.Server.IWorldManagerAPI.LoadChunkColumnPriority(System.Int32,System.Int32,System.Int32,System.Int32,Vintagestory.API.Server.ChunkLoadOptions)',
    'M:Vintagestory.API.Server.IWorldManagerAPI.UnloadChunkColumn(System.Int32,System.Int32)'
)
foreach ($signature in $columnOwnershipSignatures) {
    if (-not $apiXml.Contains($signature)) {
        throw "Required local API signature is missing: $signature"
    }
}

$chunkHandlerProperty = $handler.GetProperty('OnChunkColumnGen')
if ($null -eq $chunkHandlerProperty -or -not $chunkHandlerProperty.PropertyType.IsArray) {
    throw 'IWorldGenHandler.OnChunkColumnGen must be an array of mutable handler lists.'
}

$elementType = $chunkHandlerProperty.PropertyType.GetElementType()
if (-not $elementType.IsGenericType -or $elementType.GetGenericTypeDefinition().FullName -ne 'System.Collections.Generic.List`1') {
    throw "Unexpected OnChunkColumnGen element type: $($elementType.FullName)"
}

$requiredRequestProperties = @('Chunks', 'ChunkX', 'ChunkZ')
$missingRequestProperties = @($requiredRequestProperties | Where-Object { $null -eq $request.GetProperty($_) })
if ($missingRequestProperties.Count -ne 0) {
    throw "Missing request properties: $([string]::Join(', ', $missingRequestProperties))"
}

$requiredMapProperties = @('WorldGenTerrainHeightMap', 'RainHeightMap', 'TopRockIdMap', 'YMax')
$missingMapProperties = @($requiredMapProperties | Where-Object { $null -eq $mapChunk.GetProperty($_) })
if ($missingMapProperties.Count -ne 0) {
    throw "Missing map properties: $([string]::Join(', ', $missingMapProperties))"
}

$terrainValue = [int][Enum]::Parse($passType, 'Terrain')
if ($terrainValue -ne 1) {
    throw "EnumWorldGenPass.Terrain expected 1 but was $terrainValue."
}

$requiredNativeTypes = @(
    'Vintagestory.ServerMods.GenTerra',
    'Vintagestory.ServerMods.GenRockStrataNew',
    'Vintagestory.ServerMods.GenCaves',
    'Vintagestory.ServerMods.GenDevastationLayer',
    'Vintagestory.ServerMods.GenBlockLayers',
    'Vintagestory.ServerMods.GenTerraPostProcess',
    'Vintagestory.ServerMods.GenHotSprings',
    'Vintagestory.ServerMods.GenDungeons',
    'Vintagestory.ServerMods.GenDeposits',
    'Vintagestory.ServerMods.GenStructures',
    'Vintagestory.ServerMods.GenPonds',
    'Vintagestory.ServerMods.GenVegetationAndPatches',
    'Vintagestory.ServerMods.GenRivulets',
    'Vintagestory.ServerMods.GenSnowLayer'
)

$nativeTypeStatus = [ordered]@{}
foreach ($name in $requiredNativeTypes) {
    $nativeTypeStatus[$name] = ($null -ne $essentials.GetType($name, $false)) -or ($null -ne $survival.GetType($name, $false))
    if (-not $nativeTypeStatus[$name]) {
        throw "Expected native generator type is missing: $name"
    }
}

$storyType = 'Vintagestory.GameContent.GenStoryStructures'
$nativeTypeStatus[$storyType] = ($null -ne $survival.GetType($storyType, $false))
if (-not $nativeTypeStatus[$storyType]) {
    throw "Expected native generator type is missing: $storyType"
}

$columnRequestSignature = @('Vintagestory.API.Server.IChunkColumnGenerateRequest')
$structureType = Require-Type $essentials 'Vintagestory.ServerMods.GenStructures'
$structureMethods = @(
    Require-InstanceMethodAnyVisibility $structureType 'OnChunkColumnGen' $columnRequestSignature
    Require-InstanceMethodAnyVisibility $structureType 'OnChunkColumnGenPostPass' $columnRequestSignature
)
$lightType = Require-Type $essentials 'Vintagestory.ServerMods.GenLightSurvival'
$lightMethod = Require-InstanceMethodAnyVisibility $lightType 'OnChunkColumnGeneration' $columnRequestSignature

$result = [ordered]@{
    TestId = 'T00-04-API-AUDIT'
    Status = 'PASS'
    Utc = [DateTime]::UtcNow.ToString('o')
    ApiAssemblyVersion = $api.GetName().Version.ToString()
    ApiSha256 = (Get-FileHash -LiteralPath $apiPath -Algorithm SHA256).Hash
    EssentialsSha256 = (Get-FileHash -LiteralPath $essentialsPath -Algorithm SHA256).Hash
    SurvivalSha256 = (Get-FileHash -LiteralPath $survivalPath -Algorithm SHA256).Hash
    TerrainPassValue = $terrainValue
    HandlerListType = $chunkHandlerProperty.PropertyType.FullName
    RequestProperties = $requiredRequestProperties
    MapProperties = $requiredMapProperties
    NativeTypes = $nativeTypeStatus
    StructureHandlers = @($structureMethods | ForEach-Object { "$($_.DeclaringType.FullName)::$($_.Name)" })
    LightingAnchor = "$($lightMethod.DeclaringType.FullName)::$($lightMethod.Name)"
    OwnedColumnApi = $columnOwnershipSignatures
}

$json = $result | ConvertTo-Json -Depth 5
if ($OutputPath) {
    $directory = Split-Path -Parent $OutputPath
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }
    Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
}

$json
