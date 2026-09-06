[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
    throw "L00-C evidence is missing: $EvidencePath"
}

$evidencePathResolved = (Resolve-Path -LiteralPath $EvidencePath).Path
$evidenceRoot = Split-Path -Parent $evidencePathResolved
$evidence = Get-Content -LiteralPath $evidencePathResolved -Raw | ConvertFrom-Json

function Assert-Equal {
    param($Actual, $Expected, [string]$Label)
    if ($Actual -ne $Expected) {
        throw "$Label expected '$Expected' but was '$Actual'."
    }
}

function Resolve-EvidenceFile {
    param([string]$RelativePath, [string]$Label)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or $RelativePath -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "$Label must be a safe relative evidence path."
    }

    $root = [IO.Path]::GetFullPath($evidenceRoot).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath((Join-Path $evidenceRoot $RelativePath))
    if (-not $resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label escapes the evidence root."
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "$Label is missing: $resolved"
    }

    return $resolved
}

function Assert-Artifact {
    param($Entry, [string]$Label)

    $path = Resolve-EvidenceFile ([string]$Entry.Path) $Label
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $length = (Get-Item -LiteralPath $path).Length
    Assert-Equal $hash ([string]$Entry.Sha256) "$Label SHA256"
    Assert-Equal $length ([long]$Entry.Length) "$Label length"
    return $path
}

if ([string]$evidence.TestedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'TestedCommit must be a full lowercase Git commit id.'
}
& git -C $RepositoryRoot cat-file -e "$($evidence.TestedCommit)^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "TestedCommit is unavailable locally: $($evidence.TestedCommit)"
}

foreach ($testName in @('T00-04', 'T00-05', 'T00-06')) {
    $test = $evidence.Tests.$testName
    if ($null -eq $test) { throw "Missing evidence section: $testName" }
    Assert-Equal $test.Status 'PASS' "$testName status"
}

$dll = Assert-Artifact $evidence.Artifacts.Assembly 'Assembly'
$pdb = Assert-Artifact $evidence.Artifacts.Symbols 'Symbols'
$artifactDirectoryName = Split-Path -Leaf (Split-Path -Parent $dll)
Assert-Equal $artifactDirectoryName $evidence.TestedCommit 'Assembly artifact directory'
Assert-Equal (Split-Path -Parent $pdb) (Split-Path -Parent $dll) 'Assembly/symbol directory'

$sessionLogPaths = @()
foreach ($session in @($evidence.Sessions)) {
    if ([int]$session.ProcessId -le 0) { throw 'Every session must record a real process id.' }
    if ([string]$session.InstanceId -notmatch '^[0-9a-f]{32}$') { throw 'Every session must record a 32-character instance id.' }
    if ($session.WorldRole -like 'activated-*' -and [string]$session.MarkerId -notmatch '^[0-9a-f]{32}$') {
        throw 'Every activated session must record a 32-character marker id.'
    }
    if ($session.WorldRole -notlike 'activated-*' -and $session.MarkerId -ne 'none') {
        throw 'Inactive and missing-handler sessions must explicitly record MarkerId=none.'
    }

    $sessionLogPath = Assert-Artifact $session.ServerMainLog "Session $($session.Cycle) server-main.log"
    $sessionLogPaths += $sessionLogPath
    $log = Get-Content -LiteralPath $sessionLogPath -Raw
    $instance = [regex]::Escape([string]$session.InstanceId)
    $pid = [regex]::Escape([string]$session.ProcessId)
    if ($log -notmatch "L00C_PROBE_READY instance=$instance pid=$pid ") {
        throw "Session $($session.Cycle) log does not correlate instance and process id."
    }

    if ($session.WorldRole -eq 'disabled-witness') {
        Assert-Equal $session.GracefulShutdown $true "Session $($session.Cycle) graceful shutdown"
        if ($log -notmatch "L00C_INACTIVE instance=$instance " -or $log -notmatch "L00C_WITNESS_LOADED instance=$instance ") {
            throw 'Disabled witness log is missing its inactive or inspection marker.'
        }
        if ($log -match "L00C_FIXTURE_WRITTEN instance=$instance ") {
            throw 'Disabled witness unexpectedly ran the L00-C fixture writer.'
        }
    }
    elseif ($session.WorldRole -eq 'missing-handler') {
        if ($log -notmatch "L00C_ERROR code=expected-handler-absent instance=$instance ") {
            throw 'Missing-handler session lacks the explicit expected-handler-absent error.'
        }
        if ($log -match "L00C_ACTIVATED instance=$instance " -or $log -match "L00C_FIXTURE_WRITTEN instance=$instance ") {
            throw 'Missing-handler session silently activated or wrote the fixture.'
        }
    }
    elseif ($session.WorldRole -like 'activated-*') {
        Assert-Equal $session.GracefulShutdown $true "Session $($session.Cycle) graceful shutdown"
        $marker = [regex]::Escape([string]$session.MarkerId)
        $open = [regex]::Escape([string]$session.OpenCount)
        $isNew = ([string]$session.IsNew).ToLowerInvariant()
        $requiredPatterns = @(
            "L00C_HANDLERS phase=before instance=$instance ",
            "L00C_HANDLERS phase=after instance=$instance ",
            "L00C_ACTIVATED instance=$instance marker=$marker open=$open isnew=$isNew ",
            "L00C_FIXTURE_WRITTEN instance=$instance marker=$marker ",
            "L00C_FIXTURE_INSPECTED instance=$instance marker=$marker phase=loaded ",
            "L00C_FIXTURE_INSPECTED instance=$instance marker=$marker phase=afterticks ",
            "L00C_TICKS_STABLE instance=$instance marker=$marker ticks=40 ",
            "L00C_MARKER_SAVED instance=$instance marker=$marker open=$open"
        )
        foreach ($pattern in $requiredPatterns) {
            if ($log -notmatch $pattern) {
                throw "Activated session $($session.Cycle) is missing log pattern: $pattern"
            }
        }
        if ($log -notmatch "L00C_TICKS_STABLE instance=$instance .* unexpected=0") {
            throw "Activated session $($session.Cycle) did not preserve the exact fixture after bounded ticks."
        }
    }
    else {
        throw "Unknown WorldRole: $($session.WorldRole)"
    }

    Assert-Equal $session.DebuggerFinalMode 'Design' "Session $($session.Cycle) debugger final mode"
    Assert-Equal $session.ProcessAbsent $true "Session $($session.Cycle) process absent"
}

$witnessSessions = @($evidence.Sessions | Where-Object { $_.WorldRole -eq 'disabled-witness' })
if ($witnessSessions.Count -ne 1) { throw "Expected one disabled witness session, found $($witnessSessions.Count)." }
$missingSessions = @($evidence.Sessions | Where-Object { $_.WorldRole -eq 'missing-handler' })
if ($missingSessions.Count -ne 1) { throw "Expected one missing-handler session, found $($missingSessions.Count)." }

$cycleSessions = @($evidence.Sessions | Where-Object { $_.WorldRole -eq 'activated-primary' })
if ($cycleSessions.Count -ne 5) {
    throw "T00-06 requires exactly five activated-primary cycles, found $($cycleSessions.Count)."
}

$orderedPrimary = @($cycleSessions | Sort-Object {[int]$_.Cycle})
for ($index = 0; $index -lt $orderedPrimary.Count; $index++) {
    Assert-Equal ([int]$orderedPrimary[$index].OpenCount) ($index + 1) "Primary cycle $($index + 1) open count"
    Assert-Equal ([bool]$orderedPrimary[$index].IsNew) ($index -eq 0) "Primary cycle $($index + 1) IsNew"
}

$primaryMarkers = @($cycleSessions | Select-Object -ExpandProperty MarkerId -Unique)
if ($primaryMarkers.Count -ne 1) {
    throw 'The five primary cycles must reload the same persistent marker.'
}

$processIds = @($evidence.Sessions | Select-Object -ExpandProperty ProcessId -Unique)
if ($processIds.Count -ne @($evidence.Sessions).Count) {
    throw 'Each lifecycle session must have a distinct process id.'
}

$second = @($evidence.Sessions | Where-Object { $_.WorldRole -eq 'activated-secondary' })
if ($second.Count -ne 1 -or $second[0].MarkerId -eq $primaryMarkers[0]) {
    throw 'The second world must have one distinct persistent marker.'
}
Assert-Equal ([int]$second[0].OpenCount) 1 'Second-world open count'
Assert-Equal ([bool]$second[0].IsNew) $true 'Second-world IsNew'

$assemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
Assert-Equal $assemblyVersion "1.0.0+$($evidence.TestedCommit)" 'Assembly ProductVersion'
if ((Get-Content -LiteralPath $pdb -AsByteStream -TotalCount 4 | ForEach-Object { $_.ToString('X2') }) -join '' -ne '42534A42') {
    throw 'Symbols do not contain the expected portable PDB header.'
}

$result = [ordered]@{
    TestId = 'L00-C-EVIDENCE-CHECK'
    Status = 'PASS'
    Utc = [DateTime]::UtcNow.ToString('o')
    TestedCommit = $evidence.TestedCommit
    SessionCount = @($evidence.Sessions).Count
    DistinctProcessCount = $processIds.Count
    PrimaryMarker = $primaryMarkers[0]
    SecondaryMarker = $second[0].MarkerId
    Assembly = $dll
    Symbols = $pdb
}

$result | ConvertTo-Json -Depth 5
