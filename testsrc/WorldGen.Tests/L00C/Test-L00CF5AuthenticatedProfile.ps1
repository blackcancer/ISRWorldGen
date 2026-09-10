[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$helper = Join-Path $PSScriptRoot 'Set-L00CF5AuthenticatedProfile.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('isr-l00c-f5-transaction-' + [Guid]::NewGuid().ToString('N'))
$vsPid = 47260
$mcpPid = 72116
$childPid = 75040

function Bytes([string]$Path) { return [IO.File]::ReadAllBytes($Path) }
function Equal([byte[]]$A, [byte[]]$B) {
    if ($A.Length -ne $B.Length) { return $false }
    for ($index = 0; $index -lt $A.Length; $index++) { if ($A[$index] -ne $B[$index]) { return $false } }
    return $true
}
function Assert-Bytes([byte[]]$Expected, [string]$Path, [string]$Label) {
    if (-not (Equal $Expected (Bytes $Path))) { throw "$Label bytes changed." }
}
function Assert-Refused([scriptblock]$Operation, [string]$Pattern, [string]$Label) {
    try { & $Operation }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw "$Label refused for the wrong reason: $($_.Exception.Message)" }
        return
    }
    throw "Expected refusal: $Label"
}

function New-Fixture([string]$Name) {
    $repository = Join-Path $root $Name
    $properties = Join-Path $repository 'src\WorldGen.VintageStory\Properties'
    $project = Join-Path $repository 'src\WorldGen.VintageStory'
    $laboratory = Join-Path $repository '.local\L00C'
    $saves = Join-Path $repository 'AppData\VintagestoryData\Saves'
    $game = Join-Path $repository 'Game\Vintagestory.exe'
    [void](New-Item -ItemType Directory -Path $properties,$laboratory,$saves,(Split-Path $game -Parent) -Force)
    $projectFile=Join-Path $project 'WorldGen.VintageStory.csproj'
    $solutionText = "Microsoft Visual Studio Solution File, Format Version 12.00`r`nProject(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"WorldGen.VintageStory`", `"src\WorldGen.VintageStory\WorldGen.VintageStory.csproj`", `"{FC327668-ABD2-4C3E-9867-81D745F8F3E5}`"`r`nEndProject`r`nGlobal`r`nEndGlobal`r`n"
    [IO.File]::WriteAllText((Join-Path $repository 'ISRWorldGen.sln'), $solutionText, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($projectFile, '<Project Sdk="Microsoft.NET.Sdk" />', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $laboratory '.isrworldgen-lab'), 'fixture', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($game, 'synthetic executable identity', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(([IO.Path]::ChangeExtension($game, '.dll')), 'synthetic managed entry point identity', [Text.UTF8Encoding]::new($false))
    $launch = Join-Path $properties 'launchSettings.json'
    $profileJson = @'
{
  "profiles": {
    "ISRWorldGen Client (authenticated user data)": {
      "commandName": "Executable",
      "executablePath": "$(VintageStoryPath)\\Vintagestory.exe",
      "commandLineArgs": "--tracelog --addModPath \"$(ProjectDir)bin\\$(Configuration)\\Mods\" --addOrigin \"$(ProjectDir)assets\""
    },
    "Unchanged": { "commandName": "Executable", "executablePath": "C:\\Other.exe", "commandLineArgs": "--safe" }
  }
}
'@
    [IO.File]::WriteAllText($launch, $profileJson, [Text.UTF8Encoding]::new($true))
    $user = Join-Path $project 'WorldGen.VintageStory.csproj.user'
    [IO.File]::WriteAllText($user, "<?xml version=`"1.0`" encoding=`"utf-8`"?>`r`n<Project>`r`n  <PropertyGroup><ActiveDebugProfile>Unchanged</ActiveDebugProfile><Other>retain</Other></PropertyGroup>`r`n</Project>`r`n", [Text.UTF8Encoding]::new($true))
    $save = Join-Path $saves 'ISRWorldGen-L00C-Client.vcdbs'
    [IO.File]::WriteAllText($save, 'attested-bootstrap-save', [Text.UTF8Encoding]::new($false))
    $solution = Join-Path $repository 'ISRWorldGen.sln'
    $vsStart = [DateTimeOffset]::UtcNow.AddMinutes(-5)
    $processes = @{}
    $processes[$vsPid] = [pscustomobject]@{ ProcessId=$vsPid; ParentProcessId=100; Name='devenv.exe'; ExecutablePath='C:\Program Files\Microsoft Visual Studio\devenv.exe'; CommandLine='devenv.exe'; StartTimeUtc=$vsStart.ToString('o'); IsRunning=$true }
    $processes[$mcpPid] = [pscustomobject]@{ ProcessId=$mcpPid; ParentProcessId=$vsPid; Name='CodingWithCalvin.MCPServer.Server.exe'; ExecutablePath='C:\Program Files\Microsoft Visual Studio\MCPServer.exe'; CommandLine='MCPServer.exe'; StartTimeUtc=$vsStart.AddMinutes(1).ToString('o'); IsRunning=$true }
    $query = { param($id) return $processes[[int]$id] }.GetNewClosure()
    $listQuery = { return @($processes.Values) }.GetNewClosure()
    return [pscustomobject]@{ Root=$repository; Solution=$solution; Project=$projectFile; Launch=$launch; User=$user; Game=$game; Laboratory=$laboratory; Processes=$processes; Query=$query; ListQuery=$listQuery; VsStart=$vsStart }
}

function Prepare([object]$Fixture, [scriptblock]$Hook = $null) {
    return (& $helper -Action Prepare -SyntheticFixtureRoot $Fixture.Root -VisualStudioProcessId $vsPid -ExpectedSolutionPath $Fixture.Solution -ExpectedGameExecutablePath $Fixture.Game -TestProcessQuery $Fixture.Query -TestHook $Hook | ConvertFrom-Json)
}

function New-VisualStudioAttestation(
    [object]$Fixture,
    [object]$Prepared,
    [switch]$WrongSolution,
    [switch]$WrongProfile,
    [switch]$WrongExecutable,
    [switch]$WrongUserHash,
    [switch]$WrongStartup,
    [switch]$WrongConfiguration,
    [switch]$WrongArguments,
    [switch]$MissingEnvironment,
    [switch]$WrongWorkingDirectory,
    [switch]$WrongProjectGuid,
    [switch]$WrongMcpParent,
    [string]$SafetyMutation,
    [switch]$UnsafeIntent
) {
    $metadata = Get-Content -LiteralPath (Join-Path $Prepared.BackupDirectory 'metadata.json') -Raw | ConvertFrom-Json -DateKind String
    $path = Join-Path $Prepared.BackupDirectory 'visual-studio-consumed.json'
    $intentPath = Join-Path $Prepared.BackupDirectory 'visual-studio-reload-intent.json'
    $intended = Get-Content -LiteralPath (Join-Path $Prepared.BackupDirectory 'launchSettings.intended.bin') -Raw | ConvertFrom-Json
    $environment = [ordered]@{}
    foreach ($entry in @($intended.profiles.'ISRWorldGen Client (authenticated user data)'.environmentVariables.PSObject.Properties)) { $environment[$entry.Name] = [string]$entry.Value }
    if ($MissingEnvironment) { $environment.Remove('ISR_L00C_F5_TRANSACTION_ID') }
    $arguments = @($metadata.ExpectedArguments)
    if ($WrongArguments) { $arguments[-1] = $arguments[-1] + '-wrong' }
    $projectGuid = if ($WrongProjectGuid) { [Guid]::NewGuid().ToString('D').ToUpperInvariant() } else { 'FC327668-ABD2-4C3E-9867-81D745F8F3E5' }
    $intent = [ordered]@{
        SchemaVersion=2; Protocol='l00c-f5-debug-transaction-v2'; Status='VISUAL_STUDIO_RELOAD_INTENT'; TransactionId=[string]$metadata.TransactionId
        ProcessId=$vsPid; ProcessStartUtc=$Fixture.VsStart.ToString('o'); McpProcessId=$mcpPid; ProjectPath=$Fixture.Project; ProjectGuid=$projectGuid
        MetadataSha256=(Get-FileHash -LiteralPath (Join-Path $Prepared.BackupDirectory 'metadata.json') -Algorithm SHA256).Hash
        LaunchSettingsSha256=[string]$metadata.IntendedSha256; ProjectUserSettingsSha256=[string]$metadata.ProjectUserIntendedSha256
        DebuggerMode='Design'; UnsavedDocumentCount=0; SolutionIsDirty=[bool]$UnsafeIntent; ProjectIsDirty=$false; ProjectSaved=$true
        PreparedUtc=([DateTimeOffset]$metadata.PreparedUtc).AddMilliseconds(500).ToString('o')
    }
    [IO.File]::WriteAllText($intentPath, ($intent | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $receipt = [ordered]@{
        SchemaVersion=2; Protocol='l00c-f5-debug-transaction-v2'; Status='VISUAL_STUDIO_PROFILE_CONSUMED'; Source='VISUAL_STUDIO_DTE_MCP'; AttestationMethod='ROT_DTE_IVS_QUERY_DEBUG_TARGETS'
        TransactionId=[string]$metadata.TransactionId; Nonce=[string]$metadata.Nonce; ProcessId=$vsPid; ProcessStartUtc=$Fixture.VsStart.ToString('o')
        McpProcessId=$mcpPid; McpParentProcessId=if($WrongMcpParent){99999}else{$vsPid}
        SolutionPath=if($WrongSolution){Join-Path $Fixture.Root 'Other.sln'}else{$Fixture.Solution}
        ProjectPath=$Fixture.Project
        StartupProjectPath=if($WrongStartup){Join-Path $Fixture.Root 'Other.csproj'}else{$Fixture.Project}
        ProjectGuid=$projectGuid
        ActiveConfiguration=if($WrongConfiguration){'Release'}else{'Debug'}
        ActivePlatform='Any CPU'
        DebuggerMode='Design'; UnsavedDocumentCount=0; SolutionIsDirty=$false; ProjectIsDirty=$false; ProjectSaved=$true
        ActiveDebugProfile=if($WrongProfile){'Unchanged'}else{[string]$metadata.ProfileName}
        EvaluatedExecutablePath=if($WrongExecutable){Join-Path $Fixture.Root 'OtherGame\Vintagestory.exe'}else{$Fixture.Game}
        EvaluatedArguments=$arguments
        EvaluatedWorkingDirectory=if($WrongWorkingDirectory){Join-Path $Fixture.Root 'OtherGame'}else{Split-Path $Fixture.Game -Parent}
        EvaluatedEnvironment=[pscustomobject]$environment
        ConsumedLaunchSettingsSha256=[string]$metadata.IntendedSha256
        ConsumedProjectUserSettingsSha256=if($WrongUserHash){'0' * 64}else{[string]$metadata.ProjectUserIntendedSha256}
        ReloadIntentSha256=(Get-FileHash -LiteralPath $intentPath -Algorithm SHA256).Hash
        ObservedUtc=([DateTimeOffset]$metadata.PreparedUtc).AddSeconds(1).ToString('o')
    }
    switch -CaseSensitive ($SafetyMutation) {
        'WrongDebuggerMode' { $receipt.DebuggerMode='Run' }
        'UnsavedDocument' { $receipt.UnsavedDocumentCount=1 }
        'DirtySolution' { $receipt.SolutionIsDirty=$true }
        'DirtyProject' { $receipt.ProjectIsDirty=$true }
        'UnsavedProject' { $receipt.ProjectSaved=$false }
        'MissingField' { [void]$receipt.Remove('SolutionIsDirty') }
        '' { }
        default { throw "Unknown safety mutation: $SafetyMutation" }
    }
    $publishing = $path + '.publishing'
    [IO.File]::WriteAllText($publishing, ($receipt | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($publishing, $path)
    return $path
}

function Arm(
    [object]$Fixture,
    [object]$Prepared,
    [switch]$WrongSolution,
    [switch]$WrongProfile,
    [switch]$WrongExecutable,
    [switch]$WrongUserHash,
    [switch]$WrongStartup,
    [switch]$WrongConfiguration,
    [switch]$WrongArguments,
    [switch]$MissingEnvironment,
    [switch]$WrongWorkingDirectory,
    [switch]$WrongProjectGuid,
    [switch]$WrongMcpParent,
    [string]$SafetyMutation,
    [switch]$UnsafeIntent,
    [scriptblock]$Hook = $null
) {
    $path = New-VisualStudioAttestation $Fixture $Prepared -WrongSolution:$WrongSolution -WrongProfile:$WrongProfile -WrongExecutable:$WrongExecutable -WrongUserHash:$WrongUserHash -WrongStartup:$WrongStartup -WrongConfiguration:$WrongConfiguration -WrongArguments:$WrongArguments -MissingEnvironment:$MissingEnvironment -WrongWorkingDirectory:$WrongWorkingDirectory -WrongProjectGuid:$WrongProjectGuid -WrongMcpParent:$WrongMcpParent -SafetyMutation $SafetyMutation -UnsafeIntent:$UnsafeIntent
    return (& $helper -Action Arm -SyntheticFixtureRoot $Fixture.Root -BackupDirectory $Prepared.BackupDirectory -VisualStudioAttestationPath $path -TestProcessQuery $Fixture.Query -TestHook $Hook | ConvertFrom-Json)
}

function New-CommandLine([string]$Executable, [string[]]$Arguments, [switch]$QuoteEveryToken, [switch]$ExtraWhitespace) {
    $tokens = @($Executable) + @($Arguments)
    $serialized = @($tokens | ForEach-Object {
        $value = [string]$_
        if ($QuoteEveryToken -or $value -match '[\s\"]') { '"' + $value.Replace('"', '\\"') + '"' } else { $value }
    })
    return ($serialized -join $(if ($ExtraWhitespace) { '  ' } else { ' ' }))
}

function New-ChildAcquisition(
    [object]$Fixture,
    [object]$Prepared,
    [int]$ParentProcessId = $vsPid,
    [switch]$WrongTransaction,
    [switch]$WrongArguments,
    [switch]$LexicallyEquivalentCommandLine,
    [switch]$ProcessArgumentMismatch,
    [switch]$ProcessExecutableMismatch,
    [switch]$ReceiptExecutableMismatch,
    [switch]$ProcessExtraArgument,
    [switch]$InProcessManagedEntrypoint,
    [switch]$InProcessArgumentMismatch,
    [switch]$InProcessExecutableMismatch,
    [switch]$InProcessExtraArgument
) {
    if (-not (Test-Path -LiteralPath (Join-Path $Prepared.BackupDirectory 'pre-launch-intent.json'))) { [void](Begin-Launch $Fixture $Prepared) }
    $metadata = Get-Content -LiteralPath (Join-Path $Prepared.BackupDirectory 'metadata.json') -Raw | ConvertFrom-Json -DateKind String
    $armed = Get-Content -LiteralPath (Join-Path $Prepared.BackupDirectory 'armed.json') -Raw | ConvertFrom-Json -DateKind String
    $started = ([DateTimeOffset]$armed.ArmedUtc).AddSeconds(1)
    $arguments = @($metadata.ExpectedArguments)
    if ($WrongArguments) { $arguments[-1] = $arguments[-1] + '-wrong' }
    $receiptExecutable = $Fixture.Game
    if ($ReceiptExecutableMismatch) {
        $receiptExecutable = Join-Path $Fixture.Root 'OtherGame\Vintagestory.exe'
        [void](New-Item -ItemType Directory -Path (Split-Path $receiptExecutable -Parent) -Force)
        [IO.File]::WriteAllText($receiptExecutable, 'other synthetic executable', [Text.UTF8Encoding]::new($false))
    }
    if ($InProcessManagedEntrypoint) {
        $receiptExecutable = Join-Path (Split-Path $Fixture.Game -Parent) (([IO.Path]::GetFileNameWithoutExtension($Fixture.Game)) + '.dll')
    }
    if ($InProcessExecutableMismatch) {
        $receiptExecutable = Join-Path $Fixture.Root 'SpoofedHost\Vintagestory.dll'
        [void](New-Item -ItemType Directory -Path (Split-Path $receiptExecutable -Parent) -Force)
        [IO.File]::WriteAllText($receiptExecutable, 'spoofed managed host', [Text.UTF8Encoding]::new($false))
    }
    $inProcessArguments = @($arguments)
    if ($InProcessArgumentMismatch) { $inProcessArguments[-1] = $inProcessArguments[-1] + '-wrong' }
    if ($InProcessExtraArgument) { $inProcessArguments += '--unexpected-inprocess' }
    $commandLine = New-CommandLine $receiptExecutable $inProcessArguments -QuoteEveryToken
    $processArguments = @($arguments)
    if ($ProcessArgumentMismatch) { $processArguments[-1] = $processArguments[-1] + '-wrong' }
    if ($ProcessExtraArgument) { $processArguments += '--unexpected' }
    $processExecutable = if ($ProcessExecutableMismatch) { Join-Path $Fixture.Root 'OtherProcessGame\Vintagestory.exe' } else { $Fixture.Game }
    if ($ProcessExecutableMismatch) {
        [void](New-Item -ItemType Directory -Path (Split-Path $processExecutable -Parent) -Force)
        [IO.File]::WriteAllText($processExecutable, 'other process executable', [Text.UTF8Encoding]::new($false))
    }
    $processCommandLine = New-CommandLine $processExecutable $processArguments -ExtraWhitespace:$LexicallyEquivalentCommandLine
    $Fixture.Processes[$childPid] = [pscustomobject]@{ ProcessId=$childPid; ParentProcessId=$ParentProcessId; Name='Vintagestory.exe'; ExecutablePath=$Fixture.Game; CommandLine=$processCommandLine; StartTimeUtc=$started.ToString('o'); IsRunning=$true }
    $transactionId = if ($WrongTransaction) { [Guid]::NewGuid().ToString('N') } else { [string]$metadata.TransactionId }
    $child = [ordered]@{
        schemaVersion=2; protocol='l00c-f5-debug-transaction-v2'; status='CHILD_ACQUIRED'; transactionId=$transactionId; nonce=[string]$metadata.Nonce
        transactionDirectory=$Prepared.BackupDirectory; laboratoryRoot=$Fixture.Laboratory; solutionPath=$Fixture.Solution; visualStudioProcessId=$vsPid
        processId=$childPid; processStartUtc=$started.ToString('o'); recordedUtc=$started.AddSeconds(1).ToString('o'); debuggerAttached=$true
        executablePath=$Fixture.Game; commandLine=$commandLine; arguments=$arguments
    }
    [IO.File]::WriteAllText((Join-Path $Prepared.BackupDirectory 'child-acquisition.json'), ($child | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}

function Acquire([object]$Fixture, [object]$Prepared, [scriptblock]$Hook = $null) {
    if (-not (Test-Path -LiteralPath (Join-Path $Prepared.BackupDirectory 'pre-launch-intent.json'))) { [void](Begin-Launch $Fixture $Prepared) }
    return (& $helper -Action Acquire -SyntheticFixtureRoot $Fixture.Root -BackupDirectory $Prepared.BackupDirectory -TestProcessQuery $Fixture.Query -TestProcessListQuery $Fixture.ListQuery -TestHook $Hook | ConvertFrom-Json)
}
function Get-TestSeal([object]$Receipt) {
    $canonical=[ordered]@{}
    foreach($property in @($Receipt.PSObject.Properties)){if([string]$property.Name -cne 'SealSha256'){$canonical[[string]$property.Name]=$property.Value}}
    $algorithm=[Security.Cryptography.SHA256]::Create()
    try{return ([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes(($canonical|ConvertTo-Json -Depth 12 -Compress))))).Replace('-','')}
    finally{$algorithm.Dispose()}
}

function Begin-Launch([object]$Fixture, [object]$Prepared, [scriptblock]$Hook = $null) {
    return (& $helper -Action BeginLaunch -SyntheticFixtureRoot $Fixture.Root -BackupDirectory $Prepared.BackupDirectory -TestProcessQuery $Fixture.Query -TestProcessListQuery $Fixture.ListQuery -TestHook $Hook | ConvertFrom-Json)
}

function Block-Launch([object]$Fixture, [object]$Prepared, [scriptblock]$Hook = $null) {
    return (& $helper -Action BlockLaunch -SyntheticFixtureRoot $Fixture.Root -BackupDirectory $Prepared.BackupDirectory -TestProcessQuery $Fixture.Query -TestProcessListQuery $Fixture.ListQuery -TestHook $Hook | ConvertFrom-Json)
}

try {
    [void](New-Item -ItemType Directory -Path $root)

    # Happy path: intended profile remains installed until the in-process child
    # proves exact inherited transaction, argument vector, debugger and VS ancestry.
    $good = New-Fixture 'good'
    $originalLaunch = Bytes $good.Launch; $originalUser = Bytes $good.User
    $prepared = Prepare $good
    if ($prepared.Status -cne 'SETTINGS_PREPARED_FOR_VS') { throw 'Prepare did not stop before VS consumption.' }
    $intendedLaunch = Bytes $good.Launch; $intendedUser = Bytes $good.User
    if ((Equal $originalLaunch $intendedLaunch) -or (Equal $originalUser $intendedUser)) { throw 'Prepare did not install both intended settings.' }
    $profile = (Get-Content -LiteralPath $good.Launch -Raw | ConvertFrom-Json).profiles.'ISRWorldGen Client (authenticated user data)'
    foreach ($name in @('ISR_L00C_LAB','ISR_L00C_LAB_ROOT','ISR_L00C_F5_TRANSACTION_ID','ISR_L00C_F5_LAUNCH_NONCE','ISR_L00C_F5_TRANSACTION_DIRECTORY','ISR_L00C_F5_VISUAL_STUDIO_PID','ISR_L00C_F5_SOLUTION_PATH')) {
        if ($null -eq $profile.environmentVariables.PSObject.Properties[$name]) { throw "Prepared profile omitted $name." }
    }
    $armed = Arm $good $prepared
    if ($armed.Status -cne 'ARMED_FOR_F5') { throw 'Arm did not bind Visual Studio consumption.' }
    New-ChildAcquisition $good $armed
    $acquired = Acquire $good $armed
    if ($acquired.Status -cne 'LAUNCH_ACQUIRED' -or $acquired.ChildProcessId -ne $childPid) { throw 'Acquire did not bind the real child identity.' }
    & $helper -Action Restore -SyntheticFixtureRoot $good.Root -BackupDirectory $armed.BackupDirectory -TestProcessQuery $good.Query | Out-Null
    Assert-Bytes $originalLaunch $good.Launch 'Happy-path launchSettings'
    Assert-Bytes $originalUser $good.User 'Happy-path project user settings'
    [IO.File]::WriteAllText($good.User, "<?xml version=`"1.0`" encoding=`"utf-8`"?>`r`n<Project>`r`n  <PropertyGroup><ActiveDebugProfile>Unchanged</ActiveDebugProfile><Other>legitimate-later-edit</Other></PropertyGroup>`r`n</Project>`r`n", [Text.UTF8Encoding]::new($true))
    $legitimateLaterUser=Bytes $good.User
    $successor = Prepare $good
    $good.Processes[$vsPid].IsRunning = $false
    & $helper -Action Recover -SyntheticFixtureRoot $good.Root -BackupDirectory $successor.BackupDirectory -TestProcessQuery $good.Query | Out-Null
    Assert-Bytes $originalLaunch $good.Launch 'Successor recovery launchSettings'; Assert-Bytes $legitimateLaterUser $good.User 'Successor recovery user settings'

    # A debugger_launch refusal before action is itself a durable terminal
    # outcome.  It contains only closed, non-secret status/provenance fields and
    # restores both settings files while preserving NOT_RUN (never PASS).
    $blocked=New-Fixture 'pre-launch-blocked'; $blockedLaunch=Bytes $blocked.Launch; $blockedUser=Bytes $blocked.User; $blockedArm=Arm $blocked (Prepare $blocked)
    Assert-Refused { Block-Launch $blocked $blockedArm } 'PRE_LAUNCH_INTENT receipt is absent' 'blocked outcome without pre-launch intent'
    $launchIntent=Begin-Launch $blocked $blockedArm
    if($launchIntent.Status -cne 'PRE_LAUNCH_INTENT' -or $launchIntent.RuntimeStatus -cne 'NOT_RUN'){throw 'Pre-launch intent did not preserve NOT_RUN.'}
    $blockedResult=Block-Launch $blocked $blockedArm
    if($blockedResult.Status -cne 'BLOCKED' -or $blockedResult.RuntimeStatus -cne 'NOT_RUN' -or -not $blockedResult.NoChildProcess -or -not $blockedResult.NoCampaignStarted){throw 'Pre-launch refusal was not sealed as BLOCKED/NOT_RUN with non-start assertions.'}
    $blockedPath=Join-Path $blockedArm.BackupDirectory 'pre-launch-blocked.json'
    $blockedReceipt=Get-Content -LiteralPath $blockedPath -Raw | ConvertFrom-Json -DateKind String
    if([string]$blockedReceipt.RefusalCategory -cne 'MCP_TOOL_REFUSED_BEFORE_ACTION' -or [string]$blockedReceipt.Tool -cne 'mcp__visualstudio__debugger_launch'){throw 'Pre-launch refusal category/tool binding is not exact.'}
    if([string]$blockedReceipt.DebuggerStatusBefore -cne 'Design' -or [string]$blockedReceipt.DebuggerStatusAfter -cne 'NOT_RUN'){throw 'Sealed refusal did not distinguish attested pre-state from terminal debugger non-run status.'}
    if([string]$blockedReceipt.Status -ceq 'PASS' -or [string]$blockedReceipt.RuntimeStatus -ceq 'PASS'){throw 'NOT_RUN was transformed into PASS.'}
    foreach($forbidden in @('Arguments','CommandLine','Environment','Credential','Password','Secret','Nonce')){
        if($null -ne $blockedReceipt.PSObject.Properties[$forbidden]){throw "Sealed refusal leaked forbidden field $forbidden."}
    }
    Assert-Bytes $blockedLaunch $blocked.Launch 'Blocked launchSettings'; Assert-Bytes $blockedUser $blocked.User 'Blocked project user settings'
    Assert-Refused { Block-Launch $blocked $blockedArm } 'refuses every replay' 'blocked terminal replay'
    $blockedSuccessor=Prepare $blocked; $blocked.Processes[$vsPid].IsRunning=$false
    & $helper -Action Recover -SyntheticFixtureRoot $blocked.Root -BackupDirectory $blockedSuccessor.BackupDirectory -TestProcessQuery $blocked.Query | Out-Null

    $blockedCut=New-Fixture 'pre-launch-blocked-publication-cut'; $blockedCutLaunch=Bytes $blockedCut.Launch; $blockedCutUser=Bytes $blockedCut.User; $blockedCutArm=Arm $blockedCut (Prepare $blockedCut); [void](Begin-Launch $blockedCut $blockedCutArm)
    $cutBlocked={param($point) if($point -eq 'StageAfterFlush:pre-launch-blocked.json'){throw 'cut blocked publication'}}
    Assert-Refused { Block-Launch $blockedCut $blockedCutArm $cutBlocked } 'cut blocked publication' 'blocked terminal publication cutoff'
    Assert-Bytes (Bytes (Join-Path $blockedCutArm.BackupDirectory 'launchSettings.intended.bin')) $blockedCut.Launch 'Blocked-cut intended launchSettings'
    $blockedCut.Processes[$vsPid].IsRunning=$false
    Assert-Refused { & $helper -Action Recover -SyntheticFixtureRoot $blockedCut.Root -BackupDirectory $blockedCutArm.BackupDirectory -TestProcessQuery $blockedCut.Query } 'must complete BlockLaunch' 'Recover replacing staged blocked terminal'
    $blockedCutResult=Block-Launch $blockedCut $blockedCutArm
    if($blockedCutResult.Status -cne 'BLOCKED' -or $blockedCutResult.RuntimeStatus -cne 'NOT_RUN'){throw 'Blocked publication retry did not seal NOT_RUN.'}
    Assert-Bytes $blockedCutLaunch $blockedCut.Launch 'Blocked-cut launchSettings'; Assert-Bytes $blockedCutUser $blockedCut.User 'Blocked-cut project user settings'

    $beforeRestoreCut=New-Fixture 'pre-launch-before-restore-cut'; $beforeRestoreLaunch=Bytes $beforeRestoreCut.Launch; $beforeRestoreUser=Bytes $beforeRestoreCut.User; $beforeRestoreArm=Arm $beforeRestoreCut (Prepare $beforeRestoreCut); [void](Begin-Launch $beforeRestoreCut $beforeRestoreArm)
    $cutBeforeRestore={param($point) if($point -eq 'BlockLaunchBeforeRestore'){throw 'cut before refusal restore'}}
    Assert-Refused { Block-Launch $beforeRestoreCut $beforeRestoreArm $cutBeforeRestore } 'cut before refusal restore' 'blocked cutoff before restoration'
    [IO.File]::WriteAllBytes($beforeRestoreCut.User,(Bytes (Join-Path $beforeRestoreArm.BackupDirectory 'project.user.original.bin')))
    $beforeRestoreResult=Block-Launch $beforeRestoreCut $beforeRestoreArm
    if($beforeRestoreResult.Status -cne 'BLOCKED' -or $beforeRestoreResult.RuntimeStatus -cne 'NOT_RUN'){throw 'Split restoration retry did not preserve BLOCKED/NOT_RUN.'}
    Assert-Bytes $beforeRestoreLaunch $beforeRestoreCut.Launch 'Split-retry launchSettings'; Assert-Bytes $beforeRestoreUser $beforeRestoreCut.User 'Split-retry project user settings'

    $afterRestoreCut=New-Fixture 'pre-launch-after-restore-cut'; $afterRestoreLaunch=Bytes $afterRestoreCut.Launch; $afterRestoreUser=Bytes $afterRestoreCut.User; $afterRestoreArm=Arm $afterRestoreCut (Prepare $afterRestoreCut); [void](Begin-Launch $afterRestoreCut $afterRestoreArm)
    $cutAfterRestore={param($point) if($point -eq 'BlockLaunchAfterRestore'){throw 'cut after refusal restore'}}
    Assert-Refused { Block-Launch $afterRestoreCut $afterRestoreArm $cutAfterRestore } 'cut after refusal restore' 'blocked cutoff after restoration'
    Assert-Bytes $afterRestoreLaunch $afterRestoreCut.Launch 'After-restore-cut launchSettings'; Assert-Bytes $afterRestoreUser $afterRestoreCut.User 'After-restore-cut project user settings'
    $afterRestoreResult=Block-Launch $afterRestoreCut $afterRestoreArm
    if($afterRestoreResult.Status -cne 'BLOCKED' -or $afterRestoreResult.RuntimeStatus -cne 'NOT_RUN'){throw 'Original/original retry did not preserve BLOCKED/NOT_RUN.'}

    $observationCut=New-Fixture 'pre-launch-observation-publication-cut'; $observationCutLaunch=Bytes $observationCut.Launch; $observationCutUser=Bytes $observationCut.User; $observationCutArm=Arm $observationCut (Prepare $observationCut); [void](Begin-Launch $observationCut $observationCutArm)
    $cutObservation={param($point) if($point -eq 'PublishAfterFlush:pre-launch-refusal-observed.json'){throw 'cut refusal observation publication'}}
    Assert-Refused { Block-Launch $observationCut $observationCutArm $cutObservation } 'cut refusal observation publication' 'refusal observation publication cutoff'
    if((Get-FileHash $observationCut.Launch -Algorithm SHA256).Hash -cne (Get-FileHash (Join-Path $observationCutArm.BackupDirectory 'launchSettings.intended.bin') -Algorithm SHA256).Hash){throw 'Observation cutoff restored before durable refusal observation.'}
    $observationCutResult=Block-Launch $observationCut $observationCutArm
    if($observationCutResult.Status -cne 'BLOCKED' -or $observationCutResult.RuntimeStatus -cne 'NOT_RUN'){throw 'Observation publication retry did not seal NOT_RUN.'}
    Assert-Bytes $observationCutLaunch $observationCut.Launch 'Observation-cut launchSettings'; Assert-Bytes $observationCutUser $observationCut.User 'Observation-cut project user settings'

    $missing=New-Fixture 'pre-launch-terminal-missing'; $missingLaunch=Bytes $missing.Launch; $missingUser=Bytes $missing.User; $missingArm=Arm $missing (Prepare $missing); [void](Begin-Launch $missing $missingArm); [void](Block-Launch $missing $missingArm)
    [IO.File]::Delete((Join-Path $missingArm.BackupDirectory 'pre-launch-blocked.json'))
    Assert-Refused { Prepare $missing } 'Unresolved F5 transaction' 'missing blocked terminal artifact'
    Assert-Bytes $missingLaunch $missing.Launch 'Missing-terminal launchSettings'; Assert-Bytes $missingUser $missing.User 'Missing-terminal project user settings'

    $tampered=New-Fixture 'pre-launch-terminal-tampered'; $tamperedLaunch=Bytes $tampered.Launch; $tamperedUser=Bytes $tampered.User; $tamperedArm=Arm $tampered (Prepare $tampered); [void](Begin-Launch $tampered $tamperedArm); [void](Block-Launch $tampered $tamperedArm)
    $tamperedPath=Join-Path $tamperedArm.BackupDirectory 'pre-launch-blocked.json'; $tamperedOriginal=Get-Content -LiteralPath $tamperedPath -Raw
    foreach($mutation in @('process-status','boolean-coercion','blocked-time')){
        $tamperedReceipt=$tamperedOriginal|ConvertFrom-Json -DateKind String
        if($mutation -ceq 'process-status'){$tamperedReceipt.VisualStudioStatusAfter='STOPPED'}
        elseif($mutation -ceq 'boolean-coercion'){$tamperedReceipt.NoChildProcess='true'}
        else{$tamperedReceipt.BlockedUtc=[DateTimeOffset]::UtcNow.AddDays(1).ToString('o')}
        $tamperedReceipt.SealSha256=Get-TestSeal $tamperedReceipt
        [IO.File]::WriteAllText($tamperedPath,($tamperedReceipt|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
        Assert-Refused { Prepare $tampered } 'malformed, tampered, replayed, or does not prove NOT_RUN|time is outside' "tampered blocked terminal $mutation"
    }
    Assert-Bytes $tamperedLaunch $tampered.Launch 'Tampered-terminal launchSettings'; Assert-Bytes $tamperedUser $tampered.User 'Tampered-terminal project user settings'

    $externallyRestored=New-Fixture 'pre-launch-externally-restored'; $externallyRestoredArm=Arm $externallyRestored (Prepare $externallyRestored); [void](Begin-Launch $externallyRestored $externallyRestoredArm)
    [IO.File]::WriteAllBytes($externallyRestored.Launch,(Bytes (Join-Path $externallyRestoredArm.BackupDirectory 'launchSettings.original.bin')))
    [IO.File]::WriteAllBytes($externallyRestored.User,(Bytes (Join-Path $externallyRestoredArm.BackupDirectory 'project.user.original.bin')))
    Assert-Refused { Block-Launch $externallyRestored $externallyRestoredArm } 'without a durable refusal observation' 'externally restored settings without refusal observation'
    $externallyRestored.Processes[$vsPid].IsRunning=$false; & $helper -Action Recover -SyntheticFixtureRoot $externallyRestored.Root -BackupDirectory $externallyRestoredArm.BackupDirectory -TestProcessQuery $externallyRestored.Query | Out-Null

    $intentTampered=New-Fixture 'pre-launch-intent-tampered'; $intentTamperedArm=Arm $intentTampered (Prepare $intentTampered); [void](Begin-Launch $intentTampered $intentTamperedArm)
    $intentTamperedPath=Join-Path $intentTamperedArm.BackupDirectory 'pre-launch-intent.json'; $intentTamperedReceipt=Get-Content -LiteralPath $intentTamperedPath -Raw | ConvertFrom-Json -DateKind String; $intentTamperedReceipt.Tool='unsafe-tool'
    [IO.File]::WriteAllText($intentTamperedPath,($intentTamperedReceipt|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    Assert-Refused { Block-Launch $intentTampered $intentTamperedArm } 'malformed, unsafe, or detached' 'tampered pre-launch intent'
    $intentTampered.Processes[$vsPid].IsRunning=$false; & $helper -Action Recover -SyntheticFixtureRoot $intentTampered.Root -BackupDirectory $intentTamperedArm.BackupDirectory -TestProcessQuery $intentTampered.Query | Out-Null

    $started=New-Fixture 'pre-launch-child-started'; $startedArm=Arm $started (Prepare $started); [void](Begin-Launch $started $startedArm)
    $started.Processes[$childPid]=[pscustomobject]@{ProcessId=$childPid;ParentProcessId=$vsPid;Name='Vintagestory.exe';ExecutablePath=$started.Game;CommandLine='not serialized';StartTimeUtc=[DateTimeOffset]::UtcNow.ToString('o');IsRunning=$true}
    Assert-Refused { Block-Launch $started $startedArm } 'detected a launched Vintage Story descendant' 'false no-child assertion'
    $started.Processes[$childPid].IsRunning=$false; $started.Processes[$vsPid].IsRunning=$false
    & $helper -Action Recover -SyntheticFixtureRoot $started.Root -BackupDirectory $startedArm.BackupDirectory -TestProcessQuery $started.Query | Out-Null

    # No receipt named PREPARED is authoritative. Version 1 material is stale
    # even when it is placed under the current transaction parent.
    $stale = New-Fixture 'stale-prepared'
    $staleLaunch = Bytes $stale.Launch; $staleUser = Bytes $stale.User
    $staleId = [Guid]::NewGuid().ToString('N'); $staleDirectory = Join-Path $stale.Laboratory ('f5-profile-transactions\f5-' + $staleId)
    [void](New-Item -ItemType Directory -Path $staleDirectory -Force)
    foreach ($name in @('launchSettings.original.bin','launchSettings.intended.bin','project.user.original.bin','project.user.intended.bin')) { [IO.File]::WriteAllBytes((Join-Path $staleDirectory $name), [byte[]]@(0)) }
    [IO.File]::WriteAllText((Join-Path $staleDirectory 'metadata.json'), '{"Phase":"PREPARED"}', [Text.UTF8Encoding]::new($false))
    Assert-Refused { & $helper -Action Acquire -SyntheticFixtureRoot $stale.Root -BackupDirectory $staleDirectory -TestProcessQuery $stale.Query } 'Stale PREPARED' 'stale PREPARED receipt'
    Assert-Bytes $staleLaunch $stale.Launch 'Stale receipt launchSettings'
    Assert-Bytes $staleUser $stale.User 'Stale receipt project user settings'

    # Wrong solution and wrong PID are rejected before any configuration write.
    $wrongSolution = New-Fixture 'wrong-solution'; $wrongSolutionLaunch = Bytes $wrongSolution.Launch; $wrongSolutionUser = Bytes $wrongSolution.User
    $otherSolution = Join-Path $wrongSolution.Root 'Other.sln'; [IO.File]::WriteAllText($otherSolution, 'other', [Text.UTF8Encoding]::new($false))
    Assert-Refused { & $helper -Action Prepare -SyntheticFixtureRoot $wrongSolution.Root -VisualStudioProcessId $vsPid -ExpectedSolutionPath $otherSolution -ExpectedGameExecutablePath $wrongSolution.Game -TestProcessQuery $wrongSolution.Query } 'Expected solution' 'wrong solution'
    Assert-Bytes $wrongSolutionLaunch $wrongSolution.Launch 'Wrong-solution launchSettings'; Assert-Bytes $wrongSolutionUser $wrongSolution.User 'Wrong-solution user settings'
    $wrongPid = New-Fixture 'wrong-pid'; $wrongPid.Processes[$vsPid] = [pscustomobject]@{ ProcessId=$vsPid; ParentProcessId=100; Name='pwsh.exe'; ExecutablePath='C:\pwsh.exe'; CommandLine='pwsh'; StartTimeUtc=$wrongPid.VsStart.ToString('o'); IsRunning=$true }
    $wrongPidLaunch = Bytes $wrongPid.Launch; $wrongPidUser = Bytes $wrongPid.User
    Assert-Refused { Prepare $wrongPid } 'not the bound Visual Studio' 'wrong Visual Studio PID'
    Assert-Bytes $wrongPidLaunch $wrongPid.Launch 'Wrong-PID launchSettings'; Assert-Bytes $wrongPidUser $wrongPid.User 'Wrong-PID user settings'

    # The exact VS process is insufficient: DTE/MCP must attest the open
    # solution, consumed user profile/hash, and evaluated launch target.
    foreach ($case in @('vs-other-solution','vs-cached-profile','vs-cached-user-hash','vs-wrong-executable','vs-wrong-startup','vs-wrong-configuration','vs-wrong-arguments','vs-partial-environment','vs-wrong-working-directory','vs-wrong-project-guid','vs-wrong-mcp-parent','vs-wrong-debugger-mode','vs-unsaved-document','vs-dirty-solution','vs-dirty-project','vs-unsaved-project','vs-missing-safety-field','vs-unsafe-intent')) {
        $fixture = New-Fixture $case; $beforeLaunch=Bytes $fixture.Launch; $beforeUser=Bytes $fixture.User; $preparedForVs=Prepare $fixture
        if ($case -eq 'vs-other-solution') { Assert-Refused { Arm $fixture $preparedForVs -WrongSolution } 'another process, solution' $case }
        elseif ($case -eq 'vs-cached-profile') { Assert-Refused { Arm $fixture $preparedForVs -WrongProfile } 'stale, cached' $case }
        elseif ($case -eq 'vs-cached-user-hash') { Assert-Refused { Arm $fixture $preparedForVs -WrongUserHash } 'stale, cached' $case }
        elseif ($case -eq 'vs-wrong-executable') { Assert-Refused { Arm $fixture $preparedForVs -WrongExecutable } 'executable' $case }
        elseif ($case -eq 'vs-wrong-startup') { Assert-Refused { Arm $fixture $preparedForVs -WrongStartup } 'startup project' $case }
        elseif ($case -eq 'vs-wrong-configuration') { Assert-Refused { Arm $fixture $preparedForVs -WrongConfiguration } 'configuration' $case }
        elseif ($case -eq 'vs-wrong-arguments') { Assert-Refused { Arm $fixture $preparedForVs -WrongArguments } 'argument vector' $case }
        elseif ($case -eq 'vs-partial-environment') { Assert-Refused { Arm $fixture $preparedForVs -MissingEnvironment } 'exact launch environment' $case }
        elseif ($case -eq 'vs-wrong-working-directory') { Assert-Refused { Arm $fixture $preparedForVs -WrongWorkingDirectory } 'working directory' $case }
        elseif ($case -eq 'vs-wrong-project-guid') { Assert-Refused { Arm $fixture $preparedForVs -WrongProjectGuid } 'GUID' $case }
        elseif ($case -eq 'vs-wrong-mcp-parent') { Assert-Refused { Arm $fixture $preparedForVs -WrongMcpParent } 'live MCP child' $case }
        elseif ($case -eq 'vs-wrong-debugger-mode') { Assert-Refused { Arm $fixture $preparedForVs -SafetyMutation 'WrongDebuggerMode' } 'reload safety precondition' $case }
        elseif ($case -eq 'vs-unsaved-document') { Assert-Refused { Arm $fixture $preparedForVs -SafetyMutation 'UnsavedDocument' } 'reload safety precondition' $case }
        elseif ($case -eq 'vs-dirty-solution') { Assert-Refused { Arm $fixture $preparedForVs -SafetyMutation 'DirtySolution' } 'reload safety precondition' $case }
        elseif ($case -eq 'vs-dirty-project') { Assert-Refused { Arm $fixture $preparedForVs -SafetyMutation 'DirtyProject' } 'reload safety precondition' $case }
        elseif ($case -eq 'vs-unsaved-project') { Assert-Refused { Arm $fixture $preparedForVs -SafetyMutation 'UnsavedProject' } 'reload safety precondition' $case }
        elseif ($case -eq 'vs-missing-safety-field') { Assert-Refused { Arm $fixture $preparedForVs -SafetyMutation 'MissingField' } 'omitted a reload safety precondition' $case }
        else { Assert-Refused { Arm $fixture $preparedForVs -UnsafeIntent } 'safety precondition' $case }
        Assert-Bytes (Bytes (Join-Path $preparedForVs.BackupDirectory 'launchSettings.intended.bin')) $fixture.Launch "$case intended launchSettings"
        Assert-Bytes (Bytes (Join-Path $preparedForVs.BackupDirectory 'project.user.intended.bin')) $fixture.User "$case intended user settings"
        $fixture.Processes[$vsPid].IsRunning=$false
        & $helper -Action Recover -SyntheticFixtureRoot $fixture.Root -BackupDirectory $preparedForVs.BackupDirectory -TestProcessQuery $fixture.Query | Out-Null
        Assert-Bytes $beforeLaunch $fixture.Launch "$case recovered launchSettings"; Assert-Bytes $beforeUser $fixture.User "$case recovered user settings"
    }

    $wrongTemplate = New-Fixture 'wrong-profile-executable-template'; $wrongTemplateLaunch=Bytes $wrongTemplate.Launch; $wrongTemplateUser=Bytes $wrongTemplate.User
    $wrongTemplateDocument=Get-Content -LiteralPath $wrongTemplate.Launch -Raw | ConvertFrom-Json
    $wrongTemplateDocument.profiles.'ISRWorldGen Client (authenticated user data)'.executablePath='C:\Other\Vintagestory.exe'
    [IO.File]::WriteAllText($wrongTemplate.Launch,($wrongTemplateDocument|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false)); $wrongTemplateLaunch=Bytes $wrongTemplate.Launch
    Assert-Refused { Prepare $wrongTemplate } 'exact non-sensitive' 'profile executable template'
    Assert-Bytes $wrongTemplateLaunch $wrongTemplate.Launch 'Wrong executable template launchSettings'; Assert-Bytes $wrongTemplateUser $wrongTemplate.User 'Wrong executable template user settings'

    $attestationCut=New-Fixture 'cut-vs-attestation'; $attestationCutLaunch=Bytes $attestationCut.Launch; $attestationCutUser=Bytes $attestationCut.User; $attestationPrepared=Prepare $attestationCut
    $attestationPath=Join-Path $attestationPrepared.BackupDirectory 'visual-studio-consumed.json'
    $reloadIntentPath=Join-Path $attestationPrepared.BackupDirectory 'visual-studio-reload-intent.json'
    [IO.File]::WriteAllText(($attestationPath+'.publishing'),'{',[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(($reloadIntentPath+'.publishing'),'{',[Text.UTF8Encoding]::new($false))
    Assert-Refused { & $helper -Action Arm -SyntheticFixtureRoot $attestationCut.Root -BackupDirectory $attestationPrepared.BackupDirectory -VisualStudioAttestationPath $attestationPath -TestProcessQuery $attestationCut.Query } 'not authoritative' 'Visual Studio attestation publication cutoff'
    $attestationCut.Processes[$vsPid].IsRunning=$false
    & $helper -Action Recover -SyntheticFixtureRoot $attestationCut.Root -BackupDirectory $attestationPrepared.BackupDirectory -TestProcessQuery $attestationCut.Query | Out-Null
    if(Test-Path -LiteralPath ($attestationPath+'.publishing')){throw 'Recovery retained Visual Studio attestation publishing residue.'}
    if(Test-Path -LiteralPath ($reloadIntentPath+'.publishing')){throw 'Recovery retained Visual Studio reload-intent publishing residue.'}
    Assert-Bytes $attestationCutLaunch $attestationCut.Launch 'Attestation cutoff launchSettings'; Assert-Bytes $attestationCutUser $attestationCut.User 'Attestation cutoff user settings'

    # Early restore and recovery race are fail-closed while the bound VS process
    # can still launch. Once that exact process is stopped, recovery is allowed.
    $unacquired = New-Fixture 'child-not-acquired'; $unacquiredLaunch = Bytes $unacquired.Launch; $unacquiredUser = Bytes $unacquired.User
    $unacquiredPrepared = Prepare $unacquired; $unacquiredPrepared = Arm $unacquired $unacquiredPrepared; [void](Begin-Launch $unacquired $unacquiredPrepared)
    Assert-Refused { & $helper -Action Acquire -SyntheticFixtureRoot $unacquired.Root -BackupDirectory $unacquiredPrepared.BackupDirectory -TestProcessQuery $unacquired.Query } 'has not published' 'child not acquired'
    Assert-Refused { & $helper -Action Restore -SyntheticFixtureRoot $unacquired.Root -BackupDirectory $unacquiredPrepared.BackupDirectory -TestProcessQuery $unacquired.Query } 'forbidden before confirmed' 'early restore'
    Assert-Refused { & $helper -Action Recover -SyntheticFixtureRoot $unacquired.Root -BackupDirectory $unacquiredPrepared.BackupDirectory -TestProcessQuery $unacquired.Query } 'while its bound Visual Studio' 'recovery race with live VS'
    Assert-Bytes (Bytes (Join-Path $unacquiredPrepared.BackupDirectory 'launchSettings.intended.bin')) $unacquired.Launch 'Armed launchSettings after early refusal'
    Assert-Bytes (Bytes (Join-Path $unacquiredPrepared.BackupDirectory 'project.user.intended.bin')) $unacquired.User 'Armed user settings after early refusal'
    $unacquired.Processes[$vsPid].IsRunning = $false
    & $helper -Action Recover -SyntheticFixtureRoot $unacquired.Root -BackupDirectory $unacquiredPrepared.BackupDirectory -TestProcessQuery $unacquired.Query | Out-Null
    Assert-Bytes $unacquiredLaunch $unacquired.Launch 'Recovered unacquired launchSettings'; Assert-Bytes $unacquiredUser $unacquired.User 'Recovered unacquired user settings'
    [IO.File]::WriteAllText((Join-Path $unacquiredPrepared.BackupDirectory 'acquired.json'),'{}',[Text.UTF8Encoding]::new($false))
    $unacquired.Processes[$vsPid].IsRunning=$true
    Assert-Refused { Prepare $unacquired } 'Recovered terminal cannot coexist' 'terminal exclusivity validation'
    Assert-Bytes $unacquiredLaunch $unacquired.Launch 'Terminal exclusivity launchSettings'; Assert-Bytes $unacquiredUser $unacquired.User 'Terminal exclusivity user settings'

    # Wrong parent and wrong arguments cannot unlock restoration.
    foreach ($case in @('wrong-parent','wrong-arguments','wrong-transaction')) {
        $fixture = New-Fixture $case; $beforeLaunch = Bytes $fixture.Launch; $beforeUser = Bytes $fixture.User; $arm = Arm $fixture (Prepare $fixture)
        if ($case -eq 'wrong-parent') { New-ChildAcquisition $fixture $arm -ParentProcessId 99999 }
        elseif ($case -eq 'wrong-arguments') { New-ChildAcquisition $fixture $arm -WrongArguments }
        else { New-ChildAcquisition $fixture $arm -WrongTransaction }
        $pattern = if ($case -eq 'wrong-parent') { 'not a child' } elseif ($case -eq 'wrong-arguments') { 'argument vector' } else { 'did not inherit' }
        Assert-Refused { Acquire $fixture $arm } $pattern $case
        Assert-Bytes (Bytes (Join-Path $arm.BackupDirectory 'launchSettings.intended.bin')) $fixture.Launch "$case launchSettings"
        Assert-Bytes (Bytes (Join-Path $arm.BackupDirectory 'project.user.intended.bin')) $fixture.User "$case user settings"
        $fixture.Processes[$vsPid].IsRunning = $false
        & $helper -Action Recover -SyntheticFixtureRoot $fixture.Root -BackupDirectory $arm.BackupDirectory -TestProcessQuery $fixture.Query | Out-Null
        Assert-Bytes $beforeLaunch $fixture.Launch "$case recovered launchSettings"; Assert-Bytes $beforeUser $fixture.User "$case recovered user settings"
    }

    # Command-line provenance has two source-specific shapes. The raw Win32
    # process vector must begin with the executable. The real Vintage Story
    # managed host reports Vintagestory.dll as Environment.CommandLine argv[0],
    # while ProcessPath and the raw process command line remain Vintagestory.exe.
    # Both tails must exactly match the armed arguments.
    $quoted = New-Fixture 'commandline-lexical-equivalent'; $quotedLaunch=Bytes $quoted.Launch; $quotedUser=Bytes $quoted.User; $quotedArm=Arm $quoted (Prepare $quoted)
    New-ChildAcquisition $quoted $quotedArm -LexicallyEquivalentCommandLine -InProcessManagedEntrypoint
    [void](Acquire $quoted $quotedArm)
    & $helper -Action Restore -SyntheticFixtureRoot $quoted.Root -BackupDirectory $quotedArm.BackupDirectory -TestProcessQuery $quoted.Query | Out-Null
    Assert-Bytes $quotedLaunch $quoted.Launch 'Managed-entry-point command line launchSettings'; Assert-Bytes $quotedUser $quoted.User 'Managed-entry-point command line user settings'

    foreach ($case in @('commandline-process-argument-mismatch','commandline-process-executable-mismatch','commandline-receipt-executable-mismatch','commandline-extra-argument','commandline-inprocess-argument-mismatch','commandline-inprocess-executable-mismatch','commandline-inprocess-extra-argument')) {
        $fixture=New-Fixture $case; $beforeLaunch=Bytes $fixture.Launch; $beforeUser=Bytes $fixture.User; $arm=Arm $fixture (Prepare $fixture)
        if ($case -eq 'commandline-process-argument-mismatch') { New-ChildAcquisition $fixture $arm -ProcessArgumentMismatch }
        elseif ($case -eq 'commandline-process-executable-mismatch') { New-ChildAcquisition $fixture $arm -ProcessExecutableMismatch }
        elseif ($case -eq 'commandline-receipt-executable-mismatch') { New-ChildAcquisition $fixture $arm -ReceiptExecutableMismatch }
        elseif ($case -eq 'commandline-extra-argument') { New-ChildAcquisition $fixture $arm -ProcessExtraArgument }
        elseif ($case -eq 'commandline-inprocess-argument-mismatch') { New-ChildAcquisition $fixture $arm -InProcessManagedEntrypoint -InProcessArgumentMismatch }
        elseif ($case -eq 'commandline-inprocess-executable-mismatch') { New-ChildAcquisition $fixture $arm -InProcessExecutableMismatch }
        else { New-ChildAcquisition $fixture $arm -InProcessManagedEntrypoint -InProcessExtraArgument }
        $pattern = if ($case -match 'argument|extra') { 'argument vector' } elseif ($case -match 'receipt|inprocess') { 'host token' } else { 'executable' }
        Assert-Refused { Acquire $fixture $arm } $pattern $case
        Assert-Bytes (Bytes (Join-Path $arm.BackupDirectory 'launchSettings.intended.bin')) $fixture.Launch "$case launchSettings"
        Assert-Bytes (Bytes (Join-Path $arm.BackupDirectory 'project.user.intended.bin')) $fixture.User "$case user settings"
        $fixture.Processes[$vsPid].IsRunning=$false
        & $helper -Action Recover -SyntheticFixtureRoot $fixture.Root -BackupDirectory $arm.BackupDirectory -TestProcessQuery $fixture.Query | Out-Null
        Assert-Bytes $beforeLaunch $fixture.Launch "$case recovered launchSettings"; Assert-Bytes $beforeUser $fixture.User "$case recovered user settings"
    }

    # Exceptions preserve byte identity: failed prepare rolls both files back;
    # failed second restore write leaves the whole intended pair retryable.
    $prepareFailure = New-Fixture 'prepare-exception'; $prepareFailureLaunch = Bytes $prepareFailure.Launch; $prepareFailureUser = Bytes $prepareFailure.User
    $failPrepare = { param($point) if ($point -eq 'PrepareUserBeforeReplace') { throw 'synthetic prepare write failure' } }
    Assert-Refused { Prepare $prepareFailure $failPrepare } 'synthetic prepare write failure' 'prepare exception'
    Assert-Bytes $prepareFailureLaunch $prepareFailure.Launch 'Prepare exception launchSettings'; Assert-Bytes $prepareFailureUser $prepareFailure.User 'Prepare exception user settings'
    $prepareFailureDirectory=(Get-ChildItem -LiteralPath (Join-Path $prepareFailure.Laboratory 'f5-profile-transactions') -Directory | Select-Object -First 1).FullName
    $terminalPath=Join-Path $prepareFailureDirectory 'prepare-failed-recovered.json'; $terminalBytes=Bytes $terminalPath
    $prepareFailure.Processes[$vsPid].IsRunning=$false
    Assert-Refused { & $helper -Action Recover -SyntheticFixtureRoot $prepareFailure.Root -BackupDirectory $prepareFailureDirectory -TestProcessQuery $prepareFailure.Query } 'refuses every replay' 'terminal replay'
    Assert-Bytes $terminalBytes $terminalPath 'Terminal replay receipt'
    if (@(Get-ChildItem -LiteralPath $prepareFailureDirectory -Filter '*recovered.json').Count -ne 1) { throw 'Terminal replay created a second terminal receipt.' }

    $restoreFailure = New-Fixture 'restore-exception'; $restoreOriginalLaunch = Bytes $restoreFailure.Launch; $restoreOriginalUser = Bytes $restoreFailure.User
    $restoreArm = Arm $restoreFailure (Prepare $restoreFailure); New-ChildAcquisition $restoreFailure $restoreArm; [void](Acquire $restoreFailure $restoreArm)
    $restoreIntendedLaunch = Bytes $restoreFailure.Launch; $restoreIntendedUser = Bytes $restoreFailure.User
    $failRestore = { param($point) if ($point -eq 'RestoreLaunchAfterTruncate') { throw 'synthetic restore write failure' } }
    Assert-Refused { & $helper -Action Restore -SyntheticFixtureRoot $restoreFailure.Root -BackupDirectory $restoreArm.BackupDirectory -TestProcessQuery $restoreFailure.Query -TestHook $failRestore } 'synthetic restore write failure' 'restore exception'
    Assert-Bytes $restoreIntendedLaunch $restoreFailure.Launch 'Restore exception launchSettings'; Assert-Bytes $restoreIntendedUser $restoreFailure.User 'Restore exception user settings'
    & $helper -Action Restore -SyntheticFixtureRoot $restoreFailure.Root -BackupDirectory $restoreArm.BackupDirectory -TestProcessQuery $restoreFailure.Query | Out-Null
    Assert-Bytes $restoreOriginalLaunch $restoreFailure.Launch 'Restore retry launchSettings'; Assert-Bytes $restoreOriginalUser $restoreFailure.User 'Restore retry user settings'

    # Crash recovery accepts only an exact original/intended prefix. Unknown
    # bytes refuse without changing either file.
    $partial = New-Fixture 'partial-recovery'; $partialOriginalLaunch = Bytes $partial.Launch; $partialOriginalUser = Bytes $partial.User
    $partialArm = Arm $partial (Prepare $partial)
    [IO.File]::WriteAllBytes($partial.User, (Bytes (Join-Path $partialArm.BackupDirectory 'project.user.original.bin')))
    [IO.File]::WriteAllBytes((Join-Path $partialArm.BackupDirectory 'launch.swap.previous'), $partialOriginalLaunch)
    [IO.File]::WriteAllBytes((Join-Path $partialArm.BackupDirectory 'user.swap.new'), (Bytes (Join-Path $partialArm.BackupDirectory 'project.user.intended.bin')))
    $partial.Processes[$vsPid].IsRunning = $false
    & $helper -Action Recover -SyntheticFixtureRoot $partial.Root -BackupDirectory $partialArm.BackupDirectory -TestProcessQuery $partial.Query | Out-Null
    Assert-Bytes $partialOriginalLaunch $partial.Launch 'Partial recovery launchSettings'; Assert-Bytes $partialOriginalUser $partial.User 'Partial recovery user settings'
    if ((Test-Path -LiteralPath (Join-Path $partialArm.BackupDirectory 'launch.swap.previous')) -or (Test-Path -LiteralPath (Join-Path $partialArm.BackupDirectory 'user.swap.new'))) { throw 'Recovery retained an owned atomic-swap residue.' }

    $drift = New-Fixture 'unknown-drift'; $driftArm = Arm $drift (Prepare $drift)
    [IO.File]::WriteAllText($drift.Launch, 'unknown external bytes', [Text.UTF8Encoding]::new($false))
    $driftLaunch = Bytes $drift.Launch; $driftUser = Bytes $drift.User; $drift.Processes[$vsPid].IsRunning = $false
    Assert-Refused { & $helper -Action Recover -SyntheticFixtureRoot $drift.Root -BackupDirectory $driftArm.BackupDirectory -TestProcessQuery $drift.Query } 'neither byte-exact' 'unknown drift recovery'
    Assert-Bytes $driftLaunch $drift.Launch 'Unknown-drift launchSettings'; Assert-Bytes $driftUser $drift.User 'Unknown-drift user settings'

    $redirectedBackup=New-Fixture 'redirected-backup'; $redirectedPrepared=Prepare $redirectedBackup
    $redirectedPath=Join-Path $redirectedPrepared.BackupDirectory 'launchSettings.original.bin'; [IO.File]::Delete($redirectedPath); [void](New-Item -ItemType Directory -Path $redirectedPath)
    $redirectedLaunch=Bytes $redirectedBackup.Launch; $redirectedUser=Bytes $redirectedBackup.User
    Assert-Refused { & $helper -Action Arm -SyntheticFixtureRoot $redirectedBackup.Root -BackupDirectory $redirectedPrepared.BackupDirectory -VisualStudioAttestationPath (Join-Path $redirectedPrepared.BackupDirectory 'visual-studio-consumed.json') -TestProcessQuery $redirectedBackup.Query } 'incomplete|physical file' 'redirected backup artifact'
    Assert-Bytes $redirectedLaunch $redirectedBackup.Launch 'Redirected backup launchSettings'; Assert-Bytes $redirectedUser $redirectedBackup.User 'Redirected backup user settings'

    # Every authoritative JSON publication is temp+flush+rename. Simulated hard
    # stops leave only .publishing and are retryable/recoverable without byte loss.
    $envelopeCut = New-Fixture 'cut-envelope'; $envelopeLaunch=Bytes $envelopeCut.Launch; $envelopeUser=Bytes $envelopeCut.User
    $cutEnvelope={param($point) if($point -eq 'PublishAfterFlush:envelope.json'){throw 'cut envelope publication'}}
    Assert-Refused { Prepare $envelopeCut $cutEnvelope } 'cut envelope' 'envelope publication cutoff'
    $envelopeDirectory=(Get-ChildItem -LiteralPath (Join-Path $envelopeCut.Laboratory 'f5-profile-transactions') -Directory | Select-Object -First 1).FullName
    [IO.File]::WriteAllText((Join-Path $envelopeDirectory 'envelope.json.publishing'),'{',[Text.UTF8Encoding]::new($false))
    $envelopeCut.Processes[$vsPid].IsRunning=$false
    $envelopeRecovered=& $helper -Action Recover -SyntheticFixtureRoot $envelopeCut.Root -BackupDirectory $envelopeDirectory -TestProcessQuery $envelopeCut.Query | ConvertFrom-Json
    if($envelopeRecovered.Status -cne 'PRE_INTENT_RECOVERED'){throw 'Torn envelope was not safely abandoned.'}
    Assert-Bytes $envelopeLaunch $envelopeCut.Launch 'Envelope cutoff launchSettings'; Assert-Bytes $envelopeUser $envelopeCut.User 'Envelope cutoff user settings'

    $directoryCut=New-Fixture 'cut-directory'; $directoryCutLaunch=Bytes $directoryCut.Launch; $directoryCutUser=Bytes $directoryCut.User
    $directoryCutProcesses=$directoryCut.Processes
    $directoryCut.Query={param($id) if([int]$id -eq 0){throw 'Process 0 must never be queried'}; return $directoryCutProcesses[[int]$id]}.GetNewClosure()
    $emptyDirectory=Join-Path $directoryCut.Laboratory ('f5-profile-transactions\f5-'+[Guid]::NewGuid().ToString('N')); [void](New-Item -ItemType Directory -Path $emptyDirectory -Force)
    $cutEmptyRecovery={param($point) if($point -eq 'RecoverAbandonedEnvelopeAfterFlush'){throw 'cut empty-directory recovery'}}
    Assert-Refused { & $helper -Action Recover -SyntheticFixtureRoot $directoryCut.Root -BackupDirectory $emptyDirectory -TestProcessQuery $directoryCut.Query -TestHook $cutEmptyRecovery } 'cut empty-directory recovery' 'empty directory recovery cutoff'
    & $helper -Action Recover -SyntheticFixtureRoot $directoryCut.Root -BackupDirectory $emptyDirectory -TestProcessQuery $directoryCut.Query | Out-Null
    Assert-Bytes $directoryCutLaunch $directoryCut.Launch 'Directory cutoff launchSettings'; Assert-Bytes $directoryCutUser $directoryCut.User 'Directory cutoff user settings'

    $backupCut = New-Fixture 'cut-backup'; $backupLaunch=Bytes $backupCut.Launch; $backupUser=Bytes $backupCut.User
    $cutBackup={param($point) if($point -eq 'PublishAfterFlush:launchSettings.original.bin'){throw 'cut backup publication'}}
    Assert-Refused { Prepare $backupCut $cutBackup } 'cut backup' 'backup publication cutoff'
    $backupDirectory=(Get-ChildItem -LiteralPath (Join-Path $backupCut.Laboratory 'f5-profile-transactions') -Directory | Select-Object -First 1).FullName
    [IO.File]::WriteAllText((Join-Path $backupDirectory 'launchSettings.original.bin.publishing'),'partial',[Text.UTF8Encoding]::new($false))
    $backupCut.Processes[$vsPid].IsRunning=$false
    & $helper -Action Recover -SyntheticFixtureRoot $backupCut.Root -BackupDirectory $backupDirectory -TestProcessQuery $backupCut.Query | Out-Null
    Assert-Bytes $backupLaunch $backupCut.Launch 'Backup cutoff launchSettings'; Assert-Bytes $backupUser $backupCut.User 'Backup cutoff user settings'

    $metadataCut = New-Fixture 'cut-metadata'; $metadataLaunch=Bytes $metadataCut.Launch; $metadataUser=Bytes $metadataCut.User
    $cutMetadata={param($point) if($point -eq 'PublishAfterFlush:metadata.json'){throw 'cut metadata publication'}}
    Assert-Refused { Prepare $metadataCut $cutMetadata } 'cut metadata' 'metadata publication cutoff'
    $metadataDirectory=(Get-ChildItem -LiteralPath (Join-Path $metadataCut.Laboratory 'f5-profile-transactions') -Directory | Select-Object -First 1).FullName
    $metadataCut.Processes[$vsPid].IsRunning=$false
    & $helper -Action Recover -SyntheticFixtureRoot $metadataCut.Root -BackupDirectory $metadataDirectory -TestProcessQuery $metadataCut.Query | Out-Null
    Assert-Bytes $metadataLaunch $metadataCut.Launch 'Metadata cutoff launchSettings'; Assert-Bytes $metadataUser $metadataCut.User 'Metadata cutoff user settings'

    $preparedCut=New-Fixture 'cut-prepared'; $preparedCutLaunch=Bytes $preparedCut.Launch; $preparedCutUser=Bytes $preparedCut.User
    $cutPrepared={param($point) if($point -eq 'PublishAfterFlush:settings-prepared.json'){throw 'cut prepared receipt'}}
    Assert-Refused { Prepare $preparedCut $cutPrepared } 'cut prepared receipt' 'prepared receipt cutoff'
    Assert-Bytes $preparedCutLaunch $preparedCut.Launch 'Prepared receipt cutoff launchSettings'; Assert-Bytes $preparedCutUser $preparedCut.User 'Prepared receipt cutoff user settings'
    $preparedCutDirectory=(Get-ChildItem -LiteralPath (Join-Path $preparedCut.Laboratory 'f5-profile-transactions') -Directory | Select-Object -First 1).FullName
    if(-not (Test-Path -LiteralPath (Join-Path $preparedCutDirectory 'prepare-failed-recovered.json') -PathType Leaf)){throw 'Prepared receipt cutoff has no atomic failure terminal.'}

    $failedTerminalCut=New-Fixture 'cut-prepare-failure-terminal'; $failedTerminalLaunch=Bytes $failedTerminalCut.Launch; $failedTerminalUser=Bytes $failedTerminalCut.User
    $cutFailedTerminal={param($point) if($point -eq 'PrepareUserBeforeReplace'){throw 'force prepare failure'}; if($point -eq 'PublishAfterFlush:prepare-failed-recovered.json'){throw 'cut prepare failure terminal'}}
    Assert-Refused { Prepare $failedTerminalCut $cutFailedTerminal } 'in-action recovery could not' 'prepare failure terminal cutoff'
    $failedTerminalDirectory=(Get-ChildItem -LiteralPath (Join-Path $failedTerminalCut.Laboratory 'f5-profile-transactions') -Directory | Select-Object -First 1).FullName
    $failedTerminalCut.Processes[$vsPid].IsRunning=$false
    & $helper -Action Recover -SyntheticFixtureRoot $failedTerminalCut.Root -BackupDirectory $failedTerminalDirectory -TestProcessQuery $failedTerminalCut.Query | Out-Null
    Assert-Bytes $failedTerminalLaunch $failedTerminalCut.Launch 'Prepare failure terminal cutoff launchSettings'; Assert-Bytes $failedTerminalUser $failedTerminalCut.User 'Prepare failure terminal cutoff user settings'

    $armCut=New-Fixture 'cut-arm'; $armOriginalLaunch=Bytes $armCut.Launch; $armOriginalUser=Bytes $armCut.User; $armPrepared=Prepare $armCut
    $armAttestation=New-VisualStudioAttestation $armCut $armPrepared
    $cutArm={param($point) if($point -eq 'PublishAfterFlush:armed.json'){throw 'cut arm publication'}}
    Assert-Refused { & $helper -Action Arm -SyntheticFixtureRoot $armCut.Root -BackupDirectory $armPrepared.BackupDirectory -VisualStudioAttestationPath $armAttestation -TestProcessQuery $armCut.Query -TestHook $cutArm } 'cut arm' 'arm publication cutoff'
    $armRetry=& $helper -Action Arm -SyntheticFixtureRoot $armCut.Root -BackupDirectory $armPrepared.BackupDirectory -VisualStudioAttestationPath $armAttestation -TestProcessQuery $armCut.Query | ConvertFrom-Json
    if($armRetry.Status -cne 'ARMED_FOR_F5'){throw 'Arm publication retry failed.'}
    $armCut.Processes[$vsPid].IsRunning=$false; & $helper -Action Recover -SyntheticFixtureRoot $armCut.Root -BackupDirectory $armPrepared.BackupDirectory -TestProcessQuery $armCut.Query | Out-Null
    Assert-Bytes $armOriginalLaunch $armCut.Launch 'Arm cutoff launchSettings'; Assert-Bytes $armOriginalUser $armCut.User 'Arm cutoff user settings'

    $childCut=New-Fixture 'cut-child'; $childOriginalLaunch=Bytes $childCut.Launch; $childOriginalUser=Bytes $childCut.User; $childArm=Arm $childCut (Prepare $childCut)
    [IO.File]::WriteAllText((Join-Path $childArm.BackupDirectory 'child-acquisition.json.publishing'),'{',[Text.UTF8Encoding]::new($false))
    Assert-Refused { Acquire $childCut $childArm } 'has not published' 'child publication cutoff'
    Assert-Refused { & $helper -Action Restore -SyntheticFixtureRoot $childCut.Root -BackupDirectory $childArm.BackupDirectory -TestProcessQuery $childCut.Query } 'forbidden before confirmed' 'restore after child cutoff'
    $childCut.Processes[$vsPid].IsRunning=$false; & $helper -Action Recover -SyntheticFixtureRoot $childCut.Root -BackupDirectory $childArm.BackupDirectory -TestProcessQuery $childCut.Query | Out-Null
    Assert-Bytes $childOriginalLaunch $childCut.Launch 'Child cutoff launchSettings'; Assert-Bytes $childOriginalUser $childCut.User 'Child cutoff user settings'

    $acquireCut=New-Fixture 'cut-acquire'; $acquireOriginalLaunch=Bytes $acquireCut.Launch; $acquireOriginalUser=Bytes $acquireCut.User; $acquireArm=Arm $acquireCut (Prepare $acquireCut); New-ChildAcquisition $acquireCut $acquireArm
    $cutAcquire={param($point) if($point -eq 'PublishAfterFlush:acquired.json'){throw 'cut acquire publication'}}
    Assert-Refused { Acquire $acquireCut $acquireArm $cutAcquire } 'cut acquire' 'acquire publication cutoff'
    [void](Acquire $acquireCut $acquireArm)
    & $helper -Action Restore -SyntheticFixtureRoot $acquireCut.Root -BackupDirectory $acquireArm.BackupDirectory -TestProcessQuery $acquireCut.Query | Out-Null
    Assert-Bytes $acquireOriginalLaunch $acquireCut.Launch 'Acquire cutoff launchSettings'; Assert-Bytes $acquireOriginalUser $acquireCut.User 'Acquire cutoff user settings'

    $restoreCut=New-Fixture 'cut-restore'; $restoreCutLaunch=Bytes $restoreCut.Launch; $restoreCutUser=Bytes $restoreCut.User; $restoreCutArm=Arm $restoreCut (Prepare $restoreCut); New-ChildAcquisition $restoreCut $restoreCutArm; [void](Acquire $restoreCut $restoreCutArm)
    [IO.File]::WriteAllText((Join-Path $restoreCutArm.BackupDirectory 'child-acquisition.json.publishing'),'{',[Text.UTF8Encoding]::new($false))
    $cutRestoreReceipt={param($point) if($point -eq 'PublishAfterFlush:restored.json'){throw 'cut restore receipt'}}
    Assert-Refused { & $helper -Action Restore -SyntheticFixtureRoot $restoreCut.Root -BackupDirectory $restoreCutArm.BackupDirectory -TestProcessQuery $restoreCut.Query -TestHook $cutRestoreReceipt } 'cut restore receipt' 'restore receipt cutoff'
    if(Test-Path -LiteralPath (Join-Path $restoreCutArm.BackupDirectory 'child-acquisition.json.publishing')){throw 'Restore retained child replay publishing residue.'}
    & $helper -Action Restore -SyntheticFixtureRoot $restoreCut.Root -BackupDirectory $restoreCutArm.BackupDirectory -TestProcessQuery $restoreCut.Query | Out-Null
    Assert-Bytes $restoreCutLaunch $restoreCut.Launch 'Restore receipt cutoff launchSettings'; Assert-Bytes $restoreCutUser $restoreCut.User 'Restore receipt cutoff user settings'

    $recoverCut=New-Fixture 'cut-recover'; $recoverCutLaunch=Bytes $recoverCut.Launch; $recoverCutUser=Bytes $recoverCut.User; $recoverPrepared=Prepare $recoverCut; $recoverCut.Processes[$vsPid].IsRunning=$false
    $cutRecoverReceipt={param($point) if($point -eq 'PublishAfterFlush:recovered.json'){throw 'cut recover receipt'}}
    Assert-Refused { & $helper -Action Recover -SyntheticFixtureRoot $recoverCut.Root -BackupDirectory $recoverPrepared.BackupDirectory -TestProcessQuery $recoverCut.Query -TestHook $cutRecoverReceipt } 'cut recover receipt' 'recover receipt cutoff'
    & $helper -Action Recover -SyntheticFixtureRoot $recoverCut.Root -BackupDirectory $recoverPrepared.BackupDirectory -TestProcessQuery $recoverCut.Query | Out-Null
    Assert-Bytes $recoverCutLaunch $recoverCut.Launch 'Recover receipt cutoff launchSettings'; Assert-Bytes $recoverCutUser $recoverCut.User 'Recover receipt cutoff user settings'

    # The global exclusive action lock spans validation through publication;
    # injected Arm/Acquire interleavings cannot enter Recover at the boundary.
    $armRace=New-Fixture 'race-arm-recover'; $armRaceLaunch=Bytes $armRace.Launch; $armRaceUser=Bytes $armRace.User; $armRacePrepared=Prepare $armRace; $armRaceAttestation=New-VisualStudioAttestation $armRace $armRacePrepared
    $armRaceState=@{Blocked=$false}
    $armRaceHook={param($point) if($point -eq 'ArmBeforePublish'){ $armRace.Processes[$vsPid].IsRunning=$false; try { & $helper -Action Recover -SyntheticFixtureRoot $armRace.Root -BackupDirectory $armRacePrepared.BackupDirectory -TestProcessQuery $armRace.Query | Out-Null } catch { if($_.Exception.Message -match 'exclusive action lock'){$armRaceState.Blocked=$true}else{throw} } finally { $armRace.Processes[$vsPid].IsRunning=$true } }}.GetNewClosure()
    $armRaceResult=& $helper -Action Arm -SyntheticFixtureRoot $armRace.Root -BackupDirectory $armRacePrepared.BackupDirectory -VisualStudioAttestationPath $armRaceAttestation -TestProcessQuery $armRace.Query -TestHook $armRaceHook | ConvertFrom-Json
    if(-not $armRaceState.Blocked -or $armRaceResult.Status -cne 'ARMED_FOR_F5'){throw 'Arm/Recover interleaving was not serialized.'}
    $armRace.Processes[$vsPid].IsRunning=$false; & $helper -Action Recover -SyntheticFixtureRoot $armRace.Root -BackupDirectory $armRacePrepared.BackupDirectory -TestProcessQuery $armRace.Query | Out-Null
    Assert-Bytes $armRaceLaunch $armRace.Launch 'Arm race launchSettings'; Assert-Bytes $armRaceUser $armRace.User 'Arm race user settings'

    $acquireRace=New-Fixture 'race-acquire-recover'; $acquireRaceLaunch=Bytes $acquireRace.Launch; $acquireRaceUser=Bytes $acquireRace.User; $acquireRaceArm=Arm $acquireRace (Prepare $acquireRace); New-ChildAcquisition $acquireRace $acquireRaceArm
    $acquireRaceState=@{Blocked=$false}
    $acquireRaceHook={param($point) if($point -eq 'AcquireBeforePublish'){ $acquireRace.Processes[$vsPid].IsRunning=$false; try { & $helper -Action Recover -SyntheticFixtureRoot $acquireRace.Root -BackupDirectory $acquireRaceArm.BackupDirectory -TestProcessQuery $acquireRace.Query | Out-Null } catch { if($_.Exception.Message -match 'exclusive action lock'){$acquireRaceState.Blocked=$true}else{throw} } finally { $acquireRace.Processes[$vsPid].IsRunning=$true } }}.GetNewClosure()
    $acquireRaceResult=Acquire $acquireRace $acquireRaceArm $acquireRaceHook
    if(-not $acquireRaceState.Blocked -or $acquireRaceResult.Status -cne 'LAUNCH_ACQUIRED'){throw 'Acquire/Recover interleaving was not serialized.'}
    & $helper -Action Restore -SyntheticFixtureRoot $acquireRace.Root -BackupDirectory $acquireRaceArm.BackupDirectory -TestProcessQuery $acquireRace.Query | Out-Null
    Assert-Bytes $acquireRaceLaunch $acquireRace.Launch 'Acquire race launchSettings'; Assert-Bytes $acquireRaceUser $acquireRace.User 'Acquire race user settings'

    [ordered]@{
        TestId='L00-C-F5-DEBUG-TRANSACTION-V2'; Status='PASS'; RuntimeStatus='NOT_RUN'; Cases=68
        StateModel='DIRECTORY_RESERVED -> PREPARE_INTENT -> SETTINGS_PREPARED_FOR_VS -> VISUAL_STUDIO_PROFILE_CONSUMED -> ARMED_FOR_F5 -> PRE_LAUNCH_INTENT -> (PRE_LAUNCH_REFUSAL_OBSERVED -> BLOCKED/NOT_RUN | CHILD_ACQUIRED -> LAUNCH_ACQUIRED -> RESTORED_AFTER_ACQUISITION); recovery requires bound VS stopped'
        Proof='atomic durable receipts, content seals, non-secret debugger_launch refusal observation before restore, exact pre/post/restored hashes, VS/MCP/debugger status, no-child/no-campaign assertions, and absence/tamper/replay rejection; launch success still requires child nonce/arguments/debugger/ancestry/start proof before restore'
        Scope='Synthetic temporary fixtures and process records only; no Visual Studio, F5, AppData, credentials, or game process used.'
    } | ConvertTo-Json -Compress
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($root)
    $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedRoot.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedRoot)) { Remove-Item -LiteralPath $resolvedRoot -Recurse -Force }
}
