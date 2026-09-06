using ISRWorldGen.Core.Atlas.SpatialIndex;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L02B;

internal static class SpatialIndexTestSupport
{
    internal const string ConfigHash = "9e12605ff5e0e94ccccf28eac0ded526da1e687a1fd3a0650b782da7484906ed";
    internal const string AssetHash = "cfd370ef8c6540792e17507897d0db5a9d96b621a805d1ccc605abcf89460ddf";
    internal const int Seed = -437287116;

    internal static GenerationIdentity Identity(int seed = Seed) => new(
        seed,
        GenerationIdentity.SupportedAlgorithmVersion,
        GenerationIdentity.SupportedSchemaVersion,
        Hash256.Parse(ConfigHash),
        Hash256.Parse(AssetHash),
        "l02b-budget-candidate-sparse-v1");

    internal static AtlasIndexProfile VastProfile(
        long memoryBudgetBytes = 256L * 1024 * 1024,
        int requestedSites = 56,
        int siteQuota = 8_192) => new(
        new WorldDomain(
            new Int64Interval(0, 1_024_000),
            new Int64Interval(0, 1_024_000),
            384,
            new WorldBlockPosition(0, 0)),
        new ScaleModel("atlas-kilometre", 1_024d, "block", 1d),
        tileSize: 16_384,
        requestedSiteCount: requestedSites,
        siteQuota,
        memoryBudgetBytes);

    internal static AtlasIndexProfile FixtureProfile(long memoryBudgetBytes = 128 * 1024 * 1024) => new(
        new WorldDomain(
            new Int64Interval(0, 1_024),
            new Int64Interval(0, 1_024),
            384,
            new WorldBlockPosition(0, 0)),
        new ScaleModel("atlas-kilometre", 1_024d, "block", 1d),
        tileSize: 512,
        requestedSiteCount: 32,
        siteQuota: 64,
        memoryBudgetBytes);

    internal static SpatialPrimitiveDefinition[] CrossTileFixtures()
    {
        StableId riverId = StableId.Derive(RandomDomain.Hydrology, StableId.Zero, 7);
        StableId cavernId = StableId.Derive(RandomDomain.Caverns, StableId.Zero, 7);
        return
        [
            new SpatialPrimitiveDefinition(
                riverId,
                SpatialPrimitiveKind.River,
                "F07 river: shared level and discharge",
                [new SpatialPoint(100, 100), new SpatialPoint(900, 100),
                 new SpatialPoint(900, 900), new SpatialPoint(100, 900)]),
            new SpatialPrimitiveDefinition(
                cavernId,
                SpatialPrimitiveKind.Cavern,
                "F07 cavern: one connected cross-region decision",
                [new SpatialPoint(900, 900), new SpatialPoint(100, 900),
                 new SpatialPoint(100, 100), new SpatialPoint(900, 100)]),
        ];
    }

    internal static SpatialPrimitiveDefinition LargeInputFixture(int pointCount = 200_000)
    {
        SpatialPoint[] points = Enumerable.Range(0, pointCount)
            .Select(index => new SpatialPoint(index % 1_024, index / 1_024))
            .ToArray();
        return new SpatialPrimitiveDefinition(
            StableId.Derive(RandomDomain.Hydrology, StableId.Zero, checked((ulong)pointCount)),
            SpatialPrimitiveKind.River,
            "large immutable input must be budget-gated before capture",
            points);
    }

    internal static SpatialPrimitiveDefinition[] ManyPrimitiveFixtures(int primitiveCount = 500_000)
    {
        var primitives = new SpatialPrimitiveDefinition[primitiveCount];
        for (int index = 0; index < primitiveCount; index++)
        {
            primitives[index] = new SpatialPrimitiveDefinition(
                new StableId(1, checked((ulong)index + 1)),
                SpatialPrimitiveKind.River,
                "p",
                [new SpatialPoint(0, 0), new SpatialPoint(1, 1)]);
        }

        return primitives;
    }

    internal static AtlasIndexBuildOutcome Success(GenerationResult<AtlasIndexBuildOutcome> result)
    {
        Assert.IsInstanceOfType<GenerationSuccess<AtlasIndexBuildOutcome>>(result);
        return ((GenerationSuccess<AtlasIndexBuildOutcome>)result).Snapshot;
    }
}
