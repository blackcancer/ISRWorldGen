[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$LaboratoryRoot,
    [Parameter(Mandatory=$true)][string]$GamePathsSaves,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{32}$')][string]$RunId,
    [Parameter(Mandatory=$true)][ValidateRange(1,[int]::MaxValue)][int]$RuntimeProcessId,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$sources=@('L00CStrictEvidenceJson.cs','L00CCampaignStorage.cs','L00CCampaignInstallFailure.cs','L00CT00LifecycleValidator.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
$csc='C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
foreach($path in @($sources+$csc)){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "L00-C external validator is missing $path"}}
$out=Join-Path ([IO.Path]::GetTempPath()) ('l00c-t00-06-external-'+[Guid]::NewGuid().ToString('N')+'.dll')
try{
    & $csc /nologo /target:library "/define:L00C_STANDALONE_ORACLE" /nullable:enable /warnaserror /langversion:latest "/out:$out" $sources
    if($LASTEXITCODE -ne 0){throw 'L00-C external validator compilation failed.'}
    $assembly=[Reflection.Assembly]::LoadFrom($out);$type=$assembly.GetType('ISRWorldGen.L00C.Laboratory.L00CT00LifecycleValidator',$true)
    $method=$type.GetMethod('Validate',[Reflection.BindingFlags]'Static,NonPublic');$report=$method.Invoke($null,@($LaboratoryRoot,$GamePathsSaves,$RunId,$RuntimeProcessId,$EvidenceDirectory))
    [ordered]@{TestId='T00-06';Verdict='EVIDENCE_CONTRACT_VALIDATED_NOT_PASS';RunId=$RunId;Iterations=5;Sessions=15;DedicatedSaves=10;Note='Integrator must still decide T00-06 from real runtime evidence.'}|ConvertTo-Json -Compress
}
catch{if($_.Exception.InnerException){throw $_.Exception.InnerException};throw}
finally{if(Test-Path -LiteralPath $out){try{Remove-Item -LiteralPath $out -Force}catch{}}}
