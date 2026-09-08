Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ExpectedCodes = @('S01', 'S02', 'S03', 'S04', 'S05', 'S06')
$script:AllowedFamilies = @('RuggedRanges', 'OldMassifs', 'Plateaus', 'SedimentaryBasins', 'Plains', 'VolcanicDomains')
$script:ReceiptProtocol = 'sha256-run-bound-blind-review-receipt-v1'
$script:CommitmentProtocol = 'sha256-run-bound-selective-opening-v1'

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
    if ($item.PSIsContainer -or $item.Length -gt $MaximumBytes) { throw "Evidence file is absent, not a file, or exceeds $MaximumBytes bytes: $Path" }
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
    if ([int]$manifest.schemaVersion -ne 1 -or (@($manifest.requirementIds) -join '|') -cne 'R03-05|R03-06' -or
        [string]$manifest.automatedStatus -cne 'PASS' -or [string]$manifest.qualitativeReviewStatus -cne 'REVIEW_REQUIRED' -or
        [string]$manifest.overallStatus -cne 'REVIEW_REQUIRED' -or [string]$manifest.configuration -cne 'Release' -or
        [string]$manifest.signatureScheme -cne 'sha256-canonical-json-v1' -or
        -not (Test-L03BLowerHex ([string]$manifest.commit) 40) -or -not (Test-L03BLowerHex ([string]$manifest.tree) 40) -or
        -not (Test-L03BLowerHex ([string]$manifest.fixturesBlob) 40) -or
        -not (Test-L03BLowerHex ([string]$manifest.testAssemblySha256) 64) -or
        -not (Test-L03BLowerHex ([string]$manifest.coreAssemblySha256) 64)) {
        throw 'Blind manifest is not a reviewable PASS campaign.'
    }
    $artifacts = @($manifest.artifacts)
    if ($artifacts.Count -lt 2) { throw 'Blind manifest artifact list is incomplete.' }
    $artifactRows = @()
    foreach ($artifact in $artifacts) {
        Assert-L03BExactProperties $artifact @('path', 'sha256') 'Blind artifact record'
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
        Assert-L03BFixedHash (Get-L03BSha256File $path) ([string]$artifact.sha256) "Blind artifact $relative SHA-256"
        $artifactRows += [ordered]@{ path = $relative; sha256 = [string]$artifact.sha256 }
    }
    $artifactPaths = @($artifactRows | ForEach-Object { $_.path })
    if (@($artifactPaths | Select-Object -Unique).Count -ne $artifactPaths.Count -or
        ($artifactPaths -join "`n") -cne (@($artifactPaths | Sort-Object) -join "`n")) {
        throw 'Blind artifact paths must be unique and ordinally sorted.'
    }
    $expectedFiles = @('T03-06-S-manifest.json') + @($artifactPaths | ForEach-Object { $_.Substring(6) })
    $actualFiles = @(Get-ChildItem -LiteralPath $blind -File -Recurse -Force | ForEach-Object {
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

    $commitmentsPath = Join-Path $blind 'T03-06-S-attribution-commitments.json'
    $commitmentsBytes = Read-L03BBoundedBytes $commitmentsPath
    $commitmentsHash = Get-L03BSha256Bytes $commitmentsBytes
    $commitmentArtifact = @($artifactRows | Where-Object { $_.path -ceq 'blind/T03-06-S-attribution-commitments.json' })
    if ($commitmentArtifact.Count -ne 1) { throw 'Blind manifest does not bind exactly one attribution commitment bundle.' }
    Assert-L03BFixedHash $commitmentsHash ([string]$commitmentArtifact[0].sha256) 'Attribution commitments artifact SHA-256'
    try { $commitments = [Text.Encoding]::UTF8.GetString($commitmentsBytes) | ConvertFrom-Json } catch { throw 'Attribution commitments are not valid JSON.' }
    Assert-L03BExactProperties $commitments @('schemaVersion', 'requirementIds', 'scheme', 'binding', 'entries', 'bundleSignature') 'Attribution commitments'
    Assert-L03BExactProperties $commitments.binding @('runId', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256') 'Attribution binding'
    if ([int]$commitments.schemaVersion -ne 1 -or (@($commitments.requirementIds) -join '|') -cne 'R03-06' -or
        [string]$commitments.scheme -cne $script:CommitmentProtocol) { throw 'Attribution commitment header is invalid.' }
    $commitmentRows = @()
    foreach ($entry in @($commitments.entries)) {
        Assert-L03BExactProperties $entry @('code', 'commitmentSha256') 'Attribution commitment entry'
        if (-not (Test-L03BLowerHex ([string]$entry.commitmentSha256) 64)) { throw 'Attribution commitment hash is invalid.' }
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
    if (-not $binding.runId -or $binding.runId.Length -gt 255 -or $binding.runId.Contains('..') -or
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
    return [pscustomobject]@{
        BlindDirectory = $blind
        Manifest = $manifest
        ManifestBytes = $manifestBytes
        ManifestSha256 = Get-L03BSha256Bytes $manifestBytes
        Binding = $binding
        Commitments = $commitments
        CommitmentsBytes = $commitmentsBytes
        CommitmentsSha256 = $commitmentsHash
    }
}

function ConvertTo-L03BReviewEntries {
    param([Parameter(Mandatory = $true)]$Entries)
    $rows = @()
    foreach ($entry in @($Entries)) {
        Assert-L03BExactProperties $entry @('code', 'identifiedFamilyBeforeReveal', 'confidence0To100BeforeReveal', 'morphologyObservations') 'Blind review answer entry'
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
    if ([int]$Answers.schemaVersion -ne 1) { throw 'Blind review answers schema is unsupported.' }
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
    if ([int]$receipt.schemaVersion -ne 1 -or (@($receipt.requirementIds) -join '|') -cne 'R03-06' -or
        [string]$receipt.status -cne 'RECORDED_BEFORE_REVEAL' -or [string]$receipt.protocol -cne $script:ReceiptProtocol) {
        throw 'Blind review receipt header is invalid.'
    }
    foreach ($name in @('runId', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256')) {
        if ([string]$receipt.binding.$name -cne [string]$BlindPackage.Binding.$name) { throw "Blind review receipt binding mismatch: $name" }
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

Export-ModuleMember -Function Get-L03BSha256Bytes, Get-L03BSha256File, Test-L03BLowerHex, Assert-L03BExactProperties, ConvertTo-L03BCanonicalBytes, Assert-L03BFixedHash, Read-L03BBoundedBytes, Read-L03BVerifiedBlindPackage, ConvertTo-L03BReviewEntries, New-L03BReviewReceiptBytes, Read-L03BVerifiedReviewReceiptBytes, Write-L03BDurableNewFile
