[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sources = @('L00CMenuActionDriver.cs','L00CMenuActionLaboratoryHost.cs','L00CStrictEvidenceJson.cs','L00CCampaignStorage.cs','L00CMenuCellRebindingOracle.cs') | ForEach-Object { Join-Path $root $_ }
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$out = Join-Path ([IO.Path]::GetTempPath()) ('l00c-rebind-oracle-' + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    & $csc /nologo /target:library /define:DEBUG /langversion:latest "/out:$out" $sources
    if ($LASTEXITCODE -ne 0) { throw 'L00-C menu-cell rebinding oracle compilation failed.' }
    $a = [Reflection.Assembly]::LoadFrom($out)
    $m = $a.GetType('ISRWorldGen.L00C.Laboratory.L00CMenuCellRebindingOracle', $true).GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $m -or $m.Invoke($null, @()) -ne 0) { throw 'L00-C menu-cell rebinding oracle failed.' }
    [ordered]@{ TestId='L00-C-MENU-CELL-REBINDING-EXECUTABLE-ORACLE'; Status='PASS'; Cases='sort/index relocation; missing path; duplicate path; vanished file'; Scope='Temporary paths only; no AppData, UI or Vintage Story process.' } | ConvertTo-Json -Compress
}
finally { if (Test-Path -LiteralPath $out) { try { Remove-Item -LiteralPath $out -Force } catch { } } }
