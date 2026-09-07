using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;

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

        AssertFailure(budget, GenerationFailureCode.BudgetExceeded, "geology.landscapes.cell-budget");
        AssertFailure(mismatch, GenerationFailureCode.InvalidInput, "geology.landscapes.profile-hash");
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
