using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03A;

[TestClass]
public sealed class PlateAtlasBuilderTests
{
    [TestMethod]
    public void AtlasCellsProduceCanonicalPlateSnapshotWithBoundaryFields()
    {
        FrozenScaleProfile profile = L03ATestSupport.FrozenProfile("balanced");
        WorldBounds bounds = L03ATestSupport.Bounds(profile);
        var identity = L03ATestSupport.Identity(20260906, profile);
        GeneratedSiteSet sites = L03ATestSupport.Success(AtlasSiteGenerator.Generate(
            identity,
            bounds,
            new AtlasSiteGenerationSettings(64)));
        AtlasMesh mesh = L03ATestSupport.Success(AtlasGeometryBuilder.Build(
            identity,
            bounds,
            sites.Sites,
            new AtlasGeometryBuildOptions(4, GeometryCacheMode.Precomputed)));
        PlateGenerationSettings settings = new(
            plateCount: 7,
            L03ATestSupport.ContinentalSettings(),
            maximumCells: 1_000,
            maximumBoundaryEdges: 4_000,
            maximumBoundaryInfluenceEvaluations: 4_000_000);

        PlateAtlasSnapshot first = L03ATestSupport.Success(
            PlateAtlasBuilder.Build(identity, mesh, profile, settings));
        PlateAtlasSnapshot reversed = L03ATestSupport.Success(
            PlateAtlasBuilder.Build(identity, mesh, profile, settings));

        Assert.HasCount(7, first.Plates);
        Assert.HasCount(mesh.Cells.Count, first.Cells);
        Assert.IsGreaterThan(0, first.Boundaries.Count);
        Assert.AreEqual(first.ContentChecksum, reversed.ContentChecksum);
        Assert.AreEqual(profile.Id, first.ScaleProfileId);
        Assert.AreEqual(profile.AtlasTileSizeBlocks, first.AtlasTileSizeBlocks);
        CollectionAssert.AreEqual(first.Cells.ToArray(), reversed.Cells.ToArray());
        Assert.IsTrue(first.Cells.All(cell => double.IsFinite(cell.UpliftNormalized)));
        Assert.IsTrue(first.Cells.All(cell => double.IsFinite(cell.SubsidenceNormalized)));
        Assert.IsTrue(first.Cells.All(cell => cell.RelativeAgePpm is >= 0 and <= 1_000_000));
        Assert.IsTrue(first.Cells.All(cell => cell.ContinentalHeightPpm is >= -1_000_000 and <= 1_000_000));
        var boundaryCells = first.Boundaries.SelectMany(boundary => new[] { boundary.CellA, boundary.CellB }).ToHashSet();
        Assert.IsTrue(first.Cells.Any(cell =>
            !boundaryCells.Contains(cell.CellId) && (cell.UpliftNormalized > 0 || cell.SubsidenceNormalized > 0)),
            "Boundary fields did not reach their regional envelope.");
        Assert.IsTrue(first.Cells.Any(cell => cell.UpliftNormalized == 0 && cell.SubsidenceNormalized == 0),
            "No calm plate interior remains outside the bounded regional envelope.");
        Assert.IsGreaterThanOrEqualTo(3, first.Cells
            .Select(cell => Math.Max(cell.UpliftNormalized, cell.SubsidenceNormalized))
            .Where(value => value > 0)
            .Distinct()
            .Count(),
            "Local and regional boundary envelopes did not produce multiple field amplitudes.");
        Assert.IsTrue(first.Plates.Any(plate => plate.CrustKinds.Count > 1),
            "At least one generated plate must demonstrate mixed crust domains on this frozen fixture.");
    }

    [TestMethod]
    [DoNotParallelize]
    public void PlateBuilderRefusesImpossibleCountsAndBudgets()
    {
        FrozenScaleProfile profile = L03ATestSupport.FrozenProfile("laboratory");
        WorldBounds bounds = L03ATestSupport.Bounds(profile);
        GenerationIdentity identity = L03ATestSupport.Identity(73, profile);
        GeneratedSiteSet sites = L03ATestSupport.Success(AtlasSiteGenerator.Generate(
            identity, bounds, new AtlasSiteGenerationSettings(16)));
        AtlasMesh mesh = L03ATestSupport.Success(AtlasGeometryBuilder.Build(
            identity, bounds, sites.Sites));

        Assert.IsFalse(PlateAtlasBuilder.Build(
            identity, mesh, profile,
            new PlateGenerationSettings(17, L03ATestSupport.ContinentalSettings(), 100, 100, 10_000)).IsSuccess);
        Assert.IsFalse(PlateAtlasBuilder.Build(
            identity, mesh, profile,
            new PlateGenerationSettings(2, L03ATestSupport.ContinentalSettings(), 15, 100, 10_000)).IsSuccess);

        long allocatedBeforeInfluencePreflight = GC.GetAllocatedBytesForCurrentThread();
        GenerationResult<PlateAtlasSnapshot> influenceBudget = PlateAtlasBuilder.Build(
            identity,
            mesh,
            profile,
            new PlateGenerationSettings(2, L03ATestSupport.ContinentalSettings(), 100, 100, 1));
        long influencePreflightAllocation =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeInfluencePreflight;
        Assert.IsInstanceOfType<GenerationFailure<PlateAtlasSnapshot>>(influenceBudget);
        Assert.AreEqual(
            GenerationFailureCode.BudgetExceeded,
            ((GenerationFailure<PlateAtlasSnapshot>)influenceBudget).Error.Code);
        Assert.AreEqual(
            "geology.plates.influence-budget",
            ((GenerationFailure<PlateAtlasSnapshot>)influenceBudget).Error.Stage);
        Assert.IsLessThan(64 * 1024L, influencePreflightAllocation,
            "The coupled work-budget refusal must occur before continental construction or spread allocation.");
    }
}
