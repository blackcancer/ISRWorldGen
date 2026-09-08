Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ExpectedCodes = @('S01', 'S02', 'S03', 'S04', 'S05', 'S06')
$script:AllowedFamilies = @('RuggedRanges', 'OldMassifs', 'Plateaus', 'SedimentaryBasins', 'Plains', 'VolcanicDomains')
$script:ReceiptProtocol = 'sha256-run-bound-blind-review-receipt-v1'
$script:CommitmentProtocol = 'sha256-run-bound-selective-opening-v1'
$script:RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$script:EvidenceRoot = [IO.Path]::GetFullPath((Join-Path $script:RepositoryRoot '.local/L03B'))

function Get-L03BSha256Bytes {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Get-L03BSha256File {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-L03BLowerHex {
    param([AllowNull()][string]$Value, [int]$Length)
    return $null -ne $Value -and $Value -cmatch "^[0-9a-f]{$Length}$"
}

function Test-L03BExactStringArray {
    param([AllowNull()]$Value, [Parameter(Mandatory = $true)][string[]]$Expected)
    if ($Value -isnot [object[]]) { return $false }
    $actual = @($Value)
    if ($actual.Count -ne $Expected.Count -or @($actual | Where-Object { $_ -isnot [string] }).Count -ne 0) { return $false }
    return ($actual -join "`n") -ceq ($Expected -join "`n")
}

function Test-L03BPathEquals {
    param([Parameter(Mandatory = $true)][string]$Left, [Parameter(Mandatory = $true)][string]$Right)
    $comparison = if ([OperatingSystem]::IsWindows()) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    return [string]::Equals(
        [IO.Path]::GetFullPath($Left).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
        [IO.Path]::GetFullPath($Right).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
        $comparison)
}

function Assert-L03BPlainDirectory {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Label)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw "$Label must be a physical directory, not a symlink, junction, or other reparse point."
    }
    return $item.FullName
}

function Resolve-L03BCanonicalReviewIdentity {
    param([Parameter(Mandatory = $true)][string]$BlindDirectory, [Parameter(Mandatory = $true)]$Binding)
    $runPattern = '^evidence-s-staging-' + [Regex]::Escape([string]$Binding.commit) + '-[0-9a-f]{32}$'
    if ($Binding.runId -isnot [string] -or [string]$Binding.runId -cnotmatch $runPattern) {
        throw 'Attribution runId is not an official run identity for its bound commit.'
    }
    $expectedTerminal = [IO.Path]::GetFullPath((Join-Path $script:EvidenceRoot "evidence-s-terminal-$($Binding.commit)"))
    $expectedBlind = [IO.Path]::GetFullPath((Join-Path $expectedTerminal 'blind'))
    if (-not (Test-L03BPathEquals $BlindDirectory $expectedBlind)) {
        throw 'BlindDirectory is not the canonical blind child of this run official evidence terminal; copied terminals are forbidden.'
    }
    [void](Assert-L03BPlainDirectory $script:RepositoryRoot 'Repository root')
    [void](Assert-L03BPlainDirectory (Join-Path $script:RepositoryRoot '.local') 'Evidence .local root')
    [void](Assert-L03BPlainDirectory $script:EvidenceRoot 'L03-B evidence root')
    $terminal = Assert-L03BPlainDirectory $expectedTerminal 'Official evidence terminal'
    $blind = Assert-L03BPlainDirectory $expectedBlind 'Official blind directory'
    if (-not (Test-L03BPathEquals ([IO.Path]::GetDirectoryName($blind)) $terminal) -or
        -not (Test-L03BPathEquals ([IO.Path]::GetDirectoryName($terminal)) $script:EvidenceRoot)) {
        throw 'Official evidence terminal hierarchy is not a direct physical child of the L03-B evidence root.'
    }
    return [pscustomobject]@{
        EvidenceRoot = $script:EvidenceRoot
        EvidenceTerminal = $terminal
        BlindDirectory = $blind
        ReviewDirectory = [IO.Path]::GetFullPath((Join-Path $script:EvidenceRoot "evidence-s-review-$($Binding.runId)"))
    }
}

function Assert-L03BExactProperties {
    param([Parameter(Mandatory = $true)]$Value, [Parameter(Mandatory = $true)][string[]]$Expected, [string]$Label)
    if ($null -eq $Value) { throw "$Label is absent." }
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    if (($actual -join "`n") -cne ($wanted -join "`n")) {
        throw "$Label contains missing or unexpected fields."
    }
}

function ConvertTo-L03BCanonicalBytes {
    param([Parameter(Mandatory = $true)]$Value)
    return ,([Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 16 -Compress)))
}

function Assert-L03BFixedHash {
    param([AllowNull()][string]$Actual, [AllowNull()][string]$Expected, [string]$Label)
    if (-not (Test-L03BLowerHex $Actual 64) -or -not (Test-L03BLowerHex $Expected 64)) { throw "$Label is not a canonical SHA-256." }
    $left = [Convert]::FromHexString($Actual)
    $right = [Convert]::FromHexString($Expected)
    try {
        if (-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($left, $right)) { throw "$Label mismatch." }
    } finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($left)
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($right)
    }
}

function Read-L03BBoundedBytes {
    param([Parameter(Mandatory = $true)][string]$Path, [int]$MaximumBytes = 1048576)
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) -or $item.Length -gt $MaximumBytes) {
        throw "Evidence file is absent, is a reparse point, or exceeds $MaximumBytes bytes: $Path"
    }
    return ,([IO.File]::ReadAllBytes($item.FullName))
}

function Read-L03BVerifiedBlindPackage {
    param([Parameter(Mandatory = $true)][string]$BlindDirectory)
    $blind = (Resolve-Path -LiteralPath $BlindDirectory -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $blind -PathType Container)) { throw 'Blind package directory is absent.' }
    $manifestPath = Join-Path $blind 'T03-06-S-manifest.json'
    $manifestBytes = Read-L03BBoundedBytes $manifestPath
    try { $manifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json } catch { throw 'Blind manifest is not valid JSON.' }
    Assert-L03BExactProperties $manifest @('schemaVersion', 'requirementIds', 'automatedStatus', 'qualitativeReviewStatus', 'overallStatus', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256', 'signatureScheme', 'artifacts', 'bundleSignature') 'Blind manifest'
    if ($manifest.schemaVersion -isnot [long] -or [long]$manifest.schemaVersion -ne 1 -or
        -not (Test-L03BExactStringArray $manifest.requirementIds @('R03-05', 'R03-06')) -or
        $manifest.automatedStatus -isnot [string] -or [string]$manifest.automatedStatus -cne 'PASS' -or
        $manifest.qualitativeReviewStatus -isnot [string] -or [string]$manifest.qualitativeReviewStatus -cne 'REVIEW_REQUIRED' -or
        $manifest.overallStatus -isnot [string] -or [string]$manifest.overallStatus -cne 'REVIEW_REQUIRED' -or
        $manifest.configuration -isnot [string] -or [string]$manifest.configuration -cne 'Release' -or
        $manifest.signatureScheme -isnot [string] -or [string]$manifest.signatureScheme -cne 'sha256-canonical-json-v1' -or
        $manifest.commit -isnot [string] -or -not (Test-L03BLowerHex ([string]$manifest.commit) 40) -or
        $manifest.tree -isnot [string] -or -not (Test-L03BLowerHex ([string]$manifest.tree) 40) -or
        $manifest.fixturesBlob -isnot [string] -or -not (Test-L03BLowerHex ([string]$manifest.fixturesBlob) 40) -or
        $manifest.testAssemblySha256 -isnot [string] -or -not (Test-L03BLowerHex ([string]$manifest.testAssemblySha256) 64) -or
        $manifest.coreAssemblySha256 -isnot [string] -or -not (Test-L03BLowerHex ([string]$manifest.coreAssemblySha256) 64) -or
        $manifest.bundleSignature -isnot [string] -or $manifest.artifacts -isnot [object[]]) {
        throw 'Blind manifest is not a reviewable PASS campaign.'
    }
    $artifacts = @($manifest.artifacts)
    if ($artifacts.Count -lt 2) { throw 'Blind manifest artifact list is incomplete.' }
    $artifactRows = @()
    foreach ($artifact in $artifacts) {
        Assert-L03BExactProperties $artifact @('path', 'sha256') 'Blind artifact record'
        if ($artifact.path -isnot [string] -or $artifact.sha256 -isnot [string]) {
            throw 'Blind artifact path and hash must be JSON strings.'
        }
        $relative = [string]$artifact.path
        if (-not $relative.StartsWith('blind/', [StringComparison]::Ordinal) -or $relative.Contains('\') -or
            $relative.Contains('..') -or [IO.Path]::IsPathRooted($relative) -or -not (Test-L03BLowerHex ([string]$artifact.sha256) 64)) {
            throw "Blind artifact path or hash is invalid: $relative"
        }
        $packageRelative = $relative.Substring(6)
        $path = [IO.Path]::GetFullPath((Join-Path $blind $packageRelative))
        $prefix = $blind.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Blind artifact is absent or escapes the package: $relative"
        }
        $artifactItem = Get-Item -LiteralPath $path -Force
        if (($artifactItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Blind artifact must not be a symlink or reparse point: $relative"
        }
        Assert-L03BFixedHash (Get-L03BSha256File $path) ([string]$artifact.sha256) "Blind artifact $relative SHA-256"
        $artifactRows += [ordered]@{ path = $relative; sha256 = [string]$artifact.sha256 }
    }
    $artifactPaths = @($artifactRows | ForEach-Object { $_.path })
    if (@($artifactPaths | Select-Object -Unique).Count -ne $artifactPaths.Count -or
        ($artifactPaths -join "`n") -cne (@($artifactPaths | Sort-Object) -join "`n")) {
        throw 'Blind artifact paths must be unique and ordinally sorted.'
    }
    $packageItems = @(Get-ChildItem -LiteralPath $blind -Recurse -Force)
    if (@($packageItems | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -ne 0) {
        throw 'Blind package contains a symlink, junction, or other reparse point.'
    }
    $expectedFiles = @('T03-06-S-manifest.json') + @($artifactPaths | ForEach-Object { $_.Substring(6) })
    $actualFiles = @($packageItems | Where-Object { -not $_.PSIsContainer } | ForEach-Object {
        [IO.Path]::GetRelativePath($blind, $_.FullName).Replace('\', '/')
    })
    if ((@($expectedFiles | Sort-Object) -join "`n") -cne (@($actualFiles | Sort-Object) -join "`n")) {
        throw 'Blind package contains a missing, replaced, or extra file.'
    }
    $manifestPayload = [ordered]@{
        schemaVersion = [int]$manifest.schemaVersion
        requirementIds = @($manifest.requirementIds)
        automatedStatus = [string]$manifest.automatedStatus
        qualitativeReviewStatus = [string]$manifest.qualitativeReviewStatus
        overallStatus = [string]$manifest.overallStatus
        commit = [string]$manifest.commit
        tree = [string]$manifest.tree
        fixturesBlob = [string]$manifest.fixturesBlob
        configuration = [string]$manifest.configuration
        testAssemblySha256 = [string]$manifest.testAssemblySha256
        coreAssemblySha256 = [string]$manifest.coreAssemblySha256
        signatureScheme = [string]$manifest.signatureScheme
        artifacts = $artifactRows
    }
    Assert-L03BFixedHash ([string]$manifest.bundleSignature) (Get-L03BSha256Bytes (ConvertTo-L03BCanonicalBytes $manifestPayload)) 'Blind manifest bundle signature'

    $requestPath = Join-Path $blind 'T03-06-S-review-request.json'
    $requestBytes = Read-L03BBoundedBytes $requestPath
    $requestHash = Get-L03BSha256Bytes $requestBytes
    $requestArtifact = @($artifactRows | Where-Object { $_.path -ceq 'blind/T03-06-S-review-request.json' })
    if ($requestArtifact.Count -ne 1) { throw 'Blind manifest does not bind exactly one review request.' }
    Assert-L03BFixedHash $requestHash ([string]$requestArtifact[0].sha256) 'Blind review request artifact SHA-256'
    try { $request = [Text.Encoding]::UTF8.GetString($requestBytes) | ConvertFrom-Json } catch { throw 'Blind review request is not valid JSON.' }
    Assert-L03BExactProperties $request @('schemaVersion', 'status', 'trustBoundary', 'instructions', 'allowedFamilies', 'codes') 'Blind review request'
    $requestCodes = @($request.codes)
    $requestFamilies = @($request.allowedFamilies)
    $requestInstructions = @($request.instructions)
    if ($request.schemaVersion -isnot [long] -or [long]$request.schemaVersion -ne 2 -or
        $request.status -isnot [string] -or [string]$request.status -cne 'READY_FOR_EXTERNAL_BLIND_REVIEW' -or
        $request.trustBoundary -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$request.trustBoundary) -or
        $request.codes -isnot [object[]] -or $request.allowedFamilies -isnot [object[]] -or $request.instructions -isnot [object[]] -or
        ($requestCodes -join '|') -cne ($script:ExpectedCodes -join '|') -or
        ($requestFamilies -join '|') -cne ($script:AllowedFamilies -join '|') -or
        @($requestCodes | Where-Object { $_ -isnot [string] }).Count -ne 0 -or
        @($requestFamilies | Where-Object { $_ -isnot [string] }).Count -ne 0 -or
        $requestInstructions.Count -eq 0 -or
        @($requestInstructions | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$_) }).Count -ne 0) {
        throw 'Blind review request schema or exact S01 through S06 code set is invalid.'
    }

    $commitmentsPath = Join-Path $blind 'T03-06-S-attribution-commitments.json'
    $commitmentsBytes = Read-L03BBoundedBytes $commitmentsPath
    $commitmentsHash = Get-L03BSha256Bytes $commitmentsBytes
    $commitmentArtifact = @($artifactRows | Where-Object { $_.path -ceq 'blind/T03-06-S-attribution-commitments.json' })
    if ($commitmentArtifact.Count -ne 1) { throw 'Blind manifest does not bind exactly one attribution commitment bundle.' }
    Assert-L03BFixedHash $commitmentsHash ([string]$commitmentArtifact[0].sha256) 'Attribution commitments artifact SHA-256'
    try { $commitments = [Text.Encoding]::UTF8.GetString($commitmentsBytes) | ConvertFrom-Json } catch { throw 'Attribution commitments are not valid JSON.' }
    Assert-L03BExactProperties $commitments @('schemaVersion', 'requirementIds', 'scheme', 'binding', 'entries', 'bundleSignature') 'Attribution commitments'
    Assert-L03BExactProperties $commitments.binding @('runId', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256') 'Attribution binding'
    if ($commitments.schemaVersion -isnot [long] -or [long]$commitments.schemaVersion -ne 1 -or
        -not (Test-L03BExactStringArray $commitments.requirementIds @('R03-06')) -or
        $commitments.scheme -isnot [string] -or [string]$commitments.scheme -cne $script:CommitmentProtocol -or
        $commitments.bundleSignature -isnot [string] -or $commitments.entries -isnot [object[]]) {
        throw 'Attribution commitment header is invalid.'
    }
    foreach ($name in @('runId', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256')) {
        if ($commitments.binding.$name -isnot [string]) { throw "Attribution binding $name must be a JSON string." }
    }
    $commitmentRows = @()
    foreach ($entry in @($commitments.entries)) {
        Assert-L03BExactProperties $entry @('code', 'commitmentSha256') 'Attribution commitment entry'
        if ($entry.code -isnot [string] -or $entry.commitmentSha256 -isnot [string] -or
            -not (Test-L03BLowerHex ([string]$entry.commitmentSha256) 64)) {
            throw 'Attribution commitment code or hash type is invalid.'
        }
        $commitmentRows += [ordered]@{ code = [string]$entry.code; commitmentSha256 = [string]$entry.commitmentSha256 }
    }
    if ((@($commitmentRows | ForEach-Object { $_.code }) -join '|') -cne ($script:ExpectedCodes -join '|')) {
        throw 'Attribution commitments do not contain the six exact neutral codes.'
    }
    $binding = [ordered]@{
        runId = [string]$commitments.binding.runId
        commit = [string]$commitments.binding.commit
        tree = [string]$commitments.binding.tree
        fixturesBlob = [string]$commitments.binding.fixturesBlob
        configuration = [string]$commitments.binding.configuration
        testAssemblySha256 = [string]$commitments.binding.testAssemblySha256
        coreAssemblySha256 = [string]$commitments.binding.coreAssemblySha256
    }
    if (-not $binding.runId -or $binding.runId -cnotmatch ('^evidence-s-staging-' + [Regex]::Escape($binding.commit) + '-[0-9a-f]{32}$') -or
        $binding.commit -cne [string]$manifest.commit -or $binding.tree -cne [string]$manifest.tree -or
        $binding.fixturesBlob -cne [string]$manifest.fixturesBlob -or $binding.configuration -cne [string]$manifest.configuration -or
        $binding.testAssemblySha256 -cne [string]$manifest.testAssemblySha256 -or
        $binding.coreAssemblySha256 -cne [string]$manifest.coreAssemblySha256) {
        throw 'Attribution commitments and blind manifest belong to different runs.'
    }
    $commitmentPayload = [ordered]@{
        schemaVersion = [int]$commitments.schemaVersion
        requirementIds = @($commitments.requirementIds)
        scheme = [string]$commitments.scheme
        binding = $binding
        entries = $commitmentRows
    }
    Assert-L03BFixedHash ([string]$commitments.bundleSignature) (Get-L03BSha256Bytes (ConvertTo-L03BCanonicalBytes $commitmentPayload)) 'Attribution commitment bundle signature'
    $identity = Resolve-L03BCanonicalReviewIdentity -BlindDirectory $blind -Binding $binding
    return [pscustomobject]@{
        BlindDirectory = $identity.BlindDirectory
        EvidenceTerminal = $identity.EvidenceTerminal
        EvidenceRoot = $identity.EvidenceRoot
        ReviewDirectory = $identity.ReviewDirectory
        Manifest = $manifest
        ManifestBytes = $manifestBytes
        ManifestSha256 = Get-L03BSha256Bytes $manifestBytes
        Binding = $binding
        Commitments = $commitments
        CommitmentsBytes = $commitmentsBytes
        CommitmentsSha256 = $commitmentsHash
        ReviewRequestBytes = $requestBytes
        ReviewRequestSha256 = $requestHash
    }
}

function ConvertTo-L03BReviewEntries {
    param([Parameter(Mandatory = $true)]$Entries)
    $rows = @()
    foreach ($entry in @($Entries)) {
        Assert-L03BExactProperties $entry @('code', 'identifiedFamilyBeforeReveal', 'confidence0To100BeforeReveal', 'morphologyObservations') 'Blind review answer entry'
        if ($entry.code -isnot [string] -or $entry.identifiedFamilyBeforeReveal -isnot [string] -or
            $entry.confidence0To100BeforeReveal -isnot [long] -or $entry.morphologyObservations -isnot [string]) {
            throw 'Blind review answer types must be string, string, integer, and string.'
        }
        $code = [string]$entry.code
        $family = [string]$entry.identifiedFamilyBeforeReveal
        $confidence = [int]$entry.confidence0To100BeforeReveal
        $observation = [string]$entry.morphologyObservations
        if ($script:AllowedFamilies -cnotcontains $family -or $confidence -lt 0 -or $confidence -gt 100 -or
            [string]::IsNullOrWhiteSpace($observation) -or $observation.Length -gt 4096 -or $observation -cne $observation.Trim()) {
            throw "Blind review answer is incomplete or invalid for $code."
        }
        $rows += [ordered]@{
            code = $code
            identifiedFamilyBeforeReveal = $family
            confidence0To100BeforeReveal = $confidence
            morphologyObservations = $observation
        }
    }
    $rows = @($rows | Sort-Object { $_.code })
    if ((@($rows | ForEach-Object { $_.code }) -join '|') -cne ($script:ExpectedCodes -join '|')) {
        throw 'Blind review requires exactly one complete answer for each of S01 through S06.'
    }
    return $rows
}

function New-L03BReviewReceiptBytes {
    param([Parameter(Mandatory = $true)]$BlindPackage, [Parameter(Mandatory = $true)]$Answers, [Parameter(Mandatory = $true)][DateTimeOffset]$RecordedUtc)
    Assert-L03BExactProperties $Answers @('schemaVersion', 'entries') 'Blind review answers'
    if ($Answers.schemaVersion -isnot [long] -or [long]$Answers.schemaVersion -ne 1) { throw 'Blind review answers schema is unsupported.' }
    $entries = ConvertTo-L03BReviewEntries $Answers.entries
    $payload = [ordered]@{
        schemaVersion = 1
        requirementIds = @('R03-06')
        status = 'RECORDED_BEFORE_REVEAL'
        protocol = $script:ReceiptProtocol
        recordedUtc = $RecordedUtc.UtcDateTime.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
        binding = $BlindPackage.Binding
        blindManifest = [ordered]@{
            path = 'blind/T03-06-S-manifest.json'
            sha256 = $BlindPackage.ManifestSha256
            bundleSignature = [string]$BlindPackage.Manifest.bundleSignature
        }
        attributionCommitments = [ordered]@{
            path = 'blind/T03-06-S-attribution-commitments.json'
            sha256 = $BlindPackage.CommitmentsSha256
            bundleSignature = [string]$BlindPackage.Commitments.bundleSignature
        }
        entries = $entries
    }
    $receipt = [ordered]@{}
    foreach ($item in $payload.GetEnumerator()) { $receipt[$item.Key] = $item.Value }
    $receipt.receiptId = Get-L03BSha256Bytes (ConvertTo-L03BCanonicalBytes $payload)
    return ,([Text.Encoding]::UTF8.GetBytes(($receipt | ConvertTo-Json -Depth 16)))
}

function Read-L03BVerifiedReviewReceiptBytes {
    param([Parameter(Mandatory = $true)][byte[]]$ReceiptBytes, [Parameter(Mandatory = $true)]$BlindPackage)
    try { $receipt = [Text.Encoding]::UTF8.GetString($ReceiptBytes) | ConvertFrom-Json -DateKind String } catch { throw 'Blind review receipt is not valid JSON.' }
    Assert-L03BExactProperties $receipt @('schemaVersion', 'requirementIds', 'status', 'protocol', 'recordedUtc', 'binding', 'blindManifest', 'attributionCommitments', 'entries', 'receiptId') 'Blind review receipt'
    Assert-L03BExactProperties $receipt.binding @('runId', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256') 'Blind review receipt binding'
    Assert-L03BExactProperties $receipt.blindManifest @('path', 'sha256', 'bundleSignature') 'Blind review manifest binding'
    Assert-L03BExactProperties $receipt.attributionCommitments @('path', 'sha256', 'bundleSignature') 'Blind review commitment binding'
    if ($receipt.schemaVersion -isnot [long] -or [long]$receipt.schemaVersion -ne 1 -or
        -not (Test-L03BExactStringArray $receipt.requirementIds @('R03-06')) -or
        $receipt.status -isnot [string] -or [string]$receipt.status -cne 'RECORDED_BEFORE_REVEAL' -or
        $receipt.protocol -isnot [string] -or [string]$receipt.protocol -cne $script:ReceiptProtocol -or
        $receipt.recordedUtc -isnot [string] -or $receipt.receiptId -isnot [string] -or
        $receipt.entries -isnot [object[]]) {
        throw 'Blind review receipt header is invalid.'
    }
    foreach ($name in @('runId', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256')) {
        if ($receipt.binding.$name -isnot [string] -or [string]$receipt.binding.$name -cne [string]$BlindPackage.Binding.$name) {
            throw "Blind review receipt binding mismatch or non-string value: $name"
        }
    }
    foreach ($document in @($receipt.blindManifest, $receipt.attributionCommitments)) {
        foreach ($name in @('path', 'sha256', 'bundleSignature')) {
            if ($document.$name -isnot [string]) { throw "Blind review receipt document $name must be a JSON string." }
        }
    }
    if ([string]$receipt.blindManifest.path -cne 'blind/T03-06-S-manifest.json' -or
        [string]$receipt.attributionCommitments.path -cne 'blind/T03-06-S-attribution-commitments.json') { throw 'Blind review receipt document paths are invalid.' }
    Assert-L03BFixedHash ([string]$receipt.blindManifest.sha256) ([string]$BlindPackage.ManifestSha256) 'Blind review manifest SHA-256'
    Assert-L03BFixedHash ([string]$receipt.blindManifest.bundleSignature) ([string]$BlindPackage.Manifest.bundleSignature) 'Blind review manifest commitment'
    Assert-L03BFixedHash ([string]$receipt.attributionCommitments.sha256) ([string]$BlindPackage.CommitmentsSha256) 'Blind review commitments SHA-256'
    Assert-L03BFixedHash ([string]$receipt.attributionCommitments.bundleSignature) ([string]$BlindPackage.Commitments.bundleSignature) 'Blind review commitment bundle signature'
    $entries = ConvertTo-L03BReviewEntries $receipt.entries
    $recordedUtc = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact([string]$receipt.recordedUtc, 'O', [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind, [ref]$recordedUtc) -or $recordedUtc.Offset -ne [TimeSpan]::Zero) {
        throw 'Blind review receipt timestamp is not canonical UTC.'
    }
    $payload = [ordered]@{
        schemaVersion = [int]$receipt.schemaVersion
        requirementIds = @($receipt.requirementIds)
        status = [string]$receipt.status
        protocol = [string]$receipt.protocol
        recordedUtc = [string]$receipt.recordedUtc
        binding = $BlindPackage.Binding
        blindManifest = [ordered]@{ path = [string]$receipt.blindManifest.path; sha256 = [string]$receipt.blindManifest.sha256; bundleSignature = [string]$receipt.blindManifest.bundleSignature }
        attributionCommitments = [ordered]@{ path = [string]$receipt.attributionCommitments.path; sha256 = [string]$receipt.attributionCommitments.sha256; bundleSignature = [string]$receipt.attributionCommitments.bundleSignature }
        entries = $entries
    }
    Assert-L03BFixedHash ([string]$receipt.receiptId) (Get-L03BSha256Bytes (ConvertTo-L03BCanonicalBytes $payload)) 'Blind review receipt ID'
    return [pscustomobject]@{ Receipt = $receipt; RecordedUtc = $recordedUtc; Entries = $entries; ReceiptSha256 = Get-L03BSha256Bytes $ReceiptBytes }
}

function Write-L03BDurableNewFile {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][byte[]]$Bytes)
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $stream.Write($Bytes, 0, $Bytes.Length)
        $stream.Flush($true)
    } finally {
        $stream.Dispose()
    }
}

Export-ModuleMember -Function Get-L03BSha256Bytes, Get-L03BSha256File, Test-L03BLowerHex, Test-L03BExactStringArray, Test-L03BPathEquals, Assert-L03BPlainDirectory, Assert-L03BExactProperties, ConvertTo-L03BCanonicalBytes, Assert-L03BFixedHash, Read-L03BBoundedBytes, Read-L03BVerifiedBlindPackage, ConvertTo-L03BReviewEntries, New-L03BReviewReceiptBytes, Read-L03BVerifiedReviewReceiptBytes, Write-L03BDurableNewFile
