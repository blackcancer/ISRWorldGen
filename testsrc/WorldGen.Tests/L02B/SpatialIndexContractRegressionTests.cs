using System.Collections;
using ISRWorldGen.Core.Atlas.SpatialIndex;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L02B;

[TestClass]
public sealed class SpatialIndexContractRegressionTests
{
    [TestMethod]
    public void PublishedStableIds_AreGloballyUniqueAcrossSitesPrimitivesAndOwners()
    {
        AtlasIndexBuildOutcome outcome = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            SpatialIndexTestSupport.CrossTileFixtures()));

        StableId[] publishedIds = outcome.Snapshot.Graph.Sites.Select(site => site.Id)
            .Concat(outcome.Snapshot.Index.Primitives.Select(primitive => primitive.Id))
            .Concat(outcome.Snapshot.Index.Primitives.Select(primitive => primitive.OwnerId))
            .ToArray();

        Assert.AreEqual(publishedIds.Length, publishedIds.Distinct().Count(),
            "Every site, primitive and ownership decision requires one globally unique published StableId.");
    }

    [TestMethod]
    public void PrimitiveCollidingWithGeneratedSite_FailsTypedWithoutSnapshot()
    {
        StableId generatedSiteId = StableId.Derive(RandomDomain.Sites, StableId.Zero, 0);
        var collision = new SpatialPrimitiveDefinition(
            generatedSiteId,
            SpatialPrimitiveKind.River,
            "collides with generated site zero",
            [new SpatialPoint(100, 100), new SpatialPoint(900, 900)]);

        AssertStableIdCollision(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            [collision]));
    }

    [TestMethod]
    public void PrimitiveCollidingWithExistingOwner_FailsTypedWithoutSnapshot()
    {
        SpatialPrimitiveDefinition original = SpatialIndexTestSupport.CrossTileFixtures()[0];
        AtlasIndexBuildOutcome baseline = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            [original]));
        StableId existingOwnerId = baseline.Snapshot.Index.Primitives.Single().OwnerId;
        var collision = new SpatialPrimitiveDefinition(
            existingOwnerId,
            SpatialPrimitiveKind.Cavern,
            "collides with the first primitive ownership decision",
            [new SpatialPoint(100, 900), new SpatialPoint(900, 100)]);

        AssertStableIdCollision(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            [original, collision]));
    }

    [TestMethod]
    public void EstimateAndBuild_EnumerateCallerCollectionOnlyOnceEach()
    {
        SpatialPrimitiveDefinition[] fixtures = SpatialIndexTestSupport.CrossTileFixtures();
        var estimateInput = new SingleEnumerationPrimitiveList(fixtures);
        GenerationResult<AtlasMemoryEstimate> estimate = AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            estimateInput);

        Assert.IsInstanceOfType<GenerationSuccess<AtlasMemoryEstimate>>(estimate);
        Assert.AreEqual(1, estimateInput.EnumerationCount);

        var buildInput = new SingleEnumerationPrimitiveList(fixtures);
        AtlasIndexBuildOutcome outcome = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            buildInput));
        Assert.AreEqual(1, buildInput.EnumerationCount);
        Assert.HasCount(fixtures.Length, outcome.Snapshot.Index.Primitives);
    }

    [TestMethod]
    public void AntiDiagonalOwner_IsMinimumPhysicallyTouchedTileNotBoundingBoxMinimum()
    {
        var antiDiagonal = new SpatialPrimitiveDefinition(
            StableId.Derive(RandomDomain.Hydrology, StableId.Zero, 989),
            SpatialPrimitiveKind.River,
            "anti-diagonal misses the south-west bbox tile",
            [new SpatialPoint(100, 900), new SpatialPoint(900, 400)]);

        AtlasIndexBuildOutcome outcome = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            [antiDiagonal]));
        OwnedSpatialPrimitive published = outcome.Snapshot.Index.Primitives.Single();

        Assert.AreEqual(new SpatialTileKey(1, 0), published.OwnerTile);
        Assert.AreNotEqual(new SpatialTileKey(0, 0), published.OwnerTile,
            "The bbox minimum is only a candidate and is not physically touched by this line.");
        Assert.AreEqual(4, outcome.Snapshot.Index.PlacementReferenceCount,
            "The bbox may remain a conservative four-tile candidate index.");
    }

    [TestMethod]
    [DataRow(512L, 10L, 512L, 500L, 1L, 0L, DisplayName = "vertical east-owned border")]
    [DataRow(10L, 512L, 500L, 512L, 0L, 1L, DisplayName = "horizontal north-owned border")]
    [DataRow(512L, 512L, 900L, 900L, 1L, 1L, DisplayName = "corner starts in north-east")]
    public void BorderAndCornerOwnership_UsesSemiOpenTileGeometry(
        long firstX,
        long firstZ,
        long secondX,
        long secondZ,
        long expectedTileX,
        long expectedTileZ)
    {
        var primitive = new SpatialPrimitiveDefinition(
            StableId.Derive(RandomDomain.Hydrology, StableId.Zero, checked((ulong)(firstX + firstZ + secondX + secondZ))),
            SpatialPrimitiveKind.River,
            "semi-open owner fixture",
            [new SpatialPoint(firstX, firstZ), new SpatialPoint(secondX, secondZ)]);

        AtlasIndexBuildOutcome outcome = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            [primitive]));

        Assert.AreEqual(
            new SpatialTileKey(expectedTileX, expectedTileZ),
            outcome.Snapshot.Index.Primitives.Single().OwnerTile);
    }

    [TestMethod]
    public void WorldVolumeOverflow_FailsTypedBeforeAtlasConstruction()
    {
        var profile = new AtlasIndexProfile(
            new WorldDomain(
                new Int64Interval(0, long.MaxValue / 2),
                new Int64Interval(0, 2),
                3,
                new WorldBlockPosition(0, 0)),
            new ScaleModel("unit", 1d, "block", 1d),
            tileSize: int.MaxValue,
            requestedSiteCount: 1,
            siteQuota: 1,
            memoryBudgetBytes: 1024 * 1024);

        AssertEstimateDimensionFailure(AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, []));
        AssertDimensionFailure(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, []));
    }

    [TestMethod]
    public void IntervalSpanOverflow_FailsTypedInsteadOfEscapingProfileConstruction()
    {
        var profile = new AtlasIndexProfile(
            new WorldDomain(
                new Int64Interval(long.MinValue, long.MaxValue),
                new Int64Interval(0, 1),
                1,
                new WorldBlockPosition(0, 0)),
            new ScaleModel("unit", 1d, "block", 1d),
            tileSize: int.MaxValue,
            requestedSiteCount: 1,
            siteQuota: 1,
            memoryBudgetBytes: 1024 * 1024);

        AssertEstimateDimensionFailure(AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, []));
        AssertDimensionFailure(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, []));
    }

    [TestMethod]
    [DoNotParallelize]
    public void TwoHundredThousandPointInput_WithOneMiBBudgetRefusesBeforeMassiveCaptureAllocation()
    {
        SpatialPrimitiveDefinition primitive = SpatialIndexTestSupport.LargeInputFixture();
        AtlasIndexProfile profile = SpatialIndexTestSupport.FixtureProfile(memoryBudgetBytes: 1024 * 1024);

        long before = GC.GetAllocatedBytesForCurrentThread();
        GenerationResult<AtlasIndexBuildOutcome> result = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, [primitive]);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(result);
        GenerationError error = ((GenerationFailure<AtlasIndexBuildOutcome>)result).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, error.Code);
        Assert.AreEqual("atlas.spatial-index.budget", error.Stage);
        Assert.IsLessThan(256 * 1024L, allocated,
            "The refusal path must not clone or hash the 200,000-point polyline.");
        GC.KeepAlive(primitive);
    }

    [TestMethod]
    public void CaptureArrayExceedingBudget_IsRejectedBeforeCallerEnumeration()
    {
        var input = new OversizedReportedCountPrimitiveList(int.MaxValue);

        GenerationResult<AtlasIndexBuildOutcome> result = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            input);

        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(result);
        GenerationError error = ((GenerationFailure<AtlasIndexBuildOutcome>)result).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, error.Code);
        Assert.AreEqual("atlas.spatial-index.capture-budget", error.Stage);
        Assert.AreEqual(0, input.EnumerationCount);
    }

    [TestMethod]
    [DoNotParallelize]
    public void RuntimeMaximumPrimitiveCount_WithLongMaxBudgetFailsTypedBeforeEnumerationAndAllocation()
    {
        var input = new OversizedReportedCountPrimitiveList(int.MaxValue);
        AtlasIndexProfile fixture = SpatialIndexTestSupport.FixtureProfile();
        var profile = new AtlasIndexProfile(
            fixture.Domain,
            fixture.Scale,
            tileSize: 512,
            requestedSiteCount: 1,
            siteQuota: 1,
            memoryBudgetBytes: long.MaxValue);

        long estimateBefore = GC.GetAllocatedBytesForCurrentThread();
        GenerationResult<AtlasMemoryEstimate> estimateResult = AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, input);
        long estimateAllocation = GC.GetAllocatedBytesForCurrentThread() - estimateBefore;
        long buildBefore = GC.GetAllocatedBytesForCurrentThread();
        GenerationResult<AtlasIndexBuildOutcome> buildResult = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, input);
        long buildAllocation = GC.GetAllocatedBytesForCurrentThread() - buildBefore;

        AssertArrayCapacityFailure(estimateResult);
        AssertArrayCapacityFailure(buildResult);
        Assert.AreEqual(0, input.EnumerationCount);
        Assert.IsLessThan(64 * 1024L, estimateAllocation);
        Assert.IsLessThan(64 * 1024L, buildAllocation);
    }

    [TestMethod]
    public void SiteTopologyBeyondArrayCapacity_FailsBeforePrimitiveEnumeration()
    {
        var input = new OversizedReportedCountPrimitiveList(0);
        AtlasIndexProfile fixture = SpatialIndexTestSupport.FixtureProfile();
        var profile = new AtlasIndexProfile(
            fixture.Domain,
            fixture.Scale,
            tileSize: 512,
            requestedSiteCount: Array.MaxLength,
            siteQuota: Array.MaxLength,
            memoryBudgetBytes: long.MaxValue);

        AssertArrayCapacityFailure(AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, input));
        AssertArrayCapacityFailure(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, input));
        Assert.AreEqual(0, input.EnumerationCount);
    }

    [TestMethod]
    public void PlacementCsrRequiringArrayMaxLengthEntries_FailsBeforeAtlasConstruction()
    {
        var profile = new AtlasIndexProfile(
            new WorldDomain(
                new Int64Interval(0, Array.MaxLength),
                new Int64Interval(0, 1),
                1,
                new WorldBlockPosition(0, 0)),
            new ScaleModel("unit", 1d, "block", 1d),
            tileSize: 1,
            requestedSiteCount: 1,
            siteQuota: 1,
            memoryBudgetBytes: long.MaxValue);
        var primitive = new SpatialPrimitiveDefinition(
            new StableId(1, 1),
            SpatialPrimitiveKind.River,
            "array capacity",
            [new SpatialPoint(0, 0), new SpatialPoint(Array.MaxLength - 1L, 0)]);

        AssertIndexCapacityFailure(AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, [primitive]));
        AssertIndexCapacityFailure(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, [primitive]));
    }

    [TestMethod]
    [DoNotParallelize]
    public void FiftyThousandPrecomputedSites_WithLongMaxBudgetFailsTypedBeforeGeometryAllocation()
    {
        AtlasIndexProfile profile = SpatialIndexTestSupport.VastProfile(
            memoryBudgetBytes: long.MaxValue,
            requestedSites: 50_000,
            siteQuota: 50_000);
        var input = new OversizedReportedCountPrimitiveList(0);
        var options = new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Precomputed);

        long estimateBefore = GC.GetAllocatedBytesForCurrentThread();
        GenerationResult<AtlasMemoryEstimate> estimateResult = AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, input, options);
        long estimateAllocation = GC.GetAllocatedBytesForCurrentThread() - estimateBefore;
        long buildBefore = GC.GetAllocatedBytesForCurrentThread();
        GenerationResult<AtlasIndexBuildOutcome> buildResult = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, input, options);
        long buildAllocation = GC.GetAllocatedBytesForCurrentThread() - buildBefore;

        AssertArrayCapacityFailure(estimateResult);
        AssertArrayCapacityFailure(buildResult);
        Assert.AreEqual(0, input.EnumerationCount);
        Assert.IsLessThan(64 * 1024L, estimateAllocation);
        Assert.IsLessThan(64 * 1024L, buildAllocation);
    }

    [TestMethod]
    [DoNotParallelize]
    public void FiftyThousandColdSites_WithLongMaxBudgetFailsTypedBeforeQuadraticWork()
    {
        AtlasIndexProfile profile = SpatialIndexTestSupport.VastProfile(
            memoryBudgetBytes: long.MaxValue,
            requestedSites: 50_000,
            siteQuota: 50_000);
        var input = new OversizedReportedCountPrimitiveList(0);
        var options = new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold);

        AssertGeometryWorkCapacityFailure(AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, input, options));
        AssertGeometryWorkCapacityFailure(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, input, options));
        Assert.AreEqual(0, input.EnumerationCount);
    }

    [TestMethod]
    public void GeometryCacheMode_ChangesPlanButNotCanonicalContent()
    {
        AtlasIndexProfile profile = SpatialIndexTestSupport.VastProfile();
        SpatialPrimitiveDefinition[] primitives = SpatialIndexTestSupport.CrossTileFixtures();
        var coldOptions = new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold);
        var precomputedOptions = new SpatialIndexBuildOptions(2, SpatialIndexCacheMode.Precomputed);

        AtlasMemoryEstimate coldEstimate = ((GenerationSuccess<AtlasMemoryEstimate>)AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, primitives, coldOptions)).Snapshot;
        AtlasMemoryEstimate precomputedEstimate =
            ((GenerationSuccess<AtlasMemoryEstimate>)AtlasSpatialIndexPlanner.Estimate(
                SpatialIndexTestSupport.Identity(), profile, primitives, precomputedOptions)).Snapshot;
        AtlasIndexBuildOutcome cold = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, primitives, coldOptions));
        AtlasIndexBuildOutcome precomputed = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, primitives, precomputedOptions));

        Assert.AreEqual(0, coldEstimate.EstimatedGeometryCacheBytes);
        Assert.IsGreaterThan(0, precomputedEstimate.EstimatedGeometryCacheBytes);
        Assert.AreEqual(
            coldEstimate.EstimatedPeakBuildBytes + precomputedEstimate.EstimatedGeometryCacheBytes,
            precomputedEstimate.EstimatedPeakBuildBytes);
        Assert.AreEqual(cold.Snapshot.Header.ContentChecksum, precomputed.Snapshot.Header.ContentChecksum);
    }

    [TestMethod]
    [DoNotParallelize]
    public void FiveHundredThousandPrimitives_With24MiBBudgetAvoidPerIdAllocationBeforeRefusal()
    {
        SpatialPrimitiveDefinition[] primitives = SpatialIndexTestSupport.ManyPrimitiveFixtures();
        var profile = new AtlasIndexProfile(
            SpatialIndexTestSupport.FixtureProfile().Domain,
            SpatialIndexTestSupport.FixtureProfile().Scale,
            tileSize: 512,
            requestedSiteCount: 1,
            siteQuota: 1,
            memoryBudgetBytes: 24L * 1024 * 1024);

        long estimateBefore = GC.GetAllocatedBytesForCurrentThread();
        GenerationResult<AtlasMemoryEstimate> estimateResult = AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, primitives);
        long estimateAllocation = GC.GetAllocatedBytesForCurrentThread() - estimateBefore;
        Assert.IsInstanceOfType<GenerationSuccess<AtlasMemoryEstimate>>(estimateResult);
        AtlasMemoryEstimate estimate = ((GenerationSuccess<AtlasMemoryEstimate>)estimateResult).Snapshot;
        Assert.IsGreaterThan(profile.MemoryBudgetBytes, estimate.EstimatedPeakBuildBytes);
        Assert.AreEqual(500_000, estimate.PrimitiveCount);
        Assert.AreEqual(24 + (500_000L * IntPtr.Size) + 32, estimate.EstimatedCanonicalCaptureBytes);

        long buildBefore = GC.GetAllocatedBytesForCurrentThread();
        GenerationResult<AtlasIndexBuildOutcome> buildResult = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), profile, primitives);
        long buildAllocation = GC.GetAllocatedBytesForCurrentThread() - buildBefore;
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(buildResult);
        GenerationError error = ((GenerationFailure<AtlasIndexBuildOutcome>)buildResult).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, error.Code);
        Assert.AreEqual("atlas.spatial-index.budget", error.Stage);
        Assert.IsLessThan(8L * 1024 * 1024, estimateAllocation,
            "Estimate must only allocate the bounded canonical reference array, not a per-ID HashSet.");
        Assert.IsLessThan(8L * 1024 * 1024, buildAllocation,
            "Build must reject after the plan without allocating a per-ID HashSet.");
        GC.KeepAlive(primitives);
    }

    private static void AssertStableIdCollision(GenerationResult<AtlasIndexBuildOutcome> result)
    {
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(result);
        GenerationError error = ((GenerationFailure<AtlasIndexBuildOutcome>)result).Error;
        Assert.AreEqual(GenerationFailureCode.CorruptData, error.Code);
        Assert.AreEqual("atlas.spatial-index.stable-id", error.Stage);
        StringAssert.Contains(error.Details, "StableId");
    }

    private static void AssertArrayCapacityFailure<T>(GenerationResult<T> result)
        where T : class
    {
        Assert.IsInstanceOfType<GenerationFailure<T>>(result);
        GenerationError error = ((GenerationFailure<T>)result).Error;
        Assert.AreEqual(GenerationFailureCode.InvalidInput, error.Code);
        Assert.AreEqual("atlas.spatial-index.array-capacity", error.Stage);
    }

    private static void AssertIndexCapacityFailure<T>(GenerationResult<T> result)
        where T : class
    {
        Assert.IsInstanceOfType<GenerationFailure<T>>(result);
        GenerationError error = ((GenerationFailure<T>)result).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, error.Code);
        Assert.AreEqual("atlas.spatial-index.index-capacity", error.Stage);
    }

    private static void AssertGeometryWorkCapacityFailure<T>(GenerationResult<T> result)
        where T : class
    {
        Assert.IsInstanceOfType<GenerationFailure<T>>(result);
        GenerationError error = ((GenerationFailure<T>)result).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, error.Code);
        Assert.AreEqual("atlas.spatial-index.geometry-work-capacity", error.Stage);
    }

    private static void AssertDimensionFailure(GenerationResult<AtlasIndexBuildOutcome> result)
    {
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(result);
        GenerationError error = ((GenerationFailure<AtlasIndexBuildOutcome>)result).Error;
        Assert.AreEqual(GenerationFailureCode.InvalidInput, error.Code);
        Assert.AreEqual("atlas.spatial-index.dimensions", error.Stage);
    }

    private static void AssertEstimateDimensionFailure(GenerationResult<AtlasMemoryEstimate> result)
    {
        Assert.IsInstanceOfType<GenerationFailure<AtlasMemoryEstimate>>(result);
        GenerationError error = ((GenerationFailure<AtlasMemoryEstimate>)result).Error;
        Assert.AreEqual(GenerationFailureCode.InvalidInput, error.Code);
        Assert.AreEqual("atlas.spatial-index.dimensions", error.Stage);
    }

    private sealed class SingleEnumerationPrimitiveList : IReadOnlyList<SpatialPrimitiveDefinition>
    {
        private readonly SpatialPrimitiveDefinition[] items;

        internal SingleEnumerationPrimitiveList(IEnumerable<SpatialPrimitiveDefinition> items) =>
            this.items = items.ToArray();

        public int Count => items.Length;

        public SpatialPrimitiveDefinition this[int index] => items[index];

        public int EnumerationCount { get; private set; }

        public IEnumerator<SpatialPrimitiveDefinition> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount != 1)
            {
                throw new InvalidOperationException("Caller-owned primitive collection was enumerated more than once.");
            }

            return ((IEnumerable<SpatialPrimitiveDefinition>)items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class OversizedReportedCountPrimitiveList : IReadOnlyList<SpatialPrimitiveDefinition>
    {
        internal OversizedReportedCountPrimitiveList(int reportedCount) => Count = reportedCount;

        public int Count { get; }

        public SpatialPrimitiveDefinition this[int index] => throw new NotSupportedException();

        public int EnumerationCount { get; private set; }

        public IEnumerator<SpatialPrimitiveDefinition> GetEnumerator()
        {
            EnumerationCount++;
            throw new InvalidOperationException("Oversized input must be rejected before enumeration.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
