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
            blendSiteCount: 4);

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
            new LandscapeGenerationSettings(new ReliefBudgetRequest(28, 40, 96), 32, 4)));
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
            new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), 1, 4));
        GenerationResult<LandscapeModel> mismatch = LandscapeModelBuilder.Build(
            identity,
            atlas,
            plates,
            laboratory,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(28, 40, 96), 128, 4));
        GenerationResult<LandscapeModel> budgetBeforeProvenance = LandscapeModelBuilder.Build(
            identity,
            wrongAtlas,
            plates,
            balanced,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), 1, 4));

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
        var settings = new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 4);

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
        var settings = new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 4);
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
            new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 4)));
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
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, profile, new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 4)));
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
        foreach (LandscapeFamilyProfile family in LandscapeFamilyCatalog.Profiles)
        {
            double value = LandscapeSignatureSampler.Sample(family, LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks, -LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks, 1, 1, (long)LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks, -(long)LandscapeSignatureSampler.MaximumAbsoluteCoordinateBlocks);
            Assert.IsTrue(double.IsFinite(value) && value is >= -1 and <= 1);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LandscapeSignatureSampler.Sample(family, 0, 0, 1, 1, long.MinValue, long.MaxValue));
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
