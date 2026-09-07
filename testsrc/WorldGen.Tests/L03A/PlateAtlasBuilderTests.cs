using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03A;

[TestClass]
public sealed class PlateAtlasBuilderTests
{
    [TestMethod]
    public void AtlasCellsProduceCanonicalPlateSnapshotWithBoundaryFields()
    {
        WorldBounds bounds = new(0, 0, 1_024, 768);
        GeneratedSiteSet sites = L03ATestSupport.Success(AtlasSiteGenerator.Generate(
            L03ATestSupport.Identity(20260906),
            bounds,
            new AtlasSiteGenerationSettings(64)));
        AtlasMesh mesh = L03ATestSupport.Success(AtlasGeometryBuilder.Build(
            L03ATestSupport.Identity(20260906),
            bounds,
            sites.Sites,
            new AtlasGeometryBuildOptions(4, GeometryCacheMode.Precomputed)));
        PlateGenerationSettings settings = new(
            plateCount: 7,
            L03ATestSupport.ContinentalSettings(),
            maximumCells: 1_000,
            maximumBoundaryEdges: 4_000);

        PlateAtlasSnapshot first = L03ATestSupport.Success(
            PlateAtlasBuilder.Build(L03ATestSupport.Identity(20260906), mesh, settings));
        PlateAtlasSnapshot reversed = L03ATestSupport.Success(
            PlateAtlasBuilder.Build(L03ATestSupport.Identity(20260906), mesh, settings));

        Assert.HasCount(7, first.Plates);
        Assert.HasCount(mesh.Cells.Count, first.Cells);
        Assert.IsGreaterThan(0, first.Boundaries.Count);
        Assert.AreEqual(first.ContentChecksum, reversed.ContentChecksum);
        CollectionAssert.AreEqual(first.Cells.ToArray(), reversed.Cells.ToArray());
        Assert.IsTrue(first.Cells.All(cell => double.IsFinite(cell.UpliftNormalized)));
        Assert.IsTrue(first.Cells.All(cell => double.IsFinite(cell.SubsidenceNormalized)));
        var boundaryCells = first.Boundaries.SelectMany(boundary => new[] { boundary.CellA, boundary.CellB }).ToHashSet();
        Assert.IsTrue(first.Cells.Any(cell =>
            !boundaryCells.Contains(cell.CellId) && (cell.UpliftNormalized > 0 || cell.SubsidenceNormalized > 0)),
            "Boundary fields did not reach their regional envelope.");
        Assert.IsTrue(first.Cells.Any(cell => cell.UpliftNormalized == 0 && cell.SubsidenceNormalized == 0),
            "No calm plate interior remains outside the bounded regional envelope.");
        Assert.IsTrue(first.Plates.Any(plate => plate.CrustKinds.Count > 1),
            "At least one generated plate must demonstrate mixed crust domains on this frozen fixture.");
    }

    [TestMethod]
    public void PlateBuilderRefusesImpossibleCountsAndBudgets()
    {
        WorldBounds bounds = new(0, 0, 32, 32);
        GeneratedSiteSet sites = L03ATestSupport.Success(AtlasSiteGenerator.Generate(
            L03ATestSupport.Identity(), bounds, new AtlasSiteGenerationSettings(8)));
        AtlasMesh mesh = L03ATestSupport.Success(AtlasGeometryBuilder.Build(
            L03ATestSupport.Identity(), bounds, sites.Sites));

        Assert.IsFalse(PlateAtlasBuilder.Build(
            L03ATestSupport.Identity(), mesh,
            new PlateGenerationSettings(9, L03ATestSupport.ContinentalSettings(), 100, 100)).IsSuccess);
        Assert.IsFalse(PlateAtlasBuilder.Build(
            L03ATestSupport.Identity(), mesh,
            new PlateGenerationSettings(2, L03ATestSupport.ContinentalSettings(), 7, 100)).IsSuccess);
    }
}
