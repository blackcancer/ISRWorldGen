param(
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$LogDirectory,
    [int]$Seed = 73
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not (Test-Path -LiteralPath $OutputPath -PathType Container)) { throw 'The refusal fixture must already exist' }
if (Test-Path -LiteralPath $LogDirectory) { throw 'Evidence log directory already exists' }
New-Item -ItemType Directory -Path $LogDirectory | Out-Null
function Get-Snapshot {
    $root = (Resolve-Path -LiteralPath $OutputPath).Path
    $entries = @(Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [System.IO.Path]::GetRelativePath($root, $_.FullName); length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    return ConvertTo-Json -InputObject $entries -Depth 5 -Compress
}
$before = Get-Snapshot
$log = Join-Path $LogDirectory 'expected-refusal.log'
dotnet run --no-build --project testsrc/WorldGen.OceanPrehistoryCampaign/WorldGen.OceanPrehistoryCampaign.csproj -c Release -- $OutputPath $Seed 2>&1 | Out-File -LiteralPath $log -Encoding utf8
$actualExitCode = $LASTEXITCODE
$after = Get-Snapshot
if ($actualExitCode -eq 0) { throw 'Existing evidence was accepted for overwrite' }
if ($before -cne $after) { throw 'Earlier evidence was modified during refusal' }
if (-not ((Get-Content -LiteralPath $log -Raw).Contains('Evidence path exists; never overwrite a campaign.'))) { throw 'Failure was not the expected C# existing-directory refusal' }
$before | Set-Content -LiteralPath (Join-Path $LogDirectory 'before.json') -Encoding utf8
$after | Set-Content -LiteralPath (Join-Path $LogDirectory 'after.json') -Encoding utf8
[ordered]@{ status='PASS'; expected='C# IOException existing-directory refusal'; observedExitCode=$actualExitCode; priorFilesIntact=$true; scope='No full-world recomputation'; } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $LogDirectory 'receipt.json') -Encoding utf8
# The command MUST fail, but the successful negative test MUST return zero.
# GitHub's pwsh wrapper otherwise propagates the last native exit code.
exit 0
