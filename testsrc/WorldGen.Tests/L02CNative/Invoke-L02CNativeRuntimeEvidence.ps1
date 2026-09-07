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
$artifactNames = @(
    'ISRWorldGen.dll',
    'ISRWorldGen.Core.dll',
    'ISRWorldGen.pdb',
    'ISRWorldGen.Core.pdb'
)

function Assert-LeafFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Evidence)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required $Evidence file is missing: $Path"
    }
}

function Write-NewUtf8File {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)

    $encoding = New-Object System.Text.UTF8Encoding($false)
    $bytes = $encoding.GetBytes($Content)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
}

function Copy-NewFile {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)

    $inputStream = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $outputStream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $inputStream.CopyTo($outputStream)
        }
        finally {
            $outputStream.Dispose()
        }
    }
    finally {
        $inputStream.Dispose()
    }
}

function Get-CurrentCommit {
    $result = & git -c "safe.directory=$RepositoryRoot" -C $RepositoryRoot rev-parse HEAD 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve repository HEAD: $($result -join [Environment]::NewLine)"
    }

    return ([string]($result | Select-Object -Last 1)).Trim()
}

function Get-ServerIdentity {
    if ([string]::IsNullOrWhiteSpace($VintageStoryPath)) {
        throw 'VintageStoryPath or VINTAGE_STORY must identify the audited installation.'
    }

    $serverPath = Join-Path $VintageStoryPath 'VintagestoryServer.exe'
    Assert-LeafFile $serverPath 'Vintage Story server'
    $productVersion = (Get-Item -LiteralPath $serverPath).VersionInfo.ProductVersion
    $sha256 = (Get-FileHash -LiteralPath $serverPath -Algorithm SHA256).Hash
    if ($productVersion -ne $expectedServerProductVersion -or $sha256 -ne $expectedServerSha256) {
        throw "Vintage Story server identity differs from the audited 1.22.7 executable (ProductVersion=$productVersion SHA256=$sha256)."
    }

    return [ordered]@{
        FileName = 'VintagestoryServer.exe'
        ProductVersion = $productVersion
        Sha256 = $sha256
    }
}

function Get-PropertyValue {
    param([Parameter(Mandatory)]$Object, [Parameter(Mandatory)][string]$Name)

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Required campaign observation property is absent: $Name"
    }

    return $property.Value
}

function Assert-ExactToken {
    param(
        [Parameter(Mandatory)][hashtable]$Tokens,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Expected,
        [Parameter(Mandatory)][string]$CaseName
    )

    if (-not $Tokens.ContainsKey($Name) -or $Tokens[$Name] -cne $Expected) {
        $actual = if ($Tokens.ContainsKey($Name)) { $Tokens[$Name] } else { '<absent>' }
        throw "$CaseName marker token $Name expected '$Expected', observed '$actual'."
    }
}

function Get-StructuredMarker {
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$Marker,
        [Parameter(Mandatory)][string]$CaseName
    )

    $lines = @($Content -split "`r?`n" | Where-Object { $_.Contains($Marker, [StringComparison]::Ordinal) })
    if ($lines.Count -ne 1) {
        throw "$CaseName expected exactly one $Marker marker, observed $($lines.Count)."
    }

    $markerOffset = $lines[0].IndexOf($Marker, [StringComparison]::Ordinal)
    $payload = $lines[0].Substring($markerOffset)
    $tokens = @{}
    foreach ($part in ($payload -split ' ')) {
        $separator = $part.IndexOf('=')
        if ($separator -gt 0) {
            $name = $part.Substring(0, $separator)
            if (-not $tokens.ContainsKey($name)) {
                $tokens[$name] = $part.Substring($separator + 1)
            }
        }
    }

    return $tokens
}

function Assert-ContainsOnce {
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$Marker,
        [Parameter(Mandatory)][string]$CaseName
    )

    $count = ([regex]::Matches($Content, [regex]::Escape($Marker))).Count
    if ($count -ne 1) {
        throw "$CaseName expected exactly one '$Marker', observed $count."
    }
}

function Assert-Omits {
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$Marker,
        [Parameter(Mandatory)][string]$CaseName
    )

    if ($Content.Contains($Marker, [StringComparison]::Ordinal)) {
        throw "$CaseName unexpectedly contains '$Marker'."
    }
}

function Read-CaseLog {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$CaseName)

    Assert-LeafFile $Path "$CaseName log"
    return [ordered]@{
        Content = Get-Content -LiteralPath $Path -Raw
        Sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }
}

function Read-CampaignObservation {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$CaseName)

    $campaign = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $cases = Get-PropertyValue $campaign 'cases'
    $observation = Get-PropertyValue $cases $CaseName
    $pidValue = [int](Get-PropertyValue $observation 'pid')
    $breakpoint = [string](Get-PropertyValue $observation 'breakpoint')
    $callstack = @((Get-PropertyValue $observation 'callstack'))
    if ($pidValue -le 0 -or [string]::IsNullOrWhiteSpace($breakpoint) -or $breakpoint.Length -gt 512 -or
        $callstack.Count -lt 1 -or $callstack.Count -gt 64 -or
        @($callstack | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) -or ([string]$_).Length -gt 512 }).Count -ne 0) {
        throw "$CaseName campaign observation has invalid bounded PID/breakpoint/callstack evidence."
    }

    $debuggerClaim = $null
    if ($null -ne $observation.PSObject.Properties['debuggerClaim']) {
        $debuggerClaim = [string]$observation.debuggerClaim
        if ($debuggerClaim.Length -gt 128) {
            throw "$CaseName debugger claim exceeds its evidence bound."
        }
    }

    return [ordered]@{
        Pid = $pidValue
        Breakpoint = $breakpoint
        Callstack = $callstack
        DebuggerClaim = $debuggerClaim
        Provenance = 'campaign-supplied-unverified-by-oracle'
    }
}

if ($Phase -eq 'Snapshot') {
    if ([string]::IsNullOrWhiteSpace($ExpectedCommit) -or $ExpectedCommit -notmatch '^[0-9a-f]{40}$') {
        throw 'Snapshot requires an exact lowercase 40-character ExpectedCommit.'
    }

    $currentCommit = Get-CurrentCommit
    if ($currentCommit -cne $ExpectedCommit) {
        throw "Repository HEAD $currentCommit differs from requested snapshot commit $ExpectedCommit."
    }

    $serverIdentity = Get-ServerIdentity
    $packageRoot = Join-Path $RepositoryRoot "src\WorldGen.VintageStory\bin\$Configuration\Mods\isrworldgen"
    [IO.Directory]::CreateDirectory($EvidenceRoot) | Out-Null
    [IO.Directory]::CreateDirectory($snapshotDirectory) | Out-Null
    $artifacts = @()
    foreach ($name in $artifactNames) {
        $source = Join-Path $packageRoot $name
        $snapshot = Join-Path $snapshotDirectory $name
        Assert-LeafFile $source "packaged $name"
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        Copy-NewFile $source $snapshot
        $snapshotHash = (Get-FileHash -LiteralPath $snapshot -Algorithm SHA256).Hash
        if ($snapshotHash -ne $sourceHash) {
            throw "CreateNew snapshot hash differs for $name."
        }

        $versionInfo = (Get-Item -LiteralPath $source).VersionInfo
        $artifacts += [ordered]@{
            FileName = $name
            Length = (Get-Item -LiteralPath $snapshot).Length
            Sha256 = $snapshotHash
            ProductVersion = $versionInfo.ProductVersion
            FileVersion = $versionInfo.FileVersion
        }
    }

    $manifest = [ordered]@{
        Schema = 'isrworldgen.t02-05.prelaunch-snapshot.v1'
        Commit = $currentCommit
        Configuration = $Configuration
        CreateNew = $true
        Server = $serverIdentity
        Artifacts = $artifacts
    }
    Write-NewUtf8File $snapshotManifestPath ($manifest | ConvertTo-Json -Depth 8)
    $manifest | ConvertTo-Json -Depth 8
    return
}

foreach ($required in @($NewLog, $ReloadLog, $HeightRefusalLog, $RectangleRefusalLog, $CampaignObservationPath)) {
    if ([string]::IsNullOrWhiteSpace($required)) {
        throw 'Validate requires all four case logs and CampaignObservationPath.'
    }
}

Assert-LeafFile $snapshotManifestPath 'prelaunch snapshot manifest'
Assert-LeafFile $CampaignObservationPath 'campaign observation'
$snapshotManifest = Get-Content -LiteralPath $snapshotManifestPath -Raw | ConvertFrom-Json
if ($snapshotManifest.Schema -cne 'isrworldgen.t02-05.prelaunch-snapshot.v1' -or -not $snapshotManifest.CreateNew) {
    throw 'Prelaunch snapshot manifest has an unsupported schema or was not CreateNew-sealed.'
}

$currentCommit = Get-CurrentCommit
if ($snapshotManifest.Commit -cne $currentCommit) {
    throw "Current commit $currentCommit differs from prelaunch snapshot commit $($snapshotManifest.Commit)."
}

$serverIdentity = Get-ServerIdentity
if ($snapshotManifest.Server.ProductVersion -cne $serverIdentity.ProductVersion -or
    $snapshotManifest.Server.Sha256 -cne $serverIdentity.Sha256) {
    throw 'Vintage Story server identity changed after the prelaunch snapshot.'
}

$packageRoot = Join-Path $RepositoryRoot "src\WorldGen.VintageStory\bin\$($snapshotManifest.Configuration)\Mods\isrworldgen"
$manifestArtifactNames = @($snapshotManifest.Artifacts | ForEach-Object { [string]$_.FileName } | Sort-Object)
$expectedArtifactNames = @($artifactNames | Sort-Object)
if (($manifestArtifactNames -join '|') -cne ($expectedArtifactNames -join '|')) {
    throw 'Prelaunch snapshot does not contain exactly the required two DLL and two PDB artifacts.'
}

foreach ($artifact in @($snapshotManifest.Artifacts)) {
    if ($artifactNames -cnotcontains $artifact.FileName) {
        throw "Unexpected artifact in snapshot manifest: $($artifact.FileName)"
    }

    $snapshotPath = Join-Path $snapshotDirectory $artifact.FileName
    $packagePath = Join-Path $packageRoot $artifact.FileName
    Assert-LeafFile $snapshotPath "snapshotted $($artifact.FileName)"
    Assert-LeafFile $packagePath "current packaged $($artifact.FileName)"
    $snapshotHash = (Get-FileHash -LiteralPath $snapshotPath -Algorithm SHA256).Hash
    $packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    $packageVersionInfo = (Get-Item -LiteralPath $packagePath).VersionInfo
    if ($snapshotHash -cne $artifact.Sha256 -or $packageHash -cne $artifact.Sha256 -or
        (Get-Item -LiteralPath $snapshotPath).Length -ne [long]$artifact.Length -or
        $packageVersionInfo.ProductVersion -cne $artifact.ProductVersion -or
        $packageVersionInfo.FileVersion -cne $artifact.FileVersion) {
        throw "Packaged or snapshotted artifact changed for $($artifact.FileName)."
    }
}

$logs = [ordered]@{
    new = Read-CaseLog $NewLog 'new'
    reload = Read-CaseLog $ReloadLog 'reload'
    height = Read-CaseLog $HeightRefusalLog 'height'
    rectangle = Read-CaseLog $RectangleRefusalLog 'rectangle'
}
$observations = [ordered]@{}
foreach ($caseName in @('new', 'reload', 'height', 'rectangle')) {
    $observations[$caseName] = Read-CampaignObservation $CampaignObservationPath $caseName
    Assert-ContainsOnce $logs[$caseName].Content "L00B_DEBUG_PROBE_READY pid=$($observations[$caseName].Pid) " $caseName
    Assert-ContainsOnce $logs[$caseName].Content 'Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API' $caseName
    Assert-ContainsOnce $logs[$caseName].Content 'Stopped the server!' $caseName
    $stopRequestOffset = $logs[$caseName].Content.IndexOf('Server stop requested, begin shutdown sequence.', [StringComparison]::Ordinal)
    $stoppedOffset = $logs[$caseName].Content.IndexOf('Stopped the server!', [StringComparison]::Ordinal)
    if ($stoppedOffset -le $stopRequestOffset) {
        throw "$caseName log does not order API shutdown request before the stopped marker."
    }
}

$newTokens = Get-StructuredMarker $logs.new.Content 'L02C_NATIVE_PROFILE_FROZEN' 'new'
Assert-ExactToken $newTokens 'profile' 'laboratory' 'new'
Assert-ExactToken $newTokens 'source' 'new' 'new'
Assert-ExactToken $newTokens 'persistencewrites' '2' 'new'
Assert-ExactToken $newTokens 'gatestate' 'Frozen' 'new'
Assert-ExactToken $newTokens 'gatecangenerate' 'true' 'new'
Assert-ExactToken $newTokens 'gatecallbackregistered' 'true' 'new'
Assert-ExactToken $newTokens 'publishedprofile' 'true' 'new'
Assert-ExactToken $newTokens 'dimensions' '4096x256x4096' 'new'
if ($newTokens.envelopebytes -notmatch '^\d+$' -or [int]$newTokens.envelopebytes -lt 1 -or
    [int]$newTokens.envelopebytes -gt $maximumEnvelopeBytes -or $newTokens.envelopesha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'new marker has a non-canonical envelope length or SHA-256.'
}
Assert-Omits $logs.new.Content 'L02C_NATIVE_PROFILE_REJECTED' 'new'
Assert-ContainsOnce $logs.new.Content 'L02C_NATIVE_GATE_FROZEN profile=laboratory' 'new'
Assert-ContainsOnce $logs.new.Content 'Saved savegamedata' 'new'
Assert-ContainsOnce $logs.new.Content 'World saved!' 'new'
$newGameReadyOffset = $logs.new.Content.IndexOf('Entering runphase GameReady', [StringComparison]::Ordinal)
$newWorldReadyOffset = $logs.new.Content.IndexOf('Entering runphase WorldReady', [StringComparison]::Ordinal)
$newFrozenOffset = $logs.new.Content.IndexOf('L02C_NATIVE_PROFILE_FROZEN', [StringComparison]::Ordinal)
$newGateOffset = $logs.new.Content.IndexOf('L02C_NATIVE_GATE_FROZEN', [StringComparison]::Ordinal)
$newColumnOffset = $logs.new.Content.IndexOf('L00B_COLUMN_CALLBACK', [StringComparison]::Ordinal)
if ($newGameReadyOffset -lt 0 -or $newWorldReadyOffset -le $newGameReadyOffset -or
    $newFrozenOffset -le $newGameReadyOffset -or $newFrozenOffset -ge $newWorldReadyOffset -or
    $newGateOffset -le $newFrozenOffset -or ($newColumnOffset -ge 0 -and $newColumnOffset -le $newGateOffset)) {
    throw 'new log does not prove Frozen preparation before the registered gate and first ISR column callback.'
}

$reloadTokens = Get-StructuredMarker $logs.reload.Content 'L02C_NATIVE_PROFILE_FROZEN' 'reload'
Assert-ExactToken $reloadTokens 'profile' 'laboratory' 'reload'
Assert-ExactToken $reloadTokens 'source' 'reload' 'reload'
Assert-ExactToken $reloadTokens 'persistencewrites' '0' 'reload'
Assert-ExactToken $reloadTokens 'gatestate' 'Frozen' 'reload'
Assert-ExactToken $reloadTokens 'gatecangenerate' 'true' 'reload'
Assert-ExactToken $reloadTokens 'gatecallbackregistered' 'false' 'reload'
Assert-ExactToken $reloadTokens 'publishedprofile' 'true' 'reload'
Assert-ExactToken $reloadTokens 'dimensions' '4096x256x4096' 'reload'
Assert-ExactToken $reloadTokens 'envelopebytes' $newTokens.envelopebytes 'reload'
Assert-ExactToken $reloadTokens 'envelopesha256' $newTokens.envelopesha256 'reload'
Assert-Omits $logs.reload.Content 'L02C_NATIVE_PROFILE_REJECTED' 'reload'
Assert-Omits $logs.reload.Content 'L02C_NATIVE_GATE_FROZEN' 'reload'
Assert-ContainsOnce $logs.reload.Content 'Saved savegamedata' 'reload'
Assert-ContainsOnce $logs.reload.Content 'World saved!' 'reload'
$reloadGameReadyOffset = $logs.reload.Content.IndexOf('Entering runphase GameReady', [StringComparison]::Ordinal)
$reloadWorldReadyOffset = $logs.reload.Content.IndexOf('Entering runphase WorldReady', [StringComparison]::Ordinal)
$reloadFrozenOffset = $logs.reload.Content.IndexOf('L02C_NATIVE_PROFILE_FROZEN', [StringComparison]::Ordinal)
if ($reloadGameReadyOffset -lt 0 -or $reloadWorldReadyOffset -le $reloadGameReadyOffset -or
    $reloadFrozenOffset -le $reloadGameReadyOffset -or $reloadFrozenOffset -ge $reloadWorldReadyOffset) {
    throw 'reload log does not prove Frozen PublishedProfile and generating gate before WorldReady.'
}

$refusalExpectations = [ordered]@{
    height = [ordered]@{ Stage = 'atlas.profile.native-height'; Dimensions = '4096x320x4096' }
    rectangle = [ordered]@{ Stage = 'native-profile.effective-dimensions'; Dimensions = '4096x256x8192' }
}
foreach ($caseName in @('height', 'rectangle')) {
    $tokens = Get-StructuredMarker $logs[$caseName].Content 'L02C_NATIVE_PROFILE_REJECTED' $caseName
    Assert-ExactToken $tokens 'code' 'InvalidInput' $caseName
    Assert-ExactToken $tokens 'stage' $refusalExpectations[$caseName].Stage $caseName
    Assert-ExactToken $tokens 'source' 'new' $caseName
    Assert-ExactToken $tokens 'persistencewrites' '0' $caseName
    Assert-ExactToken $tokens 'envelopebytes' '0' $caseName
    Assert-ExactToken $tokens 'envelopesha256' 'none' $caseName
    Assert-ExactToken $tokens 'gatestate' 'Rejected' $caseName
    Assert-ExactToken $tokens 'gatecangenerate' 'false' $caseName
    Assert-ExactToken $tokens 'gatecallbackregistered' 'false' $caseName
    Assert-ExactToken $tokens 'dimensions' $refusalExpectations[$caseName].Dimensions $caseName
    Assert-Omits $logs[$caseName].Content 'L02C_NATIVE_PROFILE_FROZEN' $caseName
    Assert-Omits $logs[$caseName].Content 'L02C_NATIVE_GATE_FROZEN' $caseName
    Assert-Omits $logs[$caseName].Content 'L00B_COLUMN_CALLBACK' $caseName
    Assert-Omits $logs[$caseName].Content 'Entering runphase WorldReady' $caseName
}

$caseReports = @()
foreach ($caseName in @('new', 'reload', 'height', 'rectangle')) {
    $caseReports += [ordered]@{
        Case = $caseName
        LogSha256 = $logs[$caseName].Sha256
        DebuggerObservation = $observations[$caseName]
    }
}

$report = [ordered]@{
    Schema = 'isrworldgen.t02-05.runtime-evidence.v1'
    Status = 'PASS'
    Commit = $currentCommit
    Server = $serverIdentity
    SnapshotManifestSha256 = (Get-FileHash -LiteralPath $snapshotManifestPath -Algorithm SHA256).Hash
    CampaignObservationSha256 = (Get-FileHash -LiteralPath $CampaignObservationPath -Algorithm SHA256).Hash
    EnvelopeBytes = [int]$newTokens.envelopebytes
    EnvelopeSha256 = $newTokens.envelopesha256
    ReloadOracle = 'Frozen PublishedProfile Gate.CanGenerate before WorldReady; zero writes; no reload InitWorldGenerator callback required by design'
    Cases = $caseReports
}
$reportPath = Join-Path $EvidenceRoot 'runtime-evidence.json'
Write-NewUtf8File $reportPath ($report | ConvertTo-Json -Depth 12)
$report | ConvertTo-Json -Depth 12
