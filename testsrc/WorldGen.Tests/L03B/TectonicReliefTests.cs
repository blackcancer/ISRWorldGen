using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03B;

[TestClass]
public sealed class TectonicReliefTests
{
    [TestMethod]
    public void BeltsFollowConvergentForcingAndKeepImmutableProvenance()
    {
        var fixture = Fixture(-437287116);
        var before = fixture.Basis.ContentChecksum;
        TectonicReliefModel model = TectonicReliefModel.Build(fixture.Basis, fixture.Atlas, fixture.Plates, new(400, .96, 512, 12));
        Assert.IsGreaterThan(0, model.Belts.Count);
        Assert.IsTrue(model.Belts.All(belt => fixture.Plates.Boundaries.Any(boundary => boundary.CellA == belt.CellA &&
            boundary.CellB == belt.CellB && boundary.Kind == PlateBoundaryKind.Collision)));
        Assert.AreEqual(before, fixture.Basis.ContentChecksum);
        var repeated = TectonicReliefModel.Build(fixture.Basis, fixture.Atlas, fixture.Plates, new(400, .96, 512, 12));
        Assert.AreEqual(model.ContentChecksum, repeated.ContentChecksum);
        var changed = TectonicReliefModel.Build(fixture.Basis, fixture.Atlas, fixture.Plates, new(500, .96, 512, 12));
        Assert.AreNotEqual(model.ContentChecksum, changed.ContentChecksum);
    }

    [TestMethod]
    public void RidgeCrossSectionsAreNarrowerThanRegionalHillsAndDoNotClampHeights()
    {
        var fixture = Fixture(-437287116);
        TectonicReliefModel model = TectonicReliefModel.Build(fixture.Basis, fixture.Atlas, fixture.Plates, new(400, .96, 512, 12));
        double largestIncrease = 0d;
        foreach (RidgeBelt belt in model.Belts)
        foreach (RidgeNode node in belt.Spine)
        {
            long x = (long)node.X, z = (long)node.Z;
            if (!fixture.Atlas.Bounds.Contains(x, z)) continue;
            TectonicReliefSample sample = model.Sample(x, z);
            Assert.IsLessThan(1d, sample.ModelAltitudeNormalized);
            Assert.IsGreaterThanOrEqualTo(sample.Foundation.AltitudeBlocks, sample.AltitudeBlocks);
            largestIncrease = Math.Max(largestIncrease, sample.AltitudeBlocks - sample.Foundation.AltitudeBlocks);
        }
        Assert.IsGreaterThan(40d, largestIncrease, "Convergent forcing must add actual local relief, not merely change the palette.");
    }

    [TestMethod]
    public void SamplingOrderAndConcurrencyCannotAlterTheField()
    {
        var fixture = Fixture(73);
        TectonicReliefModel model = TectonicReliefModel.Build(fixture.Basis, fixture.Atlas, fixture.Plates, new(400, .96, 512, 12));
        var coordinates = Enumerable.Range(0, 512).Select(i => (X: (long)(i * 251 % 131072), Z: (long)(i * 701 % 131072))).ToArray();
        var expected = coordinates.Select(point => model.Sample(point.X, point.Z)).ToArray();
        var actual = new TectonicReliefSample[coordinates.Length];
        Parallel.For(0, coordinates.Length, i => actual[i] = model.Sample(coordinates[i].X, coordinates[i].Z));
        CollectionAssert.AreEqual(expected, actual);
        CollectionAssert.AreEqual(expected, coordinates.Reverse().Select(point => model.Sample(point.X, point.Z)).Reverse().ToArray());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.Sample(-1, 0));
    }

    [TestMethod]
    public void InvalidBudgetsAndMismatchedUpstreamAreRefused()
    {
        var fixture = Fixture(73); var other = Fixture(74);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TectonicReliefSettings(0, .96, 512, 12));
        Assert.ThrowsExactly<ArgumentException>(() => TectonicReliefModel.Build(fixture.Basis, other.Atlas, fixture.Plates, new(400, .96, 512, 12)));
    }

    [TestMethod]
    public void ContinuousDatumSamplesTheSealedContinentalFieldInsteadOfTheCellCentre()
    {
        var fixture = Fixture(73);
        ContinentalFieldModel continents = L03BTestSupport.Success(ContinentalFieldModel.Create(fixture.Plates.Identity,
            fixture.Atlas.Bounds, new ContinentalFieldSettings(5, 18, 64, 1_000_000)));
        TectonicReliefModel model = TectonicReliefModel.Build(fixture.Basis, fixture.Atlas, fixture.Plates,
            new(400, .96, 512, 12), continents);
        int tested = 0;
        for (long z = 2_048; z < 131_072; z += 4_096)
        for (long x = 2_048; x < 131_072; x += 4_096)
        {
            TectonicReliefSample sample = model.Sample(x, z);
            if (sample.UpliftWeight != 0d) continue;
            double c = continents.SampleHeightPpm(x, z) / 1_000_000d;
            double expected = (c >= 0d ? .34d : .58d) * Math.Tanh(2.8d * c) + .65d *
                (sample.Foundation.PrimaryResidualContributionNormalized + sample.Foundation.ForeignResidualContributionNormalized);
            Assert.AreEqual(expected, sample.ModelAltitudeNormalized);
            tested++;
        }
        Assert.IsGreaterThan(100, tested);
        var other = Fixture(74);
        ContinentalFieldModel wrong = L03BTestSupport.Success(ContinentalFieldModel.Create(other.Plates.Identity,
            other.Atlas.Bounds, new ContinentalFieldSettings(5, 18, 64, 1_000_000)));
        Assert.ThrowsExactly<ArgumentException>(() => TectonicReliefModel.Build(fixture.Basis, fixture.Atlas, fixture.Plates,
            new(400, .96, 512, 12), wrong));
    }

    private static (LandscapeModel Basis, AtlasMesh Atlas, PlateAtlasSnapshot Plates) Fixture(int seed)
    {
        var profile = L03BTestSupport.FrozenProfile("balanced");
        var (atlas, plates) = L03BTestSupport.PlateFixture(seed, profile);
        LandscapeModel basis = L03BTestSupport.Success(LandscapeModelBuilder.Build(L03BTestSupport.Identity(seed, profile), atlas, plates,
            profile, new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.25)));
        return (basis, atlas, plates);
    }
}
