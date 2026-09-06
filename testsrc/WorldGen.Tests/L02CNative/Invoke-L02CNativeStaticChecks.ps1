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
    'api.World.Config.HasAttribute(SelectionConfigKey)',
    'api.Server.ShutDown()',
    'internal const string StorageKey = "isrworldgen:l02c:frozen-profile:v1"',
    'internal const string FrozenProfileCodecId = "isrworldgen.core.frozen-scale-profile"',
    'NativeProfilePersistenceState.Pending',
    'NativeProfilePersistenceState.Committed',
    'NativeProfilePersistenceState.Rejected',
    'store.Write(pending.AsSpan())',
    'reread = store.Read()?.ToArray()',
    'return Publish(strictReload.Value!)',
    'details={2} dimensions={3} chunk={4} rules={5}'
)
$allSources = $bridgeSource + $coordinatorSource + $envelopeSource
foreach ($fragment in $requiredSourceFragments) {
    if (-not $allSources.Contains($fragment)) {
        throw "Required native probe fragment is missing: $fragment"
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
} | ConvertTo-Json -Depth 4
