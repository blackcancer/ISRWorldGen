#if DEBUG
using System.Text.Json;

namespace ISRWorldGen.WorldgenProbe;

internal static class ProbeMarkerEnvelopeReader
{
    public const string CurrentMarkerVersion = "l00c-flat-v2-map-snapshot";
    private const int ExactMapChunkCount = 9;
    private const int EnvelopeOverheadBytes = 8192;
    private const int MaximumSerializedValuesBytesPerCell = 24;
    private const int MaximumSupportedChunkSize = 64;

    public static int ComputeMaximumPayloadBytes(int chunkSize)
    {
        if (chunkSize <= 0 || chunkSize > MaximumSupportedChunkSize)
        {
            throw new InvalidOperationException($"L00-C marker chunk size {chunkSize} is outside the bounded envelope range.");
        }
        return checked(EnvelopeOverheadBytes + ExactMapChunkCount * chunkSize * chunkSize * MaximumSerializedValuesBytesPerCell);
    }

    public static ProbeMarker ReadAndValidate(
        byte[] payload,
        string expectedSavegameIdentifier,
        int expectedFixtureChunkX,
        int expectedFixtureChunkZ,
        int expectedChunkSize,
        int expectedWorldHeight)
    {
        ArgumentNullException.ThrowIfNull(payload);
        int maximumPayloadBytes = ComputeMaximumPayloadBytes(expectedChunkSize);
        if (payload.Length == 0 || payload.Length > maximumPayloadBytes)
        {
            throw new InvalidOperationException($"L00-C marker payload length {payload.Length} exceeds the bounded range 1..{maximumPayloadBytes} bytes.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                payload,
                new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 });
            ValidateEnvelopeStructure(
                document.RootElement,
                expectedSavegameIdentifier,
                expectedFixtureChunkX,
                expectedFixtureChunkZ,
                expectedChunkSize,
                expectedWorldHeight);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("L00-C marker payload is corrupt or truncated.", exception);
        }

        ProbeMarker marker;
        try
        {
            marker = JsonSerializer.Deserialize<ProbeMarker>(payload)
                ?? throw new InvalidOperationException("L00-C marker payload deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("L00-C marker payload is corrupt or truncated.", exception);
        }

        PersistedMapFootprintSnapshot mapFootprint = marker.MapFootprint
            ?? throw new InvalidOperationException("L00-C marker payload has no bounded map snapshot.");
        mapFootprint.ValidateForCopy(
            marker.MarkerId,
            expectedSavegameIdentifier,
            expectedFixtureChunkX,
            expectedFixtureChunkZ,
            expectedChunkSize,
            expectedWorldHeight);
        return marker;
    }

    private static void ValidateEnvelopeStructure(
        JsonElement root,
        string expectedSavegameIdentifier,
        int expectedFixtureChunkX,
        int expectedFixtureChunkZ,
        int expectedChunkSize,
        int expectedWorldHeight)
    {
        RequireExactProperties(root, "marker", "MarkerId", "SavegameIdentifier", "Version", "OpenCount", "MapFootprint");
        string markerId = RequireBoundedString(root, "MarkerId", 1, 64);
        string savegameIdentifier = RequireBoundedString(root, "SavegameIdentifier", 1, 128);
        string version = RequireBoundedString(root, "Version", CurrentMarkerVersion.Length, CurrentMarkerVersion.Length);
        if (version != CurrentMarkerVersion || savegameIdentifier != expectedSavegameIdentifier)
        {
            throw new InvalidOperationException("L00-C marker payload identity is incompatible with the current save.");
        }
        int openCount = RequireInt32(root, "OpenCount");
        if (openCount <= 0 || openCount > 1_000_000)
        {
            throw new InvalidOperationException($"L00-C marker OpenCount={openCount} is outside the bounded range.");
        }

        JsonElement footprint = RequireObject(root, "MapFootprint");
        RequireExactProperties(
            footprint,
            "map footprint",
            "Version",
            "MarkerId",
            "SavegameIdentifier",
            "FixtureChunkX",
            "FixtureChunkZ",
            "ChunkSize",
            "WorldHeight",
            "MapChunks",
            "ContentSha256");
        if (RequireBoundedString(footprint, "Version", PersistedMapFootprintSnapshot.CurrentVersion.Length, PersistedMapFootprintSnapshot.CurrentVersion.Length) != PersistedMapFootprintSnapshot.CurrentVersion ||
            RequireBoundedString(footprint, "MarkerId", 1, 64) != markerId ||
            RequireBoundedString(footprint, "SavegameIdentifier", 1, 128) != expectedSavegameIdentifier ||
            RequireInt32(footprint, "FixtureChunkX") != expectedFixtureChunkX ||
            RequireInt32(footprint, "FixtureChunkZ") != expectedFixtureChunkZ ||
            RequireInt32(footprint, "ChunkSize") != expectedChunkSize ||
            RequireInt32(footprint, "WorldHeight") != expectedWorldHeight)
        {
            throw new InvalidOperationException("L00-C map snapshot identity or geometry is inconsistent with the marker and current world.");
        }
        string checksum = RequireBoundedString(footprint, "ContentSha256", 64, 64);
        try
        {
            if (Convert.FromHexString(checksum).Length != 32)
            {
                throw new InvalidOperationException("L00-C map snapshot checksum is not SHA-256.");
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("L00-C map snapshot checksum is not SHA-256.", exception);
        }

        JsonElement mapChunks = RequireArray(footprint, "MapChunks");
        if (mapChunks.GetArrayLength() != ExactMapChunkCount)
        {
            throw new InvalidOperationException("L00-C map snapshot must contain exactly 9 map chunks before deserialization.");
        }
        int expectedMapLength = checked(expectedChunkSize * expectedChunkSize);
        var coordinates = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement mapChunk in mapChunks.EnumerateArray())
        {
            RequireExactProperties(
                mapChunk,
                "map chunk",
                "X",
                "Z",
                "WorldGenTerrainHeightMap",
                "RainHeightMap",
                "TopRockIdMap",
                "YMax");
            int x = RequireInt32(mapChunk, "X");
            int z = RequireInt32(mapChunk, "Z");
            if (Math.Abs(x - expectedFixtureChunkX) > 1 || Math.Abs(z - expectedFixtureChunkZ) > 1 ||
                !coordinates.Add(PersistedMapFootprintSnapshot.CoordinateKey(x, z)))
            {
                throw new InvalidOperationException($"L00-C map snapshot coordinate ({x},{z}) is duplicate or outside the exact 3x3 footprint.");
            }
            ValidateUInt16Array(mapChunk, "WorldGenTerrainHeightMap", expectedMapLength);
            ValidateUInt16Array(mapChunk, "RainHeightMap", expectedMapLength);
            ValidateInt32Array(mapChunk, "TopRockIdMap", expectedMapLength);
            int yMax = RequireInt32(mapChunk, "YMax");
            if (yMax < 0 || yMax >= expectedWorldHeight || yMax > ushort.MaxValue)
            {
                throw new InvalidOperationException($"L00-C map snapshot YMax={yMax} is outside the current world.");
            }
        }
    }

    private static void RequireExactProperties(JsonElement element, string label, params string[] expectedNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"L00-C {label} must be a JSON object.");
        }
        var missing = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        int count = 0;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            count++;
            if (!missing.Remove(property.Name))
            {
                throw new InvalidOperationException($"L00-C {label} contains an unknown or duplicate property '{property.Name}'.");
            }
        }
        if (missing.Count != 0 || count != expectedNames.Length)
        {
            throw new InvalidOperationException($"L00-C {label} property set is incomplete.");
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string propertyName)
    {
        JsonElement value = parent.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"L00-C marker property {propertyName} must be an object.");
        }
        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string propertyName)
    {
        JsonElement value = parent.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"L00-C marker property {propertyName} must be an array.");
        }
        return value;
    }

    private static string RequireBoundedString(JsonElement parent, string propertyName, int minimumLength, int maximumLength)
    {
        JsonElement value = parent.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"L00-C marker property {propertyName} must be a string.");
        }
        string text = value.GetString() ?? string.Empty;
        if (text.Length < minimumLength || text.Length > maximumLength)
        {
            throw new InvalidOperationException($"L00-C marker property {propertyName} length is outside {minimumLength}..{maximumLength}.");
        }
        return text;
    }

    private static int RequireInt32(JsonElement parent, string propertyName)
    {
        JsonElement value = parent.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new InvalidOperationException($"L00-C marker property {propertyName} must be an Int32.");
        }
        return result;
    }

    private static void ValidateUInt16Array(JsonElement parent, string propertyName, int expectedLength)
    {
        JsonElement values = RequireArray(parent, propertyName);
        if (values.GetArrayLength() != expectedLength)
        {
            throw new InvalidOperationException($"L00-C marker array {propertyName} has invalid length {values.GetArrayLength()}, expected {expectedLength}.");
        }
        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt16(out _))
            {
                throw new InvalidOperationException($"L00-C marker array {propertyName} contains a non-UInt16 value.");
            }
        }
    }

    private static void ValidateInt32Array(JsonElement parent, string propertyName, int expectedLength)
    {
        JsonElement values = RequireArray(parent, propertyName);
        if (values.GetArrayLength() != expectedLength)
        {
            throw new InvalidOperationException($"L00-C marker array {propertyName} has invalid length {values.GetArrayLength()}, expected {expectedLength}.");
        }
        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out _))
            {
                throw new InvalidOperationException($"L00-C marker array {propertyName} contains a non-Int32 value.");
            }
        }
    }
}
#endif
