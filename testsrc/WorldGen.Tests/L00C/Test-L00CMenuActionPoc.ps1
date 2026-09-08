[CmdletBinding()]
param([string]$GamePath = 'D:\Jeux\Vintagestory')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$driverPath = Join-Path $PSScriptRoot 'L00CMenuActionDriver.cs'
$libPath = Join-Path $GamePath 'VintagestoryLib.dll'
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
foreach ($required in @('RequiredLibVersion = "1.22.7.0"', 'RequiredLibSha256 = "E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0"', 'Debugger.IsAttached', 'ISR_L00C_LAB', 'public const int SaveQuitLeaveReason = 0;', 'InvokeExact(clientMain, "SendLeave", new object?[] { SaveQuitLeaveReason });', 'DestroyGameSession', 'SoftExit', 'StartMainMenu')) { if (-not (Select-String -LiteralPath $driverPath -SimpleMatch $required -Quiet)) { throw "Missing POC contract: $required" } }
if (Select-String -LiteralPath $driverPath -Pattern 'SendKeys|mouse_event|keybd_event|WindowsInput|Start-Process|Process\.Start' -Quiet) { throw 'POC must not synthesize UI input or launch the game.' }
[ordered]@{ TestId='L00-C-MENU-ACTION-POC-STATIC'; Status='PASS'; Scope='Static contract only; no T00-06 client cycle was executed.'; VintagestoryLibVersion=([Reflection.AssemblyName]::GetAssemblyName($libPath).Version.ToString()); VintagestoryLibSha256=(Get-FileHash -LiteralPath $libPath -Algorithm SHA256).Hash; Utc=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json
