[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$VintageStoryPath = $env:VINTAGE_STORY,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Utf8Fixture {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)

    [IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)

    if (-not $Condition) {
        throw $Message
    }
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $tempBase ("isrworldgen-l02c-runtime-evidence-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $oracle = Join-Path $PSScriptRoot 'Invoke-L02CNativeRuntimeEvidence.ps1'
    $commitResult = & git -c "safe.directory=$RepositoryRoot" -C $RepositoryRoot rev-parse HEAD 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve test repository HEAD: $($commitResult -join [Environment]::NewLine)"
    }

    $commit = ([string]($commitResult | Select-Object -Last 1)).Trim()
    & $oracle -Phase Snapshot -EvidenceRoot $testRoot -RepositoryRoot $RepositoryRoot `
        -VintageStoryPath $VintageStoryPath -Configuration $Configuration -ExpectedCommit $commit | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Runtime evidence snapshot self-test failed.'
    }

    $hash = '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
    $bytes = 406
    $logs = [ordered]@{
        new = @"
L00B_DEBUG_PROBE_READY pid=101 module=ISRWorldGen.dll pass=Terrain worldtype=standard
Entering runphase GameReady
L02C_NATIVE_PROFILE_FROZEN profile=laboratory source=new persistencewrites=2 envelopebytes=$bytes envelopesha256=$hash gatestate=Frozen gatecangenerate=true gatecallbackregistered=true publishedprofile=true dimensions=4096x256x4096 chunk=32 rules=vintagestory-1.22.7-effective-world-v1:1
Entering runphase WorldReady
L02C_NATIVE_GATE_FROZEN profile=laboratory
L00B_COLUMN_CALLBACK chunk=(1,1)
Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API
Saved savegamedata...2
World saved! Saved 1 chunks, 1 mapchunks, 1 mapregions.
Stopped the server!
"@
        reload = @"
L00B_DEBUG_PROBE_READY pid=102 module=ISRWorldGen.dll pass=Terrain worldtype=standard
Entering runphase GameReady
L02C_NATIVE_PROFILE_FROZEN profile=laboratory source=reload persistencewrites=0 envelopebytes=$bytes envelopesha256=$hash gatestate=Frozen gatecangenerate=true gatecallbackregistered=false publishedprofile=true dimensions=4096x256x4096 chunk=32 rules=vintagestory-1.22.7-effective-world-v1:1
Entering runphase WorldReady
Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API
Saved savegamedata...2
World saved! Saved 1 chunks, 1 mapchunks, 1 mapregions.
Stopped the server!
"@
        height = @"
L00B_DEBUG_PROBE_READY pid=103 module=ISRWorldGen.dll pass=Terrain worldtype=standard
Entering runphase GameReady
L02C_NATIVE_PROFILE_REJECTED code=InvalidInput stage=atlas.profile.native-height source=new persistencewrites=0 envelopebytes=0 envelopesha256=none gatestate=Rejected gatecangenerate=false gatecallbackregistered=false details=height dimensions=4096x320x4096 chunk=32 rules=vintagestory-1.22.7-effective-world-v1:1
Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API
Stopped the server!
"@
        rectangle = @"
L00B_DEBUG_PROBE_READY pid=104 module=ISRWorldGen.dll pass=Terrain worldtype=standard
Entering runphase GameReady
L02C_NATIVE_PROFILE_REJECTED code=InvalidInput stage=native-profile.effective-dimensions source=new persistencewrites=0 envelopebytes=0 envelopesha256=none gatestate=Rejected gatecangenerate=false gatecallbackregistered=false details=dimensions dimensions=4096x256x8192 chunk=32 rules=vintagestory-1.22.7-effective-world-v1:1
Server stop requested, begin shutdown sequence. Stop reason: Forced: Shutdown through Server API
Stopped the server!
"@
    }

    foreach ($caseName in @('new', 'reload', 'height', 'rectangle')) {
        Write-Utf8Fixture (Join-Path $testRoot "$caseName.log") $logs[$caseName]
    }

    $observations = [ordered]@{
        cases = [ordered]@{
            new = [ordered]@{ pid = 101; breakpoint = 'GameReady'; callstack = @('Frame.New') ; debuggerClaim = 'fixture claim' }
            reload = [ordered]@{ pid = 102; breakpoint = 'GameReady'; callstack = @('Frame.Reload') ; debuggerClaim = 'fixture claim' }
            height = [ordered]@{ pid = 103; breakpoint = 'ShutDown'; callstack = @('Frame.Height') ; debuggerClaim = 'fixture claim' }
            rectangle = [ordered]@{ pid = 104; breakpoint = 'ShutDown'; callstack = @('Frame.Rectangle') ; debuggerClaim = 'fixture claim' }
        }
    }
    $observationPath = Join-Path $testRoot 'campaign-observation.json'
    Write-Utf8Fixture $observationPath ($observations | ConvertTo-Json -Depth 8)

    & $oracle -Phase Validate -EvidenceRoot $testRoot -RepositoryRoot $RepositoryRoot `
        -VintageStoryPath $VintageStoryPath -Configuration $Configuration `
        -NewLog (Join-Path $testRoot 'new.log') -ReloadLog (Join-Path $testRoot 'reload.log') `
        -HeightRefusalLog (Join-Path $testRoot 'height.log') `
        -RectangleRefusalLog (Join-Path $testRoot 'rectangle.log') `
        -CampaignObservationPath $observationPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Runtime evidence validation self-test failed.'
    }

    $report = Get-Content -LiteralPath (Join-Path $testRoot 'runtime-evidence.json') -Raw | ConvertFrom-Json
    Assert-True ($report.Status -ceq 'PASS') 'Runtime evidence self-test report did not pass.'
    Assert-True ($report.EnvelopeSha256 -ceq $hash) 'Runtime evidence self-test changed the envelope hash.'
    Assert-True ($report.Cases[0].DebuggerObservation.Provenance -ceq 'campaign-supplied-unverified-by-oracle') `
        'Runtime evidence self-test invented debugger provenance.'

    $tamperedHash = '1123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
    Write-Utf8Fixture (Join-Path $testRoot 'reload.log') ($logs.reload.Replace($hash, $tamperedHash))
    $tamperRejected = $false
    try {
        & $oracle -Phase Validate -EvidenceRoot $testRoot -RepositoryRoot $RepositoryRoot `
            -VintageStoryPath $VintageStoryPath -Configuration $Configuration `
            -NewLog (Join-Path $testRoot 'new.log') -ReloadLog (Join-Path $testRoot 'reload.log') `
            -HeightRefusalLog (Join-Path $testRoot 'height.log') `
            -RectangleRefusalLog (Join-Path $testRoot 'rectangle.log') `
            -CampaignObservationPath $observationPath | Out-Null
    }
    catch {
        $tamperRejected = $true
    }
    Assert-True $tamperRejected 'Reload envelope-hash mutation unexpectedly passed the runtime oracle.'
    Write-Utf8Fixture (Join-Path $testRoot 'reload.log') $logs.reload

    $duplicateFailed = $false
    try {
        & $oracle -Phase Snapshot -EvidenceRoot $testRoot -RepositoryRoot $RepositoryRoot `
            -VintageStoryPath $VintageStoryPath -Configuration $Configuration -ExpectedCommit $commit | Out-Null
    }
    catch {
        $duplicateFailed = $true
    }
    Assert-True $duplicateFailed 'CreateNew snapshot unexpectedly allowed evidence replacement.'

    [ordered]@{
        TestId = 'T02-05-RUNTIME-EVIDENCE-ORACLE'
        Status = 'PASS'
        Configuration = $Configuration
        CreateNewReplacementRejected = $duplicateFailed
        EnvelopeHashMutationRejected = $tamperRejected
        DebuggerProvenance = 'campaign-supplied-unverified-by-oracle'
    } | ConvertTo-Json -Depth 4
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTestRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Split-Path -Leaf $resolvedTestRoot).StartsWith('isrworldgen-l02c-runtime-evidence-', [StringComparison]::Ordinal)) {
        throw "Refusing to clean unexpected runtime evidence self-test path: $resolvedTestRoot"
    }

    if (Test-Path -LiteralPath $resolvedTestRoot) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
