[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot 'Set-L00CF5AuthenticatedProfile.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('isr-l00c-f5-fixture-' + [Guid]::NewGuid().ToString('N'))
function Assert-Refused([scriptblock]$Operation, [string]$Label) { try { & $Operation } catch { return }; throw "Expected refusal: $Label" }
function New-Fixture([string]$Name, [string]$Arguments = '--tracelog') {
    $root = Join-Path $temporaryRoot $Name; $properties = Join-Path $root 'src\WorldGen.VintageStory\Properties'; $lab = Join-Path $root '.local\L00C'; $package = Join-Path $lab 'menu-action-testmod\Debug\isrworldgenl00clab'
    [void](New-Item -ItemType Directory -Path $properties -Force); [void](New-Item -ItemType Directory -Path $package -Force)
    Set-Content -LiteralPath (Join-Path $lab '.isrworldgen-lab') -Value fixture -NoNewline; Set-Content -LiteralPath (Join-Path $package 'modinfo.json') -Value '{}' -NoNewline; Set-Content -LiteralPath (Join-Path $package 'ISRWorldGen.L00C.MenuActionLab.dll') -Value fixture -NoNewline
    $json = @'
{
  "profiles": {
    "ISRWorldGen Client (authenticated user data)": { "commandName": "Executable", "executablePath": "C:\\Game\\Vintagestory.exe", "commandLineArgs": "__ARGUMENTS__" },
    "Second profile must stay unchanged": { "commandName": "Executable", "executablePath": "C:\\Game\\Vintagestory.exe", "commandLineArgs": "--second" }
  }
}
'@.Replace('__ARGUMENTS__', $Arguments)
    $launch = Join-Path $properties 'launchSettings.json'; [IO.File]::WriteAllText($launch, $json, [Text.UTF8Encoding]::new($false)); [pscustomobject]@{ Root = $root; Lab = $lab; Package = $package; Launch = $launch }
}
try {
    $good = New-Fixture good; $before = [IO.File]::ReadAllBytes($good.Launch)
    $receipt = & $helper -Action Prepare -RepositoryRoot $good.Root -LaunchSettingsPath $good.Launch -LaboratoryRoot $good.Lab -PackagePath $good.Package | ConvertFrom-Json
    $after = Get-Content -LiteralPath $good.Launch -Raw | ConvertFrom-Json
    $profiles = @($after.profiles.PSObject.Properties); $first = $profiles[0].Value; $second = $profiles[1].Value
    if ($first.commandLineArgs -notmatch [regex]::Escape($good.Package) -or $first.commandLineArgs -match '(?i)--dataPath' -or $first.environmentVariables.ISR_L00C_LAB -ne '1' -or $first.environmentVariables.ISR_L00C_LAB_ROOT -ne $good.Lab -or $second.commandLineArgs -ne '--second') { throw 'Prepare must alter only the first profile and preserve its no-dataPath contract.' }
    & $helper -Action Restore -RepositoryRoot $good.Root -LaunchSettingsPath $good.Launch -LaboratoryRoot $good.Lab -BackupDirectory $receipt.BackupDirectory | Out-Null
    if (-not [Linq.Enumerable]::SequenceEqual[byte]($before, [IO.File]::ReadAllBytes($good.Launch))) { throw 'Restore did not reinstate strict original bytes.' }
    foreach ($indicator in @('login', 'token', 'credential', 'password')) {
        $secret = New-Fixture ('secret-' + $indicator) ('--tracelog --' + $indicator + ' never-use')
        Assert-Refused { & $helper -Action Prepare -RepositoryRoot $secret.Root -LaunchSettingsPath $secret.Launch -LaboratoryRoot $secret.Lab -PackagePath $secret.Package } "$indicator indicator"
    }
    $dataPath = New-Fixture datapath '--dataPath C:\\unsafe'; Assert-Refused { & $helper -Action Prepare -RepositoryRoot $dataPath.Root -LaunchSettingsPath $dataPath.Launch -LaboratoryRoot $dataPath.Lab -PackagePath $dataPath.Package } 'dataPath profile'
    $invalid = New-Fixture invalid; Remove-Item -LiteralPath (Join-Path $invalid.Package 'modinfo.json') -Force; Assert-Refused { & $helper -Action Prepare -RepositoryRoot $invalid.Root -LaunchSettingsPath $invalid.Launch -LaboratoryRoot $invalid.Lab -PackagePath $invalid.Package } 'invalid package'
    $tamper = New-Fixture tamper; $tamperReceipt = & $helper -Action Prepare -RepositoryRoot $tamper.Root -LaunchSettingsPath $tamper.Launch -LaboratoryRoot $tamper.Lab -PackagePath $tamper.Package | ConvertFrom-Json; Add-Content -LiteralPath $tamper.Launch -Value ' '; Assert-Refused { & $helper -Action Restore -RepositoryRoot $tamper.Root -LaunchSettingsPath $tamper.Launch -LaboratoryRoot $tamper.Lab -BackupDirectory $tamperReceipt.BackupDirectory } 'modified hash'
    $owned = New-Fixture owner; $ownerReceipt = & $helper -Action Prepare -RepositoryRoot $owned.Root -LaunchSettingsPath $owned.Launch -LaboratoryRoot $owned.Lab -PackagePath $owned.Package | ConvertFrom-Json
    $ownerReceiptPath = Join-Path $ownerReceipt.BackupDirectory 'metadata.json'; $ownerMetadata = Get-Content -LiteralPath $ownerReceiptPath -Raw | ConvertFrom-Json; $ownerMetadata.Owner = 'untrusted-owner'; $ownerMetadata | ConvertTo-Json | Set-Content -LiteralPath $ownerReceiptPath -NoNewline
    Assert-Refused { & $helper -Action Restore -RepositoryRoot $owned.Root -LaunchSettingsPath $owned.Launch -LaboratoryRoot $owned.Lab -BackupDirectory $ownerReceipt.BackupDirectory } 'backup owner'
    [ordered]@{ TestId = 'L00-C-F5-AUTHENTICATED-PROFILE'; Status = 'PASS'; Scope = 'Synthetic launchSettings fixtures only; no real profile, login, token, or client process was used.' } | ConvertTo-Json -Compress
} finally { if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force } }
