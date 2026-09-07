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
    public void LocalMorphologyIsContinuousAndNotAVisibleVoronoiStep()
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("laboratory");
        var identity = L03BTestSupport.Identity(73, profile);
        var (atlas, plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(
            identity,
            atlas,
            plates,
            profile,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(28, 40, 96), profile.SiteQuota, 1.25)));

        foreach ((long x, long z) in new[] { (768L, 1024L), (896L, 896L), (1536L, 1664L) })
        {
            double left = model.Sample(x, z).ModelAltitudeNormalized;
            double right = model.Sample(x + 1, z).ModelAltitudeNormalized;
            Assert.IsLessThan(.01d, Math.Abs(right - left), $"one-block transition at ({x},{z})");
        }
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
