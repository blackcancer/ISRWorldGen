param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{64}$')][string]$Nonce
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
function Invoke-EvidenceGit {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & git -c ("safe.directory=" + $root.Replace('\', '/')) -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git preflight failed: $($Arguments -join ' ')" }
}

$head = ((Invoke-EvidenceGit rev-parse HEAD) | Out-String).Trim()
$tree = ((Invoke-EvidenceGit rev-parse 'HEAD^{tree}') | Out-String).Trim()
$null = & git -c ("safe.directory=" + $root.Replace('\', '/')) -C $root symbolic-ref -q HEAD
$attachedExitCode = $LASTEXITCODE
if ($attachedExitCode -eq 0) { throw 'Evidence requires detached HEAD.' }
if ($attachedExitCode -ne 1) { throw 'Unable to establish whether HEAD is detached.' }
$status = ((Invoke-EvidenceGit status --porcelain --untracked-files=all) | Out-String).Trim()
if ($status) { throw 'Evidence requires a clean worktree including untracked files.' }
dotnet build (Join-Path $root 'ISRWorldGen.sln') -c Release --no-restore /p:VintageStoryPath='D:\Jeux\Vintagestory' /p:InformationalVersion=("1.0.0+" + $head)
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
$finalHead = ((Invoke-EvidenceGit rev-parse HEAD) | Out-String).Trim()
$finalTree = ((Invoke-EvidenceGit rev-parse 'HEAD^{tree}') | Out-String).Trim()
$finalStatus = ((Invoke-EvidenceGit status --porcelain --untracked-files=all) | Out-String).Trim()
if ($finalHead -ne $head -or $finalTree -ne $tree -or $finalStatus) { throw 'Evidence altered provenance.' }
