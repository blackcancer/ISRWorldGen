[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$helper = Join-Path $PSScriptRoot 'Invoke-L00CVisualStudioProfileConsumption.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('isr-l00c-vs-consumption-' + [Guid]::NewGuid().ToString('N'))
$vsPid = 47260
$mcpPid = 72116
$projectGuid = 'FC327668-ABD2-4C3E-9867-81D745F8F3E5'

function Assert-Refused([scriptblock]$Operation, [string]$Pattern, [string]$Label) {
    try { & $Operation }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw "$Label refused for the wrong reason: $($_.Exception.Message)" }
        return
    }
    throw "Expected refusal: $Label"
}

function Copy-Object([object]$Value) { return $Value | ConvertTo-Json -Depth 20 | ConvertFrom-Json }

function New-Fixture([string]$Name) {
    $fixtureProjectGuid = 'FC327668-ABD2-4C3E-9867-81D745F8F3E5'
    $repository = Join-Path $root $Name
    $projectDirectory = Join-Path $repository 'src\WorldGen.VintageStory'
    $properties = Join-Path $projectDirectory 'Properties'
    $laboratory = Join-Path $repository '.local\L00C'
    $transactionsRoot = Join-Path $laboratory 'f5-profile-transactions'
    $transactionId = [Guid]::NewGuid().ToString('N')
    $transaction = Join-Path $transactionsRoot ('f5-' + $transactionId)
    [void](New-Item -ItemType Directory -Path $properties,$transaction -Force)
    [IO.File]::WriteAllText((Join-Path $laboratory '.isrworldgen-lab'), 'fixture', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllBytes((Join-Path $laboratory 'f5-profile-transaction.lock'), [byte[]]@())
    $solution = Join-Path $repository 'ISRWorldGen.sln'
    $project = Join-Path $projectDirectory 'WorldGen.VintageStory.csproj'
    $launch = Join-Path $properties 'launchSettings.json'
    $user = Join-Path $projectDirectory 'WorldGen.VintageStory.csproj.user'
    $gameDirectory = Join-Path $repository 'Game'
    $game = Join-Path $gameDirectory 'Vintagestory.exe'
    [void](New-Item -ItemType Directory -Path $gameDirectory -Force)
    $solutionText = "Microsoft Visual Studio Solution File, Format Version 12.00`r`nProject(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"WorldGen.VintageStory`", `"src\WorldGen.VintageStory\WorldGen.VintageStory.csproj`", `"{$fixtureProjectGuid}`"`r`nEndProject`r`nGlobal`r`nEndGlobal`r`n"
    [IO.File]::WriteAllText($solution, $solutionText, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk" />', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($game, 'synthetic game', [Text.UTF8Encoding]::new($false))
    $nonce = 'A' * 64
    $environment = [ordered]@{
        ISR_L00C_LAB = '1'
        ISR_L00C_AUTOSHUTDOWN = '1'
        ISR_L00C_AUTOSHUTDOWN_DELAY_MS = '15000'
        ISR_L00C_LAB_ROOT = $laboratory
        ISR_L00C_F5_TRANSACTION_ID = $transactionId
        ISR_L00C_F5_LAUNCH_NONCE = $nonce
        ISR_L00C_F5_TRANSACTION_DIRECTORY = $transaction
        ISR_L00C_F5_VISUAL_STUDIO_PID = [string]$vsPid
        ISR_L00C_F5_SOLUTION_PATH = $solution
    }
    $arguments = @('--openWorld','ISRWorldGen-L00C-Client','--tracelog','--addModPath',(Join-Path $projectDirectory 'bin\Debug\Mods'),'--addOrigin',(Join-Path $projectDirectory 'assets'))
    $launchDocument = [ordered]@{ profiles = [ordered]@{ 'ISRWorldGen Client (authenticated user data)' = [ordered]@{
        commandName='Executable'; executablePath='$(VintageStoryPath)\Vintagestory.exe'; commandLineArgs='synthetic evaluated by fixture'; workingDirectory='$(VintageStoryPath)'; environmentVariables=$environment
    } } }
    [IO.File]::WriteAllText($launch, ($launchDocument | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($user, '<Project><PropertyGroup><ActiveDebugProfile>ISRWorldGen Client (authenticated user data)</ActiveDebugProfile></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $launch -Destination (Join-Path $transaction 'launchSettings.intended.bin')
    Copy-Item -LiteralPath $user -Destination (Join-Path $transaction 'project.user.intended.bin')
    $start = [DateTimeOffset]::UtcNow.AddMinutes(-5)
    $metadata = [ordered]@{
        SchemaVersion=2; Protocol='l00c-f5-debug-transaction-v2'; State='PREPARE_INTENT'; TransactionId=$transactionId; Nonce=$nonce
        TransactionDirectory=$transaction; LaboratoryRoot=$laboratory; SolutionPath=$solution; ProjectPath=$project; LaunchSettingsPath=$launch; ProjectUserSettingsPath=$user
        ProfileName='ISRWorldGen Client (authenticated user data)'; ExpectedGameExecutablePath=$game; ExpectedArguments=$arguments
        VisualStudio=[ordered]@{ ProcessId=$vsPid; StartTimeUtc=$start.ToString('o'); Name='devenv.exe'; ExecutablePath='C:\Program Files\Microsoft Visual Studio\devenv.exe' }
        IntendedSha256=(Get-FileHash -LiteralPath $launch -Algorithm SHA256).Hash
        ProjectUserIntendedSha256=(Get-FileHash -LiteralPath $user -Algorithm SHA256).Hash
        PreparedUtc=[DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')
    }
    $metadataPath = Join-Path $transaction 'metadata.json'
    [IO.File]::WriteAllText($metadataPath, ($metadata | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
    $prepared = [ordered]@{ SchemaVersion=2; Protocol='l00c-f5-debug-transaction-v2'; Status='SETTINGS_PREPARED_FOR_VS'; TransactionId=$transactionId; MetadataSha256=(Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash }
    [IO.File]::WriteAllText((Join-Path $transaction 'settings-prepared.json'), ($prepared | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $processes = @{}
    $processes[$vsPid] = [pscustomobject]@{ ProcessId=$vsPid; ParentProcessId=100; Name='devenv.exe'; ExecutablePath='C:\Program Files\Microsoft Visual Studio\devenv.exe'; StartTimeUtc=$start.ToString('o'); IsRunning=$true }
    $processes[$mcpPid] = [pscustomobject]@{ ProcessId=$mcpPid; ParentProcessId=$vsPid; Name='CodingWithCalvin.MCPServer.Server.exe'; ExecutablePath='C:\Program Files\Microsoft Visual Studio\MCPServer.exe'; StartTimeUtc=$start.AddSeconds(1).ToString('o'); IsRunning=$true }
    $processQuery = { param($id) return $processes[[int]$id] }.GetNewClosure()
    $baseSnapshot = [ordered]@{
        ProcessId=$vsPid; ProcessStartUtc=$start.ToString('o'); SolutionPath=$solution; ProjectPath=$project; StartupProjectPath=$project; ProjectGuid=$fixtureProjectGuid
        ActiveConfiguration='Debug'; ActivePlatform='Any CPU'; ActiveDebugProfile='ISRWorldGen Client (authenticated user data)'
        DebuggerMode='Design'; UnsavedDocumentCount=0; SolutionIsDirty=$false; ProjectIsDirty=$false; ProjectSaved=$true; ObservedUtc=[DateTimeOffset]::UtcNow.ToString('o')
    }
    $staleEnvironment = [ordered]@{ ISR_L00C_LAB='1'; ISR_L00C_LAB_ROOT=$laboratory }
    $stale = [pscustomobject](Copy-Object $baseSnapshot)
    $stale | Add-Member -NotePropertyName Targets -NotePropertyValue @([pscustomobject]@{ ExecutablePath=$game; Arguments=$arguments; WorkingDirectory=$gameDirectory; Environment=[pscustomobject]$staleEnvironment })
    $exact = [pscustomobject](Copy-Object $baseSnapshot)
    $exact | Add-Member -NotePropertyName Targets -NotePropertyValue @([pscustomobject]@{ ExecutablePath=$game; Arguments=$arguments; WorkingDirectory=$gameDirectory; Environment=[pscustomobject]$environment })
    $state = @{ Snapshot=$stale; Exact=$exact; ReloadCount=0 }
    $snapshotQuery = { $copy=$state.Snapshot | ConvertTo-Json -Depth 20 | ConvertFrom-Json; $copy.ObservedUtc=[DateTimeOffset]::UtcNow.ToString('o'); return $copy }.GetNewClosure()
    $reload = { param($guid) if ([string]$guid -cne $fixtureProjectGuid) { throw 'wrong synthetic reload GUID' }; $state.ReloadCount++; $state.Snapshot = $state.Exact; return 0 }.GetNewClosure()
    return [pscustomobject]@{ Root=$repository; Transaction=$transaction; Metadata=$metadata; Environment=$environment; Processes=$processes; ProcessQuery=$processQuery; State=$state; SnapshotQuery=$snapshotQuery; Reload=$reload; Launch=$launch; User=$user }
}

function Invoke-Helper([object]$Fixture, [string]$Action, [scriptblock]$Hook = $null) {
    return & $helper -Action $Action -BackupDirectory $Fixture.Transaction -VisualStudioMcpProcessId $mcpPid -SyntheticFixtureRoot $Fixture.Root -TestProcessQuery $Fixture.ProcessQuery -TestSnapshotQuery $Fixture.SnapshotQuery -TestReloadProject $Fixture.Reload -TestHook $Hook
}

try {
    [void](New-Item -ItemType Directory -Path $root)

    $inspect = New-Fixture 'inspect'
    $result = Invoke-Helper $inspect 'Inspect' | ConvertFrom-Json
    if ($result.Status -cne 'VISUAL_STUDIO_CONFIGURATION_STALE' -or @($result.Mismatches) -notcontains 'EvaluatedEnvironment' -or $inspect.State.ReloadCount -ne 0) { throw 'Read-only inspection did not expose the partial environment.' }
    if ((Test-Path (Join-Path $inspect.Transaction 'visual-studio-reload-intent.json')) -or (Test-Path (Join-Path $inspect.Transaction 'visual-studio-consumed.json'))) { throw 'Read-only inspection created a reload artifact.' }

    $good = New-Fixture 'good'
    $goodResult = Invoke-Helper $good 'ReloadAndAttest' | ConvertFrom-Json
    if ($goodResult.Status -cne 'VISUAL_STUDIO_PROFILE_CONSUMED' -or $good.State.ReloadCount -ne 1) { throw 'Bounded ReloadProject happy path failed.' }
    $receipt = Get-Content -LiteralPath $goodResult.VisualStudioAttestationPath -Raw | ConvertFrom-Json
    if (@($receipt.EvaluatedEnvironment.PSObject.Properties).Count -ne 9 -or @($receipt.EvaluatedArguments).Count -ne 7 -or $receipt.ProjectGuid -cne $projectGuid -or $receipt.AttestationMethod -cne 'ROT_DTE_IVS_QUERY_DEBUG_TARGETS' -or
        $receipt.DebuggerMode -cne 'Design' -or [int]$receipt.UnsavedDocumentCount -ne 0 -or [bool]$receipt.SolutionIsDirty -or [bool]$receipt.ProjectIsDirty -or -not [bool]$receipt.ProjectSaved) { throw 'Consumption receipt omitted exact evaluated or safety fields.' }

    foreach ($case in @('debugger-run','unsaved-document','dirty-solution','dirty-project','unsaved-project','wrong-guid','wrong-startup','wrong-mcp-parent')) {
        $fixture = New-Fixture $case
        if ($case -eq 'debugger-run') { $fixture.State.Snapshot.DebuggerMode='Run' }
        elseif ($case -eq 'unsaved-document') { $fixture.State.Snapshot.UnsavedDocumentCount=1 }
        elseif ($case -eq 'dirty-solution') { $fixture.State.Snapshot.SolutionIsDirty=$true }
        elseif ($case -eq 'dirty-project') { $fixture.State.Snapshot.ProjectIsDirty=$true }
        elseif ($case -eq 'unsaved-project') { $fixture.State.Snapshot.ProjectSaved=$false }
        elseif ($case -eq 'wrong-guid') { $fixture.State.Snapshot.ProjectGuid=[Guid]::NewGuid().ToString('D').ToUpperInvariant() }
        elseif ($case -eq 'wrong-startup') { $fixture.State.Snapshot.StartupProjectPath=Join-Path $fixture.Root 'Other.csproj' }
        else { $fixture.Processes[$mcpPid].ParentProcessId=99999 }
        Assert-Refused { Invoke-Helper $fixture 'ReloadAndAttest' } 'Design mode|process, solution|MCP process' $case
        if ($fixture.State.ReloadCount -ne 0 -or (Test-Path (Join-Path $fixture.Transaction 'visual-studio-reload-intent.json'))) { throw "$case crossed the reload boundary." }
    }

    $partial = New-Fixture 'partial-post-reload'
    $partialReload = { param($guid) $partial.State.ReloadCount++; return 0 }.GetNewClosure()
    $partial.Reload = $partialReload
    Assert-Refused { Invoke-Helper $partial 'ReloadAndAttest' } 'EvaluatedEnvironment' 'partial post-reload environment'
    if ($partial.State.ReloadCount -ne 1 -or -not (Test-Path (Join-Path $partial.Transaction 'visual-studio-reload-intent.json')) -or (Test-Path (Join-Path $partial.Transaction 'visual-studio-consumed.json'))) { throw 'Partial post-reload state was not fail-closed.' }
    Assert-Refused { Invoke-Helper $partial 'ResumeAfterInterruption' } 'EvaluatedEnvironment' 'partial resume'
    if ($partial.State.ReloadCount -ne 1) { throw 'Read-only resume repeated ReloadProject.' }

    $afterReload = New-Fixture 'interrupt-after-reload'
    $cutAfterReload = { param($point) if ($point -eq 'AfterReloadBeforeObservation') { throw 'synthetic post-reload interruption' } }
    Assert-Refused { Invoke-Helper $afterReload 'ReloadAndAttest' $cutAfterReload } 'synthetic post-reload interruption' 'post-reload interruption'
    if ($afterReload.State.ReloadCount -ne 1 -or (Test-Path (Join-Path $afterReload.Transaction 'visual-studio-consumed.json'))) { throw 'Post-reload interruption crossed the receipt boundary.' }
    $resumed = Invoke-Helper $afterReload 'ResumeAfterInterruption' | ConvertFrom-Json
    if ($resumed.Status -cne 'VISUAL_STUDIO_PROFILE_CONSUMED' -or $afterReload.State.ReloadCount -ne 1) { throw 'Read-only post-interruption recovery did not attest the consumed target.' }

    $beforeReload = New-Fixture 'interrupt-before-reload'
    $cutBeforeReload = { param($point) if ($point -eq 'AfterReloadIntent') { throw 'synthetic pre-reload interruption' } }
    Assert-Refused { Invoke-Helper $beforeReload 'ReloadAndAttest' $cutBeforeReload } 'synthetic pre-reload interruption' 'pre-reload interruption'
    Assert-Refused { Invoke-Helper $beforeReload 'ResumeAfterInterruption' } 'EvaluatedEnvironment' 'pre-reload resume'
    if ($beforeReload.State.ReloadCount -ne 0 -or (Test-Path (Join-Path $beforeReload.Transaction 'visual-studio-consumed.json'))) { throw 'Pre-reload interruption was not recover-only.' }

    $intentPublication = New-Fixture 'interrupt-intent-publication'
    $cutIntentPublication = { param($point) if ($point -eq 'PublishAfterFlush:visual-studio-reload-intent.json') { throw 'synthetic intent publication interruption' } }
    Assert-Refused { Invoke-Helper $intentPublication 'ReloadAndAttest' $cutIntentPublication } 'synthetic intent publication interruption' 'reload-intent publication interruption'
    if ($intentPublication.State.ReloadCount -ne 0 -or (Test-Path (Join-Path $intentPublication.Transaction 'visual-studio-reload-intent.json')) -or -not (Test-Path (Join-Path $intentPublication.Transaction 'visual-studio-reload-intent.json.publishing'))) { throw 'Reload-intent publication cutoff crossed the reload boundary.' }

    $drift = New-Fixture 'settings-drift-after-intent'
    $driftHook = { param($point) if ($point -eq 'AfterReloadIntent') { [IO.File]::AppendAllText($drift.User, 'drift') } }.GetNewClosure()
    Assert-Refused { Invoke-Helper $drift 'ReloadAndAttest' $driftHook } 'Settings changed after reload intent' 'settings drift after intent'
    if ($drift.State.ReloadCount -ne 0) { throw 'Settings drift crossed the reload boundary.' }

    [ordered]@{
        TestId='L00-C-VISUAL-STUDIO-PROFILE-CONSUMPTION'
        Status='PASS'
        Cases=16
        StateModel='READ_ONLY_INSPECT -> VISUAL_STUDIO_RELOAD_INTENT -> one exact IVsSolution4.ReloadProject -> exact ROT/DTE/IVsQueryDebuggableProjectCfg2 observation -> VISUAL_STUDIO_PROFILE_CONSUMED'
        Recovery='after reload, ResumeAfterInterruption only re-observes; before reload or stale post-state, stop bound VS then use existing transaction Recover; no automatic reload replay'
        Scope='Synthetic temporary fixtures and callbacks only; no Visual Studio, F5, AppData, credentials, or game process used.'
    } | ConvertTo-Json -Compress
}
finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolved.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
