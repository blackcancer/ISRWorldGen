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
[ordered]@{ TestId='L00-C-MANAGER-LEASE-LIFECYCLE'; Status='PASS'; Scope='Executable deterministic seam simulation; no Vintage Story process was launched.'; Cases='immediate-ready-zero-listener;delayed-ready-unregister-before-install;timeout-30s;attempt-limit-600;resolver-fault;api-mismatch;dispose;terminalize-unregister-failure;late-callback;register-failure;unregister-failure;install-failure;duplicate-start;active-pump-rollback-then-reinstall;active-root-conflict-then-cleanup'; Utc=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json
