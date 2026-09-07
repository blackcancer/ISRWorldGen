[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Snapshot', 'Validate')]
    [string]$Phase,
    [Parameter(Mandatory)]
    [string]$EvidenceRoot,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$VintageStoryPath = $env:VINTAGE_STORY,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$ExpectedCommit,
    [string]$NewLog,
    [string]$ReloadLog,
    [string]$HeightRefusalLog,
    [string]$RectangleRefusalLog,
    [string]$CampaignObservationPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedServerProductVersion = '1.22.7'
$expectedServerSha256 = '3AD6294240B9B55D3E0DB3CD323D90C31EC8474EAE6E4E16B58FE76507CB9D0D'
$snapshotDirectory = Join-Path $EvidenceRoot 'prelaunch-snapshot'
$snapshotManifestPath = Join-Path $snapshotDirectory 'prelaunch-snapshot.json'
$maximumEnvelopeBytes = 8192
$maximumLogBytes = 16 * 1024 * 1024
$maximumManifestBytes = 64 * 1024
$maximumCampaignBytes = 64 * 1024
$artifactNames = @('ISRWorldGen.dll', 'ISRWorldGen.Core.dll', 'ISRWorldGen.pdb', 'ISRWorldGen.Core.pdb')
$caseNames = @('new', 'reload', 'height', 'rectangle')
$expectedProfile = 'ISRWorldGen Server (isolated data)'
$expectedProvenance = 'visual-studio-debugger-session-verified'

function Assert-LeafFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Evidence)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required $Evidence file is missing: $Path"
    }
}

function Read-BoundedTextFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][long]$MaximumBytes,
        [Parameter(Mandatory)][string]$Evidence
    )

    Assert-LeafFile $Path $Evidence
    $item = Get-Item -LiteralPath $Path
    if ($item.Length -gt $MaximumBytes) {
        throw "$Evidence exceeds maximum byte length (observed=$($item.Length) maximum=$MaximumBytes)."
    }

    return Get-Content -LiteralPath $Path -Raw
}

function Write-NewUtf8File {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)

    $encoding = [Text.UTF8Encoding]::new($false)
    $bytes = $encoding.GetBytes($Content)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
}

function Copy-NewFile {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)

    $inputStream = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $outputStream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $inputStream.CopyTo($outputStream) }
        finally { $outputStream.Dispose() }
    }
    finally { $inputStream.Dispose() }
}

function Get-CurrentCommit {
    $result = & git -c "safe.directory=$RepositoryRoot" -C $RepositoryRoot rev-parse HEAD 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Unable to resolve repository HEAD: $($result -join [Environment]::NewLine)" }
    $commit = ([string]($result | Select-Object -Last 1)).Trim()
    if ($commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Repository HEAD is not an exact lowercase 40-character commit.' }
    return $commit
}

function Get-ServerIdentity {
    if ([string]::IsNullOrWhiteSpace($VintageStoryPath)) {
        throw 'VintageStoryPath or VINTAGE_STORY must identify the audited installation.'
    }
    $serverPath = Join-Path $VintageStoryPath 'VintagestoryServer.exe'
    Assert-LeafFile $serverPath 'Vintage Story server'
    $productVersion = (Get-Item -LiteralPath $serverPath).VersionInfo.ProductVersion
    $sha256 = (Get-FileHash -LiteralPath $serverPath -Algorithm SHA256).Hash
    if ($productVersion -cne $expectedServerProductVersion -or $sha256 -cne $expectedServerSha256) {
        throw "Vintage Story server identity differs from the audited 1.22.7 executable (ProductVersion=$productVersion SHA256=$sha256)."
    }
    return [ordered]@{ FileName = 'VintagestoryServer.exe'; ProductVersion = $productVersion; Sha256 = $sha256 }
}

function Get-TextSha256 {
    param([Parameter(Mandatory)][string]$Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($Value)))
}

function Get-NormalizedPathSha256 {
    param([Parameter(Mandatory)][string]$Path)
    $normalized = [IO.Path]::GetFullPath($Path).Replace('/', '\').TrimEnd('\').ToUpperInvariant()
    return Get-TextSha256 $normalized
}

function Get-AssemblyInformationalVersion {
    param([Parameter(Mandatory)][string]$Path)
    $assembly = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($Path))
    $matches = @($assembly.GetCustomAttributesData() | Where-Object {
        $_.AttributeType.FullName -ceq 'System.Reflection.AssemblyInformationalVersionAttribute'
    })
    if ($matches.Count -ne 1 -or $matches[0].ConstructorArguments.Count -ne 1) {
        throw "AssemblyInformationalVersion is absent or ambiguous for $(Split-Path -Leaf $Path)."
    }
    return [string]$matches[0].ConstructorArguments[0].Value
}

function Get-SymbolPairIdentity {
    param([Parameter(Mandatory)][string]$AssemblyPath, [Parameter(Mandatory)][string]$PdbPath)
    Assert-LeafFile $AssemblyPath 'symbol-paired assembly'
    Assert-LeafFile $PdbPath 'symbol-paired PDB'
    $assemblyStream = $null; $peReader = $null; $pdbStream = $null; $provider = $null
    try {
        $assemblyStream = [IO.File]::Open($AssemblyPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $peReader = [Reflection.PortableExecutable.PEReader]::new($assemblyStream)
        $codeViewEntries = @($peReader.ReadDebugDirectory() | Where-Object {
            $_.Type -eq [Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView
        })
        if ($codeViewEntries.Count -ne 1) {
            throw "PDB pairing requires exactly one CodeView entry for $(Split-Path -Leaf $AssemblyPath)."
        }
        $entry = $codeViewEntries[0]
        $codeView = $peReader.ReadCodeViewDebugDirectoryData($entry)
        $expectedPdbName = Split-Path -Leaf $PdbPath
        if ((Split-Path -Leaf $codeView.Path) -cne $expectedPdbName) {
            throw "PDB pairing CodeView name differs for $(Split-Path -Leaf $AssemblyPath)."
        }
        $pdbStream = [IO.File]::Open($PdbPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        $metadataReader = $provider.GetMetadataReader()
        $contentId = [Reflection.Metadata.BlobContentId]::new([byte[]]@($metadataReader.DebugMetadataHeader.Id))
        if ($codeView.Guid -ne $contentId.Guid -or [uint32]$entry.Stamp -ne [uint32]$contentId.Stamp) {
            throw "PDB pairing CodeView identity differs from portable PDB content ID for $(Split-Path -Leaf $AssemblyPath)."
        }
        return [ordered]@{
            AssemblyFileName = Split-Path -Leaf $AssemblyPath
            PdbFileName = $expectedPdbName
            CodeViewGuid = $codeView.Guid.ToString('D').ToLowerInvariant()
            CodeViewAge = [int]$codeView.Age
            CodeViewStamp = [uint32]$entry.Stamp
            PortablePdbGuid = $contentId.Guid.ToString('D').ToLowerInvariant()
            PortablePdbStamp = [uint32]$contentId.Stamp
        }
    }
    catch {
        if ($_.Exception.Message.Contains('PDB pairing', [StringComparison]::Ordinal)) { throw }
        throw "PDB pairing could not be attested for $(Split-Path -Leaf $AssemblyPath): $($_.Exception.GetType().Name)."
    }
    finally {
        if ($null -ne $provider) { $provider.Dispose() }
        if ($null -ne $pdbStream) { $pdbStream.Dispose() }
        if ($null -ne $peReader) { $peReader.Dispose() }
        if ($null -ne $assemblyStream) { $assemblyStream.Dispose() }
    }
}

function Assert-ClosedSchema {
    param([Parameter(Mandatory)]$Object, [Parameter(Mandatory)][string[]]$ExpectedProperties, [Parameter(Mandatory)][string]$Label)
    $actual = @($Object.PSObject.Properties.Name | Sort-Object)
    $expected = @($ExpectedProperties | Sort-Object)
    if (($actual -join '|') -cne ($expected -join '|')) { throw "$Label violates its closed schema." }
}

function Get-RequiredProperty {
    param([Parameter(Mandatory)]$Object, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Label)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "$Label required property is absent: $Name." }
    return $property.Value
}

function Get-ArtifactByName {
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][string]$FileName)
    $matches = @($Manifest.Artifacts | Where-Object { $_.FileName -ceq $FileName })
    if ($matches.Count -ne 1) { throw "Snapshot manifest expected exactly one $FileName artifact." }
    return $matches[0]
}

function Get-SymbolPairByAssembly {
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][string]$AssemblyFileName)
    $matches = @($Manifest.SymbolPairs | Where-Object { $_.AssemblyFileName -ceq $AssemblyFileName })
    if ($matches.Count -ne 1) { throw "Snapshot manifest expected exactly one symbol pair for $AssemblyFileName." }
    return $matches[0]
}

function Assert-SymbolPairEquals {
    param([Parameter(Mandatory)]$Actual, [Parameter(Mandatory)]$Expected, [Parameter(Mandatory)][string]$Label)
    foreach ($property in @('AssemblyFileName', 'PdbFileName', 'CodeViewGuid', 'CodeViewAge', 'CodeViewStamp', 'PortablePdbGuid', 'PortablePdbStamp')) {
        if ([string]$Actual[$property] -cne [string]$Expected.$property) {
            throw "PDB pairing identity mismatch for $Label ($property)."
        }
    }
}

function Get-UniqueMarkerLine {
    param([Parameter(Mandatory)][string]$Content, [Parameter(Mandatory)][string]$Marker, [Parameter(Mandatory)][string]$CaseName)
    $lines = @($Content -split "`r?`n" | Where-Object { $_.Contains($Marker, [StringComparison]::Ordinal) })
    if ($lines.Count -ne 1) { throw "$CaseName expected exactly one $Marker marker, observed $($lines.Count)." }
    return $lines[0]
}

function Get-StructuredMarker {
    param([Parameter(Mandatory)][string]$Content, [Parameter(Mandatory)][string]$Marker, [Parameter(Mandatory)][string]$CaseName)
    $line = Get-UniqueMarkerLine $Content $Marker $CaseName
    $markerOffset = $line.IndexOf($Marker, [StringComparison]::Ordinal)
    $tokens = @{}
    foreach ($part in ($line.Substring($markerOffset) -split ' ')) {
        $separator = $part.IndexOf('=')
        if ($separator -gt 0) {
            $name = $part.Substring(0, $separator)
            if (-not $tokens.ContainsKey($name)) { $tokens[$name] = $part.Substring($separator + 1) }
        }
    }
    return $tokens
}

function Assert-ExactToken {
    param([Parameter(Mandatory)][hashtable]$Tokens, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Expected, [Parameter(Mandatory)][string]$CaseName)
    if (-not $Tokens.ContainsKey($Name) -or $Tokens[$Name] -cne $Expected) {
        $actual = if ($Tokens.ContainsKey($Name)) { $Tokens[$Name] } else { '<absent>' }
        throw "$CaseName marker token $Name expected '$Expected', observed '$actual'."
    }
}

function Assert-ContainsOnce {
    param([Parameter(Mandatory)][string]$Content, [Parameter(Mandatory)][string]$Marker, [Parameter(Mandatory)][string]$CaseName)
    $count = ([regex]::Matches($Content, [regex]::Escape($Marker))).Count
    if ($count -ne 1) { throw "$CaseName expected exactly one '$Marker', observed $count." }
}

function Assert-ContainsAtLeastOnce {
    param([Parameter(Mandatory)][string]$Content, [Parameter(Mandatory)][string]$Marker, [Parameter(Mandatory)][string]$CaseName)
    $count = ([regex]::Matches($Content, [regex]::Escape($Marker))).Count
    if ($count -lt 1) { throw "$CaseName expected at least one '$Marker', observed none." }
}

function Assert-Omits {
    param([Parameter(Mandatory)][string]$Content, [Parameter(Mandatory)][string]$Marker, [Parameter(Mandatory)][string]$CaseName)
    if ($Content.Contains($Marker, [StringComparison]::Ordinal)) { throw "$CaseName unexpectedly contains '$Marker'." }
}

function Read-CaseLog {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$CaseName)
    $content = Read-BoundedTextFile $Path $maximumLogBytes "$CaseName log"
    return [ordered]@{ Content = $content; Sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
}

function Convert-StrictUtcTimestamp {
    param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string]$Label)
    if ($Value -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}\+00:00$') {
        throw "$Label timestamp is not canonical UTC round-trip format."
    }
    try {
        return [DateTimeOffset]::ParseExact($Value, 'o', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None)
    }
    catch { throw "$Label timestamp is invalid." }
}

function Get-LogLineUtcTimestamp {
    param([Parameter(Mandatory)][string]$Line, [Parameter(Mandatory)][string]$Label)
    if ($Line -cnotmatch '^(?<timestamp>\d{1,2}\.\d{1,2}\.\d{4} \d{2}:\d{2}:\d{2}) ') {
        throw "$Label log timestamp is absent or malformed."
    }
    try {
        $local = [DateTime]::ParseExact($Matches.timestamp, 'd.M.yyyy HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None)
        $zone = [TimeZoneInfo]::FindSystemTimeZoneById('Romance Standard Time')
        return [DateTimeOffset]::new($local, $zone.GetUtcOffset($local)).ToUniversalTime()
    }
    catch { throw "$Label log timestamp is invalid." }
}

function Assert-MarkerTimestampInSession {
    param([Parameter(Mandatory)][string]$Content, [Parameter(Mandatory)][string]$Marker, [Parameter(Mandatory)][string]$CaseName, [Parameter(Mandatory)][DateTimeOffset]$Started, [Parameter(Mandatory)][DateTimeOffset]$Completed)
    $timestamp = Get-LogLineUtcTimestamp (Get-UniqueMarkerLine $Content $Marker $CaseName) "$CaseName $Marker"
    if ($timestamp -lt $Started.AddSeconds(-2) -or $timestamp -gt $Completed.AddSeconds(2)) {
        throw "$CaseName $Marker log timestamp is outside the verified debugger session."
    }
    return $timestamp
}

function Read-CaseObservation {
    param([Parameter(Mandatory)]$Object, [Parameter(Mandatory)][string]$CaseName, [Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)]$Log)
    Assert-ClosedSchema $Object @(
        'SessionId', 'ServerPid', 'StartedUtc', 'BreakpointUtc', 'CompletedUtc', 'LogSha256',
        'BreakpointId', 'CallstackFrames', 'CallstackSha256', 'Module'
    ) "$CaseName campaign case"
    $sessionId = [string](Get-RequiredProperty $Object 'SessionId' "$CaseName campaign case")
    $pidValue = [int](Get-RequiredProperty $Object 'ServerPid' "$CaseName campaign case")
    $started = Convert-StrictUtcTimestamp ([string](Get-RequiredProperty $Object 'StartedUtc' "$CaseName campaign case")) "$CaseName StartedUtc"
    $breakpointUtc = Convert-StrictUtcTimestamp ([string](Get-RequiredProperty $Object 'BreakpointUtc' "$CaseName campaign case")) "$CaseName BreakpointUtc"
    $completed = Convert-StrictUtcTimestamp ([string](Get-RequiredProperty $Object 'CompletedUtc' "$CaseName campaign case")) "$CaseName CompletedUtc"
    $logSha = [string](Get-RequiredProperty $Object 'LogSha256' "$CaseName campaign case")
    $breakpointId = [string](Get-RequiredProperty $Object 'BreakpointId' "$CaseName campaign case")
    $callstackFrames = @((Get-RequiredProperty $Object 'CallstackFrames' "$CaseName campaign case"))
    $callstackSha = [string](Get-RequiredProperty $Object 'CallstackSha256' "$CaseName campaign case")
    $module = Get-RequiredProperty $Object 'Module' "$CaseName campaign case"
    if ($sessionId -cnotmatch '^[0-9a-f]{32}$' -or $pidValue -le 0 -or $logSha -cnotmatch '^[0-9A-F]{64}$' -or $callstackSha -cnotmatch '^[0-9A-F]{64}$') {
        throw "$CaseName campaign case contains a non-canonical identifier, PID, or SHA-256."
    }
    if ($started -gt $breakpointUtc -or $breakpointUtc -gt $completed -or $completed -gt [DateTimeOffset]::UtcNow.AddMinutes(1)) {
        throw "$CaseName campaign timestamp order is invalid."
    }
    if ($Log.Sha256 -cne $logSha) { throw "$CaseName log SHA-256 differs from the debugger-session observation." }

    $expectedBreakpoint = if ($CaseName -in @('new', 'reload')) { 'native-profile-game-ready-frozen' } else { 'native-profile-host-shutdown' }
    $expectedFrames = if ($expectedBreakpoint -ceq 'native-profile-game-ready-frozen') {
        @('native-profile-host.create-frozen-diagnostic', 'native-profile-bridge.try-log-frozen', 'native-profile-bridge.on-game-ready')
    }
    else {
        @('native-profile-host.shutdown', 'native-profile-bridge.reject-and-stop', 'native-profile-bridge.on-game-ready')
    }
    if ($breakpointId -cne $expectedBreakpoint -or ($callstackFrames -join '|') -cne ($expectedFrames -join '|') -or
        (Get-TextSha256 ($callstackFrames -join "`n")) -cne $callstackSha) {
        throw "$CaseName breakpoint and callstack do not match the closed correlated callstack schema."
    }

    Assert-ClosedSchema $module @('FileName', 'PathSha256', 'Sha256', 'ProductVersion', 'PdbFileName', 'PdbSha256', 'CodeViewGuid', 'CodeViewAge', 'CodeViewStamp') "$CaseName module"
    $assemblyArtifact = Get-ArtifactByName $Manifest 'ISRWorldGen.dll'
    $pdbArtifact = Get-ArtifactByName $Manifest 'ISRWorldGen.pdb'
    $symbolPair = Get-SymbolPairByAssembly $Manifest 'ISRWorldGen.dll'
    $expectedModule = [ordered]@{
        FileName = 'ISRWorldGen.dll'; PathSha256 = [string]$assemblyArtifact.PackagePathSha256
        Sha256 = [string]$assemblyArtifact.Sha256; ProductVersion = [string]$Manifest.ExpectedAssemblyInformationalVersion
        PdbFileName = 'ISRWorldGen.pdb'; PdbSha256 = [string]$pdbArtifact.Sha256
        CodeViewGuid = [string]$symbolPair.CodeViewGuid; CodeViewAge = [int]$symbolPair.CodeViewAge
        CodeViewStamp = [uint32]$symbolPair.CodeViewStamp
    }
    foreach ($name in $expectedModule.Keys) {
        if ([string]$module.$name -cne [string]$expectedModule[$name]) {
            $noun = if ($name -ceq 'PathSha256') { 'path' } elseif ($name -ceq 'ProductVersion') { 'ProductVersion' } else { $name }
            throw "$CaseName module $noun does not match the sealed ISRWorldGen binary/PDB identity."
        }
    }

    $bootstrapTokens = Get-StructuredMarker $Log.Content 'L00A_BOOTSTRAP' $CaseName
    if (-not $bootstrapTokens.ContainsKey('pid') -or $bootstrapTokens.pid -cne [string]$pidValue -or
        -not $bootstrapTokens.ContainsKey('side') -or $bootstrapTokens.side -cne 'Server' -or
        -not $bootstrapTokens.ContainsKey('assembly') -or $bootstrapTokens.assembly -cne 'ISRWorldGen.dll' -or
        -not $bootstrapTokens.ContainsKey('sha256') -or $bootstrapTokens.sha256 -cne [string]$assemblyArtifact.Sha256) {
        throw "$CaseName L00A bootstrap identity does not match the sealed module/PID."
    }
    $bootstrapTimestamp = Assert-MarkerTimestampInSession $Log.Content 'L00A_BOOTSTRAP' $CaseName $started $completed
    $probeTimestamp = Assert-MarkerTimestampInSession $Log.Content 'L00B_DEBUG_PROBE_READY' $CaseName $started $completed
    $runtimeMarker = if ($CaseName -in @('new', 'reload')) { 'L02C_NATIVE_PROFILE_FROZEN' } else { 'L02C_NATIVE_PROFILE_REJECTED' }
    $runtimeTimestamp = Assert-MarkerTimestampInSession $Log.Content $runtimeMarker $CaseName $started $completed
    if ([Math]::Abs(($runtimeTimestamp - $breakpointUtc).TotalSeconds) -gt 5) {
        throw "$CaseName breakpoint timestamp is not correlated with the runtime marker."
    }
    if ($probeTimestamp -lt $bootstrapTimestamp.AddSeconds(-2)) { throw "$CaseName bootstrap/probe timestamp order is invalid." }
    return [ordered]@{
        SessionId = $sessionId; ServerPid = $pidValue; StartedUtc = $started.ToString('o')
        BreakpointUtc = $breakpointUtc.ToString('o'); CompletedUtc = $completed.ToString('o')
        LogSha256 = $logSha; BreakpointId = $expectedBreakpoint; CallstackSha256 = $callstackSha
        BootstrapModuleBinding = $true; PdbPairingVerified = $true
    }
}

if ($Phase -eq 'Snapshot') {
    if ([string]::IsNullOrWhiteSpace($ExpectedCommit) -or $ExpectedCommit -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Snapshot requires an exact lowercase 40-character ExpectedCommit.'
    }
    $currentCommit = Get-CurrentCommit
    if ($currentCommit -cne $ExpectedCommit) { throw "Repository HEAD $currentCommit differs from requested snapshot commit $ExpectedCommit." }
    $serverIdentity = Get-ServerIdentity
    $expectedInformationalVersion = "1.0.0+$currentCommit"
    $packageRoot = Join-Path $RepositoryRoot "src\WorldGen.VintageStory\bin\$Configuration\Mods\isrworldgen"
    [IO.Directory]::CreateDirectory($EvidenceRoot) | Out-Null
    [IO.Directory]::CreateDirectory($snapshotDirectory) | Out-Null
    $artifacts = @()
    foreach ($name in $artifactNames) {
        $source = Join-Path $packageRoot $name; $snapshot = Join-Path $snapshotDirectory $name
        Assert-LeafFile $source "packaged $name"
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        Copy-NewFile $source $snapshot
        $snapshotHash = (Get-FileHash -LiteralPath $snapshot -Algorithm SHA256).Hash
        if ($snapshotHash -cne $sourceHash) { throw "CreateNew snapshot hash differs for $name." }
        $versionInfo = (Get-Item -LiteralPath $source).VersionInfo
        $informationalVersion = $null
        if ($name.EndsWith('.dll', [StringComparison]::Ordinal)) {
            $informationalVersion = Get-AssemblyInformationalVersion $source
            if ($versionInfo.ProductVersion -cne $expectedInformationalVersion -or $informationalVersion -cne $expectedInformationalVersion) {
                throw "$name ProductVersion/AssemblyInformationalVersion does not match HEAD $currentCommit."
            }
        }
        $artifacts += [ordered]@{
            FileName = $name; Length = (Get-Item -LiteralPath $snapshot).Length; Sha256 = $snapshotHash
            ProductVersion = $versionInfo.ProductVersion; FileVersion = $versionInfo.FileVersion
            AssemblyInformationalVersion = $informationalVersion; PackagePathSha256 = Get-NormalizedPathSha256 $source
        }
    }
    $symbolPairs = @(
        Get-SymbolPairIdentity (Join-Path $snapshotDirectory 'ISRWorldGen.dll') (Join-Path $snapshotDirectory 'ISRWorldGen.pdb')
        Get-SymbolPairIdentity (Join-Path $snapshotDirectory 'ISRWorldGen.Core.dll') (Join-Path $snapshotDirectory 'ISRWorldGen.Core.pdb')
    )
    $manifest = [ordered]@{
        Schema = 'isrworldgen.t02-05.prelaunch-snapshot.v2'; CreatedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Commit = $currentCommit; ExpectedAssemblyInformationalVersion = $expectedInformationalVersion
        Configuration = $Configuration; CreateNew = $true; Server = $serverIdentity
        Artifacts = $artifacts; SymbolPairs = $symbolPairs
    }
    Write-NewUtf8File $snapshotManifestPath ($manifest | ConvertTo-Json -Depth 12)
    $manifest | ConvertTo-Json -Depth 12
    return
}

foreach ($required in @($NewLog, $ReloadLog, $HeightRefusalLog, $RectangleRefusalLog, $CampaignObservationPath)) {
    if ([string]::IsNullOrWhiteSpace($required)) { throw 'Validate requires all four case logs and CampaignObservationPath.' }
}
$snapshotManifestContent = Read-BoundedTextFile $snapshotManifestPath $maximumManifestBytes 'prelaunch snapshot manifest'
$snapshotManifest = $snapshotManifestContent | ConvertFrom-Json -DateKind String
if ($snapshotManifest.Schema -cne 'isrworldgen.t02-05.prelaunch-snapshot.v2' -or -not $snapshotManifest.CreateNew) {
    throw 'Prelaunch snapshot manifest has an unsupported schema or was not CreateNew-sealed.'
}
$currentCommit = Get-CurrentCommit
$expectedInformationalVersion = "1.0.0+$currentCommit"
if ($snapshotManifest.Commit -cne $currentCommit -or $snapshotManifest.ExpectedAssemblyInformationalVersion -cne $expectedInformationalVersion) {
    throw 'Current HEAD and expected AssemblyInformationalVersion differ from the prelaunch snapshot.'
}
$serverIdentity = Get-ServerIdentity
if ($snapshotManifest.Server.ProductVersion -cne $serverIdentity.ProductVersion -or $snapshotManifest.Server.Sha256 -cne $serverIdentity.Sha256) {
    throw 'Vintage Story server identity changed after the prelaunch snapshot.'
}
$packageRoot = Join-Path $RepositoryRoot "src\WorldGen.VintageStory\bin\$($snapshotManifest.Configuration)\Mods\isrworldgen"
$manifestArtifactNames = @($snapshotManifest.Artifacts | ForEach-Object { [string]$_.FileName } | Sort-Object)
if (($manifestArtifactNames -join '|') -cne (@($artifactNames | Sort-Object) -join '|')) {
    throw 'Prelaunch snapshot does not contain exactly the required two DLL and two PDB artifacts.'
}
foreach ($assemblyName in @('ISRWorldGen.dll', 'ISRWorldGen.Core.dll')) {
    $pdbName = [IO.Path]::ChangeExtension($assemblyName, '.pdb')
    $expectedPair = Get-SymbolPairByAssembly $snapshotManifest $assemblyName
    $snapshotPair = Get-SymbolPairIdentity (Join-Path $snapshotDirectory $assemblyName) (Join-Path $snapshotDirectory $pdbName)
    $packagePair = Get-SymbolPairIdentity (Join-Path $packageRoot $assemblyName) (Join-Path $packageRoot $pdbName)
    Assert-SymbolPairEquals $snapshotPair $expectedPair "snapshotted $assemblyName"
    Assert-SymbolPairEquals $packagePair $expectedPair "packaged $assemblyName"
}
foreach ($artifact in @($snapshotManifest.Artifacts)) {
    if ($artifactNames -cnotcontains $artifact.FileName) { throw "Unexpected artifact in snapshot manifest: $($artifact.FileName)" }
    $snapshotPath = Join-Path $snapshotDirectory $artifact.FileName; $packagePath = Join-Path $packageRoot $artifact.FileName
    Assert-LeafFile $snapshotPath "snapshotted $($artifact.FileName)"; Assert-LeafFile $packagePath "current packaged $($artifact.FileName)"
    $snapshotHash = (Get-FileHash -LiteralPath $snapshotPath -Algorithm SHA256).Hash
    $packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    $packageVersionInfo = (Get-Item -LiteralPath $packagePath).VersionInfo
    if ($snapshotHash -cne $artifact.Sha256 -or $packageHash -cne $artifact.Sha256 -or
        (Get-Item -LiteralPath $snapshotPath).Length -ne [long]$artifact.Length -or
        $packageVersionInfo.ProductVersion -cne $artifact.ProductVersion -or $packageVersionInfo.FileVersion -cne $artifact.FileVersion -or
        (Get-NormalizedPathSha256 $packagePath) -cne $artifact.PackagePathSha256) {
        throw "Packaged or snapshotted artifact changed for $($artifact.FileName)."
    }
    if ($artifact.FileName.EndsWith('.dll', [StringComparison]::Ordinal) -and
        ((Get-AssemblyInformationalVersion $packagePath) -cne $expectedInformationalVersion -or
         $artifact.AssemblyInformationalVersion -cne $expectedInformationalVersion)) {
        throw "$($artifact.FileName) ProductVersion/AssemblyInformationalVersion does not match HEAD."
    }
}

$logs = [ordered]@{
    new = Read-CaseLog $NewLog 'new'; reload = Read-CaseLog $ReloadLog 'reload'
    height = Read-CaseLog $HeightRefusalLog 'height'; rectangle = Read-CaseLog $RectangleRefusalLog 'rectangle'
}
$campaignContent = Read-BoundedTextFile $CampaignObservationPath $maximumCampaignBytes 'campaign observation'
$campaign = $campaignContent | ConvertFrom-Json -DateKind String
Assert-ClosedSchema $campaign @('Schema', 'TestedCommit', 'SnapshotManifestSha256', 'VisualStudioProfile', 'DebuggerTransport', 'Provenance', 'Cases') 'campaign observation'
if ($campaign.Schema -cne 'isrworldgen.t02-05.visual-studio-campaign.v2' -or $campaign.TestedCommit -cne $currentCommit -or
    $campaign.SnapshotManifestSha256 -cne (Get-FileHash -LiteralPath $snapshotManifestPath -Algorithm SHA256).Hash -or
    $campaign.VisualStudioProfile -cne $expectedProfile -or $campaign.DebuggerTransport -cne 'visual-studio-debugger' -or
    $campaign.Provenance -cne $expectedProvenance) {
    throw 'Campaign commit, profile, snapshot, transport, or Visual Studio provenance is absent/unverified.'
}
Assert-ClosedSchema $campaign.Cases $caseNames 'campaign cases'
$observations = [ordered]@{}; $seenSessions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($caseName in $caseNames) {
    $observations[$caseName] = Read-CaseObservation (Get-RequiredProperty $campaign.Cases $caseName 'campaign cases') $caseName $snapshotManifest $logs[$caseName]
    if (-not $seenSessions.Add($observations[$caseName].SessionId)) { throw 'Campaign debugger session identifiers must be unique.' }
    Assert-ContainsOnce $logs[$caseName].Content "L00B_DEBUG_PROBE_READY pid=$($observations[$caseName].ServerPid) " $caseName
    Assert-ContainsOnce $logs[$caseName].Content 'Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API' $caseName
    Assert-ContainsOnce $logs[$caseName].Content 'Stopped the server!' $caseName
    if ($logs[$caseName].Content.IndexOf('Stopped the server!', [StringComparison]::Ordinal) -le
        $logs[$caseName].Content.IndexOf('Server stop requested, begin shutdown sequence.', [StringComparison]::Ordinal)) {
        throw "$caseName log does not order API shutdown request before the stopped marker."
    }
}

$newTokens = Get-StructuredMarker $logs.new.Content 'L02C_NATIVE_PROFILE_FROZEN' 'new'
Assert-ExactToken $newTokens 'profile' 'laboratory' 'new'; Assert-ExactToken $newTokens 'source' 'new' 'new'
Assert-ExactToken $newTokens 'persistencewrites' '2' 'new'; Assert-ExactToken $newTokens 'gatestate' 'Frozen' 'new'
Assert-ExactToken $newTokens 'gatecangenerate' 'true' 'new'; Assert-ExactToken $newTokens 'gatecallbackregistered' 'true' 'new'
Assert-ExactToken $newTokens 'publishedprofile' 'true' 'new'; Assert-ExactToken $newTokens 'dimensions' '4096x256x4096' 'new'
if ($newTokens.envelopebytes -notmatch '^\d+$' -or [int]$newTokens.envelopebytes -lt 1 -or [int]$newTokens.envelopebytes -gt $maximumEnvelopeBytes -or
    $newTokens.envelopesha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'new marker has a non-canonical envelope length or SHA-256.' }
Assert-Omits $logs.new.Content 'L02C_NATIVE_PROFILE_REJECTED' 'new'
Assert-ContainsOnce $logs.new.Content 'L02C_NATIVE_GATE_FROZEN profile=laboratory' 'new'
Assert-ContainsAtLeastOnce $logs.new.Content 'L00B_COLUMN_CALLBACK' 'new'
foreach ($marker in @('L00C_INACTIVE', 'Entering runphase RunGame', 'L00C_WITNESS_NO_REQUEST', 'L00C_DELAYED_SHUTDOWN_ARMED', 'L00C_DELAYED_SHUTDOWN_FIRED', 'Saved savegamedata', 'World saved!')) {
    Assert-ContainsOnce $logs.new.Content $marker 'new'
}
$newOrder = @('Entering runphase GameReady', 'L02C_NATIVE_PROFILE_FROZEN', 'Entering runphase WorldReady', 'L02C_NATIVE_GATE_FROZEN', 'L00B_COLUMN_CALLBACK', 'Entering runphase RunGame', 'L00C_WITNESS_NO_REQUEST', 'L00C_DELAYED_SHUTDOWN_ARMED', 'L00C_DELAYED_SHUTDOWN_FIRED', 'Server stop requested, begin shutdown sequence.', 'Saved savegamedata', 'World saved!', 'Stopped the server!')
$previousOffset = -1
foreach ($marker in $newOrder) {
    $offset = $logs.new.Content.IndexOf($marker, [StringComparison]::Ordinal)
    if ($offset -le $previousOffset) { throw "new log does not prove ordered Frozen/gate/L00B column/runtime/soft-save lifecycle at $marker." }
    $previousOffset = $offset
}
$newInactiveOffset = $logs.new.Content.IndexOf('L00C_INACTIVE', [StringComparison]::Ordinal)
if ($newInactiveOffset -le $logs.new.Content.IndexOf('Entering runphase WorldReady', [StringComparison]::Ordinal) -or
    $newInactiveOffset -ge $logs.new.Content.IndexOf('Entering runphase RunGame', [StringComparison]::Ordinal)) {
    throw 'new log does not place L00C_INACTIVE between WorldReady and RunGame.'
}

$reloadTokens = Get-StructuredMarker $logs.reload.Content 'L02C_NATIVE_PROFILE_FROZEN' 'reload'
Assert-ExactToken $reloadTokens 'profile' 'laboratory' 'reload'; Assert-ExactToken $reloadTokens 'source' 'reload' 'reload'
Assert-ExactToken $reloadTokens 'persistencewrites' '0' 'reload'; Assert-ExactToken $reloadTokens 'gatestate' 'Frozen' 'reload'
Assert-ExactToken $reloadTokens 'gatecangenerate' 'true' 'reload'; Assert-ExactToken $reloadTokens 'gatecallbackregistered' 'false' 'reload'
Assert-ExactToken $reloadTokens 'publishedprofile' 'true' 'reload'; Assert-ExactToken $reloadTokens 'dimensions' '4096x256x4096' 'reload'
Assert-ExactToken $reloadTokens 'envelopebytes' $newTokens.envelopebytes 'reload'; Assert-ExactToken $reloadTokens 'envelopesha256' $newTokens.envelopesha256 'reload'
Assert-Omits $logs.reload.Content 'L02C_NATIVE_PROFILE_REJECTED' 'reload'; Assert-Omits $logs.reload.Content 'L02C_NATIVE_GATE_FROZEN' 'reload'
foreach ($marker in @('L00C_INACTIVE', 'Entering runphase RunGame', 'L00C_WITNESS_NO_REQUEST', 'L00C_DELAYED_SHUTDOWN_ARMED', 'L00C_DELAYED_SHUTDOWN_FIRED', 'Saved savegamedata', 'World saved!')) {
    Assert-ContainsOnce $logs.reload.Content $marker 'reload'
}
$reloadOrder = @('Entering runphase GameReady', 'L02C_NATIVE_PROFILE_FROZEN', 'Entering runphase WorldReady', 'L00C_INACTIVE', 'Entering runphase RunGame', 'L00C_WITNESS_NO_REQUEST', 'L00C_DELAYED_SHUTDOWN_ARMED', 'L00C_DELAYED_SHUTDOWN_FIRED', 'Server stop requested, begin shutdown sequence.', 'Saved savegamedata', 'World saved!', 'Stopped the server!')
$previousOffset = -1
foreach ($marker in $reloadOrder) {
    $offset = $logs.reload.Content.IndexOf($marker, [StringComparison]::Ordinal)
    if ($offset -le $previousOffset) { throw "reload log does not prove Frozen in-memory gate and complete runtime/soft-save lifecycle at $marker." }
    $previousOffset = $offset
}

$refusalExpectations = [ordered]@{
    height = [ordered]@{ Stage = 'atlas.profile.native-height'; Dimensions = '4096x320x4096' }
    rectangle = [ordered]@{ Stage = 'native-profile.effective-dimensions'; Dimensions = '4096x256x8192' }
}
foreach ($caseName in @('height', 'rectangle')) {
    $tokens = Get-StructuredMarker $logs[$caseName].Content 'L02C_NATIVE_PROFILE_REJECTED' $caseName
    Assert-ExactToken $tokens 'code' 'InvalidInput' $caseName; Assert-ExactToken $tokens 'stage' $refusalExpectations[$caseName].Stage $caseName
    Assert-ExactToken $tokens 'source' 'new' $caseName; Assert-ExactToken $tokens 'persistencewrites' '0' $caseName
    Assert-ExactToken $tokens 'envelopebytes' '0' $caseName; Assert-ExactToken $tokens 'envelopesha256' 'none' $caseName
    Assert-ExactToken $tokens 'gatestate' 'Rejected' $caseName; Assert-ExactToken $tokens 'gatecangenerate' 'false' $caseName
    Assert-ExactToken $tokens 'gatecallbackregistered' 'false' $caseName; Assert-ExactToken $tokens 'dimensions' $refusalExpectations[$caseName].Dimensions $caseName
    foreach ($marker in @('L02C_NATIVE_PROFILE_FROZEN', 'L02C_NATIVE_GATE_FROZEN', 'L00B_COLUMN_CALLBACK', 'Entering runphase WorldReady', 'Entering runphase RunGame', 'L00C_WITNESS_NO_REQUEST', 'Saved savegamedata', 'World saved!')) {
        Assert-Omits $logs[$caseName].Content $marker $caseName
    }
}

$caseReports = @()
foreach ($caseName in $caseNames) {
    $caseReports += [ordered]@{
        Case = $caseName; SessionId = $observations[$caseName].SessionId; ServerPid = $observations[$caseName].ServerPid
        StartedUtc = $observations[$caseName].StartedUtc; BreakpointUtc = $observations[$caseName].BreakpointUtc
        CompletedUtc = $observations[$caseName].CompletedUtc; LogSha256 = $observations[$caseName].LogSha256
        BreakpointId = $observations[$caseName].BreakpointId; CallstackSha256 = $observations[$caseName].CallstackSha256
        BootstrapModuleBinding = $true; PdbPairingVerified = $true
    }
}
$report = [ordered]@{
    Schema = 'isrworldgen.t02-05.runtime-evidence.v2'; Status = 'PASS'; Commit = $currentCommit
    ExpectedAssemblyInformationalVersion = $expectedInformationalVersion; Server = $serverIdentity
    SnapshotManifestSha256 = (Get-FileHash -LiteralPath $snapshotManifestPath -Algorithm SHA256).Hash
    CampaignObservationSha256 = (Get-FileHash -LiteralPath $CampaignObservationPath -Algorithm SHA256).Hash
    EnvelopeBytes = [int]$newTokens.envelopebytes; EnvelopeSha256 = $newTokens.envelopesha256
    ReloadOracle = 'frozen-published-gate-before-worldready-zero-writes-no-reload-initworldgenerator-callback'
    VisualStudio = [ordered]@{ Profile = $expectedProfile; DebuggerTransport = 'visual-studio-debugger'; Provenance = $expectedProvenance }
    Cases = $caseReports
}
Write-NewUtf8File (Join-Path $EvidenceRoot 'runtime-evidence.json') ($report | ConvertTo-Json -Depth 12)
$report | ConvertTo-Json -Depth 12
