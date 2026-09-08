[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$ExpectedReceiptSha256)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'L03BBlindReviewProtocol.psm1') -Force

function Read-HeldReceiptBytes {
    param([Parameter(Mandatory = $true)][IO.FileStream]$Stream)
    if ($Stream.Length -gt 131072) { throw 'Blind review receipt exceeds 128 KiB.' }
    $bytes = [byte[]]::new([int]$Stream.Length)
    $offset = 0
    while ($offset -lt $bytes.Length) {
        $read = $Stream.Read($bytes, $offset, $bytes.Length - $offset)
        if ($read -eq 0) { throw 'Blind review receipt ended before its declared length.' }
        $offset += $read
    }
    return ,$bytes
}

function Read-VerifiedSealedCampaign {
    param([string]$Evidence, $BlindPackage)
    [void](Assert-L03BPlainDirectory (Join-Path $Evidence 'sealed') 'Official sealed directory')
    $manifestPath = Join-Path $Evidence 'sealed/T03-05-06-S-manifest.json'
    $manifestBytes = Read-L03BBoundedBytes $manifestPath
    try { $manifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json } catch { throw 'Sealed campaign manifest is not valid JSON.' }
    Assert-L03BExactProperties $manifest @('schemaVersion', 'requirementIds', 'automatedStatus', 'qualitativeReviewStatus', 'overallStatus', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256', 'signatureScheme', 'artifacts', 'bundleSignature') 'Sealed campaign manifest'
    if ($manifest.schemaVersion -isnot [long] -or [long]$manifest.schemaVersion -ne 1 -or
        -not (Test-L03BExactStringArray $manifest.requirementIds @('R03-05', 'R03-06')) -or
        $manifest.automatedStatus -isnot [string] -or [string]$manifest.automatedStatus -cne 'PASS' -or
        $manifest.qualitativeReviewStatus -isnot [string] -or [string]$manifest.qualitativeReviewStatus -cne 'REVIEW_REQUIRED' -or
        $manifest.overallStatus -isnot [string] -or [string]$manifest.overallStatus -cne 'REVIEW_REQUIRED' -or
        $manifest.configuration -isnot [string] -or [string]$manifest.configuration -cne 'Release' -or
        $manifest.signatureScheme -isnot [string] -or [string]$manifest.signatureScheme -cne 'sha256-canonical-json-v1' -or
        $manifest.bundleSignature -isnot [string] -or $manifest.artifacts -isnot [object[]]) {
        throw 'Sealed campaign is not a reviewable PASS publication.'
    }
    foreach ($name in @('commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256')) {
        if ($manifest.$name -isnot [string] -or [string]$manifest.$name -cne [string]$BlindPackage.Binding.$name) {
            throw "Sealed campaign binding mismatch or non-string value: $name"
        }
    }
    $artifacts = @()
    foreach ($artifact in @($manifest.artifacts)) {
        Assert-L03BExactProperties $artifact @('path', 'sha256') 'Sealed campaign artifact'
        if ($artifact.path -isnot [string] -or $artifact.sha256 -isnot [string]) {
            throw 'Sealed campaign artifact path and hash must be JSON strings.'
        }
        $relative = [string]$artifact.path
        if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains('..') -or $relative.Contains('\') -or
            -not (Test-L03BLowerHex ([string]$artifact.sha256) 64)) { throw "Invalid sealed campaign artifact path: $relative" }
        $path = [IO.Path]::GetFullPath((Join-Path $Evidence $relative))
        $prefix = $Evidence.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Sealed campaign artifact is absent or escapes the terminal: $relative"
        }
        $artifactItem = Get-Item -LiteralPath $path -Force
        if (($artifactItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Sealed campaign artifact must not be a symlink or reparse point: $relative"
        }
        Assert-L03BFixedHash (Get-L03BSha256File $path) ([string]$artifact.sha256) "Sealed artifact $relative SHA-256"
        $artifacts += [ordered]@{ path = $relative; sha256 = [string]$artifact.sha256 }
    }
    $paths = @($artifacts | ForEach-Object { $_.path })
    if (@($paths | Select-Object -Unique).Count -ne $paths.Count -or ($paths -join "`n") -cne (@($paths | Sort-Object) -join "`n")) {
        throw 'Sealed campaign artifact list is not unique and sorted.'
    }
    foreach ($required in @('blind/T03-06-S-manifest.json', 'blind/T03-06-S-attribution-commitments.json',
        'sealed/T03-05-06-S.json', 'sealed/T03-05-06-S.trx', 'sealed/T03-06-S-progress.json',
        'sealed/T03-06-S-review-key.json', 'sealed/T03-06-S-success.json')) {
        if ($paths -cnotcontains $required) { throw "Sealed campaign manifest omits required artifact $required" }
    }
    $payload = [ordered]@{
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
        artifacts = $artifacts
    }
    Assert-L03BFixedHash ([string]$manifest.bundleSignature) (Get-L03BSha256Bytes (ConvertTo-L03BCanonicalBytes $payload)) 'Sealed campaign manifest signature'
    return [pscustomobject]@{ Manifest = $manifest; Artifacts = $artifacts }
}

function Assert-SuccessMarker {
    param([string]$Evidence, $BlindPackage, $SealedCampaign)
    $path = Join-Path $Evidence 'sealed/T03-06-S-success.json'
    try { $marker = [IO.File]::ReadAllText($path) | ConvertFrom-Json } catch { throw 'Campaign success marker is not valid JSON.' }
    Assert-L03BExactProperties $marker @('schemaVersion', 'status', 'runId', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256', 'reportSha256', 'blindManifestSha256', 'reviewKeySha256', 'commitmentsSha256') 'Campaign success marker'
    if ($marker.schemaVersion -isnot [long] -or [long]$marker.schemaVersion -ne 1 -or
        $marker.status -isnot [string] -or [string]$marker.status -cne 'COMPLETE' -or
        $marker.runId -isnot [string] -or [string]$marker.runId -cne [string]$BlindPackage.Binding.runId) {
        throw 'Campaign success marker is incomplete or belongs to another run.'
    }
    foreach ($name in @('commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256')) {
        if ($marker.$name -isnot [string] -or [string]$marker.$name -cne [string]$BlindPackage.Binding.$name) {
            throw "Campaign success marker binding mismatch or non-string value: $name"
        }
    }
    foreach ($name in @('reportSha256', 'blindManifestSha256', 'reviewKeySha256', 'commitmentsSha256')) {
        if ($marker.$name -isnot [string]) { throw "Campaign success marker $name must be a JSON string." }
    }
    $expected = @{
        reportSha256 = Get-L03BSha256File (Join-Path $Evidence 'sealed/T03-05-06-S.json')
        blindManifestSha256 = $BlindPackage.ManifestSha256
        reviewKeySha256 = Get-L03BSha256File (Join-Path $Evidence 'sealed/T03-06-S-review-key.json')
        commitmentsSha256 = $BlindPackage.CommitmentsSha256
    }
    foreach ($name in $expected.Keys) { Assert-L03BFixedHash ([string]$marker.$name) ([string]$expected[$name]) "Campaign success marker $name" }
    try { $report = [IO.File]::ReadAllText((Join-Path $Evidence 'sealed/T03-05-06-S.json')) | ConvertFrom-Json } catch { throw 'Campaign report is not valid JSON.' }
    if ($report.automatedStatus -isnot [string] -or [string]$report.automatedStatus -cne 'PASS' -or
        $report.qualitativeReviewStatus -isnot [string] -or [string]$report.qualitativeReviewStatus -cne 'REVIEW_REQUIRED' -or
        $report.overallStatus -isnot [string] -or [string]$report.overallStatus -cne 'REVIEW_REQUIRED') {
        throw 'Campaign report is not a reviewable PASS result.'
    }
    return $marker
}

function Read-VerifiedReviewKey {
    param([string]$Evidence, $BlindPackage)
    # This is the only key-content read in the controller. The caller reaches it
    # only while holding an already verified durable receipt against replacement.
    $keyPath = Join-Path $Evidence 'sealed/T03-06-S-review-key.json'
    $keyBytes = Read-L03BBoundedBytes -Path $keyPath -MaximumBytes 131072
    try { $key = [Text.Encoding]::UTF8.GetString($keyBytes) | ConvertFrom-Json } catch { throw 'Sealed review key is not valid JSON.' }
    Assert-L03BExactProperties $key @('schemaVersion', 'status', 'protocol', 'binding', 'blindManifestSha256', 'attributionCommitmentsSha256', 'nonce', 'entries') 'Sealed review key'
    Assert-L03BExactProperties $key.binding @('runId', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256') 'Sealed review key binding'
    if ($key.schemaVersion -isnot [long] -or [long]$key.schemaVersion -ne 3 -or
        $key.status -isnot [string] -or [string]$key.status -cne 'SEALED_AWAITING_VERIFIED_RECEIPT' -or
        $key.protocol -isnot [string] -or [string]$key.protocol -cne 'sha256-run-bound-blind-review-receipt-v1' -or
        $key.blindManifestSha256 -isnot [string] -or $key.attributionCommitmentsSha256 -isnot [string] -or
        $key.nonce -isnot [string] -or $key.entries -isnot [object[]]) { throw 'Sealed review key header is invalid.' }
    foreach ($name in @('runId', 'commit', 'tree', 'fixturesBlob', 'configuration', 'testAssemblySha256', 'coreAssemblySha256')) {
        if ($key.binding.$name -isnot [string] -or [string]$key.binding.$name -cne [string]$BlindPackage.Binding.$name) {
            throw "Sealed review key binding mismatch or non-string value: $name"
        }
    }
    Assert-L03BFixedHash ([string]$key.blindManifestSha256) ([string]$BlindPackage.ManifestSha256) 'Sealed review key blind manifest SHA-256'
    Assert-L03BFixedHash ([string]$key.attributionCommitmentsSha256) ([string]$BlindPackage.CommitmentsSha256) 'Sealed review key commitment SHA-256'
    $nonce = [string]$key.nonce
    if (-not (Test-L03BLowerHex $nonce 64)) { throw 'Sealed review key nonce is invalid.' }
    $keyRows = @()
    foreach ($entry in @($key.entries)) {
        Assert-L03BExactProperties $entry @('code', 'family') 'Sealed review key entry'
        if ($entry.code -isnot [string] -or $entry.family -isnot [string]) { throw 'Sealed review key entry values must be JSON strings.' }
        $keyRows += [ordered]@{ code = [string]$entry.code; family = [string]$entry.family }
    }
    $codes = @($keyRows | ForEach-Object { $_.code })
    $families = @($keyRows | ForEach-Object { $_.family })
    if (($codes -join '|') -cne 'S01|S02|S03|S04|S05|S06' -or @($families | Select-Object -Unique).Count -ne 6) {
        throw 'Sealed review key mapping is incomplete or duplicated.'
    }
    $nonceBytes = [Convert]::FromHexString($nonce)
    try {
        foreach ($row in $keyRows) {
            $context = [ordered]@{ domain = 'ISRW-L03B-FAILURE-OPENING-V1'; binding = $BlindPackage.Binding; code = $row.code }
            $contextBytes = ConvertTo-L03BCanonicalBytes $context
            try {
                $hmac = [Security.Cryptography.HMACSHA256]::new($nonceBytes)
                try { $opening = [Convert]::ToHexString($hmac.ComputeHash($contextBytes)).ToLowerInvariant() } finally { $hmac.Dispose() }
            } finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($contextBytes) }
            $openingPayload = [ordered]@{ domain = 'ISRW-L03B-FAILURE-COMMITMENT-V1'; binding = $BlindPackage.Binding; code = $row.code; family = $row.family; opening = $opening }
            $openingBytes = ConvertTo-L03BCanonicalBytes $openingPayload
            try { $expectedCommitment = Get-L03BSha256Bytes $openingBytes } finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($openingBytes) }
            $committed = @($BlindPackage.Commitments.entries | Where-Object { [string]$_.code -ceq [string]$row.code })
            if ($committed.Count -ne 1) { throw "No unique attribution commitment exists for $($row.code)." }
            Assert-L03BFixedHash ([string]$committed[0].commitmentSha256) $expectedCommitment "Review key opening $($row.code)"
        }
    } finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($nonceBytes)
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($keyBytes)
        $nonce = $null
    }
    return $keyRows
}

$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$blindPackage = Read-L03BVerifiedBlindPackage (Join-Path $evidence 'blind')
$evidence = [string]$blindPackage.EvidenceTerminal
$review = [string]$blindPackage.ReviewDirectory
if (-not (Test-Path -LiteralPath $review -PathType Container)) { throw 'Canonical review terminal is absent for this blind run.' }
$review = Assert-L03BPlainDirectory $review 'Canonical review terminal'
$reveal = "$evidence-review-reveal"
$revealParent = [IO.Path]::GetDirectoryName($reveal)
if (-not $revealParent -or -not (Test-Path -LiteralPath $revealParent -PathType Container)) { throw 'Reveal directory parent must already exist.' }
$reviewFiles = @(Get-ChildItem -LiteralPath $review -File -Recurse -Force)
if ($reviewFiles.Count -ne 1 -or $reviewFiles[0].Name -cne 'T03-06-S-review-receipt.json' -or $reviewFiles[0].DirectoryName -cne $review) {
    throw 'Review directory must contain exactly one atomic receipt and no replacement or partial file.'
}
if (($reviewFiles[0].Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Canonical blind review receipt must be a physical file, not a symlink or reparse point.'
}

$receiptPath = $reviewFiles[0].FullName
$publishing = "$reveal-publishing-$([guid]::NewGuid().ToString('N'))"
$controllerLockPath = "$evidence-review-controller.lock"
$controllerLock = $null
$receiptStream = $null
$receiptBytes = $null
try {
    try {
        $controllerLock = [IO.FileStream]::new($controllerLockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None, 1, [IO.FileOptions]::DeleteOnClose)
    } catch [IO.IOException] {
        throw 'Another controller owns the exclusive review/reveal lock for this evidence terminal.'
    }
    $receiptStream = [IO.FileStream]::new($receiptPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $receiptBytes = Read-HeldReceiptBytes $receiptStream
    $receiptFileSha256 = Get-L03BSha256Bytes $receiptBytes
    if (Test-Path -LiteralPath $reveal) {
        $existingPath = Join-Path $reveal 'T03-06-S-review-reveal.json'
        try { $existing = [IO.File]::ReadAllText($existingPath) | ConvertFrom-Json } catch { throw 'Existing reveal publication is invalid; manual replacement is forbidden.' }
        if ([string]$existing.receipt.receiptFileSha256 -cne $receiptFileSha256) { throw 'Blind review receipt was modified or replaced after reveal.' }
        Assert-L03BFixedHash $receiptFileSha256 $ExpectedReceiptSha256 'Expected blind review receipt SHA-256'
        throw 'Reveal publication already exists; review and reveal are immutable and cannot be repeated.'
    }
    Assert-L03BFixedHash $receiptFileSha256 $ExpectedReceiptSha256 'Expected blind review receipt SHA-256'
    $verifiedReceipt = Read-L03BVerifiedReviewReceiptBytes -ReceiptBytes $receiptBytes -BlindPackage $blindPackage
    $now = [DateTimeOffset]::UtcNow
    if ($verifiedReceipt.RecordedUtc -gt $now.AddMinutes(5)) { throw 'Blind review receipt timestamp is in the future.' }
    if ([Math]::Abs(($reviewFiles[0].LastWriteTimeUtc - $verifiedReceipt.RecordedUtc.UtcDateTime).TotalMinutes) -gt 5) {
        throw 'Blind review receipt filesystem time does not corroborate its recorded UTC timestamp.'
    }

    $sealedCampaign = Read-VerifiedSealedCampaign -Evidence $evidence -BlindPackage $blindPackage
    $successMarker = Assert-SuccessMarker -Evidence $evidence -BlindPackage $blindPackage -SealedCampaign $sealedCampaign

    # Do not move this call above receipt verification. It is the reveal boundary.
    $keyRows = Read-VerifiedReviewKey -Evidence $evidence -BlindPackage $blindPackage
    $revealedUtc = [DateTimeOffset]::UtcNow
    if ($revealedUtc -lt $verifiedReceipt.RecordedUtc) { throw 'Reveal time cannot precede durable receipt time.' }
    $results = foreach ($answer in $verifiedReceipt.Entries) {
        $mapping = @($keyRows | Where-Object { $_.code -ceq $answer.code })
        if ($mapping.Count -ne 1) { throw "Review key has no unique mapping for $($answer.code)." }
        [ordered]@{
            code = $answer.code
            identifiedFamilyBeforeReveal = $answer.identifiedFamilyBeforeReveal
            confidence0To100BeforeReveal = $answer.confidence0To100BeforeReveal
            morphologyObservations = $answer.morphologyObservations
            revealedFamily = $mapping[0].family
            identifiedCorrectly = $answer.identifiedFamilyBeforeReveal -ceq $mapping[0].family
        }
    }
    $revealDocument = [ordered]@{
        schemaVersion = 1
        status = 'REVEALED_AFTER_VERIFIED_RECEIPT'
        protocol = 'sha256-run-bound-blind-review-receipt-v1'
        revealedUtc = $revealedUtc.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
        binding = $blindPackage.Binding
        receipt = [ordered]@{
            recordedUtc = $verifiedReceipt.RecordedUtc.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
            receiptId = [string]$verifiedReceipt.Receipt.receiptId
            receiptFileSha256 = $receiptFileSha256
        }
        blindManifestSha256 = $blindPackage.ManifestSha256
        attributionCommitmentsSha256 = $blindPackage.CommitmentsSha256
        reviewKeySha256 = [string]$successMarker.reviewKeySha256
        entries = @($results)
        correctIdentifications = @($results | Where-Object { $_.identifiedCorrectly }).Count
        totalIdentifications = @($results).Count
        qualitativeStatus = 'REVIEW_RECORDED_NOT_AUTOMATICALLY_APPROVED'
    }
    $revealBytes = [Text.Encoding]::UTF8.GetBytes(($revealDocument | ConvertTo-Json -Depth 16))
    [void][IO.Directory]::CreateDirectory($publishing)
    Write-L03BDurableNewFile -Path (Join-Path $publishing 'T03-06-S-review-reveal.json') -Bytes $revealBytes
    [IO.Directory]::Move($publishing, $reveal)
    [ordered]@{
        status = 'REVEALED_AFTER_VERIFIED_RECEIPT'
        revealDirectory = $reveal
        receiptId = [string]$verifiedReceipt.Receipt.receiptId
        receiptFileSha256 = $receiptFileSha256
        correctIdentifications = $revealDocument.correctIdentifications
        totalIdentifications = $revealDocument.totalIdentifications
        qualitativeStatus = $revealDocument.qualitativeStatus
    } | ConvertTo-Json -Depth 4
} finally {
    if ($null -ne $receiptStream) { $receiptStream.Dispose() }
    if ($null -ne $receiptBytes) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($receiptBytes) }
    if (Test-Path -LiteralPath $publishing) { Remove-Item -LiteralPath $publishing -Recurse -Force }
    if ($null -ne $controllerLock) { $controllerLock.Dispose() }
    if (Test-Path -LiteralPath $controllerLockPath) { Remove-Item -LiteralPath $controllerLockPath -Force }
}
