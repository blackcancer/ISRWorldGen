using System.Collections;
using System.Runtime.InteropServices;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.SpatialIndex;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L02B;

[TestClass]
public sealed class SpatialIndexProcessProbeTests
{
    [TestMethod]
    public void DeterminismProcessProbe()
    {
        AtlasIndexProfile profile = SpatialIndexTestSupport.VastProfile();
        SpatialPrimitiveDefinition[] fixtures = SpatialIndexTestSupport.CrossTileFixtures();
        AtlasIndexBuildOutcome measured = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            profile,
            fixtures,
            new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold)));
        AtlasIndexBuildOutcome parallel = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            profile,
            fixtures.Reverse().ToArray(),
            new SpatialIndexBuildOptions(16, SpatialIndexCacheMode.Precomputed)));
        Assert.AreEqual(measured.Snapshot.Header.ContentChecksum, parallel.Snapshot.Header.ContentChecksum);
        AtlasIndexBuildOutcome ownership = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            SpatialIndexTestSupport.FixtureProfile(),
            fixtures.Reverse().ToArray(),
            new SpatialIndexBuildOptions(16, SpatialIndexCacheMode.Precomputed)));
        Assert.AreEqual(4, ownership.Snapshot.Index.OccupiedTileCount);
        Assert.AreEqual(8, ownership.Snapshot.Index.PlacementReferenceCount);
        Assert.HasCount(2, ownership.Snapshot.Index.Query(new SpatialTileKey(1, 1)));
        StableId[] publishedIds = ownership.Snapshot.Graph.Sites.Select(site => site.Id)
            .Concat(ownership.Snapshot.Index.Primitives.Select(primitive => primitive.Id))
            .Concat(ownership.Snapshot.Index.Primitives.Select(primitive => primitive.OwnerId))
            .ToArray();
        Assert.AreEqual(publishedIds.Length, publishedIds.Distinct().Count());
        AtlasIndexProfile laboratory64Profile = SpatialIndexTestSupport.VastProfile(
            memoryBudgetBytes: 512L * 1024 * 1024,
            requestedSites: 64,
            siteQuota: 8_192);
        AtlasIndexBuildOutcome laboratory64 = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            laboratory64Profile,
            fixtures,
            new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold)));
        AtlasIndexProfile rejectedDenseProfile = SpatialIndexTestSupport.VastProfile(
            memoryBudgetBytes: 512L * 1024 * 1024,
            requestedSites: 256,
            siteQuota: 8_192);
        AtlasMemoryEstimate rejectedDensePlan = ((GenerationSuccess<AtlasMemoryEstimate>)AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), rejectedDenseProfile, fixtures)).Snapshot;
        GenerationResult<AtlasIndexBuildOutcome> rejectedDense = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), rejectedDenseProfile, fixtures);
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(rejectedDense);
        GenerationError rejectedDenseError = ((GenerationFailure<AtlasIndexBuildOutcome>)rejectedDense).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, rejectedDenseError.Code);
        Assert.AreEqual("atlas.spatial-index.budget", rejectedDenseError.Stage);

        string? outputPath = Environment.GetEnvironmentVariable("ISRW_L02B_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        string commit = Environment.GetEnvironmentVariable("ISRW_L02B_COMMIT")
            ?? throw new InvalidOperationException("ISRW_L02B_COMMIT is required for a persisted process proof.");
        var report = new
        {
            schemaVersion = 2,
            status = "PASS",
            requirements = new[] { "R02-03", "R02-04" },
            tests = new[] { "T02-03", "T02-04" },
            commit,
            seed = SpatialIndexTestSupport.Seed,
            configHash = SpatialIndexTestSupport.ConfigHash,
            algorithmVersion = SpatialIndexTestSupport.Identity().AlgorithmVersion,
            snapshotSchemaVersion = SpatialIndexTestSupport.Identity().SchemaVersion,
            profile = SpatialIndexTestSupport.Identity().DeterminismProfileId,
            profileQualification = "sparse atlas under candidate 256 MiB budget; threshold not frozen; not a final preset or density claim",
            budgetQualification = new
            {
                status = "candidate-not-frozen",
                candidateBudgetBytes = profile.MemoryBudgetBytes,
                laboratoryBudgetBytes = laboratory64Profile.MemoryBudgetBytes,
            },
            worldWidth = 1_024_000,
            worldLength = 1_024_000,
            worldColumns = measured.Estimate.WorldColumnCount,
            siteQuota = profile.SiteQuota,
            siteCount = measured.Snapshot.Graph.SiteCount,
            edgeCount = measured.Snapshot.Graph.EdgeCount,
            neighborReferences = measured.Snapshot.Graph.NeighborReferenceCount,
            occupiedTiles = measured.Snapshot.Index.OccupiedTileCount,
            placementReferences = measured.Snapshot.Index.PlacementReferenceCount,
            estimatedCanonicalCaptureBytes = measured.Estimate.EstimatedCanonicalCaptureBytes,
            estimatedSiteBytes = measured.Estimate.EstimatedSiteBytes,
            estimatedCompactGraphBytes = measured.Estimate.EstimatedCompactGraphBytes,
            estimatedSpatialIndexBytes = measured.Estimate.EstimatedSpatialIndexBytes,
            estimatedGeometryWorkingBytes = measured.Estimate.EstimatedGeometryWorkingBytes,
            estimatedGeometryCacheBytes = measured.Estimate.EstimatedGeometryCacheBytes,
            parallelEstimatedGeometryCacheBytes = parallel.Estimate.EstimatedGeometryCacheBytes,
            estimatedSnapshotBytes = measured.Estimate.EstimatedSnapshotBytes,
            estimatedPeakBuildBytes = measured.Estimate.EstimatedPeakBuildBytes,
            contentHash = measured.Snapshot.Header.ContentChecksum.ToString(),
            parallelContentHash = parallel.Snapshot.Header.ContentChecksum.ToString(),
            ownershipContentHash = ownership.Snapshot.Header.ContentChecksum.ToString(),
            ownerId = ownership.Snapshot.Index.Primitives[0].OwnerId.ToString(),
            ownerIds = ownership.Snapshot.Index.Primitives.Select(primitive => primitive.OwnerId.ToString()).ToArray(),
            publishedStableIdCount = publishedIds.Length,
            uniquePublishedStableIdCount = publishedIds.Distinct().Count(),
            ownerTile = new
            {
                ownership.Snapshot.Index.Primitives[0].OwnerTile.X,
                ownership.Snapshot.Index.Primitives[0].OwnerTile.Z,
            },
            northeastQueryCount = ownership.Snapshot.Index.Query(new SpatialTileKey(1, 1)).Count,
            laboratory64 = new
            {
                qualification = "additional laboratory path; not the candidate-budget acceptance",
                requestedSites = laboratory64Profile.RequestedSiteCount,
                siteQuota = laboratory64Profile.SiteQuota,
                memoryBudgetBytes = laboratory64Profile.MemoryBudgetBytes,
                estimatedPeakBuildBytes = laboratory64.Estimate.EstimatedPeakBuildBytes,
                contentHash = laboratory64.Snapshot.Header.ContentChecksum.ToString(),
            },
            rejectedDenseProbe = new
            {
                requestedSites = rejectedDenseProfile.RequestedSiteCount,
                siteQuota = rejectedDenseProfile.SiteQuota,
                memoryBudgetBytes = rejectedDenseProfile.MemoryBudgetBytes,
                estimatedPeakBuildBytes = rejectedDensePlan.EstimatedPeakBuildBytes,
                failureCode = rejectedDenseError.Code.ToString(),
                failureStage = rejectedDenseError.Stage,
                snapshotVisible = false,
            },
            processId = Environment.ProcessId,
            framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        };
        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    [TestMethod]
    [DoNotParallelize]
    [TestCategory("DedicatedProcessProbe")]
    public void DedicatedAllocationProcessProbe()
    {
        AtlasIndexProfile profile = SpatialIndexTestSupport.VastProfile();
        SpatialPrimitiveDefinition[] fixtures = SpatialIndexTestSupport.CrossTileFixtures();
        string? outputPath = Environment.GetEnvironmentVariable("ISRW_L02B_ALLOCATION_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            GenerationResult<AtlasMemoryEstimate> plan = AtlasSpatialIndexPlanner.Estimate(
                SpatialIndexTestSupport.Identity(), profile, fixtures);
            Assert.IsInstanceOfType<GenerationSuccess<AtlasMemoryEstimate>>(plan,
                "The ordinary parallel suite validates discovery only; persisted allocation evidence requires the dedicated runner.");
            return;
        }

        WarmUp(profile, fixtures);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        long liveBefore = GC.GetTotalMemory(forceFullCollection: false);
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long before = GC.GetTotalAllocatedBytes(precise: true);
        AtlasIndexBuildOutcome measured = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            profile,
            fixtures,
            new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold)));
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - before;
        long liveAfter = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(measured);
        int gen0Collections = GC.CollectionCount(0) - gen0;
        int gen1Collections = GC.CollectionCount(1) - gen1;
        int gen2Collections = GC.CollectionCount(2) - gen2;

        AtlasIndexProfile rejectedDenseProfile = SpatialIndexTestSupport.VastProfile(
            memoryBudgetBytes: 512L * 1024 * 1024,
            requestedSites: 256,
            siteQuota: 8_192);
        long refusalBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasIndexBuildOutcome> rejectedDense = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), rejectedDenseProfile, fixtures);
        long refusalAllocation = GC.GetTotalAllocatedBytes(precise: true) - refusalBefore;
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(rejectedDense);
        GenerationError refusalError = ((GenerationFailure<AtlasIndexBuildOutcome>)rejectedDense).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, refusalError.Code);
        Assert.AreEqual("atlas.spatial-index.budget", refusalError.Stage);
        Assert.IsLessThan(64 * 1024L, refusalAllocation);

        SpatialPrimitiveDefinition largeInput = SpatialIndexTestSupport.LargeInputFixture();
        AtlasIndexProfile oneMiBProfile = SpatialIndexTestSupport.FixtureProfile(memoryBudgetBytes: 1024 * 1024);
        long largeInputBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasIndexBuildOutcome> largeInputRefusal = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), oneMiBProfile, [largeInput]);
        long largeInputRefusalAllocation = GC.GetTotalAllocatedBytes(precise: true) - largeInputBefore;
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(largeInputRefusal);
        GenerationError largeInputError = ((GenerationFailure<AtlasIndexBuildOutcome>)largeInputRefusal).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, largeInputError.Code);
        Assert.AreEqual("atlas.spatial-index.budget", largeInputError.Stage);
        Assert.IsLessThan(256 * 1024L, largeInputRefusalAllocation);
        GC.KeepAlive(largeInput);

        SpatialPrimitiveDefinition[] manyPrimitives = SpatialIndexTestSupport.ManyPrimitiveFixtures();
        var manyPrimitiveProfile = new AtlasIndexProfile(
            SpatialIndexTestSupport.FixtureProfile().Domain,
            SpatialIndexTestSupport.FixtureProfile().Scale,
            tileSize: 512,
            requestedSiteCount: 1,
            siteQuota: 1,
            memoryBudgetBytes: 24L * 1024 * 1024);
        long manyEstimateBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasMemoryEstimate> manyEstimateResult = AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), manyPrimitiveProfile, manyPrimitives);
        long manyEstimateAllocation = GC.GetTotalAllocatedBytes(precise: true) - manyEstimateBefore;
        Assert.IsInstanceOfType<GenerationSuccess<AtlasMemoryEstimate>>(manyEstimateResult);
        AtlasMemoryEstimate manyEstimate = ((GenerationSuccess<AtlasMemoryEstimate>)manyEstimateResult).Snapshot;
        long manyBuildBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasIndexBuildOutcome> manyBuildResult = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), manyPrimitiveProfile, manyPrimitives);
        long manyBuildAllocation = GC.GetTotalAllocatedBytes(precise: true) - manyBuildBefore;
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(manyBuildResult);
        GenerationError manyBuildError = ((GenerationFailure<AtlasIndexBuildOutcome>)manyBuildResult).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, manyBuildError.Code);
        Assert.AreEqual("atlas.spatial-index.budget", manyBuildError.Stage);
        Assert.IsLessThan(8L * 1024 * 1024, manyEstimateAllocation);
        Assert.IsLessThan(8L * 1024 * 1024, manyBuildAllocation);
        GC.KeepAlive(manyPrimitives);

        var maximumCountInput = new ReportedCountPrimitiveList(int.MaxValue);
        AtlasIndexProfile fixtureProfile = SpatialIndexTestSupport.FixtureProfile();
        var maximumCountProfile = new AtlasIndexProfile(
            fixtureProfile.Domain,
            fixtureProfile.Scale,
            tileSize: 512,
            requestedSiteCount: 1,
            siteQuota: 1,
            memoryBudgetBytes: long.MaxValue);
        long maximumCountEstimateBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasMemoryEstimate> maximumCountEstimateResult = AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), maximumCountProfile, maximumCountInput);
        long maximumCountEstimateAllocation =
            GC.GetTotalAllocatedBytes(precise: true) - maximumCountEstimateBefore;
        long maximumCountBuildBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasIndexBuildOutcome> maximumCountBuildResult = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), maximumCountProfile, maximumCountInput);
        long maximumCountBuildAllocation = GC.GetTotalAllocatedBytes(precise: true) - maximumCountBuildBefore;
        Assert.IsInstanceOfType<GenerationFailure<AtlasMemoryEstimate>>(maximumCountEstimateResult);
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(maximumCountBuildResult);
        GenerationError maximumCountEstimateError =
            ((GenerationFailure<AtlasMemoryEstimate>)maximumCountEstimateResult).Error;
        GenerationError maximumCountBuildError =
            ((GenerationFailure<AtlasIndexBuildOutcome>)maximumCountBuildResult).Error;
        Assert.AreEqual(GenerationFailureCode.InvalidInput, maximumCountEstimateError.Code);
        Assert.AreEqual(GenerationFailureCode.InvalidInput, maximumCountBuildError.Code);
        Assert.AreEqual("atlas.spatial-index.array-capacity", maximumCountEstimateError.Stage);
        Assert.AreEqual("atlas.spatial-index.array-capacity", maximumCountBuildError.Stage);
        Assert.AreEqual(0, maximumCountInput.EnumerationCount);
        Assert.IsLessThan(64 * 1024L, maximumCountEstimateAllocation);
        Assert.IsLessThan(64 * 1024L, maximumCountBuildAllocation);

        AtlasIndexProfile fiftyThousandProfile = SpatialIndexTestSupport.VastProfile(
            memoryBudgetBytes: long.MaxValue,
            requestedSites: 50_000,
            siteQuota: 50_000);
        var fiftyThousandInput = new ReportedCountPrimitiveList(0);
        var precomputedOptions = new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Precomputed);
        var coldOptions = new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold);
        long precomputedEstimateBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasMemoryEstimate> precomputedEstimateResult = AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), fiftyThousandProfile, fiftyThousandInput, precomputedOptions);
        long precomputedEstimateAllocation = GC.GetTotalAllocatedBytes(precise: true) - precomputedEstimateBefore;
        long precomputedBuildBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasIndexBuildOutcome> precomputedBuildResult = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), fiftyThousandProfile, fiftyThousandInput, precomputedOptions);
        long precomputedBuildAllocation = GC.GetTotalAllocatedBytes(precise: true) - precomputedBuildBefore;
        long coldEstimateBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasMemoryEstimate> coldEstimateResult = AtlasSpatialIndexPlanner.Estimate(
            SpatialIndexTestSupport.Identity(), fiftyThousandProfile, fiftyThousandInput, coldOptions);
        long coldEstimateAllocation = GC.GetTotalAllocatedBytes(precise: true) - coldEstimateBefore;
        long coldBuildBefore = GC.GetTotalAllocatedBytes(precise: true);
        GenerationResult<AtlasIndexBuildOutcome> coldBuildResult = AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(), fiftyThousandProfile, fiftyThousandInput, coldOptions);
        long coldBuildAllocation = GC.GetTotalAllocatedBytes(precise: true) - coldBuildBefore;
        Assert.IsInstanceOfType<GenerationFailure<AtlasMemoryEstimate>>(precomputedEstimateResult);
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(precomputedBuildResult);
        Assert.IsInstanceOfType<GenerationFailure<AtlasMemoryEstimate>>(coldEstimateResult);
        Assert.IsInstanceOfType<GenerationFailure<AtlasIndexBuildOutcome>>(coldBuildResult);
        GenerationError precomputedEstimateError =
            ((GenerationFailure<AtlasMemoryEstimate>)precomputedEstimateResult).Error;
        GenerationError precomputedBuildError =
            ((GenerationFailure<AtlasIndexBuildOutcome>)precomputedBuildResult).Error;
        GenerationError coldEstimateError = ((GenerationFailure<AtlasMemoryEstimate>)coldEstimateResult).Error;
        GenerationError coldBuildError = ((GenerationFailure<AtlasIndexBuildOutcome>)coldBuildResult).Error;
        Assert.AreEqual(GenerationFailureCode.InvalidInput, precomputedEstimateError.Code);
        Assert.AreEqual(GenerationFailureCode.InvalidInput, precomputedBuildError.Code);
        Assert.AreEqual("atlas.spatial-index.array-capacity", precomputedEstimateError.Stage);
        Assert.AreEqual("atlas.spatial-index.array-capacity", precomputedBuildError.Stage);
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, coldEstimateError.Code);
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, coldBuildError.Code);
        Assert.AreEqual("atlas.spatial-index.geometry-work-capacity", coldEstimateError.Stage);
        Assert.AreEqual("atlas.spatial-index.geometry-work-capacity", coldBuildError.Stage);
        Assert.AreEqual(0, fiftyThousandInput.EnumerationCount);
        Assert.IsLessThan(64 * 1024L, precomputedEstimateAllocation);
        Assert.IsLessThan(64 * 1024L, precomputedBuildAllocation);
        Assert.IsLessThan(64 * 1024L, coldEstimateAllocation);
        Assert.IsLessThan(64 * 1024L, coldBuildAllocation);

        string commit = Environment.GetEnvironmentVariable("ISRW_L02B_COMMIT")
            ?? throw new InvalidOperationException("ISRW_L02B_COMMIT is required for a persisted allocation proof.");
        var report = new
        {
            schemaVersion = 2,
            status = "PASS",
            requirement = "R02-03",
            test = "T02-03-dedicated-allocation",
            commit,
            seed = SpatialIndexTestSupport.Seed,
            configHash = SpatialIndexTestSupport.ConfigHash,
            algorithmVersion = SpatialIndexTestSupport.Identity().AlgorithmVersion,
            snapshotSchemaVersion = SpatialIndexTestSupport.Identity().SchemaVersion,
            profile = SpatialIndexTestSupport.Identity().DeterminismProfileId,
            isolation = "dedicated vstest process; exact one-test filter; DoNotParallelize; no concurrent test method",
            workers = 1,
            estimatedPeakBuildBytes = measured.Estimate.EstimatedPeakBuildBytes,
            cumulativeAllocatedBytes = allocatedBytes,
            liveManagedBytesDelta = liveAfter - liveBefore,
            allocationScope = "GC.GetTotalAllocatedBytes(precise:true) in a dedicated single-test process; warmed workers=1 build; cumulative, not peak",
            liveMemoryScope = "GC.GetTotalMemory around the isolated build with full collection after; signed live managed delta, not working set",
            gen0Collections,
            gen1Collections,
            gen2Collections,
            contentHash = measured.Snapshot.Header.ContentChecksum.ToString(),
            rejectedDenseRefusalAllocatedBytes = refusalAllocation,
            rejectedDenseSnapshotVisible = false,
            largeInputPointCount = largeInput.Points.Count,
            largeInputMemoryBudgetBytes = oneMiBProfile.MemoryBudgetBytes,
            largeInputRefusalAllocatedBytes = largeInputRefusalAllocation,
            largeInputFailureCode = largeInputError.Code.ToString(),
            largeInputFailureStage = largeInputError.Stage,
            largeInputSnapshotVisible = false,
            manyPrimitiveCount = manyPrimitives.Length,
            manyPrimitiveMemoryBudgetBytes = manyPrimitiveProfile.MemoryBudgetBytes,
            manyPrimitiveEstimatedPeakBuildBytes = manyEstimate.EstimatedPeakBuildBytes,
            manyPrimitiveEstimateAllocatedBytes = manyEstimateAllocation,
            manyPrimitiveBuildAllocatedBytes = manyBuildAllocation,
            manyPrimitiveFailureCode = manyBuildError.Code.ToString(),
            manyPrimitiveFailureStage = manyBuildError.Stage,
            manyPrimitiveSnapshotVisible = false,
            maximumPrimitiveCount = maximumCountInput.Count,
            maximumPrimitiveMemoryBudgetBytes = maximumCountProfile.MemoryBudgetBytes,
            maximumPrimitiveEstimateAllocatedBytes = maximumCountEstimateAllocation,
            maximumPrimitiveBuildAllocatedBytes = maximumCountBuildAllocation,
            maximumPrimitiveEstimateFailureCode = maximumCountEstimateError.Code.ToString(),
            maximumPrimitiveBuildFailureCode = maximumCountBuildError.Code.ToString(),
            maximumPrimitiveEstimateFailureStage = maximumCountEstimateError.Stage,
            maximumPrimitiveBuildFailureStage = maximumCountBuildError.Stage,
            maximumPrimitiveEnumerationCount = maximumCountInput.EnumerationCount,
            maximumPrimitiveSnapshotVisible = false,
            geometryCapacitySiteCount = fiftyThousandProfile.RequestedSiteCount,
            geometryCapacityMemoryBudgetBytes = fiftyThousandProfile.MemoryBudgetBytes,
            precomputedGeometryEstimateAllocatedBytes = precomputedEstimateAllocation,
            precomputedGeometryBuildAllocatedBytes = precomputedBuildAllocation,
            precomputedGeometryEstimateFailureCode = precomputedEstimateError.Code.ToString(),
            precomputedGeometryBuildFailureCode = precomputedBuildError.Code.ToString(),
            precomputedGeometryEstimateFailureStage = precomputedEstimateError.Stage,
            precomputedGeometryBuildFailureStage = precomputedBuildError.Stage,
            coldGeometryEstimateAllocatedBytes = coldEstimateAllocation,
            coldGeometryBuildAllocatedBytes = coldBuildAllocation,
            coldGeometryEstimateFailureCode = coldEstimateError.Code.ToString(),
            coldGeometryBuildFailureCode = coldBuildError.Code.ToString(),
            coldGeometryEstimateFailureStage = coldEstimateError.Stage,
            coldGeometryBuildFailureStage = coldBuildError.Stage,
            geometryCapacityEnumerationCount = fiftyThousandInput.EnumerationCount,
            geometryCapacitySnapshotVisible = false,
            processId = Environment.ProcessId,
            framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        };
        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WarmUp(
        AtlasIndexProfile profile,
        IReadOnlyList<SpatialPrimitiveDefinition> fixtures)
    {
        _ = SpatialIndexTestSupport.Success(AtlasSpatialIndexBuilder.Build(
            SpatialIndexTestSupport.Identity(),
            profile,
            fixtures,
            new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold)));
    }

    private sealed class ReportedCountPrimitiveList : IReadOnlyList<SpatialPrimitiveDefinition>
    {
        internal ReportedCountPrimitiveList(int count) => Count = count;

        public int Count { get; }

        public SpatialPrimitiveDefinition this[int index] => throw new NotSupportedException();

        public int EnumerationCount { get; private set; }

        public IEnumerator<SpatialPrimitiveDefinition> GetEnumerator()
        {
            EnumerationCount++;
            throw new InvalidOperationException("Structurally impossible input must be rejected before enumeration.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
