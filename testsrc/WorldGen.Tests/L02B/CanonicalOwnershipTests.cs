using ISRWorldGen.Core.Atlas.SpatialIndex;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L02B;

[TestClass]
public sealed class CanonicalOwnershipTests
{
    [TestMethod]
    public void CrossTileRiverAndCavern_HaveOneCanonicalOwnerInEveryOrderWorkerAndCacheMode()
    {
        AtlasIndexProfile profile = SpatialIndexTestSupport.FixtureProfile();
        SpatialPrimitiveDefinition[] linear = SpatialIndexTestSupport.CrossTileFixtures();
        AtlasIndexBuildOutcome baseline = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            profile,
            linear,
            new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold)));
        string expectedHash = baseline.Snapshot.Header.ContentChecksum.ToString();

        (SpatialPrimitiveDefinition[] Input, SpatialIndexBuildOptions Options)[] variants =
        [
            (linear.Reverse().ToArray(), new SpatialIndexBuildOptions(2, SpatialIndexCacheMode.Cold)),
            (linear.OrderBy(item => item.Id.Low ^ item.Id.High).ToArray(),
                new SpatialIndexBuildOptions(16, SpatialIndexCacheMode.Precomputed)),
            (linear, new SpatialIndexBuildOptions(2, SpatialIndexCacheMode.Precomputed)),
        ];

        foreach ((SpatialPrimitiveDefinition[] input, SpatialIndexBuildOptions options) in variants)
        {
            AtlasIndexBuildOutcome candidate = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
                SpatialIndexTestSupport.Identity(), profile, input, options));
            Assert.AreEqual(expectedHash, candidate.Snapshot.Header.ContentChecksum.ToString());
            AssertIndexContentsEqual(baseline.Snapshot.Index, candidate.Snapshot.Index);
        }
    }

    [TestMethod]
    public void FourLocalQueries_ReturnSameDescriptionsAndIdsWithoutDuplicatePlacement()
    {
        SpatialPrimitiveDefinition[] fixtures = SpatialIndexTestSupport.CrossTileFixtures();
        AtlasIndexBuildOutcome outcome = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            fixtures));
        CompactSpatialIndex index = outcome.Snapshot.Index;
        SpatialTileKey[] fourTiles =
        [
            new(0, 0),
            new(1, 0),
            new(0, 1),
            new(1, 1),
        ];

        IReadOnlyList<OwnedSpatialPrimitive>[] queries = fourTiles.Select(index.Query).ToArray();
        foreach (SpatialPrimitiveDefinition fixture in fixtures)
        {
            CollectionAssert.AreEquivalent(
                fourTiles,
                fixture.Points.Select(point => new SpatialTileKey(point.X / 512, point.Z / 512)).Distinct().ToArray(),
                $"Fixture {fixture.Id} must physically visit every queried tile.");
        }

        foreach (IReadOnlyList<OwnedSpatialPrimitive> query in queries)
        {
            Assert.HasCount(2, query);
            Assert.AreEqual(2, query.Select(item => item.Id).Distinct().Count());
            CollectionAssert.AreEqual(
                queries[0].Select(item => (item.Id, item.Description, item.OwnerId)).ToArray(),
                query.Select(item => (item.Id, item.Description, item.OwnerId)).ToArray());
        }

        foreach (OwnedSpatialPrimitive primitive in outcome.Snapshot.Index.Primitives)
        {
            Assert.AreEqual(new SpatialTileKey(0, 0), primitive.OwnerTile);
            Assert.AreEqual(index.GetOwnerId(primitive.Id, new SpatialTileKey(0, 0)), primitive.OwnerId);
        }

        OwnedSpatialPrimitive northeastRiver = queries[3].Single(item => item.Kind == SpatialPrimitiveKind.River);
        Assert.AreNotEqual(new SpatialTileKey(1, 1), northeastRiver.OwnerTile,
            "A query cannot steal ownership from the canonical tile.");
        Assert.AreEqual(8, index.PlacementReferenceCount);
    }

    [TestMethod]
    public void SemiOpenTileBordersAndFiniteWorld_DoNotWrapOrDoublePlace()
    {
        AtlasIndexProfile profile = SpatialIndexTestSupport.FixtureProfile();
        var border = new SpatialPrimitiveDefinition(
            StableId.Derive(RandomDomain.Hydrology, StableId.Zero, 99),
            SpatialPrimitiveKind.River,
            "boundary point belongs to east storage",
            [new SpatialPoint(512, 10), new SpatialPoint(512, 500)]);
        AtlasIndexBuildOutcome outcome = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, [border]));

        Assert.IsEmpty(outcome.Snapshot.Index.Query(new SpatialTileKey(0, 0)));
        Assert.HasCount(1, outcome.Snapshot.Index.Query(new SpatialTileKey(1, 0)));
        Assert.IsEmpty(outcome.Snapshot.Index.Query(new SpatialTileKey(-1, 0)));
        Assert.IsEmpty(outcome.Snapshot.Index.Query(new SpatialTileKey(2, 0)));
        Assert.IsEmpty(outcome.Snapshot.Index.Query(new SpatialTileKey(0, -1)));
        Assert.IsEmpty(outcome.Snapshot.Index.Query(new SpatialTileKey(0, 2)));
        Assert.AreEqual(new SpatialTileKey(1, 0), outcome.Snapshot.Index.Primitives.Single().OwnerTile);
    }

    [TestMethod]
    public void PublishedCollectionsAreDefensiveAndDuplicateIdsAreRejectedAtomically()
    {
        SpatialPoint[] mutablePoints = [new(100, 100), new(900, 900)];
        var primitive = new SpatialPrimitiveDefinition(
            StableId.Derive(RandomDomain.Hydrology, StableId.Zero, 123),
            SpatialPrimitiveKind.River,
            "immutable fixture",
            mutablePoints);
        mutablePoints[0] = new SpatialPoint(999, 999);
        AtlasIndexBuildOutcome outcome = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), SpatialIndexTestSupport.FixtureProfile(), [primitive]));
        Assert.AreEqual(new SpatialPoint(100, 100), outcome.Snapshot.Index.Primitives.Single().Points[0]);
        Assert.AreSame(primitive.Points, outcome.Snapshot.Index.Primitives.Single().Points,
            "The sealed immutable definition storage is safe to reuse without a second polyline allocation.");

        var conflicting = new SpatialPrimitiveDefinition(
            primitive.Id,
            SpatialPrimitiveKind.River,
            "conflicting duplicate",
            [new SpatialPoint(100, 900), new SpatialPoint(900, 100)]);
        GenerationResult<AtlasIndexBuildOutcome> failure = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            [primitive, conflicting]);
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(failure);
        Assert.AreEqual(GenerationFailureCode.InvalidInput, ((GenerationFailure<AtlasIndexBuildOutcome>)failure).Error.Code);
        Assert.AreEqual("atlas.spatial-index.validate", ((GenerationFailure<AtlasIndexBuildOutcome>)failure).Error.Stage);
    }

    private static void AssertIndexContentsEqual(CompactSpatialIndex expected, CompactSpatialIndex actual)
    {
        CollectionAssert.AreEqual(expected.Primitives.ToArray(), actual.Primitives.ToArray());
        Assert.AreEqual(expected.OccupiedTileCount, actual.OccupiedTileCount);
        Assert.AreEqual(expected.PlacementReferenceCount, actual.PlacementReferenceCount);
        for (long z = 0; z < 2; z++)
        {
            for (long x = 0; x < 2; x++)
            {
                CollectionAssert.AreEqual(
                    expected.Query(new SpatialTileKey(x, z)).ToArray(),
                    actual.Query(new SpatialTileKey(x, z)).ToArray());
            }
        }
    }
}
