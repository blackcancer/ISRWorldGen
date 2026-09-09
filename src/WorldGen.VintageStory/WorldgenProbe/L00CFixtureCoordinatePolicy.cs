#if DEBUG
namespace ISRWorldGen.WorldgenProbe;

/// <summary>
/// Defines the coordinate contract for the disposable L00-C world-generation fixture.
/// Values supplied to the world manager are chunk-column coordinates, while the
/// Vintage Story map extents are block coordinates.  The conversion is deliberately
/// centralized so a profile cannot accidentally use a block coordinate as a chunk.
/// </summary>
internal static class L00CFixtureCoordinatePolicy
{
    internal const int DefaultLaboratoryFixtureChunk = 31_990;

    internal static FixtureCoordinateBounds Validate(
        int mapSizeXBlocks,
        int mapSizeZBlocks,
        int chunkSize,
        int fixtureChunkX,
        int fixtureChunkZ,
        int protectionRadius)
    {
        if (chunkSize <= 0)
        {
            throw new InvalidOperationException("L00-C fixture coordinate validation requires a positive chunk size.");
        }
        if (protectionRadius < 1)
        {
            throw new InvalidOperationException("L00-C fixture coordinate validation requires a positive protection radius.");
        }

        int mapChunkCountX = ToExactChunkCount(mapSizeXBlocks, chunkSize, "X");
        int mapChunkCountZ = ToExactChunkCount(mapSizeZBlocks, chunkSize, "Z");
        ValidateInteriorCoordinate(fixtureChunkX, mapChunkCountX, protectionRadius, "X");
        ValidateInteriorCoordinate(fixtureChunkZ, mapChunkCountZ, protectionRadius, "Z");
        return new FixtureCoordinateBounds(mapChunkCountX, mapChunkCountZ, fixtureChunkX, fixtureChunkZ, protectionRadius);
    }

    private static int ToExactChunkCount(int mapSizeBlocks, int chunkSize, string axis)
    {
        if (mapSizeBlocks <= 0 || mapSizeBlocks % chunkSize != 0)
        {
            throw new InvalidOperationException($"L00-C map size {axis}={mapSizeBlocks} must be a positive exact multiple of chunk size {chunkSize}.");
        }
        return mapSizeBlocks / chunkSize;
    }

    private static void ValidateInteriorCoordinate(int coordinate, int mapChunkCount, int protectionRadius, string axis)
    {
        if (coordinate < protectionRadius || coordinate >= mapChunkCount - protectionRadius)
        {
            throw new InvalidOperationException($"L00-C fixture chunk {axis}={coordinate} is outside the bounded interior [{protectionRadius},{mapChunkCount - protectionRadius - 1}] of {mapChunkCount} chunk columns.");
        }
    }
}

internal readonly record struct FixtureCoordinateBounds(
    int MapChunkCountX,
    int MapChunkCountZ,
    int FixtureChunkX,
    int FixtureChunkZ,
    int ProtectionRadius);
#endif
