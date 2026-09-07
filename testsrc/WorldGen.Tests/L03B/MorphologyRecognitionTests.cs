using System.Reflection;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03B;

[TestClass]
public sealed class MorphologyRecognitionTests
{
    private const int RecognitionGridSide = 65;
    private static readonly IReadOnlyList<int> RecognitionSeeds = Array.AsReadOnly(
        new[] { 20260907, -20260907, 731, -731, 196883, -196883, 48731, -48731 });
    private static readonly Lazy<IReadOnlyDictionary<LandscapeFamily, MorphologyProbe[]>> RecognitionCorpus =
        new(BuildRecognitionCorpus);

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
    public void ActualMixedPlateausRetainAFlatInteriorAndSharpEscarpment()
    {
        MorphologyProbe[] plateaus = Probes(LandscapeFamily.Plateaus);
        MorphologyProbe[] plains = Probes(LandscapeFamily.Plains);
        double flatInterior = Median(plateaus.Select(probe => probe.HighFlatFraction));
        double plainFlat = Median(plains.Select(probe => probe.HighFlatFraction));
        double escarpmentContrast = Median(plateaus.Select(probe => probe.Curvature90 / probe.MedianCurvature));

        Assert.IsGreaterThan(.75d, flatInterior,
            $"The upper plateau surface must be flat at the one-cell relief scale; observed median fraction {flatInterior:R}.");
        Assert.IsGreaterThan(plainFlat, flatInterior,
            "A plateau must retain a broader elevated flat surface than the non-elevation-conditioned plain probe.");
        Assert.IsGreaterThan(8d, escarpmentContrast,
            $"Plateau escarpment curvature must exceed typical interior curvature eightfold; observed {escarpmentContrast:R}.");
    }

    [TestMethod]
    public void ActualMixedBasinsRetainAClosedFloorAndEnclosingRim()
    {
        MorphologyProbe[] basins = Probes(LandscapeFamily.SedimentaryBasins);
        MorphologyProbe[] plains = Probes(LandscapeFamily.Plains);
        double basinRim = Median(basins.Select(probe => probe.BroadRimLift / probe.NominalReliefIncrement));
        double plainRim = Median(plains.Select(probe => probe.BroadRimLift / probe.NominalReliefIncrement));

        Assert.IsGreaterThan(20d, basinRim,
            $"A basin floor must sit inside a broad rim by twenty mean grid relief increments; observed {basinRim:R}.");
        Assert.IsGreaterThan(plainRim * 4d, basinRim,
            "The closed basin-and-rim signal must be distinct from broad undulations in plains.");
    }

    [TestMethod]
    public void ActualMixedRangesRetainAlignedRidgesAndMultiplePeaks()
    {
        MorphologyProbe[] ranges = Probes(LandscapeFamily.RuggedRanges);
        MorphologyProbe[] massifs = Probes(LandscapeFamily.OldMassifs);
        double anisotropy = Median(ranges.Select(probe => probe.GradientAnisotropy));
        double massifAnisotropy = Median(massifs.Select(probe => probe.GradientAnisotropy));
        double peaks = Median(ranges.Select(probe => (double)probe.ProminentPeaks));

        // Anisotropy 0.6 means at least a 4:1 principal-direction gradient-energy ratio.
        Assert.IsGreaterThan(.6d, anisotropy,
            $"Range ridges must preserve a dominant direction after blending; observed anisotropy {anisotropy:R}.");
        // This is a primitive-regression discriminator, not a stand-alone R03-06
        // acceptance oracle; regional purity/transition properties are asserted in
        // LandscapeCompositionTests without a median aggregation.
        Assert.IsGreaterThan(massifAnisotropy * 1.5d, anisotropy,
            "Parallel ranges must remain materially more directional than rounded old massifs.");
        Assert.IsGreaterThanOrEqualTo(2d, peaks,
            $"Ranges must retain multiple separated prominent peaks; observed median {peaks:R}.");
    }

    [TestMethod]
    public void ActualMixedMassifsRetainMultipleSummitsAndRoundedValleysDistinctFromRanges()
    {
        MorphologyProbe[] massifs = Probes(LandscapeFamily.OldMassifs);
        MorphologyProbe[] ranges = Probes(LandscapeFamily.RuggedRanges);
        double peaks = Median(massifs.Select(probe => (double)probe.ProminentPeaks));
        double valleys = Median(massifs.Select(probe => (double)probe.ProminentValleys));
        double anisotropy = Median(massifs.Select(probe => probe.GradientAnisotropy));
        double rangeAnisotropy = Median(ranges.Select(probe => probe.GradientAnisotropy));

        Assert.IsGreaterThanOrEqualTo(2d, peaks,
            $"The twice-smoothed mixed surface must retain multiple separated massif summits; observed median {peaks:R}.");
        Assert.IsGreaterThanOrEqualTo(1d, valleys,
            $"The twice-smoothed mixed surface must retain a rounded massif valley; observed median {valleys:R}.");
        Assert.IsLessThan(rangeAnisotropy / 1.5d, anisotropy,
            "Rounded massif relief must remain less directional than parallel mountain ranges.");
    }

    [TestMethod]
    public void ActualMixedPlainsHaveTheLowestPhysicalSlopeAndCurvatureWithoutUsingAltitudeMean()
    {
        IReadOnlyDictionary<LandscapeFamily, MorphologyProbe[]> corpus = RecognitionCorpus.Value;
        double plainSlope = MedianPhysicalSlope(corpus[LandscapeFamily.Plains]);
        double plainCurvature = MedianPhysicalCurvature(corpus[LandscapeFamily.Plains]);
        double nextSlope = corpus.Where(pair => pair.Key != LandscapeFamily.Plains)
            .Min(pair => MedianPhysicalSlope(pair.Value));
        double nextCurvature = corpus.Where(pair => pair.Key != LandscapeFamily.Plains)
            .Min(pair => MedianPhysicalCurvature(pair.Value));

        Assert.IsLessThan(nextSlope, plainSlope,
            $"Plains must have the lowest median slope per block; plain={plainSlope:R}, next={nextSlope:R}.");
        Assert.IsLessThan(nextCurvature, plainCurvature,
            $"Plains must have the lowest median curvature per block squared; plain={plainCurvature:R}, next={nextCurvature:R}.");
    }

    [TestMethod]
    public void ActualMixedVolcanoesRetainConeCalderaAndSecondarySummit()
    {
        MorphologyProbe[] volcanoes = Probes(LandscapeFamily.VolcanicDomains);
        MorphologyProbe[] plains = Probes(LandscapeFamily.Plains);
        double calderaLift = Median(volcanoes.Select(probe => probe.CraterLift / probe.NominalReliefIncrement));
        double coneDrop = Median(volcanoes.Select(probe => probe.ConeDrop / probe.NominalReliefIncrement));
        double peaks = Median(volcanoes.Select(probe => (double)probe.ProminentPeaks));
        double valleys = Median(volcanoes.Select(probe => (double)probe.ProminentValleys));
        double plainCraterLift = Median(plains.Select(probe => probe.CraterLift / probe.NominalReliefIncrement));
        double plainPeaks = Median(plains.Select(probe => (double)probe.ProminentPeaks));

        Assert.IsGreaterThan(2d, calderaLift,
            $"The caldera rim must rise by two mean grid relief increments above its floor; observed {calderaLift:R}.");
        Assert.IsGreaterThan(2.5d, coneDrop,
            $"The same annular rim must fall outward into a cone by 2.5 mean increments; observed {coneDrop:R}.");
        Assert.IsGreaterThan(plainCraterLift, calderaLift,
            "The caldera depression must remain stronger than accidental annular relief in plains.");
        Assert.IsGreaterThanOrEqualTo(2d, peaks,
            $"The smoothed volcanic surface must retain a separated secondary summit; observed median peaks {peaks:R}.");
        Assert.IsGreaterThan(plainPeaks, peaks,
            "The main and secondary volcanic summits must be distinct from the calm plain surface.");
        Assert.IsGreaterThanOrEqualTo(2d, valleys,
            $"The smoothed volcanic surface must retain the caldera depression across the corpus; observed median valleys {valleys:R}.");
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
                if (left.DominantCellId == right.DominantCellId && leftContributors.SequenceEqual(rightContributors))
                {
                    continue;
                }

                crossings++;
                Assert.IsLessThan(.01d, Math.Abs(right.ModelAltitudeNormalized - left.ModelAltitudeNormalized), $"crossing ({x},{z})");
            }
        }

        Assert.IsGreaterThan(4, crossings, "Transects must exercise actual ownership or support-membership changes.");
    }

    private static MorphologyProbe[] Probes(LandscapeFamily family)
    {
        MorphologyProbe[] probes = RecognitionCorpus.Value[family];
        Assert.HasCount(RecognitionSeeds.Count, probes, $"Every declared seed must expose an interior {family} fixture.");
        return probes;
    }

    private static IReadOnlyDictionary<LandscapeFamily, MorphologyProbe[]> BuildRecognitionCorpus()
    {
        var result = Enum.GetValues<LandscapeFamily>().ToDictionary(family => family, _ => new List<MorphologyProbe>());
        foreach (int seed in RecognitionSeeds)
        {
            LandscapeModel model = Model("vast-expeditions", seed, out _, out var atlas);
            var sites = atlas.Sites.ToDictionary(site => site.Id);
            foreach (LandscapeFamily family in Enum.GetValues<LandscapeFamily>())
            {
                LandscapeFamilyProfile familyProfile = LandscapeFamilyCatalog.Get(family);
                double spanBlocks = familyProfile.MacroWavelengthBlocks * 1.7;
                LandscapeCellProfile? chosen = null;
                foreach (LandscapeCellProfile cell in model.Cells.Where(cell => cell.Family == family))
                {
                    var site = sites[cell.CellId];
                    double half = spanBlocks / 2;
                    if (site.X - half >= atlas.Bounds.MinX && site.X + half <= atlas.Bounds.MaxXExclusive - 1 &&
                        site.Z - half >= atlas.Bounds.MinZ && site.Z + half <= atlas.Bounds.MaxZExclusive - 1)
                    {
                        chosen = cell;
                        break;
                    }
                }

                if (chosen is null)
                {
                    continue;
                }

                var center = sites[chosen.Value.CellId];
                var samples = new double[RecognitionGridSide, RecognitionGridSide];
                for (int z = 0; z < RecognitionGridSide; z++)
                {
                    for (int x = 0; x < RecognitionGridSide; x++)
                    {
                        long sampleX = checked((long)Math.Round(center.X + (((x / (double)(RecognitionGridSide - 1)) - .5) * spanBlocks)));
                        long sampleZ = checked((long)Math.Round(center.Z + (((z / (double)(RecognitionGridSide - 1)) - .5) * spanBlocks)));
                        samples[z, x] = model.Sample(sampleX, sampleZ).ModelAltitudeNormalized;
                    }
                }

                result[family].Add(Measure(samples, spanBlocks / (RecognitionGridSide - 1)));
            }
        }

        return result.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static MorphologyProbe Measure(double[,] values, double stepBlocks)
    {
        double[,] residual = Detrend(values);
        double[] gradients = GradientMagnitudes(residual);
        double[] curvatures = Curvatures(residual);
        double span = residual.Cast<double>().Max() - residual.Cast<double>().Min();
        double nominalIncrement = span / RecognitionGridSide;
        double[,] smooth = BoxBlur(BoxBlur(residual));
        (double craterLift, double coneDrop) = BestNestedRelief(smooth, 5, 15);
        return new MorphologyProbe(
            span,
            stepBlocks,
            Quantile(gradients, .5),
            Quantile(curvatures, .5),
            Quantile(curvatures, .9),
            GradientAnisotropy(residual),
            CountProminentExtrema(smooth, true, nominalIncrement),
            CountProminentExtrema(smooth, false, nominalIncrement),
            BestRingLift(smooth, 14, 26),
            craterLift,
            coneDrop,
            HighFlatFraction(residual, nominalIncrement));
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

    private static double[,] Detrend(double[,] values)
    {
        int height = values.GetLength(0);
        int width = values.GetLength(1);
        var points = new (double X, double Z)[height * width];
        var samples = new double[height * width];
        int index = 0;
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                points[index] = (-1d + (2d * x / (width - 1)), -1d + (2d * z / (height - 1)));
                samples[index++] = values[z, x];
            }
        }

        double mean = samples.Average();
        double varianceX = points.Sum(point => point.X * point.X);
        double varianceZ = points.Sum(point => point.Z * point.Z);
        double slopeX = points.Zip(samples, (point, value) => point.X * (value - mean)).Sum() / varianceX;
        double slopeZ = points.Zip(samples, (point, value) => point.Z * (value - mean)).Sum() / varianceZ;
        var residual = new double[height, width];
        index = 0;
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                residual[z, x] = samples[index] - mean - (slopeX * points[index].X) - (slopeZ * points[index].Z);
                index++;
            }
        }

        return residual;
    }

    private static double[] GradientMagnitudes(double[,] values)
    {
        var result = new List<double>();
        for (int z = 1; z < values.GetLength(0) - 1; z++)
        {
            for (int x = 1; x < values.GetLength(1) - 1; x++)
            {
                (double gx, double gz) = Gradient(values, x, z);
                result.Add(Math.Sqrt((gx * gx) + (gz * gz)));
            }
        }

        return result.ToArray();
    }

    private static double[] Curvatures(double[,] values)
    {
        var result = new List<double>();
        for (int z = 1; z < values.GetLength(0) - 1; z++)
        {
            for (int x = 1; x < values.GetLength(1) - 1; x++)
            {
                result.Add(Math.Abs(values[z, x - 1] + values[z, x + 1] + values[z - 1, x] + values[z + 1, x] - (4 * values[z, x])));
            }
        }

        return result.ToArray();
    }

    private static double GradientAnisotropy(double[,] values)
    {
        double xx = 0;
        double zz = 0;
        double xz = 0;
        for (int z = 1; z < values.GetLength(0) - 1; z++)
        {
            for (int x = 1; x < values.GetLength(1) - 1; x++)
            {
                (double gx, double gz) = Gradient(values, x, z);
                xx += gx * gx;
                zz += gz * gz;
                xz += gx * gz;
            }
        }

        double trace = xx + zz;
        return Math.Sqrt(((xx - zz) * (xx - zz)) + (4 * xz * xz)) / trace;
    }

    private static double HighFlatFraction(double[,] values, double maximumGradient)
    {
        double high = Quantile(values.Cast<double>(), .6);
        int flat = 0;
        int count = 0;
        for (int z = 1; z < values.GetLength(0) - 1; z++)
        {
            for (int x = 1; x < values.GetLength(1) - 1; x++)
            {
                if (values[z, x] < high)
                {
                    continue;
                }

                (double gx, double gz) = Gradient(values, x, z);
                if (Math.Sqrt((gx * gx) + (gz * gz)) <= maximumGradient)
                {
                    flat++;
                }

                count++;
            }
        }

        return flat / (double)count;
    }

    private static (double X, double Z) Gradient(double[,] values, int x, int z) =>
        ((values[z, x + 1] - values[z, x - 1]) / 2, (values[z + 1, x] - values[z - 1, x]) / 2);

    private static double[,] BoxBlur(double[,] values)
    {
        var result = new double[values.GetLength(0), values.GetLength(1)];
        for (int z = 0; z < values.GetLength(0); z++)
        {
            for (int x = 0; x < values.GetLength(1); x++)
            {
                double total = 0;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        total += values[Math.Clamp(z + dz, 0, values.GetLength(0) - 1), Math.Clamp(x + dx, 0, values.GetLength(1) - 1)];
                    }
                }

                result[z, x] = total / 9;
            }
        }

        return result;
    }

    private static int CountProminentExtrema(double[,] values, bool maxima, double threshold)
    {
        int count = 0;
        const int radius = 4;
        for (int z = radius; z < values.GetLength(0) - radius; z++)
        {
            for (int x = radius; x < values.GetLength(1) - radius; x++)
            {
                double center = values[z, x];
                double ring = 0;
                int ringCount = 0;
                bool extremum = true;
                for (int dz = -radius; dz <= radius; dz++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx == 0 && dz == 0)
                        {
                            continue;
                        }

                        double other = values[z + dz, x + dx];
                        if ((maxima && other >= center) || (!maxima && other <= center))
                        {
                            extremum = false;
                        }

                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == radius)
                        {
                            ring += other;
                            ringCount++;
                        }
                    }
                }

                double prominence = maxima ? center - (ring / ringCount) : (ring / ringCount) - center;
                if (extremum && prominence > threshold)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static double BestRingLift(double[,] values, int minimumRadius, int maximumRadius)
    {
        double best = double.NegativeInfinity;
        for (int z = maximumRadius; z < values.GetLength(0) - maximumRadius; z++)
        {
            for (int x = maximumRadius; x < values.GetLength(1) - maximumRadius; x++)
            {
                for (int radius = minimumRadius; radius <= maximumRadius; radius++)
                {
                    best = Math.Max(best, RingMean(values, x, z, radius) - values[z, x]);
                }
            }
        }

        return best;
    }

    private static (double CraterLift, double ConeDrop) BestNestedRelief(double[,] values, int rimRadius, int outerRadius)
    {
        (double CraterLift, double ConeDrop) best = (double.NegativeInfinity, double.NegativeInfinity);
        double bestScore = double.NegativeInfinity;
        for (int z = outerRadius; z < values.GetLength(0) - outerRadius; z++)
        {
            for (int x = outerRadius; x < values.GetLength(1) - outerRadius; x++)
            {
                double rim = RingMean(values, x, z, rimRadius);
                double craterLift = rim - values[z, x];
                double coneDrop = rim - RingMean(values, x, z, outerRadius);
                double score = Math.Min(craterLift, coneDrop);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (craterLift, coneDrop);
                }
            }
        }

        return best;
    }

    private static double RingMean(double[,] values, int x, int z, int radius)
    {
        double total = 0;
        int count = 0;
        for (int dz = -radius - 1; dz <= radius + 1; dz++)
        {
            for (int dx = -radius - 1; dx <= radius + 1; dx++)
            {
                double distance = Math.Sqrt((dx * dx) + (dz * dz));
                if (Math.Abs(distance - radius) <= .75)
                {
                    total += values[z + dz, x + dx];
                    count++;
                }
            }
        }

        return total / count;
    }

    private static double MedianPhysicalSlope(IEnumerable<MorphologyProbe> probes) =>
        Median(probes.Select(probe => probe.MedianGradient / probe.StepBlocks));

    private static double MedianPhysicalCurvature(IEnumerable<MorphologyProbe> probes) =>
        Median(probes.Select(probe => probe.MedianCurvature / (probe.StepBlocks * probe.StepBlocks)));

    private static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.Order().ToArray();
        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }

    private static double Quantile(IEnumerable<double> values, double probability)
    {
        double[] ordered = values.Order().ToArray();
        return ordered[(int)Math.Round(probability * (ordered.Length - 1))];
    }

    private sealed record MorphologyProbe(
        double Span,
        double StepBlocks,
        double MedianGradient,
        double MedianCurvature,
        double Curvature90,
        double GradientAnisotropy,
        int ProminentPeaks,
        int ProminentValleys,
        double BroadRimLift,
        double CraterLift,
        double ConeDrop,
        double HighFlatFraction)
    {
        internal double NominalReliefIncrement => Span / RecognitionGridSide;
    }
}
