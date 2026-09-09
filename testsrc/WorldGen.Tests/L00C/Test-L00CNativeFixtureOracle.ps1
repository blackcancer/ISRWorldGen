[CmdletBinding()]
param([string]$GamePath = 'D:\Jeux\Vintagestory')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sources = @('L00CMenuActionDriver.cs','L00CMenuActionLaboratoryHost.cs','L00CFixtureBootstrap.cs','L00CStrictEvidenceJson.cs','L00CCampaignStorage.cs','L00CNativeFixtureOracle.cs','L00CNativeOpenControllerCompileStub.cs', (Join-Path $root '..\..\..\src\WorldGen.VintageStory\WorldgenProbe\L00CLifecycleShutdownBarrier.cs'), (Join-Path $root '..\..\..\src\WorldGen.VintageStory\WorldgenProbe\L00CLevelFinalizeGate.cs')) | ForEach-Object { if ([IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $root $_ } }
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
foreach ($path in @($sources + $csc, (Join-Path $GamePath 'VintagestoryLib.dll'), (Join-Path $GamePath 'VintagestoryAPI.dll'), (Join-Path $GamePath 'Lib\Newtonsoft.Json.dll'))) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "L00-C native fixture oracle is missing $path" } }
$out = Join-Path ([IO.Path]::GetTempPath()) ('l00c-native-fixture-oracle-' + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    & $csc /nologo /target:library "/define:DEBUG,L00C_STANDALONE_ORACLE" /nullable:enable /warnaserror /langversion:latest "/out:$out" $sources
    if ($LASTEXITCODE -ne 0) { throw 'L00-C native fixture oracle compilation failed.' }
    [Environment]::SetEnvironmentVariable('ISR_L00C_ORACLE_GAME_PATH', $GamePath, 'Process')
    $assembly = [Reflection.Assembly]::LoadFrom($out)
    $oracle = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CNativeFixtureOracle', $true).GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $oracle -or $oracle.Invoke($null, @()) -ne 0) { throw 'L00-C native fixture executable oracle failed.' }
    [ordered]@{ TestId='L00-C-NATIVE-FIXTURE-EXECUTABLE-ORACLE'; Status='PASS'; Mapping='StartServerArgs exact 1.22.7 fields and non-null JsonObject/JToken'; Readiness='all eleven predicates required, including canonical client save GUID; each individual false refused'; LibrarySha256=(Get-FileHash (Join-Path $GamePath 'VintagestoryLib.dll') -Algorithm SHA256).Hash } | ConvertTo-Json
}
finally { if (Test-Path -LiteralPath $out) { try { Remove-Item -LiteralPath $out -Force } catch { } } }
