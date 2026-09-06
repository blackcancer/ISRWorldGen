[CmdletBinding()]
param(
    [string]$GamePath = 'D:\Jeux\Vintagestory',
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$apiPath = Join-Path $GamePath 'VintagestoryAPI.dll'
$apiXmlPath = Join-Path $GamePath 'VintagestoryAPI.xml'
$libPath = Join-Path $GamePath 'VintagestoryLib.dll'
$cecilPath = Join-Path $GamePath 'Lib\Mono.Cecil.dll'
$essentialsPath = Join-Path $GamePath 'Mods\VSEssentials.dll'
$survivalPath = Join-Path $GamePath 'Mods\VSSurvivalMod.dll'

foreach ($path in @($apiPath, $apiXmlPath, $libPath, $cecilPath, $essentialsPath, $survivalPath)) {
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
$worldManager = Require-Type $api 'Vintagestory.API.Server.IWorldManagerAPI'
$worldChunk = Require-Type $api 'Vintagestory.API.Common.IWorldChunk'
$serverChunk = Require-Type $api 'Vintagestory.API.Server.IServerChunk'
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
$blockingExists = Require-Method $worldManager 'BlockingTestMapChunkExists' @('System.Int32', 'System.Int32')
$blockingLoad = Require-Method $worldManager 'BlockingLoadChunkColumn' @('System.Int32', 'System.Int32')
$getMapChunk = Require-Method $worldManager 'GetMapChunk' @('System.Int32', 'System.Int32')
if ($blockingExists.ReturnType.FullName -ne 'System.Boolean' -or $blockingLoad.ReturnType.FullName -ne 'Vintagestory.API.Server.IServerChunk[]') {
    throw 'Blocking persisted-column API return types drifted.'
}
if ($getMapChunk.ReturnType.FullName -ne 'Vintagestory.API.Common.IServerMapChunk') {
    throw 'GetMapChunk return type drifted.'
}
[void](Require-Method $worldChunk 'MarkModified' @())
[void](Require-Method $worldChunk 'Dispose' @())
[void](Require-Method $mapChunk 'MarkFresh' @())
[void](Require-Method $mapChunk 'MarkDirty' @())
if (-not $worldChunk.IsAssignableFrom($serverChunk)) {
    throw 'IServerChunk must inherit the MarkModified and Dispose contracts.'
}
$apiXml = Get-Content -LiteralPath $apiXmlPath -Raw
$transientColumnSignatures = @(
    'M:Vintagestory.API.Server.IWorldManagerAPI.GetMapChunk(System.Int32,System.Int32)',
    'M:Vintagestory.API.Server.IWorldManagerAPI.GetChunk(System.Int32,System.Int32,System.Int32)',
    'M:Vintagestory.API.Server.IWorldManagerAPI.LoadChunkColumnPriority(System.Int32,System.Int32,System.Int32,System.Int32,Vintagestory.API.Server.ChunkLoadOptions)',
    'M:Vintagestory.API.Server.IWorldManagerAPI.BlockingTestMapChunkExists(System.Int32,System.Int32)'
    'M:Vintagestory.API.Server.IWorldManagerAPI.BlockingLoadChunkColumn(System.Int32,System.Int32)'
    'M:Vintagestory.API.Server.IWorldManagerAPI.GetMapChunk(System.Int32,System.Int32)'
    'M:Vintagestory.API.Common.IWorldChunk.MarkModified'
    'M:Vintagestory.API.Common.IMapChunk.MarkFresh'
    'M:Vintagestory.API.Common.IMapChunk.MarkDirty'
)
foreach ($signature in $transientColumnSignatures) {
    if (-not $apiXml.Contains($signature)) {
        throw "Required local API signature is missing: $signature"
    }
}
foreach ($fragment in @(
    'BlockingTestMapChunkExists(System.Int32,System.Int32)">',
    'can only be called before EnumServerRunPhase.RunGame',
    'BlockingLoadChunkColumn(System.Int32,System.Int32)">',
    'only loads and deserializes the chunk data',
    'you need to call .Dispose() after you do not need them anymore',
    'Gets the Server map chunk at given coordinate. Returns null if it''s not loaded or does not exist yet',
    'Generates a chunk at a given coordinate from scratch without keeping it in the list of loaded chunks.',
    'Causes the TTL counter to reset so that it the mapchunk does not unload',
    'stored to disk on the next autosave or during shutdown',
    'Tells the server that it has to save the changes of this chunk to disk'
)) {
    if (-not $apiXml.Contains($fragment)) {
        throw "Blocking persisted-column API contract text is missing: $fragment"
    }
}

[void][Reflection.Assembly]::LoadFrom($cecilPath)
$lib = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($libPath)
function Require-CecilType([string]$Name) {
    $type = $lib.MainModule.Types | Where-Object FullName -eq $Name | Select-Object -First 1
    if ($null -eq $type) { throw "Required implementation type is missing: $Name" }
    return $type
}
function Require-CecilMethod($Type, [string]$Name, [int]$ParameterCount) {
    $method = $Type.Methods | Where-Object { $_.Name -eq $Name -and $_.Parameters.Count -eq $ParameterCount } | Select-Object -First 1
    if ($null -eq $method -or -not $method.HasBody) { throw "Required implementation method is missing: $($Type.FullName).$Name/$ParameterCount" }
    return $method
}
function Get-CecilBodyText($Method) {
    return (($Method.Body.Instructions | ForEach-Object { "$($_.OpCode) $($_.Operand)" }) -join "`n")
}

$worldApiImplementation = Require-CecilType 'Vintagestory.Server.WorldAPI'
$serverMainImplementation = Require-CecilType 'Vintagestory.Server.ServerMain'
$supplyImplementation = Require-CecilType 'Vintagestory.Server.ServerSystemSupplyChunks'
$worldApiBlockingBody = Get-CecilBodyText (Require-CecilMethod $worldApiImplementation 'BlockingLoadChunkColumn' 2)
$serverBlockingBody = Get-CecilBodyText (Require-CecilMethod $serverMainImplementation 'BlockingLoadChunkColumn' 2)
$tryLoadBody = Get-CecilBodyText (Require-CecilMethod $supplyImplementation 'TryLoadChunkColumn' 1)
$getMapChunkBody = Get-CecilBodyText (Require-CecilMethod $worldApiImplementation 'GetMapChunk' 2)
if ($worldApiBlockingBody -notmatch 'ServerMain::BlockingLoadChunkColumn' -or
    $serverBlockingBody -notmatch 'ServerSystemSupplyChunks::TryLoadChunkColumn' -or
    $tryLoadBody -notmatch 'GameDatabase::GetChunk' -or $tryLoadBody -notmatch 'ServerChunk::FromBytes' -or
    $tryLoadBody -match 'TryLoadMapChunk|GetMapChunk|set_MapChunk|loadedMapChunks') {
    throw 'BlockingLoadChunkColumn no longer proves a chunk-only deserialize path with no attached mapchunk.'
}
if ($getMapChunkBody -notmatch 'loadedMapChunks' -or $getMapChunkBody -notmatch 'TryGetValue' -or
    $getMapChunkBody -match 'TryLoadMapChunk|GameDatabase::GetMapChunk') {
    throw 'GetMapChunk no longer proves a loaded-cache-only lookup.'
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
    TransientColumnApi = $transientColumnSignatures
    BlockingLoadImplementation = 'TryLoadChunkColumn:GetChunk+ServerChunk.FromBytes;no-mapchunk-attachment'
    GetMapChunkImplementation = 'loadedMapChunks.TryGetValue;no-disk-load'
    PersistedMapSource = 'versioned-marker-envelope-copy'
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
