[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot 'Set-L00CF5AuthenticatedProfile.ps1'; $root = Join-Path ([IO.Path]::GetTempPath()) ('isr-l00c-f5-' + [Guid]::NewGuid().ToString('N'))
function Assert-Refused([scriptblock]$Operation, [string]$Label) { try { & $Operation } catch { return }; throw "Expected refusal: $Label" }
function Bytes([string]$Path) { [IO.File]::ReadAllBytes($Path) }
function Equal([byte[]]$A, [byte[]]$B) { if ($A.Length -ne $B.Length) { return $false }; for ($i=0; $i -lt $A.Length; $i++) { if ($A[$i] -ne $B[$i]) { return $false } }; return $true }
function New-Fixture([string]$Name, [string]$Arguments = '--tracelog --addModPath "$(ProjectDir)bin\$(Configuration)\Mods" --addOrigin "$(ProjectDir)assets"') {
    $r = Join-Path $root $Name; $properties = Join-Path $r 'src\WorldGen.VintageStory\Properties'; $lab = Join-Path $r '.local\L00C'; $saves = Join-Path $r 'AppData\VintagestoryData\Saves'; [void](New-Item -ItemType Directory -Path $properties,$lab,$saves -Force); Set-Content -LiteralPath (Join-Path $lab '.isrworldgen-lab') -Value fixture -NoNewline
    $json = ('{"profiles":{"ISRWorldGen Client (authenticated user data)":{"commandName":"Executable","executablePath":"C:\\Game\\Vintagestory.exe","commandLineArgs":"__ARGS__"},"Unchanged":{"commandName":"Executable","executablePath":"C:\\Game\\Vintagestory.exe","commandLineArgs":"--safe"}}}').Replace('__ARGS__',$Arguments.Replace('\','\\').Replace('"','\"')); $launch = Join-Path $properties 'launchSettings.json'; [IO.File]::WriteAllText($launch,$json,[Text.UTF8Encoding]::new($false))
    $user = Join-Path $r 'src\WorldGen.VintageStory\WorldGen.VintageStory.csproj.user'; [IO.File]::WriteAllText($user,'<Project><PropertyGroup><ActiveDebugProfile>Unchanged</ActiveDebugProfile><Other>retain</Other></PropertyGroup></Project>',[Text.UTF8Encoding]::new($false)); $save = Join-Path $saves 'ISRWorldGen-L00C-Client.vcdbs'; [IO.File]::WriteAllText($save,'attested')
    [pscustomobject]@{ Root=$r; Launch=$launch; User=$user; Save=$save }
}
try {
    $good = New-Fixture good; $before = Bytes $good.Launch; $beforeUser = Bytes $good.User; $receipt = & $helper -Action Prepare -SyntheticFixtureRoot $good.Root | ConvertFrom-Json; $profile = (Get-Content -LiteralPath $good.Launch -Raw | ConvertFrom-Json).profiles.'ISRWorldGen Client (authenticated user data)'; $user = [Xml.XmlDocument]::new(); $user.Load($good.User)
    if ($profile.commandLineArgs -cne '--openWorld "ISRWorldGen-L00C-Client" --tracelog --addModPath "$(ProjectDir)bin\$(Configuration)\Mods" --addOrigin "$(ProjectDir)assets"' -or $profile.environmentVariables.ISR_L00C_LAB -ne '1' -or [string]::IsNullOrWhiteSpace($profile.environmentVariables.ISR_L00C_LAB_ROOT) -or $user.SelectSingleNode('//*[local-name()="ActiveDebugProfile"]').InnerText -ne 'ISRWorldGen Client (authenticated user data)' -or $user.SelectSingleNode('//*[local-name()="Other"]').InnerText -ne 'retain') { throw 'Prepare did not make the narrow F5 change.' }
    & $helper -Action Restore -SyntheticFixtureRoot $good.Root -BackupDirectory $receipt.BackupDirectory | Out-Null; if (-not (Equal $before (Bytes $good.Launch)) -or -not (Equal $beforeUser (Bytes $good.User))) { throw 'Restore did not reinstate original bytes.' }
    $existing = New-Fixture existing '--addModPath "$(ProjectDir)bin\$(Configuration)\Mods" --openWorld "other"'; Assert-Refused { & $helper -Action Prepare -SyntheticFixtureRoot $existing.Root } 'existing openWorld'
    $missing = New-Fixture missing; Remove-Item -LiteralPath $missing.Save -Force; Assert-Refused { & $helper -Action Prepare -SyntheticFixtureRoot $missing.Root } 'missing bootstrap save'
    $tamper = New-Fixture tamper; $prepared = & $helper -Action Prepare -SyntheticFixtureRoot $tamper.Root | ConvertFrom-Json; Add-Content -LiteralPath $tamper.Launch -Value ' '; Assert-Refused { & $helper -Action Restore -SyntheticFixtureRoot $tamper.Root -BackupDirectory $prepared.BackupDirectory } 'post-prepare mutation'
    $secret = New-Fixture secret '--addModPath "$(ProjectDir)bin\$(Configuration)\Mods" --token never'; Assert-Refused { & $helper -Action Prepare -SyntheticFixtureRoot $secret.Root } 'sensitive profile'
    [ordered]@{ TestId='L00-C-F5-AUTHENTICATED-PROFILE'; Status='PASS'; Scope='Synthetic profile transaction only; no real profile, login, token, or client process was used.' } | ConvertTo-Json -Compress
}
finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
