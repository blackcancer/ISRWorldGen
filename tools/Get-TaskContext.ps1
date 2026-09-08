# Documentation utility only. Does not build the mod or invoke Codex/Visual Studio.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^L[0-9]{2}-[A-Z]$')]
    [string]$TaskId,
    [ValidateRange(1024, 1048576)]
    [int]$MaxBytes = 49152
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$manifestPath = Join-Path $root 'registry/tasks.json'
$manifest = [IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
$matches = @($manifest.tasks | Where-Object { $_.id -eq $TaskId })
if ($matches.Count -ne 1) { throw "Task not found or duplicated: $TaskId" }
$task = $matches[0]
$limit = [Math]::Min($MaxBytes, [int]$task.context_max_bytes)
$parts = New-Object 'System.Collections.Generic.List[string]'
$parts.Add("# Context: $TaskId`n`nDocumentation capsule. Read relative links from each original source path. Code and upstream handoffs are not included automatically.`n")
$seen = @{}
$prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($relative in $task.required_read) {
    if ($seen.ContainsKey($relative)) { continue }
    $seen[$relative] = $true
    $full = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Source outside repository: $relative"
    }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Missing source: $relative" }
    $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
    $text = [IO.File]::ReadAllText($full, [Text.Encoding]::UTF8)
    $parts.Add("---`n`n## SOURCE: $relative`nSHA256: $hash`n`n$text")
}
$content = [string]::Join("`n", $parts)
$bytes = [Text.Encoding]::UTF8.GetByteCount($content)
if ($bytes -gt $limit) {
    throw "Context too large ($bytes bytes > $limit). Split the task or reduce its declared reading scope; no content was truncated."
}
$outDir = Join-Path $root 'artifacts/contexts'
[void][IO.Directory]::CreateDirectory($outDir)
$outFile = Join-Path $outDir ($TaskId + '.md')
[IO.File]::WriteAllText($outFile, $content, [Text.UTF8Encoding]::new($false))
Write-Output ("Context: {0}" -f $outFile)
Write-Output ("UTF-8 bytes: {0}; source files: {1}; limit: {2}" -f $bytes, $seen.Count, $limit)
