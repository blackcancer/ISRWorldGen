[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sources = @('L00CStrictEvidenceJson.cs','L00CCampaignStorage.cs','L00CCampaignInstallFailure.cs','L00CCampaignStorageOracle.cs') | ForEach-Object { Join-Path $root $_ }
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
foreach ($path in @($sources + $csc)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "L00-C campaign storage oracle is missing $path" } }
$out = Join-Path ([IO.Path]::GetTempPath()) ('l00c-campaign-storage-oracle-' + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    & $csc /nologo /target:library /langversion:latest "/out:$out" $sources
    if ($LASTEXITCODE -ne 0) { throw 'L00-C campaign storage oracle compilation failed.' }
    $assembly = [Reflection.Assembly]::LoadFrom($out)
    $oracle = $assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CCampaignStorageOracle', $true).GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $oracle -or $oracle.Invoke($null, @()) -ne 0) { throw 'L00-C campaign storage executable oracle failed.' }
    & (Join-Path $root 'Test-L00CLegacyRecoveryLaunchers.ps1') -DebugAssemblyPath $out | Out-Null
    [ordered]@{ TestId='L00-C-CAMPAIGN-STORAGE-EXECUTABLE-ORACLE'; Status='PASS'; Isolation='prior campaign bytes preserved; each campaign receives unique flat owned names beneath a supplied GamePaths.Saves test directory'; Collision='exact db, wal, shm, marker and final-receipt path/type collisions are refused without overwrite or deletion'; AbortRecovery='db-only, db+wal and db+wal+shm shapes; both roles; byte-exact immutable v2 intent; whitespace/reordering tamper after every prefix refuses; every ordered deletion boundary resumes; unknown sidecars and out-of-order disappearance preserve bytes'; LegacyRecovery='single pinned run/PID; explicit db+wal+shm paths and hashes; immutable manifest, seal and intent; generic no-journal path still refuses; every deletion boundary resumes in shm,wal,db order; tamper, collision, path escape, reparse and replay preserve bytes'; Cleanup='runtime-stopped proof is rechecked before every effect; sealed v2 cleanup covers both roles and every artifact boundary'; Scope='Temporary paths only; no real AppData, Vintage Story process or UI was used.' } | ConvertTo-Json -Compress
}
finally { if (Test-Path -LiteralPath $out) { try { Remove-Item -LiteralPath $out -Force } catch { } } }
