[CmdletBinding()]
param(
    [string]$GamePath = 'D:\Jeux\Vintagestory'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$driverPath = Join-Path $PSScriptRoot 'L00CMenuActionDriver.cs'
$hostPath = Join-Path $PSScriptRoot 'L00CMenuActionLaboratoryHost.cs'
$modelPath = Join-Path $root 'src\WorldGen.VintageStory\L00CScenarioModel.cs'
$libPath = Join-Path $GamePath 'VintagestoryLib.dll'
$cecilPath = Join-Path $GamePath 'Lib\Mono.Cecil.dll'

foreach ($path in @($driverPath, $hostPath, $modelPath, $libPath, $cecilPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "L00-C S2 audit input is absent: $path"
    }
}

$source = [IO.File]::ReadAllText($driverPath) + [IO.File]::ReadAllText($hostPath) + [IO.File]::ReadAllText($modelPath)
foreach ($forbidden in @(
    'OnClickCellLeft', 'RebindCurrentCell', 'GuiScreenSingleplayer.entries', 'Task.Run',
    'MapSizeY',
    'clientPlayingFired', 'Spawned', 'AssetsReceived', 'BlocksReceivedAndLoaded',
    'DoneColorMaps', 'DoneBlockAndItemShapeLoading'
)) {
    if ($source.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0) {
        throw "L00-C S2 source retained forbidden menu/graphical binding: $forbidden"
    }
}
foreach ($required in @('ConnectToSingleplayer', 'DestroyGameSession', 'SoftExit', 'IsServerRunning',
        'L00C_S2_RUN_COMPLETED_NOT_T00_06_PASS', 'SaveCommitted')) {
    if ($source.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "L00-C S2 source lost required native/diagnostic contract: $required"
    }
}

Add-Type -Path $cecilPath
$module = [Mono.Cecil.ModuleDefinition]::ReadModule($libPath)
try {
    if ($module.Assembly.Name.Version.ToString() -ne '1.22.7.0') {
        throw "L00-C S2 audit requires VintagestoryLib 1.22.7.0, found $($module.Assembly.Name.Version)."
    }

    function Get-ExactType([string]$name) {
        $type = @($module.Types | Where-Object FullName -eq $name)
        if ($type.Count -ne 1) { throw "Expected one installed type $name, found $($type.Count)." }
        return $type[0]
    }

    function Get-ExactMethod([object]$type, [string]$name, [int]$parameterCount, [int]$token) {
        $method = @($type.Methods | Where-Object { $_.Name -eq $name -and $_.Parameters.Count -eq $parameterCount })
        if ($method.Count -ne 1) { throw "Expected one installed method $($type.FullName)::$name/$parameterCount, found $($method.Count)." }
        if ($method[0].MetadataToken.ToInt32() -ne $token -or -not $method[0].HasBody) {
            throw "Installed method token/body drifted for $($type.FullName)::$name."
        }
        return $method[0]
    }

    function Find-OperandIndex([object]$method, [string]$operandFullName, [string]$opcode = '', [int]$startIndex = 0) {
        for ($index = $startIndex; $index -lt $method.Body.Instructions.Count; $index++) {
            $instruction = $method.Body.Instructions[$index]
            if (-not ($instruction.Operand -is [Mono.Cecil.MemberReference]) -or
                $instruction.Operand.FullName -ne $operandFullName) { continue }
            if ($opcode.Length -eq 0 -or $instruction.OpCode.Code.ToString() -eq $opcode) { return $index }
        }
        return -1
    }

    $singleplayer = Get-ExactType 'Vintagestory.Client.GuiScreenSingleplayer'
    $onClick = Get-ExactMethod $singleplayer 'OnClickCellLeft' 1 0x06001fe9
    foreach ($field in @('SaveFileLocation', 'DisabledMods', 'Language', 'ClientModPaths')) {
        $match = @($onClick.Body.Instructions | Where-Object {
            $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Stfld -and
            $null -ne $_.Operand -and $_.Operand.FullName.EndsWith("Vintagestory.Common.StartServerArgs::$field", [StringComparison]::Ordinal)
        })
        if ($match.Count -ne 1) { throw "Native reopen mapping for StartServerArgs.$field drifted." }
    }
    if ((Find-OperandIndex $onClick 'System.Void Vintagestory.Client.ScreenManager::ConnectToSingleplayer(Vintagestory.Common.StartServerArgs)' 'Callvirt') -lt 0) {
        throw 'Native reopen no longer calls ScreenManager.ConnectToSingleplayer(StartServerArgs).'
    }
    if (@($onClick.Body.Instructions | Where-Object {
            $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Stfld -and
            $null -ne $_.Operand -and $_.Operand.FullName.EndsWith('Vintagestory.Common.StartServerArgs::IsNew', [StringComparison]::Ordinal)
        }).Count -ne 0) {
        throw 'Native reopen unexpectedly mutates StartServerArgs.IsNew.'
    }

    $screenManager = Get-ExactType 'Vintagestory.Client.ScreenManager'
    $startMainMenu = Get-ExactMethod $screenManager 'StartMainMenu' 0 0x06002133
    if ((Find-OperandIndex $startMainMenu 'GuiScreen Vintagestory.Client.ScreenManager::CurrentScreen' 'Stfld') -lt 0 -or
        (Find-OperandIndex $startMainMenu 'GuiScreenMainRight Vintagestory.Client.ScreenManager::mainScreen' 'Ldfld') -lt 0) {
        throw 'StartMainMenu no longer installs GuiScreenMainRight as CurrentScreen.'
    }

    $clientProgram = Get-ExactType 'Vintagestory.Client.ClientProgram'
    $serverThread = Get-ExactMethod $clientProgram 'ServerThreadStart' 0 0x06001eaf
    $stopIndex = Find-OperandIndex $serverThread 'System.Void Vintagestory.Server.ServerMain::Stop(System.String,EnumExitMode,System.String,Vintagestory.API.Common.EnumLogType)' 'Callvirt'
    $stoppedIndex = Find-OperandIndex $serverThread 'System.Void Vintagestory.Client.NoObf.ClientPlatformAbstract::set_IsServerRunning(System.Boolean)' 'Callvirt' ($stopIndex + 1)
    $disposeIndex = Find-OperandIndex $serverThread 'System.Void Vintagestory.Server.ServerMain::Dispose()' 'Callvirt' ($stoppedIndex + 1)
    if ($stopIndex -lt 0 -or $stoppedIndex -le $stopIndex -or $disposeIndex -le $stoppedIndex) {
        throw 'ServerThreadStart no longer orders ServerMain.Stop -> IsServerRunning=false -> Dispose.'
    }

    $serverMain = Get-ExactType 'Vintagestory.Server.ServerMain'
    $stop = Get-ExactMethod $serverMain 'Stop' 4 0x060010e8
    $shutdownPhase = Find-OperandIndex $stop 'System.Void Vintagestory.Server.ServerMain::EnterRunPhase(Vintagestory.API.Server.EnumServerRunPhase)' 'Call'
    $processMain = Find-OperandIndex $stop 'System.Void Vintagestory.Server.ServerMain::ProcessMain()' 'Call'
    if ($shutdownPhase -lt 0 -or $processMain -le $shutdownPhase) {
        throw 'ServerMain.Stop no longer enters its shutdown phase and processes it synchronously.'
    }

    $enterRunPhase = Get-ExactMethod $serverMain 'EnterRunPhase' 1 0x060010df
    if ((Find-OperandIndex $enterRunPhase 'System.Void Vintagestory.Server.ServerSystem::OnBeginShutdown()' 'Callvirt') -lt 0) {
        throw 'Server shutdown phase no longer invokes ServerSystem.OnBeginShutdown.'
    }

    $saveSystem = Get-ExactType 'Vintagestory.Server.ServerSystemLoadAndSaveGame'
    $beginRun = Get-ExactMethod $saveSystem 'OnBeginRunGame' 0 0x0600144b
    $beginShutdown = Get-ExactMethod $saveSystem 'OnBeginShutdown' 0 0x0600144c
    $worldSaved = Get-ExactMethod $saveSystem 'OnWorldBeingSaved' 0 0x0600144e
    $saveWorld = Get-ExactMethod $saveSystem 'SaveGameWorld' 1 0x06001450
    if ((Find-OperandIndex $beginRun 'System.Void Vintagestory.Server.ServerEventManager::add_OnGameWorldBeingSaved(System.Action)' 'Callvirt') -lt 0 -or
        (Find-OperandIndex $beginRun 'System.Void Vintagestory.Server.ServerSystemLoadAndSaveGame::OnWorldBeingSaved()' 'Ldftn') -lt 0) {
        throw 'Load/save system no longer registers its world-save callback.'
    }
    if ((Find-OperandIndex $beginShutdown 'System.Void Vintagestory.Server.ServerEventManager::TriggerGameWorldBeingSaved()' 'Callvirt') -lt 0) {
        throw 'Load/save shutdown no longer triggers the synchronous world-save event.'
    }
    if ((Find-OperandIndex $worldSaved 'System.Void Vintagestory.Server.ServerSystemLoadAndSaveGame::SaveGameWorld(System.Boolean)' 'Call') -lt 0) {
        throw 'World-save callback no longer calls SaveGameWorld.'
    }
    if ((Find-OperandIndex $saveWorld 'System.Void Vintagestory.Common.GameDatabase::StoreSaveGame(SaveGame,Vintagestory.API.Datastructures.FastMemoryStream)' 'Callvirt') -lt 0) {
        throw 'SaveGameWorld no longer stores the save-game record in the database.'
    }

    $sha = (Get-FileHash -LiteralPath $libPath -Algorithm SHA256).Hash
    if ($sha -ne 'E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0') {
        throw "VintagestoryLib SHA-256 drifted: $sha"
    }

    [ordered]@{
        TestId = 'L00-C-S2-NATIVE-1.22.7-STATIC-AUDIT'
        Status = 'PASS'
        Reopen = 'canonical StartServerArgs mapped directly to ConnectToSingleplayer; IsNew remains false'
        SaveQuit = 'ServerMain.Stop -> shutdown save event -> SaveGameWorld -> StoreSaveGame -> IsServerRunning=false'
        ForbiddenBindings = 'menu cell/index, graphical flags and Task.Run absent'
        LibrarySha256 = $sha
    } | ConvertTo-Json -Depth 4
}
finally {
    $module.Dispose()
}
