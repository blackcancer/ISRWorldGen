# PowerShell 7+ only. Discovery probes installed runtimes; it never installs one.
# A filename or Microsoft Store alias is not evidence of a usable interpreter.
function Invoke-TestPythonProbe {
    param([string]$Executable, [string[]]$PrefixArguments = @())
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($Executable)
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        $process.StartInfo.StandardOutputEncoding = [Text.Encoding]::UTF8
        $process.StartInfo.StandardErrorEncoding = [Text.Encoding]::UTF8
        $process.StartInfo.Environment['PYTHON_MANAGER_AUTOMATIC_INSTALL'] = 'false'
        foreach ($name in @('PYLAUNCHER_ALLOW_INSTALL', 'PYLAUNCHER_ALWAYS_INSTALL')) {
            [void]$process.StartInfo.Environment.Remove($name)
        }
        foreach ($argument in $PrefixArguments) { [void]$process.StartInfo.ArgumentList.Add($argument) }
        foreach ($argument in @('-I', '-B', '-c', 'import sys,json; print(json.dumps({"executable":sys.executable,"version":list(sys.version_info[:3])}))')) {
            [void]$process.StartInfo.ArgumentList.Add($argument)
        }
        if (-not $process.Start()) { return $null }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $finished = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($process.WaitForExitAsync(), $stdout, $stderr))
        if (-not $finished.Wait(10000)) {
            if (-not $process.HasExited) { $process.Kill($true) }
            if (-not $finished.Wait(3000)) { throw 'PYTHON_PROBE_CLEANUP_FAILED: process or output streams remain active.' }
            return $null
        }
        if ($process.ExitCode -ne 0) { return $null }
        try {
            $info = $stdout.Result | ConvertFrom-Json -ErrorAction Stop
            $version = @($info.version)
            if ($version.Count -ne 3 -or $version[0] -ne 3 -or $version[1] -lt 10 -or
                [string]::IsNullOrWhiteSpace([string]$info.executable) -or
                -not [IO.Path]::IsPathFullyQualified([string]$info.executable) -or
                -not (Test-Path -LiteralPath ([string]$info.executable) -PathType Leaf)) { return $null }
            return [pscustomobject]@{
                Path = [IO.Path]::GetFullPath([string]$info.executable)
                Version = $version -join '.'
            }
        } catch { return $null }
    } catch [ComponentModel.Win32Exception] {
        return $null
    } finally {
        $process.Dispose()
    }
}

function Resolve-TestPython {
    param([string]$ExplicitPath)
    if (-not [string]::IsNullOrEmpty($ExplicitPath)) {
        if (-not [IO.Path]::IsPathFullyQualified($ExplicitPath) -or
            -not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw 'PYTHON_PREREQUISITE_INVALID: PythonPath/ISR_TEST_PYTHON must name an existing absolute Python executable. No fallback; no tests were run.'
        }
        $selected = Invoke-TestPythonProbe -Executable $ExplicitPath
        if ($null -eq $selected) {
            throw 'PYTHON_PREREQUISITE_INVALID: the explicitly selected executable is not a usable Python 3.10+ runtime. No fallback; no tests were run.'
        }
        # Pin the actual interpreter, not an alias/launcher that selected it.
        $confirmed = Invoke-TestPythonProbe -Executable $selected.Path
        if ($null -eq $confirmed -or $confirmed.Path -cne $selected.Path) {
            throw 'PYTHON_PREREQUISITE_INVALID: the reported interpreter could not be confirmed directly. No tests were run.'
        }
        return $confirmed
    }
    foreach ($name in @('python', 'python3', 'py')) {
        $commands = @(Get-Command $name -CommandType Application -All -ErrorAction SilentlyContinue)
        foreach ($command in $commands) {
            $prefix = if ($name -eq 'py') { @('-3') } else { @() }
            $selected = Invoke-TestPythonProbe -Executable $command.Source -PrefixArguments $prefix
            if ($null -eq $selected) { continue }
            $confirmed = Invoke-TestPythonProbe -Executable $selected.Path
            if ($null -ne $confirmed -and $confirmed.Path -ceq $selected.Path) { return $confirmed }
        }
    }
    throw 'PYTHON_PREREQUISITE_MISSING: Python 3.10+ is required by the L05-D tests. Install Python or use -PythonPath with its real executable. A Microsoft Store alias is not sufficient. No tests were run.'
}
