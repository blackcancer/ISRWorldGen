[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$model = Join-Path $root '..\..\..\src\WorldGen.VintageStory\L00CScenarioModel.cs'
$oracle = Join-Path $root 'L00CScenarioModelOracle.cs'
foreach ($path in @($compiler, $model, $oracle)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "L00-C S2 scenario oracle input is missing: $path"
    }
}

$output = Join-Path ([IO.Path]::GetTempPath()) ('l00c-s2-scenario-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe /nullable:enable /warnaserror /langversion:latest /define:L00C_STANDALONE_ORACLE "/out:$output" $model $oracle
    if ($LASTEXITCODE -ne 0) { throw 'L00-C S2 immutable scenario oracle compilation failed.' }
    & $output
    if ($LASTEXITCODE -ne 0) { throw "L00-C S2 immutable scenario oracle failed ($LASTEXITCODE)." }
}
finally {
    if (Test-Path -LiteralPath $output -PathType Leaf) { Remove-Item -LiteralPath $output -Force }
}
