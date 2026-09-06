using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L02A;

[TestClass]
public sealed class DegenerateGeometryTests
{
    [TestMethod]
    public void DuplicateCoordinates_AreCollapsedToTheSmallestStableId()
    {
        AtlasSite first = GeometryTestSupport.Site(0, 20, 20);
        AtlasSite duplicate = GeometryTestSupport.Site(1, 20, 20);
        AtlasSite canonical = GeometryTestSupport.StableIdComparer.Instance.Compare(first.Id, duplicate.Id) < 0
            ? first
            : duplicate;
        AtlasSite removed = canonical == first ? duplicate : first;
        AtlasSite[] input = [first, GeometryTestSupport.Site(2, 80, 20), duplicate, GeometryTestSupport.Site(3, 50, 80)];

        AtlasMesh mesh = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
            GeometryTestSupport.Identity(),
            new WorldBounds(0, 0, 100, 100),
            input));

        Assert.HasCount(3, mesh.Sites);
        Assert.HasCount(1, mesh.CollapsedDuplicates);
        Assert.AreEqual(new CollapsedDuplicate(removed.Id, canonical.Id), mesh.CollapsedDuplicates[0]);
        GeometryTestSupport.AssertValidTopology(mesh);
    }

    [TestMethod]
    public void CollinearQuasiCollinearCocircularUnequalScaleAndBorderSites_AreValid()
    {
        (WorldBounds Bounds, AtlasSite[] Sites)[] cases =
        [
            (new WorldBounds(0, 0, 101, 101),
                [GeometryTestSupport.Site(10, 0, 50), GeometryTestSupport.Site(11, 20, 50),
                 GeometryTestSupport.Site(12, 80, 50), GeometryTestSupport.Site(13, 100, 50)]),
            (new WorldBounds(-10, -10, 3_000_011, 20),
                [GeometryTestSupport.Site(20, 0, 0), GeometryTestSupport.Site(21, 1_000_000, 1),
                 GeometryTestSupport.Site(22, 2_000_000, 2), GeometryTestSupport.Site(23, 3_000_000, 4),
                 GeometryTestSupport.Site(24, 1_500_000, 12)]),
            (new WorldBounds(0, 0, 100, 100),
                [GeometryTestSupport.Site(30, 20, 20), GeometryTestSupport.Site(31, 80, 20),
                 GeometryTestSupport.Site(32, 80, 80), GeometryTestSupport.Site(33, 20, 80)]),
            (new WorldBounds(-4_000_000_000, -4_000_000_000, 4_000_000_001, 4_000_000_001),
                [GeometryTestSupport.Site(40, -3_999_999_999, -3_999_999_999), GeometryTestSupport.Site(41, -1, 0),
                 GeometryTestSupport.Site(42, 0, 1), GeometryTestSupport.Site(43, 1, 0),
                 GeometryTestSupport.Site(44, 3_999_999_999, 3_999_999_999)]),
            (new WorldBounds(-50, -50, 51, 51),
                [GeometryTestSupport.Site(50, -50, -50), GeometryTestSupport.Site(51, 50, -50),
                 GeometryTestSupport.Site(52, 50, 50), GeometryTestSupport.Site(53, -50, 50),
                 GeometryTestSupport.Site(54, 0, 0)]),
            (new WorldBounds(0, 0, 101, 101),
                [GeometryTestSupport.Site(60, 0, 0), GeometryTestSupport.Site(61, 50, 0),
                 GeometryTestSupport.Site(62, 100, 0), GeometryTestSupport.Site(63, 100, 100),
                 GeometryTestSupport.Site(64, 0, 100), GeometryTestSupport.Site(65, 40, 40)]),
            (new WorldBounds(0, 0, 101, 101),
                [GeometryTestSupport.Site(70, 75, 50), GeometryTestSupport.Site(71, 70, 65),
                 GeometryTestSupport.Site(72, 50, 75), GeometryTestSupport.Site(73, 30, 65),
                 GeometryTestSupport.Site(74, 25, 50), GeometryTestSupport.Site(75, 30, 35),
                 GeometryTestSupport.Site(76, 50, 25), GeometryTestSupport.Site(77, 70, 35)]),
        ];

        foreach ((WorldBounds bounds, AtlasSite[] sites) in cases)
        {
            AtlasMesh mesh = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
                GeometryTestSupport.Identity(),
                bounds,
                sites));
            GeometryTestSupport.AssertValidTopology(mesh);
        }

        AtlasMesh collinear = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
            GeometryTestSupport.Identity(),
            cases[0].Bounds,
            cases[0].Sites));
        Assert.IsEmpty(collinear.Triangles);
        Assert.AreEqual(AtlasTopologyDimension.Linear, collinear.TopologyDimension);
        Assert.HasCount(3, collinear.Edges, "Finite collinear sites form a chain, not a torus.");
        StableId leftBorderId = cases[0].Sites.Single(site => site.X == 0).Id;
        StableId rightBorderId = cases[0].Sites.Single(site => site.X == 100).Id;
        VoronoiCell leftBorderCell = collinear.Cells.Single(cell => cell.SiteId == leftBorderId);
        Assert.DoesNotContain(rightBorderId, leftBorderCell.NeighborIds);

        AtlasMesh cocircular = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
            GeometryTestSupport.Identity(),
            cases[2].Bounds,
            cases[2].Sites));
        Assert.HasCount(2, cocircular.Triangles);
        Assert.HasCount(5, cocircular.Edges, "A deterministic legal diagonal must resolve the cocircular face.");
        AtlasMesh cocircularReversed = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
            GeometryTestSupport.Identity(),
            cases[2].Bounds,
            cases[2].Sites.Reverse(),
            new AtlasGeometryBuildOptions(16, GeometryCacheMode.Precomputed)));
        Assert.AreEqual(
            GeometryTestSupport.Fingerprint(cocircular),
            GeometryTestSupport.Fingerprint(cocircularReversed),
            "The canonical cocircular diagonal cannot depend on input order, workers, or cache.");

        AtlasMesh border = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
            GeometryTestSupport.Identity(),
            cases[4].Bounds,
            cases[4].Sites));
        AtlasSite southwest = cases[4].Sites.Single(site => site.X == -50 && site.Z == -50);
        AtlasSite northeast = cases[4].Sites.Single(site => site.X == 50 && site.Z == 50);
        Assert.AreEqual(
            WorldBoundaryMask.MinX | WorldBoundaryMask.MinZ,
            border.Cells.Single(cell => cell.SiteId == southwest.Id).BoundaryMask);
        Assert.AreEqual(
            WorldBoundaryMask.MaxX | WorldBoundaryMask.MaxZ,
            border.Cells.Single(cell => cell.SiteId == northeast.Id).BoundaryMask);
    }

    [TestMethod]
    public void InputOrderWorkersAndCache_CannotChangeCanonicalGeometry()
    {
        WorldBounds bounds = new(0, 0, 64, 64);
        AtlasSite[] linear = GeometryTestSupport.DeterminismCorpus().ToArray();
        AtlasMesh baseline = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
            GeometryTestSupport.Identity(),
            bounds,
            linear,
            new AtlasGeometryBuildOptions(1, GeometryCacheMode.Cold)));
        string expected = GeometryTestSupport.Fingerprint(baseline);

        AtlasSite[] reverse = linear.Reverse().ToArray();
        AtlasSite[] permuted = linear.OrderBy(site => site.Id.Low ^ site.Id.High).ToArray();
        (AtlasSite[] Sites, AtlasGeometryBuildOptions Options)[] variants =
        [
            (reverse, new AtlasGeometryBuildOptions(2, GeometryCacheMode.Cold)),
            (permuted, new AtlasGeometryBuildOptions(16, GeometryCacheMode.Precomputed)),
            (linear, new AtlasGeometryBuildOptions(2, GeometryCacheMode.Precomputed)),
        ];

        foreach ((AtlasSite[] sites, AtlasGeometryBuildOptions options) in variants)
        {
            AtlasMesh mesh = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
                GeometryTestSupport.Identity(), bounds, sites, options));
            Assert.AreEqual(expected, GeometryTestSupport.Fingerprint(mesh));
        }
    }

    [TestMethod]
    public void InvalidInputs_ReturnTypedFailureWithoutPartialMesh()
    {
        AtlasSite site = GeometryTestSupport.Site(100, 10, 10);
        GenerationResult<AtlasMesh>[] failures =
        [
            AtlasGeometryBuilder.Build(GeometryTestSupport.Identity(), new WorldBounds(0, 0, 20, 20), []),
            AtlasGeometryBuilder.Build(
                GeometryTestSupport.Identity(),
                new WorldBounds(0, 0, 20, 20),
                [site, site with { X = 11 }]),
            AtlasGeometryBuilder.Build(
                GeometryTestSupport.Identity(),
                new WorldBounds(0, 0, 20, 20),
                [GeometryTestSupport.Site(101, 20, 10)]),
        ];

        foreach (GenerationResult<AtlasMesh> failure in failures)
        {
            Assert.IsInstanceOfType<GenerationFailure<AtlasMesh>>(failure);
            GenerationError error = ((GenerationFailure<AtlasMesh>)failure).Error;
            Assert.AreEqual(GenerationFailureCode.InvalidInput, error.Code);
            Assert.AreEqual("atlas.geometry.validate", error.Stage);
            Assert.AreEqual(Hash256.Parse(GeometryTestSupport.ConfigHash), error.InputHash);
        }
    }
}
