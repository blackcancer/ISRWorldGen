[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$VintageStoryPath = $env:VINTAGE_STORY,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($VintageStoryPath)) {
    throw 'VintageStoryPath or VINTAGE_STORY must identify the audited 1.22.7 installation.'
}

$apiPath = Join-Path $VintageStoryPath 'VintagestoryAPI.dll'
$libraryPath = Join-Path $VintageStoryPath 'VintagestoryLib.dll'
$apiXmlPath = Join-Path $VintageStoryPath 'VintagestoryAPI.xml'
$expectedHashes = [ordered]@{
    $apiPath = '034283E7E9D98EAE45EE63005576FD89BADC3C995B531CC4C3FE46F3EB2D3296'
    $libraryPath = 'E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0'
}

foreach ($entry in $expectedHashes.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Key -PathType Leaf)) {
        throw "Audited Vintage Story artifact is missing: $($entry.Key)"
    }

    $actualHash = (Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash
    if ($actualHash -ne $entry.Value) {
        throw "Unexpected SHA-256 for $($entry.Key): $actualHash"
    }
}

function Get-CecilMethod {
    param(
        [Parameter(Mandatory)]$Assembly,
        [Parameter(Mandatory)][string]$TypeName,
        [Parameter(Mandatory)][string]$MethodName
    )

    $type = $Assembly.MainModule.GetType($TypeName)
    if ($null -eq $type) {
        throw "Audited IL type is absent: $TypeName"
    }

    $methods = @($type.Methods | Where-Object Name -eq $MethodName)
    if ($methods.Count -ne 1) {
        throw "Expected one audited IL method $TypeName::$MethodName, found $($methods.Count)."
    }

    return $methods[0]
}

function Get-OperandTexts {
    param([Parameter(Mandatory)]$Method)

    return @($Method.Body.Instructions | ForEach-Object {
        $operandText = [string]$_.Operand
        if ($operandText.Length -gt 0) {
            $operandText
        }
    })
}

function Assert-OperandContains {
    param(
        [Parameter(Mandatory)][string[]]$Operands,
        [Parameter(Mandatory)][string]$Expected,
        [Parameter(Mandatory)][string]$Evidence
    )

    if (-not ($Operands | Where-Object { $_.Contains($Expected, [StringComparison]::Ordinal) })) {
        throw "Audited IL evidence is absent for $Evidence ($Expected)."
    }
}

function Find-OperandIndex {
    param(
        [Parameter(Mandatory)][string[]]$Operands,
        [Parameter(Mandatory)][string]$Expected
    )

    for ($index = 0; $index -lt $Operands.Count; $index++) {
        if ($Operands[$index].Contains($Expected, [StringComparison]::Ordinal)) {
            return $index
        }
    }

    return -1
}

$cecilPath = Join-Path $VintageStoryPath 'Lib\Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $cecilPath -PathType Leaf)) {
    throw "Mono.Cecil required for the bounded installed-IL audit is missing: $cecilPath"
}

Add-Type -Path $cecilPath
$apiAssemblyDefinition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($apiPath)
$libraryAssemblyDefinition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($libraryPath)

$loadModInfoOperands = Get-OperandTexts (Get-CecilMethod $libraryAssemblyDefinition 'Vintagestory.Common.ModContainer' 'LoadModInfo')
Assert-OperandContains $loadModInfoOperands 'worldconfig.json' 'mod world-configuration discovery'
Assert-OperandContains $loadModInfoOperands 'DeserializeObject<Vintagestory.API.Common.ModWorldConfiguration>' 'mod world-configuration deserialization'
Assert-OperandContains $loadModInfoOperands 'Mod::set_WorldConfig' 'mod world-configuration publication'

$loadWorldConfigOperands = Get-OperandTexts (Get-CecilMethod $libraryAssemblyDefinition 'Vintagestory.Common.WorldConfig' 'loadWorldConfigValues')
Assert-OperandContains $loadWorldConfigOperands 'Mod::get_WorldConfig' 'world-configuration mod enumeration'
Assert-OperandContains $loadWorldConfigOperands 'ModWorldConfiguration::WorldConfigAttributes' 'declared-key filtering'
Assert-OperandContains $loadWorldConfigOperands 'JsonObject::get_Item' 'declared-key value lookup'

$updateWorldConfigOperands = Get-OperandTexts (Get-CecilMethod $libraryAssemblyDefinition 'Vintagestory.Common.WorldConfig' 'updateJWorldConfig')
Assert-OperandContains $updateWorldConfigOperands 'WorldConfig::allDefaultValues' 'declared defaults rebuild'
Assert-OperandContains $updateWorldConfigOperands 'WorldConfig::updateJWorldConfigFrom' 'declared custom values publication'

$setNewWorldConfigOperands = Get-OperandTexts (Get-CecilMethod $libraryAssemblyDefinition 'SaveGame' 'SetNewWorldConfig')
Assert-OperandContains $setNewWorldConfigOperands 'ServerConfig::get_WorldConfig' 'server world configuration source'
Assert-OperandContains $setNewWorldConfigOperands 'StartServerArgs::WorldConfiguration' 'startup JSON object source'
Assert-OperandContains $setNewWorldConfigOperands 'WorldConfig::loadWorldConfigValues' 'startup value filtering'
Assert-OperandContains $setNewWorldConfigOperands 'WorldConfig::updateJWorldConfigFrom' 'startup value publication'
Assert-OperandContains $setNewWorldConfigOperands 'SaveGame::WorldConfiguration' 'save world-configuration publication'

$loadAssetsOperands = Get-OperandTexts (Get-CecilMethod $libraryAssemblyDefinition 'Vintagestory.Server.ServerSystemModHandler' 'OnLoadAssets')
$loadModsIndex = Find-OperandIndex $loadAssetsOperands 'ModLoader::LoadMods'
$setNewWorldConfigIndex = Find-OperandIndex $loadAssetsOperands 'SaveGame::SetNewWorldConfig'
$runModPhaseIndex = Find-OperandIndex $loadAssetsOperands 'ModLoader::RunModPhase'
if ($loadModsIndex -lt 0 -or $setNewWorldConfigIndex -le $loadModsIndex -or $runModPhaseIndex -le $setNewWorldConfigIndex) {
    throw 'Installed IL no longer loads mod declarations, applies the new-world configuration, then starts mod phases in the audited order.'
}

$beginRunGameOperands = Get-OperandTexts (Get-CecilMethod $libraryAssemblyDefinition 'Vintagestory.Server.ServerSystemLoadConfig' 'OnBeginRunGame')
Assert-OperandContains $beginRunGameOperands 'ServerConfig::get_StartupCommands' 'startup command source'
Assert-OperandContains $beginRunGameOperands 'ServerMain::ReceiveServerConsole' 'startup command execution'

$runPhaseType = $apiAssemblyDefinition.MainModule.GetType('Vintagestory.API.Server.EnumServerRunPhase')
$gameReadyValue = ($runPhaseType.Fields | Where-Object Name -eq 'GameReady').Constant
$runGameValue = ($runPhaseType.Fields | Where-Object Name -eq 'RunGame').Constant
if ($gameReadyValue -ne 6 -or $runGameValue -ne 8 -or $runGameValue -le $gameReadyValue) {
    throw "Unexpected server lifecycle values: GameReady=$gameReadyValue RunGame=$runGameValue"
}

$worldConfigPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\worldconfig.json'
if (-not (Test-Path -LiteralPath $worldConfigPath -PathType Leaf)) {
    throw "ISRWorldGen world configuration declaration is missing: $worldConfigPath"
}

[Reflection.Assembly]::LoadFrom((Join-Path $VintageStoryPath 'Lib\Newtonsoft.Json.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom($apiPath) | Out-Null
$libraryAssembly = [Reflection.Assembly]::LoadFrom($libraryPath)
$declaration = [Newtonsoft.Json.JsonConvert]::DeserializeObject(
    (Get-Content -LiteralPath $worldConfigPath -Raw),
    [Vintagestory.API.Common.ModWorldConfiguration])
$declaredAttribute = @($declaration.WorldConfigAttributes)
if ($declaredAttribute.Count -ne 1 -or
    $declaredAttribute[0].Code -ne 'isrworldgenProfileId' -or
    $declaredAttribute[0].DataType -ne [Vintagestory.API.Common.EnumDataType]::String -or
    $declaredAttribute[0].TypedDefault -ne '' -or
    $declaredAttribute[0].OnCustomizeScreen -or
    -not $declaredAttribute[0].OnlyDuringWorldCreate) {
    throw 'ISRWorldGen worldconfig.json does not match the audited 1.22.7 declaration contract.'
}

$serverConfigType = $libraryAssembly.GetType('Vintagestory.Server.ServerConfig', $true)
$startServerArgsType = $libraryAssembly.GetType('Vintagestory.Common.StartServerArgs', $true)
$worldConfigProperty = $serverConfigType.GetProperty('WorldConfig')
if ($null -eq $worldConfigProperty -or $worldConfigProperty.PropertyType -ne $startServerArgsType) {
    throw 'ServerConfig.WorldConfig is no longer the audited StartServerArgs type.'
}

$serverConfigJson = '{"WorldConfig":{"SaveFileLocation":"X:\\disposable\\laboratory.vcdbs","WorldName":"T02-05 laboratory","PlayStyle":"surviveandbuild","WorldType":"standard","WorldConfiguration":{"isrworldgenProfileId":"laboratory"}}}'
$worldConfigToken = [Newtonsoft.Json.Linq.JObject]::Parse($serverConfigJson)['WorldConfig']
$worldConfig = [Newtonsoft.Json.JsonConvert]::DeserializeObject($worldConfigToken.ToString(), $startServerArgsType)
$injectedProfileId = $worldConfig.WorldConfiguration['isrworldgenProfileId'].AsString()
if ($injectedProfileId -ne 'laboratory') {
    throw "ServerConfig.WorldConfig.WorldConfiguration did not retain the laboratory selection: $injectedProfileId"
}

$requiredApiMembers = @(
    'P:Vintagestory.API.Server.ICoreServerAPI.Event',
    'P:Vintagestory.API.Server.ICoreServerAPI.WorldManager',
    'P:Vintagestory.API.Server.ICoreServerAPI.Server',
    'P:Vintagestory.API.Server.IWorldManagerAPI.SaveGame',
    'P:Vintagestory.API.Server.IWorldManagerAPI.MapSizeX',
    'P:Vintagestory.API.Server.IWorldManagerAPI.MapSizeY',
    'P:Vintagestory.API.Server.IWorldManagerAPI.MapSizeZ',
    'P:Vintagestory.API.Server.IWorldManagerAPI.ChunkSize',
    'P:Vintagestory.API.Server.ISaveGame.IsNew',
    'P:Vintagestory.API.Server.ISaveGame.SavegameIdentifier',
    'M:Vintagestory.API.Server.ISaveGame.GetData(System.String)',
    'M:Vintagestory.API.Server.ISaveGame.StoreData(System.String,System.Byte[])',
    'M:Vintagestory.API.Server.IServerEventAPI.ServerRunPhase(Vintagestory.API.Server.EnumServerRunPhase,System.Action)',
    'M:Vintagestory.API.Server.IServerEventAPI.InitWorldGenerator(System.Action,System.String)',
    'M:Vintagestory.API.Server.IServerAPI.ShutDown',
    'P:Vintagestory.API.Common.IWorldAccessor.Config'
)
$apiXml = Get-Content -LiteralPath $apiXmlPath -Raw
foreach ($member in $requiredApiMembers) {
    if (-not $apiXml.Contains("name=`"$member`"")) {
        throw "Audited API member is absent from VintagestoryAPI.xml: $member"
    }
}

$bridgePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\ScaleProfiles\VintageStoryNativeProfileBridge.cs'
$coordinatorPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\ScaleProfiles\NativeProfileCoordinator.cs'
$envelopePath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\ScaleProfiles\NativeFrozenProfileEnvelopeCodec.cs'
$bridgeSource = Get-Content -LiteralPath $bridgePath -Raw
$coordinatorSource = Get-Content -LiteralPath $coordinatorPath -Raw
$envelopeSource = Get-Content -LiteralPath $envelopePath -Raw
$requiredSourceFragments = @(
    'EnumServerRunPhase.GameReady',
    'api.Event.InitWorldGenerator(callback, api.WorldManager.SaveGame.WorldType)',
    'worldManager.MapSizeX',
    'worldManager.MapSizeY',
    'worldManager.MapSizeZ',
    'ReadSelection(api.World.Config)',
    'config.HasAttribute(SelectionConfigKey)',
    'string.IsNullOrWhiteSpace(profileId)',
    'api.Server.ShutDown()',
    'internal const string StorageKey = "isrworldgen:l02c:frozen-profile:v1"',
    'internal const string FrozenProfileCodecId = "isrworldgen.core.frozen-scale-profile"',
    'NativeProfilePersistenceState.Pending',
    'NativeProfilePersistenceState.Committed',
    'NativeProfilePersistenceState.Rejected',
    'WriteEnvelope(store, pending)',
    'reread = store.Read()?.ToArray()',
    'return Publish(strictReload.Value!)',
    'NativeProfilePreparationSource.Reload',
    'persistenceWrites = checked(persistenceWrites + 1)',
    'RecordEnvelope(committed)',
    'RecordEnvelope(persisted)',
    'L02C_NATIVE_PROFILE_FROZEN profile={0} source={1} persistencewrites={2}',
    'L02C_NATIVE_PROFILE_REJECTED code={0} stage={1} source={2} persistencewrites={3}',
    'coordinator.PublishedProfile is not null',
    'Native profile GameReady preparation failed ({exception.GetType().Name}).'
)
$allSources = $bridgeSource + $coordinatorSource + $envelopeSource
foreach ($fragment in $requiredSourceFragments) {
    if (-not $allSources.Contains($fragment)) {
        throw "Required native probe fragment is missing: $fragment"
    }
}

$runtimeOraclePath = Join-Path $RepositoryRoot 'testsrc\WorldGen.Tests\L02CNative\Invoke-L02CNativeRuntimeEvidence.ps1'
$runtimeOracleTestPath = Join-Path $RepositoryRoot 'testsrc\WorldGen.Tests\L02CNative\Test-L02CNativeRuntimeEvidence.ps1'
$sqliteExtractorPath = Join-Path $RepositoryRoot 'testsrc\WorldGen.Tests\L02CNative\Invoke-L02CNativeSqliteExtraction.ps1'
$sqliteExtractorTestPath = Join-Path $RepositoryRoot 'testsrc\WorldGen.Tests\L02CNative\Test-L02CNativeSqliteExtraction.ps1'
$sqliteFixtureSupportPath = Join-Path $RepositoryRoot 'testsrc\WorldGen.Tests\L02CNative\L02CNativeSqliteFixtureSupport.ps1'
foreach ($scriptPath in @(
    $runtimeOraclePath,
    $runtimeOracleTestPath,
    $sqliteExtractorPath,
    $sqliteExtractorTestPath,
    $sqliteFixtureSupportPath
)) {
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "T02-05 runtime evidence script is missing: $scriptPath"
    }
}

$sqliteExtractorSource = Get-Content -LiteralPath $sqliteExtractorPath -Raw
foreach ($fragment in @(
    "'isrworldgen.t02-05.sqlite-extraction.v1'",
    '[IO.FileMode]::CreateNew',
    'function Get-SourceFileSeal',
    'function Copy-SealedSourceFile',
    'function Read-CloneDatabaseSnapshot',
    'Assert-PathWithinCloneRoot',
    'Mode=ReadOnly;Pooling=False',
    'PreCopy',
    'AfterCopy',
    'PostExtraction',
    'WalContribution',
    'RequiredForObservedState'
)) {
    if (-not $sqliteExtractorSource.Contains($fragment)) {
        throw "Required T02-05 SQLite extractor fragment is missing: $fragment"
    }
}
if ($sqliteExtractorSource.Contains('ProbeOpenConnection', [StringComparison]::Ordinal)) {
    throw 'T02-05 SQLite extractor must not contain or call ProbeOpenConnection.'
}
$sqliteOpenSites = @([regex]::Matches(
    $sqliteExtractorSource,
    '\[Microsoft\.Data\.Sqlite\.SqliteConnection\]::new\(\$connectionString\)'))
if ($sqliteOpenSites.Count -ne 1) {
    throw 'T02-05 SQLite extractor must centralize its only SQLite open behind the clone-root guard.'
}
if ($sqliteExtractorSource -match 'Data Source=\$(resolved)?Source') {
    throw 'T02-05 SQLite extractor must never construct a source-path SQLite connection string.'
}

$sqliteExtractorTestSource = Get-Content -LiteralPath $sqliteExtractorTestPath -Raw
$sqliteNegativeTestSources = $sqliteExtractorTestSource + (Get-Content -LiteralPath $runtimeOracleTestPath -Raw)
foreach ($fragment in @(
    "'Output replacement'",
    "'Cross-session extraction'",
    "'Extraction hash mismatch'",
    "'Contradictory main source path hash'",
    "'Missing WAL'",
    "'Source changed after extraction'",
    "'Oversized source sidecar'",
    "'Oversized extraction report'"
)) {
    if (-not $sqliteNegativeTestSources.Contains($fragment)) {
        throw "Required T02-05 SQLite extraction negative test is missing: $fragment"
    }
}

$runtimeOracleSource = Get-Content -LiteralPath $runtimeOraclePath -Raw
$requiredRuntimeOracleFragments = @(
    '[IO.FileMode]::CreateNew',
    "'ISRWorldGen.dll'",
    "'ISRWorldGen.Core.dll'",
    "'ISRWorldGen.pdb'",
    "'ISRWorldGen.Core.pdb'",
    "'3AD6294240B9B55D3E0DB3CD323D90C31EC8474EAE6E4E16B58FE76507CB9D0D'",
    "'L00A_BOOTSTRAP'",
    "'visual-studio-debugger-session-verified-v3'",
    "'isrworldgen.t02-05.visual-studio-campaign.v3'",
    "'isrworldgen.t02-05.runtime-evidence.v4'",
    "'isrworldgen.t02-05.sqlite-extraction.v1'",
    "'ISRWorldGen Server (isolated data)'",
    'ExpectedAssemblyInformationalVersion',
    'AssemblyInformationalVersionAttribute',
    'Get-SymbolPairIdentity',
    '[Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView',
    '[Reflection.Metadata.BlobContentId]',
    'Assert-ClosedSchema',
    'CallstackSha256',
    'BreakpointHitUtc',
    'DebuggerContinueUtc',
    '$maximumInteractiveInspection = [TimeSpan]::FromMinutes(5)',
    '$maximumPostContinueMarkerDelay = [TimeSpan]::FromSeconds(5)',
    'BootstrapModuleBinding',
    'PdbPairingVerified',
    '$maximumLogBytes = 16 * 1024 * 1024',
    '$maximumManifestBytes = 64 * 1024',
    '$maximumCampaignBytes = 64 * 1024',
    '$maximumExtractionReportBytes = 256 * 1024',
    'function Read-BoundedTextFile',
    '$item = Get-Item -LiteralPath $Path',
    '$item.Length -gt $MaximumBytes',
    'Read-BoundedTextFile $Path $maximumLogBytes',
    'Read-BoundedTextFile $snapshotManifestPath $maximumManifestBytes',
    'Read-BoundedTextFile $CampaignObservationPath $maximumCampaignBytes',
    "Assert-ExactToken `$reloadTokens 'persistencewrites' '0' 'reload'",
    "Assert-ExactToken `$reloadTokens 'gatecallbackregistered' 'false' 'reload'",
    "Assert-ExactToken `$reloadTokens 'envelopesha256' `$newTokens.envelopesha256 'reload'",
    "Assert-Omits `$logs.reload.Content 'L02C_NATIVE_GATE_FROZEN' 'reload'",
    "Assert-ContainsAtLeastOnce `$logs.new.Content 'L00B_COLUMN_CALLBACK' 'new'",
    'function Read-ExtractionReport',
    'NewExtractionReport',
    'NewSealedSourceDirectory',
    'SealedSourceSetSha256',
    'SQLiteExtraction',
    'RequiredForObservedState',
    'PresentStateEquivalent',
    'WAL contribution implication',
    'main source path identity is contradictory',
    'isrworldgen.t02-05.sqlite-clone-result.v1'
)
foreach ($fragment in $requiredRuntimeOracleFragments) {
    if (-not $runtimeOracleSource.Contains($fragment)) {
        throw "Required T02-05 runtime evidence oracle fragment is missing: $fragment"
    }
}

$runtimeRawReads = @([regex]::Matches($runtimeOracleSource, 'Get-Content\s+-LiteralPath\s+\$[A-Za-z]+\s+-Raw'))
if ($runtimeRawReads.Count -ne 1 -or $runtimeRawReads[0].Value -cne 'Get-Content -LiteralPath $Path -Raw') {
    throw 'T02-05 runtime evidence oracle must centralize every raw input read behind one bounded reader.'
}

$runtimeOracleTestSource = Get-Content -LiteralPath $runtimeOracleTestPath -Raw
foreach ($fragment in @(
    "'Unverified provenance'",
    "'Absent provenance'",
    "'Visual Studio profile mismatch'",
    "'Malicious extra field'",
    "'Malicious callstack field'",
    "'Module version mismatch'",
    "'Module path mismatch'",
    "'Bootstrap hash mismatch'",
    "'Bootstrap absence'",
    "'New callback absence'",
    "'Stale log'",
    "'Arbitrary log mutation'",
    "'PDB mismatch'",
    "'Oversized log'",
    "'Oversized manifest'",
    "'Oversized campaign'",
    "'Legacy v2 campaign'",
    "'Absent debugger continue timestamp'",
    "'Excessive interactive inspection window'",
    "'Inverted breakpoint and continue order'",
    "'Stale marker after debugger continue'",
    "'PID mismatch'",
    "'Debugger session mismatch'",
    "'Log after debugger session'",
    "'Rectangle dimensions mismatch'",
    "'Required WAL with equivalent readable main'",
    "'Equivalent WAL with unreadable main'",
    "'Equivalent WAL with different readable main'"
)) {
    if (-not $runtimeOracleTestSource.Contains($fragment)) {
        throw "Required T02-05 runtime evidence negative test is missing: $fragment"
    }
}

$launchSettingsPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\Properties\launchSettings.json'
$launchSettings = Get-Content -LiteralPath $launchSettingsPath -Raw
if ($launchSettings.Contains('isrworldgenProfileId')) {
    throw 'An existing L00 launch profile opts into ISRWorldGen implicitly.'
}

$env:VINTAGE_STORY = $VintageStoryPath
$projectPath = Join-Path $RepositoryRoot 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj'
$buildOutput = & dotnet build $projectPath -c $Configuration --nologo --no-restore 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Native adapter build failed:`n$($buildOutput -join [Environment]::NewLine)"
}

$packageRoot = Join-Path $RepositoryRoot "src\WorldGen.VintageStory\bin\$Configuration\Mods\isrworldgen"
$packagedDlls = @(Get-ChildItem -LiteralPath $packageRoot -File -Filter '*.dll' | Select-Object -ExpandProperty Name | Sort-Object)
$expectedDlls = @('ISRWorldGen.Core.dll', 'ISRWorldGen.dll')
if (($packagedDlls -join '|') -ne ($expectedDlls -join '|')) {
    throw "Unexpected packaged DLL set: $($packagedDlls -join ', ')"
}

$packagedWorldConfigPath = Join-Path $packageRoot 'worldconfig.json'
if (-not (Test-Path -LiteralPath $packagedWorldConfigPath -PathType Leaf)) {
    throw "Packaged worldconfig.json is missing: $packagedWorldConfigPath"
}

if ((Get-FileHash -LiteralPath $packagedWorldConfigPath -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $worldConfigPath -Algorithm SHA256).Hash) {
    throw 'Packaged worldconfig.json differs from the audited source declaration.'
}

$dependencyManifestPath = Join-Path $packageRoot 'ISRWorldGen.deps.json'
$dependencyManifest = Get-Content -LiteralPath $dependencyManifestPath -Raw
if (-not $dependencyManifest.Contains('"ISRWorldGen.Core.dll"')) {
    throw 'ISRWorldGen.deps.json does not declare the packaged Core runtime dependency.'
}

[ordered]@{
    TestId = 'T02-05-STATIC-NATIVE'
    Status = 'PASS'
    RuntimeEngine = 'NOT_RUN'
    Configuration = $Configuration
    ApiAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($apiPath).Version.ToString()
    ApiSha256 = $expectedHashes[$apiPath]
    VintagestoryLibSha256 = $expectedHashes[$libraryPath]
    PackageDlls = $packagedDlls
    SelectionConfigKey = 'isrworldgenProfileId'
    StorageKey = 'isrworldgen:l02c:frozen-profile:v1'
    ExistingL00ProfilesImplicitlyActivated = $false
    SelectionDeclaration = 'worldconfig.json:String:default-empty:hidden:create-only'
    StartupCommandsPhase = "RunGame:$runGameValue"
    SelectionReadPhase = "GameReady:$gameReadyValue"
    ServerConfigInjection = "WorldConfig.WorldConfiguration.isrworldgenProfileId=$injectedProfileId"
} | ConvertTo-Json -Depth 4
