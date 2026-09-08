[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BlindDirectory,
    [Parameter(Mandatory = $true)][string]$AnswersPath,
    [Parameter(Mandatory = $true)][string]$ReviewDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'L03BBlindReviewProtocol.psm1') -Force

$blind = (Resolve-Path -LiteralPath $BlindDirectory -ErrorAction Stop).Path
$answers = (Resolve-Path -LiteralPath $AnswersPath -ErrorAction Stop).Path
$review = [IO.Path]::GetFullPath($ReviewDirectory)
$reviewParent = [IO.Path]::GetDirectoryName($review)
if (-not $reviewParent -or -not (Test-Path -LiteralPath $reviewParent -PathType Container)) {
    throw 'The review directory parent must already exist.'
}
$blindPrefix = $blind.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($review -eq $blind -or $review.StartsWith($blindPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The durable review receipt must be stored outside the immutable blind package.'
}
if (Test-Path -LiteralPath $review) {
    throw 'Review directory already exists; a receipt cannot be modified, replaced, or resubmitted.'
}

$publishing = "$review-publishing-$([guid]::NewGuid().ToString('N'))"
try {
    $package = Read-L03BVerifiedBlindPackage $blind
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
    if (Test-Path -LiteralPath $publishing) { Remove-Item -LiteralPath $publishing -Recurse -Force }
}
