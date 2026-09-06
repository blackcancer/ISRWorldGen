#if DEBUG
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;

namespace ISRWorldGen.WorldgenProbe;

internal sealed class PersistedMapFootprintSnapshot
{
    public const string CurrentVersion = "l00c-map-footprint-v1";

    public string Version { get; set; } = CurrentVersion;
    public string MarkerId { get; set; } = string.Empty;
    public string SavegameIdentifier { get; set; } = string.Empty;
    public int FixtureChunkX { get; set; }
    public int FixtureChunkZ { get; set; }
    public int ChunkSize { get; set; }
    public int WorldHeight { get; set; }
    public List<PersistedMapChunkSnapshot> MapChunks { get; set; } = [];
    public string ContentSha256 { get; set; } = string.Empty;

    public static PersistedMapFootprintSnapshot Create(
        string markerId,
        string savegameIdentifier,
        int fixtureChunkX,
        int fixtureChunkZ,
        int chunkSize,
        int worldHeight,
        IEnumerable<PersistedMapChunkSnapshot> mapChunks)
    {
        ArgumentNullException.ThrowIfNull(mapChunks);
        var mapChunkCopies = new List<PersistedMapChunkSnapshot>();
        foreach (PersistedMapChunkSnapshot mapChunk in mapChunks)
        {
            if (mapChunk is null)
            {
                throw new InvalidOperationException("L00-C persisted map snapshot contains a null map chunk copy.");
            }
            mapChunkCopies.Add(mapChunk.DeepCopy());
        }
        var snapshot = new PersistedMapFootprintSnapshot
        {
            MarkerId = markerId,
            SavegameIdentifier = savegameIdentifier,
            FixtureChunkX = fixtureChunkX,
            FixtureChunkZ = fixtureChunkZ,
            ChunkSize = chunkSize,
            WorldHeight = worldHeight,
            MapChunks = mapChunkCopies.OrderBy(mapChunk => mapChunk.X).ThenBy(mapChunk => mapChunk.Z).ToList()
        };
        snapshot.ContentSha256 = snapshot.ComputeContentSha256();
        snapshot.ValidateForCopy(markerId, savegameIdentifier, fixtureChunkX, fixtureChunkZ, chunkSize, worldHeight);
        return snapshot;
    }

    public void ValidateForCopy(
        string markerId,
        string savegameIdentifier,
        int fixtureChunkX,
        int fixtureChunkZ,
        int chunkSize,
        int worldHeight)
    {
        if (Version != CurrentVersion || MarkerId != markerId || SavegameIdentifier != savegameIdentifier ||
            FixtureChunkX != fixtureChunkX || FixtureChunkZ != fixtureChunkZ || ChunkSize != chunkSize || WorldHeight != worldHeight)
        {
            throw new InvalidOperationException("L00-C persisted map snapshot identity or geometry is inconsistent with the current marker and world.");
        }
        if (chunkSize <= 0 || chunkSize > 64 || worldHeight <= 0 || worldHeight > 4096 || MapChunks is null || MapChunks.Count != 9)
        {
            throw new InvalidOperationException("L00-C persisted map snapshot must contain exactly 9 bounded map chunks.");
        }

        int expectedMapLength = checked(chunkSize * chunkSize);
        var coordinates = new HashSet<string>(StringComparer.Ordinal);
        foreach (PersistedMapChunkSnapshot mapChunk in MapChunks)
        {
            if (mapChunk is null)
            {
                throw new InvalidOperationException("L00-C persisted map snapshot contains a null map chunk copy.");
            }
            if (Math.Abs(mapChunk.X - fixtureChunkX) > 1 || Math.Abs(mapChunk.Z - fixtureChunkZ) > 1)
            {
                throw new InvalidOperationException($"L00-C persisted map snapshot coordinate ({mapChunk.X},{mapChunk.Z}) escapes the exact 3x3 footprint.");
            }
            if (mapChunk.WorldGenTerrainHeightMap is null || mapChunk.WorldGenTerrainHeightMap.Length != expectedMapLength ||
                mapChunk.RainHeightMap is null || mapChunk.RainHeightMap.Length != expectedMapLength ||
                mapChunk.TopRockIdMap is null || mapChunk.TopRockIdMap.Length != expectedMapLength)
            {
                throw new InvalidOperationException($"L00-C persisted map snapshot coordinate ({mapChunk.X},{mapChunk.Z}) has an invalid heightmap length.");
            }
            if (mapChunk.YMax >= worldHeight)
            {
                throw new InvalidOperationException($"L00-C persisted map snapshot coordinate ({mapChunk.X},{mapChunk.Z}) has invalid YMax={mapChunk.YMax}.");
            }

            string key = CoordinateKey(mapChunk.X, mapChunk.Z);
            if (!coordinates.Add(key))
            {
                throw new InvalidOperationException($"L00-C persisted map snapshot contains duplicate coordinate ({mapChunk.X},{mapChunk.Z}).");
            }
        }

        for (int deltaX = -1; deltaX <= 1; deltaX++)
        {
            for (int deltaZ = -1; deltaZ <= 1; deltaZ++)
            {
                string key = CoordinateKey(fixtureChunkX + deltaX, fixtureChunkZ + deltaZ);
                if (!coordinates.Contains(key))
                {
                    throw new InvalidOperationException($"L00-C persisted map snapshot is missing coordinate {key}.");
                }
            }
        }

        string computed = ComputeContentSha256();
        if (!string.Equals(ContentSha256, computed, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"L00-C persisted map snapshot checksum mismatch: stored={ContentSha256}, computed={computed}.");
        }
    }

    public IReadOnlyDictionary<string, PersistedMapChunkSnapshot> ValidateAndCopy(
        string markerId,
        string savegameIdentifier,
        int fixtureChunkX,
        int fixtureChunkZ,
        int chunkSize,
        int worldHeight)
    {
        ValidateForCopy(markerId, savegameIdentifier, fixtureChunkX, fixtureChunkZ, chunkSize, worldHeight);
        var copies = new Dictionary<string, PersistedMapChunkSnapshot>(StringComparer.Ordinal);
        foreach (PersistedMapChunkSnapshot mapChunk in MapChunks)
        {
            copies.Add(CoordinateKey(mapChunk.X, mapChunk.Z), mapChunk.DeepCopy());
        }
        return copies;
    }

    public PersistedMapFootprintSnapshot DeepCopy()
    {
        var mapChunkCopies = new List<PersistedMapChunkSnapshot>();
        foreach (PersistedMapChunkSnapshot mapChunk in MapChunks ?? [])
        {
            if (mapChunk is null)
            {
                throw new InvalidOperationException("L00-C persisted map snapshot contains a null map chunk copy.");
            }
            mapChunkCopies.Add(mapChunk.DeepCopy());
        }
        return new PersistedMapFootprintSnapshot
        {
            Version = Version,
            MarkerId = MarkerId,
            SavegameIdentifier = SavegameIdentifier,
            FixtureChunkX = FixtureChunkX,
            FixtureChunkZ = FixtureChunkZ,
            ChunkSize = ChunkSize,
            WorldHeight = WorldHeight,
            MapChunks = mapChunkCopies,
            ContentSha256 = ContentSha256
        };
    }

    public static string CoordinateKey(int x, int z) => $"{x},{z}";

    private string ComputeContentSha256()
    {
        var canonical = new StringBuilder();
        canonical.Append(Version).Append('|').Append(MarkerId).Append('|').Append(SavegameIdentifier).Append('|')
            .Append(FixtureChunkX).Append('|').Append(FixtureChunkZ).Append('|').Append(ChunkSize).Append('|').Append(WorldHeight).Append('|');
        foreach (PersistedMapChunkSnapshot mapChunk in (MapChunks ?? []).OrderBy(mapChunk => mapChunk.X).ThenBy(mapChunk => mapChunk.Z))
        {
            canonical.Append(mapChunk.X).Append(',').Append(mapChunk.Z).Append(',').Append(mapChunk.YMax).Append('|');
            Append(canonical, mapChunk.WorldGenTerrainHeightMap);
            Append(canonical, mapChunk.RainHeightMap);
            Append(canonical, mapChunk.TopRockIdMap);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Append<T>(StringBuilder canonical, IReadOnlyList<T>? values)
    {
        if (values is null)
        {
            canonical.Append("null|");
            return;
        }
        canonical.Append(values.Count).Append(':');
        for (int index = 0; index < values.Count; index++)
        {
            canonical.Append(values[index]).Append(',');
        }
        canonical.Append('|');
    }
}

internal sealed class PersistedMapChunkSnapshot
{
    public int X { get; set; }
    public int Z { get; set; }
    public ushort[] WorldGenTerrainHeightMap { get; set; } = [];
    public ushort[] RainHeightMap { get; set; } = [];
    public int[] TopRockIdMap { get; set; } = [];
    public ushort YMax { get; set; }

    public static PersistedMapChunkSnapshot Capture(int x, int z, IMapChunk mapChunk)
    {
        ArgumentNullException.ThrowIfNull(mapChunk);
        return new PersistedMapChunkSnapshot
        {
            X = x,
            Z = z,
            WorldGenTerrainHeightMap = (ushort[])mapChunk.WorldGenTerrainHeightMap.Clone(),
            RainHeightMap = (ushort[])mapChunk.RainHeightMap.Clone(),
            TopRockIdMap = (int[])mapChunk.TopRockIdMap.Clone(),
            YMax = mapChunk.YMax
        };
    }

    public PersistedMapChunkSnapshot DeepCopy()
    {
        return new PersistedMapChunkSnapshot
        {
            X = X,
            Z = Z,
            WorldGenTerrainHeightMap = WorldGenTerrainHeightMap is null ? [] : (ushort[])WorldGenTerrainHeightMap.Clone(),
            RainHeightMap = RainHeightMap is null ? [] : (ushort[])RainHeightMap.Clone(),
            TopRockIdMap = TopRockIdMap is null ? [] : (int[])TopRockIdMap.Clone(),
            YMax = YMax
        };
    }
}
#endif
