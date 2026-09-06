using ISRWorldGen.Core.Atlas.SpatialIndex;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L02B;

[TestClass]
public sealed class VastAtlasBudgetTests
{
    [TestMethod]
    public void MillionBlockProfile_PlansCompactStorageIndependentOfWorldColumns()
    {
        AtlasIndexProfile profile = SpatialIndexTestSupport.VastProfile();
        SpatialPrimitiveDefinition[] fixtures = SpatialIndexTestSupport.CrossTileFixtures();

        AtlasMemoryEstimate estimate = Success(AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, fixtures));

        Assert.AreEqual(1_048_576_000_000L, estimate.WorldColumnCount);
        Assert.AreEqual(56, estimate.SiteCount);
        Assert.AreEqual(2, estimate.PlacementReferenceCount);
        Assert.IsGreaterThan(0L, estimate.EstimatedSiteBytes);
        Assert.IsGreaterThan(0L, estimate.EstimatedCompactGraphBytes);
        Assert.IsGreaterThan(0L, estimate.EstimatedSpatialIndexBytes);
        Assert.IsGreaterThan(0L, estimate.EstimatedGeometryWorkingBytes);
        Assert.IsLessThan(4 * 1024 * 1024L, estimate.EstimatedSnapshotBytes);
        Assert.IsLessThanOrEqualTo(profile.MemoryBudgetBytes, estimate.EstimatedPeakBuildBytes);
        Assert.IsLessThan(estimate.WorldColumnCount, estimate.SiteCount);

        var smallDomainProfile = new AtlasIndexProfile(
            new WorldDomain(
                new Int64Interval(0, 1_024),
                new Int64Interval(0, 1_024),
                384,
                new WorldBlockPosition(0, 0)),
            profile.Scale,
            tileSize: 256,
            requestedSiteCount: 56,
            siteQuota: 8_192,
            memoryBudgetBytes: profile.MemoryBudgetBytes);
        AtlasMemoryEstimate smallEmpty = Success(AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), smallDomainProfile, []));
        AtlasMemoryEstimate vastEmpty = Success(AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), profile, []));
        Assert.AreEqual(smallEmpty.EstimatedSiteBytes, vastEmpty.EstimatedSiteBytes);
        Assert.AreEqual(smallEmpty.EstimatedCompactGraphBytes, vastEmpty.EstimatedCompactGraphBytes);
        Assert.AreEqual(smallEmpty.EstimatedSpatialIndexBytes, vastEmpty.EstimatedSpatialIndexBytes);
        Assert.AreEqual(smallEmpty.EstimatedSnapshotBytes, vastEmpty.EstimatedSnapshotBytes);
    }

    [TestMethod]
    public void InsufficientBudgetAndExceededQuota_FailBeforeLargeAllocationWithoutSnapshot()
    {
        SpatialPrimitiveDefinition[] fixtures = SpatialIndexTestSupport.CrossTileFixtures();
        AtlasIndexProfile reference = SpatialIndexTestSupport.VastProfile();
        AtlasMemoryEstimate estimate = Success(AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), reference, fixtures));
        AtlasIndexProfile underBudget = SpatialIndexTestSupport.VastProfile(
            memoryBudgetBytes: estimate.EstimatedPeakBuildBytes - 1);
        AtlasIndexProfile overQuota = SpatialIndexTestSupport.VastProfile(
            requestedSites: 8_193,
            siteQuota: 8_192);
        AtlasIndexProfile rejectedDenseProbe = SpatialIndexTestSupport.VastProfile(
            memoryBudgetBytes: 512L * 1024 * 1024,
            requestedSites: 256,
            siteQuota: 8_192);
        AtlasMemoryEstimate denseEstimate = Success(AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), rejectedDenseProbe, fixtures));
        Assert.IsGreaterThan(rejectedDenseProbe.MemoryBudgetBytes, denseEstimate.EstimatedPeakBuildBytes);

        long before = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasIndexBuildOutcome> budgetFailure = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), underBudget, fixtures);
        long budgetFailureAllocation = GC.GetTotalAllocatedBytes(precise: true) - before;
        GenerationResult<AtlasIndexBuildOutcome> quotaFailure = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), overQuota, fixtures);
        long denseBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasIndexBuildOutcome> denseFailure = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), rejectedDenseProbe, fixtures);
        long denseRefusalAllocation = GC.GetTotalAllocatedBytes(precise: true) - denseBefore;

        AssertTypedBudgetFailure(budgetFailure, "atlas.spatial-index.budget");
        AssertTypedBudgetFailure(quotaFailure, "atlas.spatial-index.quota");
        AssertTypedBudgetFailure(denseFailure, "atlas.spatial-index.budget");
        Assert.IsLessThan(64 * 1024L, budgetFailureAllocation, "Budget refusal must precede atlas allocation.");
        Assert.IsLessThan(64 * 1024L, denseRefusalAllocation, "The rejected 256-site probe must not invoke L02-A.");
    }

    [TestMethod]
    public void VastAtlas_ReportsPlanCumulativeAllocationLiveDeltaAndGcSeparately()
    {
        AtlasIndexProfile profile = SpatialIndexTestSupport.VastProfile();
        SpatialPrimitiveDefinition[] fixtures = SpatialIndexTestSupport.CrossTileFixtures();

        _ = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            profile,
            fixtures,
            new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold)));
        Measurement[] measurements = Enumerable.Range(0, 2)
            .Select(_ => Measure(profile, fixtures))
            .ToArray();

        foreach (Measurement measurement in measurements)
        {
            Assert.AreEqual(56, measurement.Outcome.Snapshot.Graph.SiteCount);
            Assert.AreEqual(2, measurement.Outcome.Snapshot.Index.PrimitiveCount);
            Assert.AreEqual(1, measurement.Outcome.Snapshot.Index.OccupiedTileCount);
            Assert.AreEqual(2, measurement.Outcome.Snapshot.Index.PlacementReferenceCount);
            Assert.IsGreaterThan(0L, measurement.CumulativeAllocatedBytes);
        }

        Assert.AreEqual(
            1,
            measurements.Select(item => item.Outcome.Snapshot.Header.ContentChecksum).Distinct().Count());
        Assert.AreEqual(
            1,
            measurements.Select(item => item.Outcome.Estimate.EstimatedPeakBuildBytes).Distinct().Count());
    }

    private static Measurement Measure(
        AtlasIndexProfile profile,
        IReadOnlyList<SpatialPrimitiveDefinition> fixtures)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        long liveBefore = GC.GetTotalMemory(forceFullCollection: false);
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long before = GC.GetTotalAllocatedBytes(precise: true);
        AtlasIndexBuildOutcome outcome = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            profile,
            fixtures,
            new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold)));
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        long liveAfter = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(outcome);
        return new Measurement(
            outcome,
            allocated,
            liveAfter - liveBefore,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2);
    }

    private static AtlasMemoryEstimate Success(GenerationResult<AtlasMemoryEstimate> result)
    {
        Assert.IsInstanceOfType<GenerationSuccess<AtlasMemoryEstimate>>(result);
        return ((GenerationSuccess<AtlasMemoryEstimate>)result).Snapshot;
    }

    private static void AssertTypedBudgetFailure(
        GenerationResult<AtlasIndexBuildOutcome> result,
        string expectedStage)
    {
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(result);
        GenerationError error = ((GenerationFailure<AtlasIndexBuildOutcome>)result).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, error.Code);
        Assert.AreEqual(expectedStage, error.Stage);
        Assert.AreEqual(SpatialIndexTestSupport.Identity().GeographyConfigHash, error.InputHash);
    }

    private sealed record Measurement(
        AtlasIndexBuildOutcome Outcome,
        long CumulativeAllocatedBytes,
        long LiveManagedBytesDelta,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections);
}
