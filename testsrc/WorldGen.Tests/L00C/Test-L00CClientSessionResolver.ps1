[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'L00CMenuActionDriver.cs'
$tests = Join-Path $PSScriptRoot 'L00CClientSessionResolverTests.cs'
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
foreach ($path in @($driver, $tests, $csc)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing L00-C resolver test input: $path" } }
$text = Get-Content -LiteralPath $driver -Raw
foreach ($required in @('TryFindScreenManagerFromClientApi', 'TryResolveClientSessionChain', 'ClientCoreAPI.game -> ClientMain.ScreenRunningGame -> GuiScreen.ScreenManager', 'RequireDeclaredInstanceField', 'BindingFlags.DeclaredOnly', 'field.MetadataToken & 0x00ffffff')) {
    if (-not $text.Contains($required)) { throw "Missing audited resolver contract: $required" }
}
if (-not $text.Contains('TryFindReachable(clientApi, "Vintagestory.Client.NoObf.ClientMain", out clientMain)')) { throw 'Manager-root campaign discovery must remain available.' }
$resolver = $text.IndexOf('internal static bool TryFindScreenManagerFromClientApi')
$resolverBody = $text.Substring($resolver, $text.IndexOf('    // ScreenManager owns', $resolver) - $resolver)
if ($resolverBody.Contains('TryFindReachable')) { throw 'ClientCoreAPI manager resolution must not fall back to generic traversal.' }
$output = Join-Path ([IO.Path]::GetTempPath()) ('l00c-client-session-resolver-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $csc /nologo /target:exe /define:DEBUG /langversion:latest "/out:$output" $driver $tests
    if ($LASTEXITCODE -ne 0) { throw 'L00-C client-session resolver deterministic compilation failed.' }
    & $output
    if ($LASTEXITCODE -ne 0) { throw "L00-C client-session resolver deterministic oracle failed ($LASTEXITCODE)." }
}
finally { if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force } }
[ordered]@{ TestId='L00-C-CLIENT-SESSION-RESOLVER'; Status='PASS'; Scope='Deterministic surrogate reflection chain only; no Vintage Story process was launched.'; Cases='depth3-success;wrong-root;null-link;token-drift;all-three-fields-public-internal-protected-visibility-drift;static-drift;field-type-drift'; Utc=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json
