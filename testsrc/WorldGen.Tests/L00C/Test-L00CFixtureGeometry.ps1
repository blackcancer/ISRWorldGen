[CmdletBinding()]
param(
    [int]$ChunkSize = 32,
    [int]$WorldHeight = 256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ChunkSize -lt 8 -or $WorldHeight -lt 32) {
    throw 'The analytical fixture requires ChunkSize >= 8 and WorldHeight >= 32.'
}

$rockSurface = [Math]::Min([Math]::Max([int]($WorldHeight / 4), 8), $WorldHeight - 8)
$waterSurface = $rockSurface + 3
$dividerX = [int]($ChunkSize / 2)
$freshColumns = 0
$saltColumns = 0
$wallColumns = 0

for ($x = 0; $x -lt $ChunkSize; $x++) {
    for ($z = 0; $z -lt $ChunkSize; $z++) {
        $isWall = $x -eq 0 -or $z -eq 0 -or $x -eq ($ChunkSize - 1) -or $z -eq ($ChunkSize - 1) -or $x -eq $dividerX
        if ($isWall) {
            $wallColumns++
        }
        elseif ($x -lt $dividerX) {
            $freshColumns++
        }
        else {
            $saltColumns++
        }
    }
}

$waterDepth = $waterSurface - $rockSurface
$freshBlocks = $freshColumns * $waterDepth
$saltBlocks = $saltColumns * $waterDepth
$solidBlocks = (($freshColumns + $saltColumns) * ($rockSurface + 1)) + ($wallColumns * ($waterSurface + 1))

if ($freshBlocks -le 0 -or $saltBlocks -le 0) {
    throw 'Both fresh and saline fixture zones must contain fluid blocks.'
}
if ($rockSurface -ge $waterSurface) {
    throw 'The terrain height must remain below the fluid surface.'
}
if (($freshColumns + $saltColumns + $wallColumns) -ne ($ChunkSize * $ChunkSize)) {
    throw 'Fixture column ownership is incomplete or overlapping.'
}

$result = [ordered]@{
    TestId = 'L00-C-FIXTURE-GEOMETRY'
    Status = 'PASS'
    ChunkSize = $ChunkSize
    WorldHeight = $WorldHeight
    RockSurface = $rockSurface
    WaterSurface = $waterSurface
    FreshColumns = $freshColumns
    SaltColumns = $saltColumns
    WallColumns = $wallColumns
    FreshFluidBlocks = $freshBlocks
    SaltFluidBlocks = $saltBlocks
    SolidBlocks = $solidBlocks
}

$result | ConvertTo-Json -Depth 4
