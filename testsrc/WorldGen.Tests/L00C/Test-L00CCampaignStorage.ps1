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
    [ordered]@{ TestId='L00-C-CAMPAIGN-STORAGE-EXECUTABLE-ORACLE'; Status='PASS'; Isolation='prior campaign bytes preserved; each campaign owns exactly ten canonical A_i/B_i save paths beneath a supplied GamePaths.Saves test directory'; Collision='all ten db targets, sidecars, internal-authority markers, journal, and receipts refuse path/type collision without overwrite or deletion'; AbortRecovery='all journal prefixes across ten saves; byte-exact immutable v3 provenance/intent; copied, missing, corrupt, reordered, or altered marker and provenance evidence refuses cleanup; every ordered deletion boundary resumes; unknown or changed artifacts preserve bytes'; LegacyRecovery='single pinned run/PID; explicit db+wal+shm paths and hashes; immutable manifest, seal and intent; generic no-journal path still refuses; every deletion boundary resumes in shm,wal,db order; tamper, collision, path escape, reparse and replay preserve bytes'; Cleanup='runtime-stopped proof is rechecked before every effect; sealed v3 cleanup covers exactly ten saves and forty owned artifacts, is recoverable/idempotent, and preserves user worlds'; Scope='Temporary paths only; no real AppData, Vintage Story process or UI was used.' } | ConvertTo-Json -Compress
}
finally { if (Test-Path -LiteralPath $out) { try { Remove-Item -LiteralPath $out -Force } catch { } } }
