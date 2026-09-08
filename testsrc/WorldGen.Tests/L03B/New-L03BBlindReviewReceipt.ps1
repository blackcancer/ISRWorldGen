[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BlindDirectory,
    [Parameter(Mandatory = $true)][string]$AnswersPath,
    [string]$ReviewParent)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'L03BBlindReviewProtocol.psm1') -Force

$blind = (Resolve-Path -LiteralPath $BlindDirectory -ErrorAction Stop).Path
$answers = (Resolve-Path -LiteralPath $AnswersPath -ErrorAction Stop).Path
$review = $null
$publishing = $null
try {
    $package = Read-L03BVerifiedBlindPackage $blind
    if ([IO.Path]::GetFileName($blind) -cne 'blind') { throw 'BlindDirectory must be the exact blind child of an evidence terminal.' }
    $evidenceTerminal = [IO.Path]::GetDirectoryName($blind)
    $canonicalParent = [IO.Path]::GetDirectoryName($evidenceTerminal)
    if (-not $canonicalParent -or -not (Test-Path -LiteralPath $canonicalParent -PathType Container)) {
        throw 'The canonical review parent derived from BlindDirectory is absent.'
    }
    if ($ReviewParent) {
        $assertedParent = [IO.Path]::GetFullPath($ReviewParent).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if ($assertedParent -cne $canonicalParent.TrimEnd([IO.Path]::DirectorySeparatorChar)) {
            throw 'ReviewParent does not match the canonical parent derived from BlindDirectory; alternate review locations are forbidden.'
        }
    }
    $review = Join-Path $canonicalParent "evidence-s-review-$($package.Binding.runId)"
    if (Test-Path -LiteralPath $review) {
        throw 'Canonical review terminal already exists; a receipt cannot be modified, replaced, or resubmitted.'
    }
    $publishing = "$review-publishing-$([guid]::NewGuid().ToString('N'))"
    $answerBytes = Read-L03BBoundedBytes -Path $answers -MaximumBytes 65536
    try { $answerDocument = [Text.Encoding]::UTF8.GetString($answerBytes) | ConvertFrom-Json } catch { throw 'Blind review answers are not valid JSON.' }
    $recordedUtc = [DateTimeOffset]::UtcNow
    $receiptBytes = New-L03BReviewReceiptBytes -BlindPackage $package -Answers $answerDocument -RecordedUtc $recordedUtc

    [void][IO.Directory]::CreateDirectory($publishing)
    $receiptPath = Join-Path $publishing 'T03-06-S-review-receipt.json'
    Write-L03BDurableNewFile -Path $receiptPath -Bytes $receiptBytes
    [IO.Directory]::Move($publishing, $review)

    $summary = [ordered]@{
        status = 'RECORDED_BEFORE_REVEAL'
        reviewDirectory = $review
        receiptPath = Join-Path $review 'T03-06-S-review-receipt.json'
        receiptId = [string](([Text.Encoding]::UTF8.GetString($receiptBytes) | ConvertFrom-Json).receiptId)
        receiptFileSha256 = Get-L03BSha256Bytes $receiptBytes
        recordedUtc = $recordedUtc.UtcDateTime.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
        instruction = 'Give this entire immutable directory and its receiptFileSha256 to the controller. Do not access sealed campaign artifacts.'
    }
    $summary | ConvertTo-Json -Depth 4
} finally {
    if ($publishing -and (Test-Path -LiteralPath $publishing)) { Remove-Item -LiteralPath $publishing -Recurse -Force }
}
