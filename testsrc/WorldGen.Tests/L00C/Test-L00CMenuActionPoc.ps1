[CmdletBinding()]
param([string]$GamePath = 'D:\Jeux\Vintagestory')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$driverPath = Join-Path $PSScriptRoot 'L00CMenuActionDriver.cs'
$hostPath = Join-Path $PSScriptRoot 'L00CMenuActionLaboratoryHost.cs'
$libPath = Join-Path $GamePath 'VintagestoryLib.dll'
$cscPath = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path -LiteralPath $cscPath -PathType Leaf)) { throw "L00-C menu POC compile gate cannot find csc.exe: $cscPath" }
$compileOutput = Join-Path ([IO.Path]::GetTempPath()) ("l00c-menu-action-poc-" + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    & $cscPath /nologo /target:library /define:DEBUG /langversion:latest "/out:$compileOutput" $driverPath $hostPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $compileOutput -PathType Leaf)) { throw 'L00-C menu POC driver/host compilation failed.' }
}
finally {
    if (Test-Path -LiteralPath $compileOutput) { Remove-Item -LiteralPath $compileOutput -Force }
}
if ((Get-FileHash -LiteralPath $libPath -Algorithm SHA256).Hash -ne 'E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0') { throw 'L00-C menu POC version lock refused VintagestoryLib.dll.' }
Add-Type -Path (Join-Path $GamePath 'Lib\Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule($libPath)
function Assert-Calls([string]$type, [string]$methodName, [string]$target) {
    $method = $module.GetType($type).Methods | Where-Object Name -eq $methodName | Select-Object -First 1
    if ($null -eq $method -or -not ($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -like "*$target*" })) { throw "Audited call graph drifted: $type::$methodName -> $target" }
}
Assert-Calls 'Vintagestory.Client.GuiCompositeMainMenuLeft' 'OnSingleplayer' 'ScreenManager::LoadAndCacheScreen'
Assert-Calls 'Vintagestory.Client.GuiScreenSingleplayer' 'OnClickCellLeft' 'ScreenManager::ConnectToSingleplayer'
Assert-Calls 'Vintagestory.Client.ScreenManager' 'ConnectToSingleplayer' 'ScreenManager::StartGame'
$clientMain = $module.GetType('Vintagestory.Client.NoObf.ClientMain')
$sendLeave = $clientMain.Methods | Where-Object { $_.Name -eq 'SendLeave' } | Select-Object -First 1
if ($null -eq $sendLeave -or $sendLeave.ReturnType.FullName -ne 'System.Void' -or $sendLeave.Parameters.Count -ne 1 -or $sendLeave.Parameters[0].ParameterType.FullName -ne 'System.Int32' -or $sendLeave.Parameters[0].Name -ne 'reason') {
    throw 'L00-C menu POC version lock refused: ClientMain.SendLeave(System.Int32 reason) drifted.'
}
if (-not ($sendLeave.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'Packet_Client Vintagestory.Client.ClientPackets::Leave(System.Int32)' })) {
    throw 'L00-C menu POC version lock refused: SendLeave no longer forwards its reason to ClientPackets.Leave(int).'
}
foreach ($required in @('internal static class L00CMenuActionDriver', 'internal sealed class L00CMenuActionReceipt', 'RequiredLibVersion = "1.22.7.0"', 'RequiredLibSha256 = "E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0"', 'Debugger.IsAttached', 'debugger origin is not inferred', 'ISR_L00C_LAB', 'internal const int SaveQuitLeaveReason = 0;', 'GuardTarget(clientMain, "Vintagestory.Client.NoObf.ClientMain", "SendLeave", new[] { typeof(int) });', 'MethodInfo destroy = GuardDestroyGameSession(clientMain);', 'GuardTarget(screenManager, "Vintagestory.Client.ScreenManager", "StartMainMenu", Type.EmptyTypes);', 'InvokeExact(clientMain, "SendLeave", new object?[] { SaveQuitLeaveReason });', 'IsInstanceOfType(target)', 'DestroyGameSession', 'SoftExit', 'StartMainMenu')) { if (-not (Select-String -LiteralPath $driverPath -SimpleMatch $required -Quiet)) { throw "Missing POC contract: $required" } }
foreach ($required in @('public sealed class L00CMenuActionLaboratoryHost', 'RequiredPrimaryCycles = 5', 'ISR_L00C_LAB', 'Debugger.IsAttached', 'RequireMarkedSave', 'File.Exists(savePath)', 'CanonicalLaboratoryRoot', 'ClientSaveCellIndex', 'ClientCellBindingConfirmed', 'L00CStrictJsonObject', 'RequireExactly', 'duplicate property', 'trailing data', 'strict integers', 'RequiredNonNegativeInt', 'FileMode.CreateNew', 'ExpectPrimaryMenu', 'PrimaryMenuOpen', 'PrimaryWorldOpen', 'ExpectSecondaryMenu', 'SecondaryMenuOpen', 'SecondaryWorldOpen', 'ReadyToComplete', 'Completed', 'five-primary-menu-open-return;secondary-menu-open-final-return', 'finalSecondaryReturnRequired', 'OpenSecondary', 'Complete')) { if (-not (Select-String -LiteralPath $hostPath -SimpleMatch $required -Quiet)) { throw "Missing laboratory host contract: $required" } }
if (Select-String -LiteralPath $hostPath -Pattern 'OpenPrimary\(object singleplayerScreen, int|OpenSecondary\(object singleplayerScreen, int' -Quiet) { throw 'Laboratory host must not accept an arbitrary menu-cell index.' }
if (-not (Select-String -LiteralPath $hostPath -SimpleMatch 'primary.CellIndex' -Quiet) -or -not (Select-String -LiteralPath $hostPath -SimpleMatch 'secondary.CellIndex' -Quiet)) { throw 'Laboratory host must invoke only marker-confirmed cells.' }
if (Select-String -LiteralPath @($driverPath, $hostPath) -Pattern 'SendKeys|mouse_event|keybd_event|WindowsInput|Start-Process|Process\.Start' -Quiet) { throw 'POC must not synthesize UI input or launch the game.' }
if (Select-String -LiteralPath $hostPath -Pattern 'using Vintagestory|Vintagestory\.' -Quiet) { throw 'Laboratory host must not take a stable Vintage Story assembly reference.' }
if (Select-String -LiteralPath $hostPath -Pattern 'Regex|System\.Text\.Json|Newtonsoft' -Quiet) { throw 'Laboratory host must use its strict local parser without an unavailable JSON dependency.' }
if (Select-String -LiteralPath $hostPath -SimpleMatch 'char.IsWhiteSpace' -Quiet) { throw 'Strict JSON parser must not accept non-JSON whitespace.' }

$behaviorAssembly = Join-Path ([IO.Path]::GetTempPath()) ("l00c-menu-action-behavior-" + [Guid]::NewGuid().ToString('N') + '.dll')
$behaviorRoot = Join-Path ([IO.Path]::GetTempPath()) ("l00c-menu-action-behavior-" + [Guid]::NewGuid().ToString('N'))
try {
    & $cscPath /nologo /target:library /define:DEBUG /langversion:latest "/out:$behaviorAssembly" $driverPath $hostPath
    if ($LASTEXITCODE -ne 0) { throw 'Behavior oracle compilation failed.' }
    $labRoot = Join-Path $behaviorRoot 'repository\.local\L00C'
    $saveDirectory = Join-Path $labRoot 'saves'
    [void](New-Item -ItemType Directory -Path $saveDirectory -Force)
    [IO.File]::WriteAllText((Join-Path $labRoot '.isrworldgen-lab'), 'disposable')
    $savePath = Join-Path $saveDirectory 'activated-primary.vcdbs'
    [IO.File]::WriteAllText($savePath, 'fixture')
    $markerPath = $savePath + '.l00c-lab.json'
    function New-ValidMarker([string]$Schema = 'l00c-lab-save-marker-v1', [string]$Save = $savePath, [string]$Root = $labRoot, [string]$Role = 'activated-primary', $Cell = 2, $Confirmed = $true, [switch]$WithoutCreated) {
        $marker = [ordered]@{ Schema = $Schema; WorldRole = $Role; SavePath = $Save; LaboratoryRoot = $Root; ClientSaveCellIndex = $Cell; ClientCellBindingConfirmed = $Confirmed }
        if (-not $WithoutCreated) { $marker.CreatedUtc = '2026-09-08T00:00:00.0000000+00:00' }
        return $marker | ConvertTo-Json -Compress
    }
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($behaviorAssembly))
    $hostType = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CMenuActionLaboratoryHost', $true)
    $require = $hostType.GetMethod('RequireMarkedSave', [Reflection.BindingFlags]'Static,NonPublic')
    function Invoke-MarkerGuard {
        try { return $require.Invoke($null, @([string]$labRoot, [string]$savePath, 'activated-primary')) }
        catch [Reflection.TargetInvocationException] { throw $_.Exception.InnerException }
    }
    function Assert-RejectedMarker([string]$Json, [string]$Label) {
        [IO.File]::WriteAllText($markerPath, $Json)
        try { [void](Invoke-MarkerGuard) } catch { return }
        throw "Behavior oracle accepted invalid marker: $Label"
    }
    [IO.File]::WriteAllText($markerPath, (New-ValidMarker))
    if ($null -eq (Invoke-MarkerGuard)) { throw 'Behavior oracle did not accept canonical marker.' }
    $valid = [string](New-ValidMarker)
    [IO.File]::WriteAllText($markerPath, (" `t`r`n" + $valid + "`n`r`t "))
    if ($null -eq (Invoke-MarkerGuard)) { throw 'Behavior oracle did not accept JSON SP/TAB/CR/LF whitespace.' }
    [IO.File]::WriteAllText($markerPath, '{broken marker')
    [IO.File]::Delete($savePath)
    $missingSaveRejected = $false
    try { [void](Invoke-MarkerGuard) } catch { if ($_.Exception.Message -notlike '*unmarked or out-of-laboratory*') { throw "Missing-save guard parsed marker before rejecting: $($_.Exception.Message)" }; $missingSaveRejected = $true }
    finally { [IO.File]::WriteAllText($savePath, 'fixture') }
    if (-not $missingSaveRejected) { throw 'Missing save was accepted.' }
    Assert-RejectedMarker ('{"Schema":"l00c-lab-save-marker-v1","Schema":"l00c-lab-save-marker-v1"}') 'duplicate key'
    Assert-RejectedMarker ($valid + 'x') 'trailing data'
    Assert-RejectedMarker ($valid.Substring(0, $valid.Length - 1) + ',"Extra":true}') 'extra property'
    Assert-RejectedMarker (New-ValidMarker -WithoutCreated) 'missing property'
    Assert-RejectedMarker (New-ValidMarker -Cell '2') 'wrong index type'
    Assert-RejectedMarker (New-ValidMarker -Schema 'other') 'wrong schema'
    Assert-RejectedMarker (New-ValidMarker -Cell 1.5) 'fractional index'
    Assert-RejectedMarker (New-ValidMarker -Cell '2e0') 'exponent index'
    Assert-RejectedMarker (New-ValidMarker -Cell -1) 'negative index'
    Assert-RejectedMarker (New-ValidMarker -Cell '2147483648') 'out of int index'
    Assert-RejectedMarker ("{" + [char]0x00a0 + $valid.Substring(1)) 'NBSP whitespace'
    Assert-RejectedMarker (New-ValidMarker -Save (Join-Path $saveDirectory 'other.vcdbs')) 'incoherent save path'
    Assert-RejectedMarker (New-ValidMarker -Root (Join-Path $behaviorRoot 'other\.local\L00C')) 'incoherent root'
    Assert-RejectedMarker (New-ValidMarker -Role 'activated-secondary') 'incoherent role'
    Assert-RejectedMarker (New-ValidMarker -Confirmed $false) 'unconfirmed cell binding'
}
finally {
    if (Test-Path -LiteralPath $behaviorAssembly) { Remove-Item -LiteralPath $behaviorAssembly -Force }
    if (Test-Path -LiteralPath $behaviorRoot) { Remove-Item -LiteralPath $behaviorRoot -Recurse -Force }
}
[ordered]@{ TestId='L00-C-MENU-ACTION-POC-STATIC'; Status='PASS'; Scope='Compile and static contract only; no T00-06 client cycle was executed.'; DriverCompilation='PASS'; VintagestoryLibVersion=([Reflection.AssemblyName]::GetAssemblyName($libPath).Version.ToString()); VintagestoryLibSha256=(Get-FileHash -LiteralPath $libPath -Algorithm SHA256).Hash; Utc=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json
