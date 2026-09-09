[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$sources = @('L00CMenuActionDriver.cs','L00CManagerLeaseLifecycle.cs','L00CManagerLeaseLifecycleTests.cs') | ForEach-Object { Join-Path $root $_ }
foreach ($path in @($csc) + $sources) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing L00-C lease simulation input: $path" } }
$output = Join-Path ([IO.Path]::GetTempPath()) ('l00c-manager-lease-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $csc /nologo /target:exe /langversion:latest "/out:$output" $sources
    if ($LASTEXITCODE -ne 0) { throw 'L00-C manager lease simulation did not compile.' }
    & $output
    if ($LASTEXITCODE -ne 0) { throw "L00-C manager lease simulation failed ($LASTEXITCODE)." }
}
finally { if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force } }
[ordered]@{ TestId='L00-C-MANAGER-LEASE-LIFECYCLE'; Status='PASS'; Scope='Executable deterministic production-adapter ownership and acquisition simulation; no Vintage Story process was launched.'; Cases='manager-immediate-acquired-install-before-release;manager-delayed-unregister-install-release;timeout-30s;attempt-limit-600;resolver-fault;api-mismatch;transfer-cancelled;terminalize-unregister-failure;late-callback;register-failure;unregister-failure;pump-publication-failure;duplicate-start;active-pump-rollback-then-reinstall;active-root-conflict-then-cleanup;manager-waiting-session-transfer-dispose-old-first;manager-waiting-session-transfer-dispose-current-first;subscription-failure-manager-immediate;subscription-failure-after-delayed-handoff;second-start-same-receipt-no-write;receipt-other-pid;receipt-other-start;receipt-other-tx;receipt-other-nonce;receipt-other-args;receipt-other-provenance;receipt-tamper;publishing-residue'; Utc=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json
