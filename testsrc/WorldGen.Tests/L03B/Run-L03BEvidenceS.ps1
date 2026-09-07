param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{64}$')][string]$Nonce
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$head = (& git -C $root rev-parse HEAD).Trim()
$tree = (& git -C $root rev-parse 'HEAD^{tree}').Trim()
if ((& git -C $root symbolic-ref -q HEAD) -eq $null -and $LASTEXITCODE -eq 0) { throw 'Evidence requires detached HEAD.' }
if ((& git -C $root status --porcelain --untracked-files=all)) { throw 'Evidence requires a clean worktree.' }
dotnet build (Join-Path $root 'ISRWorldGen.sln') -c Release --no-restore /p:VintageStoryPath='D:\Jeux\Vintagestory'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$testAssembly = Join-Path $root 'testsrc\WorldGen.Tests\bin\Release\net10.0\ISRWorldGen.Tests.dll'
$coreAssembly = Join-Path $root 'src\WorldGen.Core\bin\Release\net10.0\ISRWorldGen.Core.dll'
$env:ISR_L03B_EVIDENCE_COMMIT = $head
$env:ISR_L03B_EVIDENCE_TREE = $tree
$env:ISR_L03B_EVIDENCE_NONCE = $Nonce
$env:ISR_L03B_EVIDENCE_CONFIGURATION = 'Release'
$env:ISR_L03B_EVIDENCE_RUN = "evidence-s-terminal-$head"
$env:ISR_L03B_EVIDENCE_TEST_ASSEMBLY_SHA256 = (Get-FileHash -LiteralPath $testAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
$env:ISR_L03B_EVIDENCE_CORE_ASSEMBLY_SHA256 = (Get-FileHash -LiteralPath $coreAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
dotnet test (Join-Path $root 'testsrc\WorldGen.Tests\WorldGen.Tests.csproj') -c Release --no-build --filter 'FullyQualifiedName~EvidenceArtifactTests' /p:VintageStoryPath='D:\Jeux\Vintagestory'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ((& git -C $root rev-parse HEAD).Trim() -ne $head -or (& git -C $root rev-parse 'HEAD^{tree}').Trim() -ne $tree -or (& git -C $root status --porcelain --untracked-files=all)) { throw 'Evidence altered provenance.' }
