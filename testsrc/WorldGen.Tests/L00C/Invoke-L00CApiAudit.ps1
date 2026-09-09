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
    'M:Vintagestory.API.Common.IEventAPI.RegisterCallback(System.Action{System.Single},System.Int32)'
    'M:Vintagestory.API.Common.IEventAPI.UnregisterCallback(System.Int64)'
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
    'Calls given method after supplied amount of milliseconds.'
    'Removes a delayed callback'
)) {
    if (-not $apiXml.Contains($fragment)) {
        throw "Blocking persisted-column API contract text is missing: $fragment"
    }
}

[void][Reflection.Assembly]::LoadFrom($cecilPath)
$lib = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($libPath)
$apiDefinition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($apiPath)
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

$saveGameImplementation = Require-CecilType 'SaveGame'
$savegameIdentifierField = $saveGameImplementation.Fields | Where-Object Name -eq 'SavegameIdentifier' | Select-Object -First 1
$createNewSaveBody = Get-CecilBodyText (Require-CecilMethod $saveGameImplementation 'CreateNew' 1)
$afterDeserializationBody = Get-CecilBodyText (Require-CecilMethod $saveGameImplementation 'afterDeserialization' 0)
$startServerArgsImplementation = Require-CecilType 'Vintagestory.Common.StartServerArgs'
$saveFileLocationField = $startServerArgsImplementation.Fields | Where-Object Name -eq 'SaveFileLocation' | Select-Object -First 1
$clientMainImplementation = Require-CecilType 'Vintagestory.Client.NoObf.ClientMain'
$clientSaveGuidGetter = Require-CecilMethod $clientMainImplementation 'get_SavegameIdentifier' 0
$clientSaveGuidBody = Get-CecilBodyText $clientSaveGuidGetter
if ($null -eq $savegameIdentifierField -or $savegameIdentifierField.FieldType.FullName -ne 'System.String' -or
    $null -eq $saveFileLocationField -or $saveFileLocationField.FieldType.FullName -ne 'System.String' -or
    $clientSaveGuidGetter.ReturnType.FullName -ne 'System.String' -or
    $createNewSaveBody -notmatch 'System.Guid::NewGuid' -or $createNewSaveBody -notmatch 'System.Object::ToString' -or
    $createNewSaveBody -notmatch 'stfld System.String SaveGame::SavegameIdentifier' -or
    $afterDeserializationBody -notmatch 'System.Guid::NewGuid' -or $afterDeserializationBody -notmatch 'System.Object::ToString' -or
    $afterDeserializationBody -notmatch 'stfld System.String SaveGame::SavegameIdentifier' -or
    $clientSaveGuidBody -notmatch 'System.String Vintagestory.Client.NoObf.ServerInformation::SavegameIdentifier') {
    throw 'Savegame GUID generation/client exposure or the distinct StartServerArgs save path drifted.'
}

$serverEventImplementation = Require-CecilType 'Vintagestory.Server.ServerEventAPI'
$eventManagerImplementation = Require-CecilType 'Vintagestory.Common.EventManager'
$serverMainForDelayed = Require-CecilType 'Vintagestory.Server.ServerMain'
$serverEventRegisterBody = Get-CecilBodyText (Require-CecilMethod $serverEventImplementation 'RegisterCallback' 2)
$serverEventUnregisterBody = Get-CecilBodyText (Require-CecilMethod $serverEventImplementation 'UnregisterCallback' 1)
$serverRegisterBody = Get-CecilBodyText (Require-CecilMethod $serverMainForDelayed 'RegisterCallback' 2)
$serverUnregisterBody = Get-CecilBodyText (Require-CecilMethod $serverMainForDelayed 'UnregisterCallback' 1)
$addDelayedBody = Get-CecilBodyText (Require-CecilMethod $eventManagerImplementation 'AddDelayedCallback' 2)
$removeDelayedBody = Get-CecilBodyText (Require-CecilMethod $eventManagerImplementation 'RemoveDelayedCallback' 1)
$incrementIndex = $addDelayedBody.IndexOf('System.Threading.Interlocked::Increment', [StringComparison]::Ordinal)
$insertIndex = $addDelayedBody.IndexOf('ConcurrentDictionary`2<System.Int64,Vintagestory.Common.DelayedCallback>::set_Item', [StringComparison]::Ordinal)
$returnIndex = $addDelayedBody.LastIndexOf("ret ", [StringComparison]::Ordinal)
if ($serverEventRegisterBody -notmatch 'ServerMain::RegisterCallback' -or $serverRegisterBody -notmatch 'EventManager::AddDelayedCallback' -or
    $incrementIndex -lt 0 -or $insertIndex -le $incrementIndex -or $returnIndex -le $insertIndex -or
    $serverEventUnregisterBody -notmatch 'ServerMain::UnregisterCallback' -or $serverUnregisterBody -notmatch 'EventManager::RemoveDelayedCallback' -or
    $removeDelayedBody -notmatch 'ConcurrentDictionary`2<System.Int64,Vintagestory.Common.DelayedCallback>::TryRemove') {
    throw 'Native delayed callback registration/removal transaction drifted.'
}

$commonEventApi = $apiDefinition.MainModule.Types | Where-Object FullName -eq 'Vintagestory.API.Common.IEventAPI' | Select-Object -First 1
if ($null -eq $commonEventApi) {
    throw 'Required delayed-callback API type is missing: Vintagestory.API.Common.IEventAPI'
}
$delayedRegister = $commonEventApi.Methods | Where-Object {
    $_.Name -eq 'RegisterCallback' -and $_.ReturnType.FullName -eq 'System.Int64' -and $_.Parameters.Count -eq 2 -and
    $_.Parameters[0].ParameterType.FullName -eq 'System.Action`1<System.Single>' -and
    $_.Parameters[1].ParameterType.FullName -eq 'System.Int32'
} | Select-Object -First 1
$delayedUnregister = $commonEventApi.Methods | Where-Object {
    $_.Name -eq 'UnregisterCallback' -and $_.ReturnType.FullName -eq 'System.Void' -and $_.Parameters.Count -eq 1 -and
    $_.Parameters[0].ParameterType.FullName -eq 'System.Int64'
} | Select-Object -First 1
if ($null -eq $delayedRegister -or $null -eq $delayedUnregister) {
    throw 'Delayed callback registration/unregistration API drifted.'
}

$worldApiImplementation = Require-CecilType 'Vintagestory.Server.WorldAPI'
$serverMainImplementation = Require-CecilType 'Vintagestory.Server.ServerMain'
$supplyImplementation = Require-CecilType 'Vintagestory.Server.ServerSystemSupplyChunks'
$worldApiBlockingBody = Get-CecilBodyText (Require-CecilMethod $worldApiImplementation 'BlockingLoadChunkColumn' 2)
$serverBlockingBody = Get-CecilBodyText (Require-CecilMethod $serverMainImplementation 'BlockingLoadChunkColumn' 2)
$tryLoadBody = Get-CecilBodyText (Require-CecilMethod $supplyImplementation 'TryLoadChunkColumn' 1)
$getMapChunkBody = Get-CecilBodyText (Require-CecilMethod $worldApiImplementation 'GetMapChunk' 2)
$serverEventApiImplementation = Require-CecilType 'Vintagestory.Server.ServerEventAPI'
$triggerInitWorldGen = Require-CecilMethod $serverEventApiImplementation 'TriggerInitWorldGen' 0
$triggerInitBody = Get-CecilBodyText $triggerInitWorldGen
$serverMainType = Require-CecilType 'Vintagestory.Server.ServerMain'
$launchBody = Get-CecilBodyText (Require-CecilMethod $serverMainType 'Launch' 0)
$modHandlerType = Require-CecilType 'Vintagestory.Server.ServerSystemModHandler'
$loadAndSaveType = Require-CecilType 'Vintagestory.Server.ServerSystemLoadAndSaveGame'
$modRunGameBody = Get-CecilBodyText (Require-CecilMethod $modHandlerType 'OnBeginRunGame' 0)
$loadSaveRunGameBody = Get-CecilBodyText (Require-CecilMethod $loadAndSaveType 'OnBeginRunGame' 0)
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
if ($triggerInitWorldGen.Body.ExceptionHandlers.Count -eq 0 -or $triggerInitBody -notmatch 'System.Action::Invoke' -or
    $triggerInitBody -notmatch 'Error during Init worldgen' -or $triggerInitBody -notmatch 'Done all worldgens') {
    throw 'TriggerInitWorldGen no longer proves that handler exceptions are logged and iteration continues.'
}
if ($launchBody -notmatch '(?s)ldc\.i4\.6\s+ldloc\.0\s+stelem\.ref' -or
    $launchBody -notmatch '(?s)ldc\.i4\.s 28\s+ldloc\.s V_5\s+stelem\.ref' -or
    $modRunGameBody -notmatch 'ServerEventAPI::OnServerStage' -or
    $loadSaveRunGameBody -notmatch 'add_OnGameWorldBeingSaved') {
    throw 'Server system order drifted: ModHandler index 6 must precede LoadAndSave index 28 and its RunGame save subscription.'
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
    InitWorldGenFailureSemantics = 'catch-log-continue'
    RunGameSystemOrder = 'ModHandler=6;LoadAndSave=28'
    DelayedCallbackApi = 'RegisterCallback(Action<float>,int)->long;UnregisterCallback(long)'
    SavegameIdentity = 'SaveGame.CreateNew/afterDeserialization:Guid.NewGuid().ToString();ClientMain.SavegameIdentifier:string'
    SavePathIdentity = 'StartServerArgs.SaveFileLocation:string;distinct-from-savegame-guid'
    DelayedCallbackImplementation = 'ServerEventAPI->ServerMain->EventManager;Interlocked id then dictionary insert before return;exact-id removal'
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
