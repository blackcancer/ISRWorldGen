using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;
using ISRWorldGen.Core.Foundation;
using System.Globalization;
using System.Reflection;

namespace ISRWorldGen.Tests.L03B;

[TestClass]
public sealed class LandscapeCompositionTests
{
    [TestMethod]
    public void CatalogContainsSixDistinctFamiliesWithDistinctParametersAndSignatures()
    {
        LandscapeFamilyProfile[] profiles = LandscapeFamilyCatalog.Profiles.ToArray();
        CollectionAssert.AreEquivalent(
            Enum.GetValues<LandscapeFamily>(),
            profiles.Select(profile => profile.Family).ToArray());
        Assert.HasCount(6, profiles);
        Assert.AreEqual(6, profiles.Select(profile => profile.ParameterChecksum).Distinct().Count());

        double[][] signatures = profiles.Select(profile => Enumerable.Range(0, 257)
            .Select(index => LandscapeSignatureSampler.Sample(
                profile,
                index * 113.0,
                (index * index * 17.0) + 31,
                seed: 20260906,
                streamOrdinal: 9))
            .ToArray()).ToArray();
        for (int left = 0; left < signatures.Length; left++)
        {
            Assert.IsTrue(signatures[left].All(value => double.IsFinite(value) && value is >= -1 and <= 1));
            Assert.IsGreaterThan(0.0001, Variance(signatures[left]));
            for (int right = left + 1; right < signatures.Length; right++)
            {
                double meanAbsoluteDifference = signatures[left]
                    .Zip(signatures[right], (a, b) => Math.Abs(a - b))
                    .Average();
                Assert.IsGreaterThan(0.035, meanAbsoluteDifference,
                    $"{profiles[left].Family} and {profiles[right].Family}");
            }
        }
    }

    [TestMethod]
    public void ModelConsumesFrozenProfileAndPlateSnapshotWithoutMutatingEither()
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("balanced");
        GenerationIdentity identity = L03BTestSupport.Identity(-437287116, profile);
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
        Hash256 plateChecksum = plates.ContentChecksum;
        Hash256 profileChecksum = profile.GeographyConfigHash;
        LandscapeGenerationSettings settings = new(
            new ReliefBudgetRequest(64, 48, 128),
            maximumCells: profile.SiteQuota,
            supportOverlapFactor: 1.25);

        LandscapeModel first = L03BTestSupport.Success(
            LandscapeModelBuilder.Build(identity, atlas, plates, profile, settings));
        LandscapeModel repeated = L03BTestSupport.Success(
            LandscapeModelBuilder.Build(identity, atlas, plates, profile, settings));

        Assert.AreEqual(first.ContentChecksum, repeated.ContentChecksum);
        Assert.AreEqual(plateChecksum, plates.ContentChecksum);
        Assert.AreEqual(profileChecksum, profile.GeographyConfigHash);
        CollectionAssert.AreEqual(first.Cells.ToArray(), repeated.Cells.ToArray());
        Assert.IsGreaterThanOrEqualTo(3, first.Cells.Select(cell => cell.Family).Distinct().Count());
        Assert.IsTrue(first.Cells.Any(cell => cell.UpliftNormalized == 0 && cell.SubsidenceNormalized == 0),
            "A calm plate interior from L03-A must remain represented.");

        foreach ((long x, long z) in SampleCoordinates(profile))
        {
            LandscapeSample a = first.Sample(x, z);
            LandscapeSample b = repeated.Sample(x, z);
            Assert.AreEqual(a, b);
            Assert.IsGreaterThanOrEqualTo(-1, a.ModelAltitudeNormalized);
            Assert.IsLessThanOrEqualTo(1, a.ModelAltitudeNormalized);
            Assert.AreEqual(
                first.VerticalPlan.Transform.MapModelAltitudeToBlocks(a.ModelAltitudeNormalized),
                a.AltitudeBlocks);
            Assert.AreEqual(Math.Max(0, first.VerticalPlan.Transform.SeaLevelBlocks - a.AltitudeBlocks),
                a.BathymetryBlocks);
        }
    }

    [TestMethod]
    public void SamplingUsesGlobalCoordinatesAndIsIndependentOfWindowOrder()
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("laboratory");
        GenerationIdentity identity = L03BTestSupport.Identity(73, profile);
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(
            identity,
            atlas,
            plates,
            profile,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(28, 40, 96), 32, 1.25)));
        (long X, long Z)[] coordinates = SampleCoordinates(profile).ToArray();
        LandscapeSample[] forward = coordinates.Select(item => model.Sample(item.X, item.Z)).ToArray();
        LandscapeSample[] reverse = coordinates.Reverse().Select(item => model.Sample(item.X, item.Z)).Reverse().ToArray();

        CollectionAssert.AreEqual(forward, reverse);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.Sample(-1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.Sample(profile.WidthBlocks, 0));
    }

    [TestMethod]
    public void CellBudgetAndMismatchedFrozenProfileFailBeforePublication()
    {
        FrozenScaleProfile balanced = L03BTestSupport.FrozenProfile("balanced");
        FrozenScaleProfile laboratory = L03BTestSupport.FrozenProfile("laboratory");
        GenerationIdentity identity = L03BTestSupport.Identity(73, balanced);
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, balanced);
        (AtlasMesh wrongAtlas, _) = L03BTestSupport.PlateFixture(74, balanced);

        GenerationResult<LandscapeModel> budget = LandscapeModelBuilder.Build(
            identity,
            atlas,
            plates,
            balanced,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), 1, 1.25));
        GenerationResult<LandscapeModel> mismatch = LandscapeModelBuilder.Build(
            identity,
            atlas,
            plates,
            laboratory,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(28, 40, 96), 128, 1.25));
        GenerationResult<LandscapeModel> budgetBeforeProvenance = LandscapeModelBuilder.Build(
            identity,
            wrongAtlas,
            plates,
            balanced,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), 1, 1.25));

        AssertFailure(budget, GenerationFailureCode.BudgetExceeded, "geology.landscapes.cell-budget");
        AssertFailure(mismatch, GenerationFailureCode.InvalidInput, "geology.landscapes.profile-hash");
        AssertFailure(budgetBeforeProvenance, GenerationFailureCode.BudgetExceeded, "geology.landscapes.cell-budget");
    }

    [TestMethod]
    public void MismatchedAtlasAndSealedPlateSnapshotFailBeforeSamplingAndCannotShareChecksum()
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("balanced");
        GenerationIdentity identityA = L03BTestSupport.Identity(7301, profile);
        GenerationIdentity identityB = L03BTestSupport.Identity(7302, profile);
        (AtlasMesh atlasA, PlateAtlasSnapshot platesA) = L03BTestSupport.PlateFixture(identityA.NativeSeed, profile);
        (AtlasMesh atlasB, PlateAtlasSnapshot platesB) = L03BTestSupport.PlateFixture(identityB.NativeSeed, profile);
        var settings = new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.25);

        GenerationResult<LandscapeModel> rejected = LandscapeModelBuilder.Build(identityA, atlasB, platesA, profile, settings);
        AssertFailure(rejected, GenerationFailureCode.InvalidInput, "geology.landscapes.plate-provenance");

        LandscapeModel modelA = L03BTestSupport.Success(LandscapeModelBuilder.Build(identityA, atlasA, platesA, profile, settings));
        LandscapeModel modelB = L03BTestSupport.Success(LandscapeModelBuilder.Build(identityB, atlasB, platesB, profile, settings));
        Assert.AreNotEqual(modelA.ContentChecksum, modelB.ContentChecksum);
        Assert.AreEqual(platesA.ContentChecksum, modelA.PlateSnapshotChecksum);
        Assert.AreEqual(platesA.AtlasContentChecksum, modelA.AtlasContentChecksum);
    }

    [TestMethod]
    [DoNotParallelize]
    public void ContentChecksumIsInvariantUnderHostileCulture()
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("balanced");
        GenerationIdentity identity = L03BTestSupport.Identity(-437287116, profile);
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
        var settings = new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.25);
        Hash256 invariant = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, profile, settings)).ContentChecksum;
        CultureInfo old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Hash256 hostile = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, profile, settings)).ContentChecksum;
            Assert.AreEqual(invariant, hostile);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [TestMethod]
    public void SiteAnchoredMorphologyTranslatesToRemoteVastWorldCellsWithNonzeroVariance()
    {
        LandscapeFamilyProfile family = LandscapeFamilyCatalog.Get(LandscapeFamily.VolcanicDomains);
        double near = LandscapeSignatureSampler.Sample(family, 12_400, -8_200, 97, 123, 0, 0);
        double remote = LandscapeSignatureSampler.Sample(family, 912_400, -808_200, 97, 123, 900_000, -800_000);
        Assert.AreEqual(near, remote, "A remote owner site must receive the same local morphology.");

        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("vast-expeditions");
        GenerationIdentity identity = L03BTestSupport.Identity(90210, profile);
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, profile,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.25)));
        double[] samples = Enumerable.Range(1, 64).Select(index => model.Sample(
            (profile.WidthBlocks * index) / 65,
            (profile.LengthBlocks * ((index * 23) % 64 + 1)) / 65).ModelAltitudeNormalized).ToArray();
        Assert.IsGreaterThan(0.0001, Variance(samples));
    }

    [TestMethod]
    public void PublishedMorphologyParametersEachChangeTheSampleAndSiteLimitIsContinuous()
    {
        foreach (LandscapeFamilyProfile source in LandscapeFamilyCatalog.Profiles)
        {
            double baseline = LandscapeSignatureSampler.Sample(source, 12_345, -6_789, 73, 11);
            foreach (LandscapeFamilyProfile changed in new[]
            {
                new LandscapeFamilyProfile(source.Family, source.MacroWavelengthBlocks * 1.11, source.MesoWavelengthBlocks, source.DetailWavelengthBlocks, source.ReliefAmplitudeNormalized, source.MacroWeight, source.MesoWeight, source.DetailWeight),
                new LandscapeFamilyProfile(source.Family, source.MacroWavelengthBlocks, source.MesoWavelengthBlocks * 1.11, source.DetailWavelengthBlocks, source.ReliefAmplitudeNormalized, source.MacroWeight, source.MesoWeight, source.DetailWeight),
                new LandscapeFamilyProfile(source.Family, source.MacroWavelengthBlocks, source.MesoWavelengthBlocks, source.DetailWavelengthBlocks * 1.11, source.ReliefAmplitudeNormalized, source.MacroWeight, source.MesoWeight, source.DetailWeight),
                new LandscapeFamilyProfile(source.Family, source.MacroWavelengthBlocks, source.MesoWavelengthBlocks, source.DetailWavelengthBlocks, source.ReliefAmplitudeNormalized, source.MacroWeight + .01, source.MesoWeight - .01, source.DetailWeight),
                new LandscapeFamilyProfile(source.Family, source.MacroWavelengthBlocks, source.MesoWavelengthBlocks, source.DetailWavelengthBlocks, source.ReliefAmplitudeNormalized, source.MacroWeight, source.MesoWeight + .01, source.DetailWeight - .01),
                new LandscapeFamilyProfile(source.Family, source.MacroWavelengthBlocks, source.MesoWavelengthBlocks, source.DetailWavelengthBlocks, source.ReliefAmplitudeNormalized, source.MacroWeight - .01, source.MesoWeight, source.DetailWeight + .01),
            }) Assert.IsTrue(new[] { (.1 * source.MacroWavelengthBlocks, -.1 * source.MacroWavelengthBlocks), (.7 * source.MacroWavelengthBlocks, .2 * source.MacroWavelengthBlocks), (-.4 * source.MacroWavelengthBlocks, .5 * source.MacroWavelengthBlocks) }
                .Any(point => LandscapeSignatureSampler.Sample(source, point.Item1, point.Item2, 73, 11) != LandscapeSignatureSampler.Sample(changed, point.Item1, point.Item2, 73, 11)), source.Family.ToString());
        }

        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("balanced");
        GenerationIdentity identity = L03BTestSupport.Identity(73, profile);
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, profile, new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.25)));
        AtlasSite site = atlas.Sites.First(item => item.X > atlas.Bounds.MinX && item.X < atlas.Bounds.MaxXExclusive - 1);
        double center = model.Sample(site.X, site.Z).ModelAltitudeNormalized;
        double left = model.Sample(site.X - 1, site.Z).ModelAltitudeNormalized;
        double right = model.Sample(site.X + 1, site.Z).ModelAltitudeNormalized;
        Assert.IsLessThan(.01, Math.Max(Math.Abs(center - left), Math.Abs(center - right)), "One block is far below the blend scale; a continuous weighted field cannot jump materially.");
    }

    [TestMethod]
    public void LandscapeProfileRejectsHostilePublishedParameters()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LandscapeFamilyProfile(LandscapeFamily.Plains, double.NaN, 2, 1, .1, .6, .3, .1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LandscapeFamilyProfile(LandscapeFamily.Plains, 10, 20, 1, .1, .6, .3, .1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LandscapeFamilyProfile(LandscapeFamily.Plains, 10, 2, 1, 1.1, .6, .3, .1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LandscapeFamilyProfile(LandscapeFamily.Plains, 10, 2, 1, .1, .6, .3, .2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LandscapeFamilyProfile(LandscapeFamily.Plains, double.MaxValue, 1, double.Epsilon, .1, .6, .3, .1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LandscapeSignatureSampler.Sample(LandscapeFamilyCatalog.Get(LandscapeFamily.Plains), double.MaxValue, 0, 1, 1));
        foreach (LandscapeFamilyProfile family in LandscapeFamilyCatalog.Profiles.Concat(Enum.GetValues<LandscapeFamily>().Select(item => new LandscapeFamilyProfile(item, 1_000_000, 2, 1, .2, .34, .33, .33))))
        {
            double value = LandscapeSignatureSampler.Sample(family, LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks, -LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks, 1, 1, -(long)LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks, (long)LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks);
            double repeated = LandscapeSignatureSampler.Sample(family, LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks, -LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks, 1, 1, -(long)LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks, (long)LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks);
            Assert.IsTrue(double.IsFinite(value) && value is >= -1 and <= 1);
            Assert.AreEqual(value, repeated);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LandscapeSignatureSampler.Sample(family, 0, 0, 1, 1, long.MinValue, long.MaxValue));
        }

        foreach (LandscapeFamily family in Enum.GetValues<LandscapeFamily>())
        {
            foreach ((double macroWeight, double mesoWeight, double detailWeight) weights in new[]
            {
                (1d, 0d, 0d),
                (0d, 1d, 0d),
                (0d, 0d, 1d),
            })
            {
                LandscapeFamilyProfile extreme = new(family, 1_000_000, 2, 1, .2, weights.macroWeight, weights.mesoWeight, weights.detailWeight);
                double value = LandscapeSignatureSampler.Sample(
                    extreme,
                    LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks,
                    -LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks,
                    1,
                    1,
                    -(long)LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks,
                    (long)LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks);
                Assert.IsTrue(double.IsFinite(value) && value is > -1 and < 1, $"{family} extreme ratio {weights}");
            }
        }
    }

    [TestMethod]
    public void ReliefAmplitudeChangesComposedCellAltitudeWithinEnvelope()
    {
        LandscapeFamilyProfile low = new(LandscapeFamily.Plains, 42_000, 12_000, 3_200, .03, .65, .25, .10);
        LandscapeFamilyProfile high = new(LandscapeFamily.Plains, 42_000, 12_000, 3_200, .20, .65, .25, .10);
        LandscapeCellProfile cell = new(StableId.Zero, StableId.Zero, LandscapeFamily.Plains, CrustKind.Continental, 10, 100_000, .1, .05, low.ParameterChecksum);
        MethodInfo compose = typeof(LandscapeModel).GetMethod("ComposeCellAltitude", BindingFlags.NonPublic | BindingFlags.Static)!;
        double a = (double)compose.Invoke(null, [cell, low, .6])!;
        double b = (double)compose.Invoke(null, [cell, high, .6])!;
        Assert.AreNotEqual(low.ParameterChecksum, high.ParameterChecksum);
        Assert.AreNotEqual(a, b);
        Assert.IsTrue(double.IsFinite(a) && double.IsFinite(b) && a is >= -1 and <= 1 && b is >= -1 and <= 1);
    }

    [TestMethod]
    public void AnalyticPrimitiveBoundsAvoidUniformClippingForPublishedExtremeProfiles()
    {
        foreach (LandscapeFamily family in Enum.GetValues<LandscapeFamily>())
        {
            foreach ((double macroWeight, double mesoWeight, double detailWeight) weights in new[]
            {
                (1d, 0d, 0d),
                (0d, 1d, 0d),
                (0d, 0d, 1d),
            })
            {
                LandscapeFamilyProfile profile = new(family, 3, 2, 1, .2, weights.macroWeight, weights.mesoWeight, weights.detailWeight);
                foreach ((double x, double z) point in new[] { (0d, 0d), (-4_000_000_000_000d, 0d), (31d, -17d) })
                {
                    double value = LandscapeSignatureSampler.Sample(profile, point.x, point.z, 73, 11, (long)point.x, (long)point.z);
                    Assert.IsTrue(value is > -1 and < 1, $"{family} {weights} at {point} must be analytically bounded, not clipped.");
                }
            }
        }

        LandscapeFamilyProfile basin = LandscapeFamilyCatalog.Get(LandscapeFamily.SedimentaryBasins);
        double center = LandscapeSignatureSampler.Sample(basin, 0, 0, 73, 11, 0, 0);
        double neighbor = LandscapeSignatureSampler.Sample(basin, 1, 0, 73, 11, 0, 0);
        Assert.AreNotEqual(-1d, center, "Basin center must retain its analytic depth rather than be flattened by a clamp.");
        Assert.AreNotEqual(-1d, neighbor, "Basin neighbor must retain its analytic depth rather than be flattened by a clamp.");
    }

    [TestMethod]
    public void CompactSupportRemovesMembershipJumpsAtFormerRankExchangeCoordinates()
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("laboratory");
        GenerationIdentity identity = L03BTestSupport.Identity(73, profile);
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(
            identity,
            atlas,
            plates,
            profile,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(28, 40, 96), profile.SiteQuota, 1.25)));

        // These are local regressions for the two prior rank-exchange reports, not a campaign acceptance threshold.
        // The independently measured compact-support maxima were about 8.1e-4; 1.1e-3 leaves only run-to-run margin.
        Assert.IsLessThan(.0011, AdjacentDelta(model, 896, 1020, 896, 1021));
        Assert.IsLessThan(.0011, AdjacentDelta(model, 2104, 1024, 2105, 1024));

        foreach (IEnumerable<(long X, long Z)> line in new[]
        {
            Enumerable.Range(768, 257).Select(x => ((long)x, 1024L)),
            Enumerable.Range(768, 257).Select(z => (896L, (long)z)),
            Enumerable.Range(768, 257).Select(offset => ((long)offset, (long)offset)),
        })
        {
            double[] samples = line.Select(point => model.Sample(point.X, point.Z).ModelAltitudeNormalized).ToArray();
            Assert.IsTrue(samples.All(value => double.IsFinite(value) && value is >= -1 and <= 1));
            Assert.IsLessThan(.0011, samples.Zip(samples.Skip(1), (left, right) => Math.Abs(left - right)).Max(),
                "Compact-support scan margin around the independently measured local maximum; not a T03-06 policy threshold.");
        }
    }

    [TestMethod]
    public void CompactVoronoiSupportIsLocalContinuousAndChangesThePublishedModel()
    {
        MethodInfo weight = typeof(LandscapeModel).GetMethod("CompactSupportWeight", BindingFlags.NonPublic | BindingFlags.Static)!;
        double atSite = (double)weight.Invoke(null, [0d])!;
        double nearBoundary = (double)weight.Invoke(null, [.99d])!;
        double atBoundary = (double)weight.Invoke(null, [1d])!;
        double outside = (double)weight.Invoke(null, [1.01d])!;
        Assert.IsTrue(atSite > nearBoundary && nearBoundary > 0d);
        Assert.AreEqual(0d, atBoundary);
        Assert.AreEqual(0d, outside);

        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("balanced");
        GenerationIdentity identity = L03BTestSupport.Identity(73, profile);
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
        LandscapeModel narrow = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, profile,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.05)));
        LandscapeModel wide = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, profile,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.5)));
        Assert.AreNotEqual(narrow.ContentChecksum, wide.ContentChecksum);
        Assert.IsTrue(SampleCoordinates(profile).Any(point =>
            narrow.Sample(point.X, point.Z).ModelAltitudeNormalized != wide.Sample(point.X, point.Z).ModelAltitudeNormalized));
    }

    [TestMethod]
    public void SixFamilyAnalyticRangesAndVariancesAreMeasuredAfterBoundedReformulation()
    {
        foreach (LandscapeFamilyProfile family in LandscapeFamilyCatalog.Profiles)
        {
            double[] samples = Enumerable.Range(0, 257).Select(index => LandscapeSignatureSampler.Sample(
                family,
                ((index * 487) % 31_337) - 15_000d,
                ((index * index * 71) % 29_719) - 14_000d,
                20260907,
                17)).ToArray();
            double minimum = samples.Min();
            double maximum = samples.Max();
            double variance = Variance(samples);
            Console.WriteLine($"{family.Family}: range=[{minimum:R};{maximum:R}], variance={variance:R}");
            Assert.IsTrue(samples.All(value => double.IsFinite(value) && value is > -1 and < 1));
            Assert.IsGreaterThan(0d, variance, $"{family.Family} must retain morphology after analytic bounding.");
        }
    }

    [TestMethod]
    public void CompactSupportsCoverWorldBoundariesAndEvaluateOnlyLocalMorphologies()
    {
        MethodInfo contributors = typeof(LandscapeModel).GetMethod("CountContributors", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (string profileId in new[] { "laboratory", "balanced", "vast-expeditions" })
        {
            FrozenScaleProfile profile = L03BTestSupport.FrozenProfile(profileId);
            GenerationIdentity identity = L03BTestSupport.Identity(73, profile);
            (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
            LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, profile,
                new LandscapeGenerationSettings(
                    profileId == "laboratory" ? new ReliefBudgetRequest(28, 40, 96) : new ReliefBudgetRequest(64, 48, 128),
                    profile.SiteQuota,
                    1.25)));
            (long X, long Z)[] points =
            [
                (atlas.Bounds.MinX, atlas.Bounds.MinZ),
                (atlas.Bounds.MaxXExclusive - 1, atlas.Bounds.MinZ),
                (atlas.Bounds.MinX, atlas.Bounds.MaxZExclusive - 1),
                (atlas.Bounds.MaxXExclusive - 1, atlas.Bounds.MaxZExclusive - 1),
                .. GridCoordinates(profile, 33),
            ];
            int[] counts = points.Select(point => (int)contributors.Invoke(model, [point.X, point.Z])!).ToArray();
            Console.WriteLine($"{profileId}: sites={atlas.Sites.Count}, contributors min={counts.Min()}, max={counts.Max()}, mean={counts.Average():R}");
            Assert.IsGreaterThan(0, counts.Min(), "Voronoi supports must cover bounds and corners.");
            Assert.IsLessThan(atlas.Sites.Count, counts.Max(), "SampleCell work must be local rather than all-site linear.");
            Assert.IsTrue(points.All(point => double.IsFinite(model.Sample(point.X, point.Z).ModelAltitudeNormalized)));
        }
    }

    [TestMethod]
    public void CompactSupportGeometryCoversPointLinearAndPlanarAtlases()
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("laboratory");
        GenerationIdentity identity = L03BTestSupport.Identity(73, profile);
        MethodInfo supports = typeof(LandscapeModelBuilder).GetMethod("BuildSupportRadii", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo weight = typeof(LandscapeModel).GetMethod("CompactSupportWeight", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach ((AtlasTopologyDimension topology, AtlasSite[] sites) scenario in new[]
        {
            (AtlasTopologyDimension.Point, new[] { Site(1, 50, 50) }),
            (AtlasTopologyDimension.Linear, new[] { Site(2, 0, 50), Site(3, 50, 50), Site(4, 100, 50) }),
            (AtlasTopologyDimension.Planar, new[] { Site(5, 10, 10), Site(6, 90, 10), Site(7, 50, 90) }),
        })
        {
            WorldBounds bounds = new(0, 0, 101, 101);
            AtlasMesh atlas = L03BTestSupport.Success(AtlasGeometryBuilder.Build(identity, bounds, scenario.sites));
            Assert.AreEqual(scenario.topology, atlas.TopologyDimension);
            var radii = (IReadOnlyDictionary<StableId, double>)supports.Invoke(null, [atlas, 1.25d])!;
            for (long x = bounds.MinX; x < bounds.MaxXExclusive; x += 10)
            {
                for (long z = bounds.MinZ; z < bounds.MaxZExclusive; z += 10)
                {
                    double total = atlas.Sites.Sum(site =>
                    {
                        double dx = x - site.X;
                        double dz = z - site.Z;
                        return (double)weight.Invoke(null, [Math.Sqrt((dx * dx) + (dz * dz)) / radii[site.Id]])!;
                    });
                    Assert.IsGreaterThan(0d, total, $"{scenario.topology} ({x},{z})");
                }
            }
        }
    }

    private static AtlasSite Site(int index, long x, long z) =>
        new(StableId.Derive(RandomDomain.Sites, StableId.Zero, (ulong)index), x, z);

    private static IEnumerable<(long X, long Z)> GridCoordinates(FrozenScaleProfile profile, int side)
    {
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                yield return (
                    ((profile.WidthBlocks - 1) * x) / (side - 1),
                    ((profile.LengthBlocks - 1) * z) / (side - 1));
            }
        }
    }

    private static double AdjacentDelta(LandscapeModel model, long leftX, long leftZ, long rightX, long rightZ)
        => Math.Abs(model.Sample(leftX, leftZ).ModelAltitudeNormalized - model.Sample(rightX, rightZ).ModelAltitudeNormalized);

    private static IEnumerable<(long X, long Z)> SampleCoordinates(FrozenScaleProfile profile)
    {
        for (int z = 1; z <= 7; z++)
        {
            for (int x = 1; x <= 7; x++)
            {
                yield return (
                    (profile.WidthBlocks * x) / 8,
                    (profile.LengthBlocks * z) / 8);
            }
        }
    }

    private static double Variance(IReadOnlyList<double> values)
    {
        double mean = values.Average();
        return values.Select(value => (value - mean) * (value - mean)).Average();
    }

    private static void AssertFailure(
        GenerationResult<LandscapeModel> result,
        GenerationFailureCode code,
        string stage)
    {
        Assert.IsInstanceOfType<GenerationFailure<LandscapeModel>>(result);
        GenerationError error = ((GenerationFailure<LandscapeModel>)result).Error;
        Assert.AreEqual(code, error.Code);
        Assert.AreEqual(stage, error.Stage);
    }
}
