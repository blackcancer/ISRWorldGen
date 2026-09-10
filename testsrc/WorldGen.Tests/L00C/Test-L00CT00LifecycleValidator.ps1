[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$sources=@('L00CStrictEvidenceJson.cs','L00CCampaignStorage.cs','L00CCampaignInstallFailure.cs','L00CT00LifecycleValidator.cs','L00CT00LifecycleValidatorOracle.cs')|ForEach-Object{Join-Path $root $_}
$csc='C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
foreach($path in @($sources+$csc)){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "L00-C T00-06 validator test is missing $path"}}
$validatorText=Get-Content -LiteralPath (Join-Path $root 'L00CT00LifecycleValidator.cs') -Raw
foreach($forbidden in @('MapChunk','HeightMap','Teleport','FixtureChunk','FluidLayer')){if($validatorText.Contains($forbidden)){throw "Standalone T00-06 validator is coupled to spatial evidence: $forbidden"}}
$out=Join-Path ([IO.Path]::GetTempPath()) ('l00c-t00-06-validator-'+[Guid]::NewGuid().ToString('N')+'.dll')
try{
    & $csc /nologo /target:library "/define:L00C_STANDALONE_ORACLE" /nullable:enable /warnaserror /langversion:latest "/out:$out" $sources
    if($LASTEXITCODE -ne 0){throw 'L00-C T00-06 validator oracle compilation failed.'}
    $assembly=[Reflection.Assembly]::LoadFrom($out)
    $method=$assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CT00LifecycleValidatorOracle',$true).GetMethod('Run',[Reflection.BindingFlags]'Static,NonPublic')
    if($null -eq $method -or $method.Invoke($null,@()) -ne 0){throw 'L00-C T00-06 validator oracle failed.'}
}
finally{if(Test-Path -LiteralPath $out){try{Remove-Item -LiteralPath $out -Force}catch{}}}
[ordered]@{TestId='L00-C-T00-06-EXTERNAL-VALIDATOR-ORACLE';Status='PASS';Positive='5 iterations, 15 sessions, 10 saves, A reopen, B isolation, internal SaveGame marker reread, native close proof, releases, exact cleanup';Negative='timeout, partial stop, external guard substituted for game proof, missing/corrupt/copied marker authority, collision, stale/duplicate/closing callbacks, missing unregistration, provenance tamper';Scope='Standalone deterministic validator oracle over temporary files only; no spatial evidence, runtime, VS, F5, or T00-06 PASS claim.'}|ConvertTo-Json -Compress
