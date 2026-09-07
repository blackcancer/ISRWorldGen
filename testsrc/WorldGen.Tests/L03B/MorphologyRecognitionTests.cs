using System.Reflection;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03B;

[TestClass]
public sealed class MorphologyRecognitionTests
{
    [TestMethod]
    public void EveryFamilyRetainsDetrendedResidualEnergyAfterComposition()
    {
        MethodInfo compose = typeof(LandscapeModel).GetMethod("ComposeCellAltitude", BindingFlags.NonPublic | BindingFlags.Static)!;
        var energies = new Dictionary<LandscapeFamily, double>();
        foreach (LandscapeFamilyProfile family in LandscapeFamilyCatalog.Profiles)
        {
            LandscapeCellProfile cell = new(
                StableId.Derive(RandomDomain.Geology, StableId.Zero, 991),
                StableId.Zero,
                family.Family,
                CrustKind.Continental,
                500_000,
                360_000,
                .18,
                .04,
                family.ParameterChecksum);
            double[] values = Grid(25).Select(point =>
            {
                double signature = LandscapeSignatureSampler.Sample(
                    family,
                    point.X * family.MacroWavelengthBlocks,
                    point.Z * family.MacroWavelengthBlocks,
                    20260907,
                    991);
                return (double)compose.Invoke(null, [cell, family, signature])!;
            }).ToArray();
            double energy = DetrendedEnergy(values, Grid(25).ToArray());
            energies.Add(family.Family, energy);
            Assert.IsGreaterThan(0.00001d, energy, $"{family.Family} residual must survive the geological datum.");
        }

        Assert.IsLessThan(energies[LandscapeFamily.RuggedRanges], energies[LandscapeFamily.Plains],
            "Continental plains must remain materially calmer than multi-ridge ranges.");
    }

    [TestMethod]
    public void FamilyPrimitivesHaveDistinctObjectiveMorphologyProbes()
    {
        IReadOnlyList<(double X, double Z)> points = Grid(41).ToArray();
        var energies = new Dictionary<LandscapeFamily, double>();
        var spans = new Dictionary<LandscapeFamily, double>();
        foreach (LandscapeFamilyProfile family in LandscapeFamilyCatalog.Profiles)
        {
            double[] values = points.Select(point => LandscapeSignatureSampler.Sample(
                family,
                point.X * family.MacroWavelengthBlocks,
                point.Z * family.MacroWavelengthBlocks,
                73,
                17)).ToArray();
            energies.Add(family.Family, DetrendedEnergy(values, points));
            spans.Add(family.Family, values.Max() - values.Min());
            Assert.IsGreaterThan(.04d, spans[family.Family], $"{family.Family} needs a measurable structural span.");
        }

        Assert.IsGreaterThan(energies[LandscapeFamily.Plains] * 4, energies[LandscapeFamily.RuggedRanges]);
        Assert.IsGreaterThan(energies[LandscapeFamily.Plains] * 4, energies[LandscapeFamily.VolcanicDomains]);
        Assert.IsGreaterThan(.12d, spans[LandscapeFamily.Plateaus], "Plateau needs a flat high surface and a break.");
        Assert.IsGreaterThan(.12d, spans[LandscapeFamily.SedimentaryBasins], "Basin needs a closed floor and enclosing rim.");
    }

    [TestMethod]
    public void QuietOceanCellsAreNeverPublishedAsContinentalPlains()
    {
        MethodInfo classify = typeof(LandscapeModelBuilder).GetMethod("Classify", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (int seed in new[] { -437287116, 73, 20260907 })
        {
            PlateCellState ocean = new(
                StableId.Derive(RandomDomain.Geology, StableId.Zero, (ulong)(uint)seed),
                StableId.Zero,
                CrustKind.Oceanic,
                500_000,
                -500_000,
                0,
                0);
            LandscapeFamily family = (LandscapeFamily)classify.Invoke(null, [seed, ocean])!;
            Assert.AreNotEqual(LandscapeFamily.Plains, family);
            Assert.AreEqual(LandscapeFamily.SedimentaryBasins, family);
        }
    }

    [TestMethod]
    public void AnalyticDatumResidualAndCompositionBoundsAreConstructionInvariants()
    {
        Type bounds = typeof(LandscapeModel).Assembly.GetType("ISRWorldGen.Core.Geology.Landscapes.LandscapeAltitudeBounds")!;
        MethodInfo datum = bounds.GetMethod("GeologicalDatum", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo composed = bounds.GetMethod("Composed", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo compose = typeof(LandscapeModel).GetMethod("ComposeCellAltitude", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (LandscapeFamilyProfile family in LandscapeFamilyCatalog.Profiles)
        {
            (double Minimum, double Maximum) interval = ((ValueTuple<double, double>)composed.Invoke(null, [family])!)!;
            Assert.IsGreaterThanOrEqualTo(-1d, interval.Minimum);
            Assert.IsLessThanOrEqualTo(1d, interval.Maximum);
            foreach (LandscapeCellProfile cell in BoundaryCells(family))
            {
                double baseAltitude = (double)datum.Invoke(null, [cell])!;
                Assert.IsGreaterThanOrEqualTo(-.63d, baseAltitude);
                Assert.IsLessThanOrEqualTo(.58d, baseAltitude);
                foreach (double signature in new[] { -1d, 1d })
                {
                    double altitude = (double)compose.Invoke(null, [cell, family, signature])!;
                    Assert.IsGreaterThanOrEqualTo(-1d, altitude);
                    Assert.IsLessThanOrEqualTo(1d, altitude);
                }
            }
        }
    }

    [TestMethod]
    public void ComposedModelRetainsAllSixFamilySignaturesRatherThanOnlySyntheticCellEnergy()
    {
        LandscapeModel model = Model("balanced", -437287116, out FrozenScaleProfile profile, out var atlas);
        var observed = new Dictionary<LandscapeFamily, double>();
        foreach (LandscapeFamily family in Enum.GetValues<LandscapeFamily>())
        {
            LandscapeCellProfile cell = model.Cells.First(profile => profile.Family == family);
            var site = atlas.Sites.Single(site => site.Id == cell.CellId);
            double[] values = Enumerable.Range(-24, 49).SelectMany(dx => Enumerable.Range(-24, 49)
                .Select(dz => model.Sample(
                    Math.Clamp(site.X + (dx * 24L), 0, profile.WidthBlocks - 1),
                    Math.Clamp(site.Z + (dz * 24L), 0, profile.LengthBlocks - 1)))
                .Where(sample => sample.DominantCellId == cell.CellId)
                .Select(sample => sample.ModelAltitudeNormalized)).ToArray();
            Assert.IsGreaterThan(64, values.Length, $"{family} requires a real dominant-cell sample neighbourhood.");
            observed.Add(family, values.Max() - values.Min());
            Assert.IsGreaterThan(.001d, observed[family], $"{family} must alter actual blended samples.");
            Assert.AreNotEqual(default, cell.FamilyParameterChecksum);
        }

        Assert.HasCount(6, observed, "Every family must be observed through the real compact blend.");
    }

    [TestMethod]
    public void TransectsCrossActualOwnershipOrContributorChangesWithoutVoronoiSteps()
    {
        LandscapeModel model = Model("laboratory", 73, out _, out _);
        MethodInfo contributors = typeof(LandscapeModel).GetMethod("ContributorIds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        int crossings = 0;
        foreach (long z in new[] { 512L, 1024L, 1536L, 2048L, 3072L })
        {
            for (long x = 1; x < 4095; x++)
            {
                LandscapeSample left = model.Sample(x, z);
                LandscapeSample right = model.Sample(x + 1, z);
                StableId[] leftContributors = (StableId[])contributors.Invoke(model, [x, z])!;
                StableId[] rightContributors = (StableId[])contributors.Invoke(model, [x + 1, z])!;
                if (left.DominantCellId == right.DominantCellId && leftContributors.SequenceEqual(rightContributors)) continue;
                crossings++;
                Assert.IsLessThan(.01d, Math.Abs(right.ModelAltitudeNormalized - left.ModelAltitudeNormalized), $"crossing ({x},{z})");
            }
        }

        Assert.IsGreaterThan(4, crossings, "Transects must exercise actual ownership or support-membership changes.");
    }

    private static IEnumerable<LandscapeCellProfile> BoundaryCells(LandscapeFamilyProfile family) =>
    [new(StableId.Zero, StableId.Zero, family.Family, CrustKind.Oceanic, 0, -1_000_000, 0, 1, family.ParameterChecksum),
     new(StableId.Zero, StableId.Zero, family.Family, CrustKind.Continental, 0, 1_000_000, 1, 0, family.ParameterChecksum)];

    private static LandscapeModel Model(string profileId, int seed, out FrozenScaleProfile profile, out ISRWorldGen.Core.Atlas.Geometry.AtlasMesh atlas)
    {
        profile = L03BTestSupport.FrozenProfile(profileId);
        var identity = L03BTestSupport.Identity(seed, profile);
        (atlas, var plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
        return L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, profile,
            new LandscapeGenerationSettings(profileId == "laboratory" ? new ReliefBudgetRequest(28, 40, 96) : new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.25)));
    }

    private static IEnumerable<(double X, double Z)> Grid(int side)
    {
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                yield return (-1d + ((2d * x) / (side - 1)), -1d + ((2d * z) / (side - 1)));
            }
        }
    }

    private static double DetrendedEnergy(IReadOnlyList<double> values, IReadOnlyList<(double X, double Z)> points)
    {
        double mean = values.Average();
        double meanX = points.Average(point => point.X);
        double meanZ = points.Average(point => point.Z);
        double varianceX = points.Sum(point => (point.X - meanX) * (point.X - meanX));
        double varianceZ = points.Sum(point => (point.Z - meanZ) * (point.Z - meanZ));
        double slopeX = points.Zip(values, (point, value) => (point.X - meanX) * (value - mean)).Sum() / varianceX;
        double slopeZ = points.Zip(values, (point, value) => (point.Z - meanZ) * (value - mean)).Sum() / varianceZ;
        return points.Zip(values, (point, value) =>
        {
            double fitted = mean + (slopeX * (point.X - meanX)) + (slopeZ * (point.Z - meanZ));
            double residual = value - fitted;
            return residual * residual;
        }).Average();
    }
}
