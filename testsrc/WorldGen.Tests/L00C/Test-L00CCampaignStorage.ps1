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
    [ordered]@{ TestId='L00-C-CAMPAIGN-STORAGE-EXECUTABLE-ORACLE'; Status='PASS'; Isolation='prior campaign bytes preserved; each campaign receives unique flat owned names beneath a supplied GamePaths.Saves test directory'; Collision='existing owned filename is refused without overwrite'; AbortRecovery='prepared; primary-create-intent raw primary; primary-created; secondary-create-intent raw secondary; secondary-created; cycling; interrupted deletion resume; every malformed provenance/PID/extra-owned-name refusal preserves bytes'; Cleanup='runtime-alive and changed-hash cleanup both refuse and preserve files; sealed exact pairs retain their separate contract and clean only after stopped proof'; Scope='Temporary paths only; no real AppData, Vintage Story process or UI was used.' } | ConvertTo-Json -Compress
}
finally { if (Test-Path -LiteralPath $out) { try { Remove-Item -LiteralPath $out -Force } catch { } } }
