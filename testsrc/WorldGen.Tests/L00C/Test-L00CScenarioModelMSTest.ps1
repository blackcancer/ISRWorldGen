[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$compiler = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$runner = 'C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe'
$framework = Join-Path $env:USERPROFILE '.nuget\packages\mstest.testframework\4.0.2\lib\net462\MSTest.TestFramework.dll'
$adapter = Join-Path $env:USERPROFILE '.nuget\packages\mstest.testadapter\4.0.2\buildTransitive\net462'
$model = Join-Path $PSScriptRoot '..\..\..\src\WorldGen.VintageStory\L00CScenarioModel.cs'
$tests = Join-Path $PSScriptRoot 'L00CScenarioModelTests.cs'
foreach ($path in @($compiler, $runner, $framework, $adapter, $model, $tests)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "L00-C S2 MSTest input is absent: $path" }
}

$tempDirectory = Join-Path ([IO.Path]::GetTempPath()) ('l00c-s2-mstest-' + [Guid]::NewGuid().ToString('N'))
$testAssembly = Join-Path $tempDirectory 'L00CScenarioModelTests.dll'
$frameworkCopy = Join-Path $tempDirectory 'MSTest.TestFramework.dll'
[void][IO.Directory]::CreateDirectory($tempDirectory)
try {
    Copy-Item -LiteralPath $framework -Destination $frameworkCopy
    & $compiler /nologo /target:library /nullable:enable /warnaserror /langversion:latest "/define:DEBUG,L00C_STANDALONE_ORACLE" "/out:$testAssembly" "/reference:$framework" $model $tests
    if ($LASTEXITCODE -ne 0) { throw 'L00-C S2 MSTest source compilation failed.' }
    & $runner $testAssembly "/TestAdapterPath:$adapter"
    if ($LASTEXITCODE -ne 0) { throw "L00-C S2 MSTest execution failed ($LASTEXITCODE)." }
}
finally {
    foreach ($path in @($testAssembly, $frameworkCopy)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
    }
    if (Test-Path -LiteralPath $tempDirectory -PathType Container) { Remove-Item -LiteralPath $tempDirectory -Force }
}
