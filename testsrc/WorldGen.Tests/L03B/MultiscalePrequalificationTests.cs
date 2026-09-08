using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03B;

[TestClass]
public sealed class MultiscalePrequalificationTests
{
    private const int FixtureSeed = 20260907;

    private static readonly LandscapePhysicalScaleRadii ExpectedPhysicalRadii = new(450, 4_500, 26_000);

    [TestMethod]
    public void PhysicalRadiiArePublishedDistinctAndNeverCollapsedAtWorldEdges()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LandscapePhysicalScaleRadii(0, 1, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LandscapePhysicalScaleRadii(1, 1, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LandscapePhysicalScaleRadii(1, 2, 2));

        (LandscapeModel model, AtlasMesh atlas) = Build("vast-expeditions", FixtureSeed);
        IReadOnlyList<L03BMultiscaleFixture> fixtures =
            L03BMultiscalePrequalification.CreateFamilyFixtures(model, atlas, FixtureSeed);
        CollectionAssert.AreEquivalent(Enum.GetValues<LandscapeFamily>(), fixtures.Select(item => item.Family).ToArray());
        Assert.AreEqual(1, fixtures.Select(item => item.Radii).Distinct().Count(),
            "Physical scales must not disclose the family mapping in a blind packet.");

        foreach (L03BMultiscaleFixture fixture in fixtures)
        {
            LandscapePhysicalScaleRadii expected = ExpectedPhysicalRadii;
            Assert.AreEqual(expected, fixture.Radii, $"{fixture.Family} must publish the common block radii.");
            Assert.IsLessThan(fixture.Radii.DetailRadiusBlocks, fixture.Radii.CoreRadiusBlocks);
            Assert.IsLessThan(fixture.Radii.RegionRadiusBlocks, fixture.Radii.DetailRadiusBlocks);
            Assert.IsGreaterThanOrEqualTo(fixture.Radii.RegionRadiusBlocks, WorldEdgeMargin(fixture),
                $"{fixture.Family} fixture must contain the regional view without clipping.");

            LandscapeCellProfile published = model.Cells.Single(cell => cell.CellId == fixture.CellId);
            Assert.AreEqual(expected, published.PhysicalScaleRadii);
            L03BFixtureAssessment assessment = L03BMultiscalePrequalification.Assess(model, atlas, fixture);
            CollectionAssert.AreEqual(
                new[] { expected.CoreRadiusBlocks, expected.DetailRadiusBlocks, expected.RegionRadiusBlocks },
                assessment.Scales.OrderBy(scale => scale.Scale).Select(scale => scale.RadiusBlocks).ToArray());
            foreach (L03BScaleMeasurement scale in assessment.Scales)
            {
                Assert.AreEqual(L03BMultiscalePrequalification.MapSide * L03BMultiscalePrequalification.MapSide, scale.MapPointCount);
                Assert.AreEqual(scale.MapPointCount, scale.UniqueMapPointCount,
                    $"{fixture.Family}/{scale.Scale} must use unique physical coordinates.");
                Assert.AreEqual(scale.RadiusBlocks,
                    scale.MapSamples.Max(point => Math.Abs(point.X - fixture.CenterX)));
                Assert.AreEqual(scale.RadiusBlocks,
                    scale.MapSamples.Max(point => Math.Abs(point.Z - fixture.CenterZ)));
            }
        }
    }

    [TestMethod]
    public void FixedMinimumCoreIsPureAndConnectivityIsMeasuredIndependently()
    {
        bool[] disconnectedMask =
        [
            true, false, false,
            false, true, false,
            false, false, false,
        ];
        Assert.AreEqual(2, disconnectedMask.Count(value => value));
        Assert.AreEqual(1, L03BMultiscalePrequalification.CountConnectedFromCenter(disconnectedMask, 3),
            "The connectivity oracle must not reduce to counting true samples.");

        (LandscapeModel model, AtlasMesh atlas) = Build("vast-expeditions", FixtureSeed);
        foreach (L03BMultiscaleFixture fixture in L03BMultiscalePrequalification.CreateFamilyFixtures(model, atlas, FixtureSeed))
        {
            L03BScaleMeasurement core = L03BMultiscalePrequalification.Assess(model, atlas, fixture)
                .Scales.Single(scale => scale.Scale == L03BScale.Core);
            int expectedSamples = L03BMultiscalePrequalification.MapSide * L03BMultiscalePrequalification.MapSide;
            int expectedBoundary = 4 * (L03BMultiscalePrequalification.MapSide - 1);
            Assert.IsGreaterThanOrEqualTo(450L, core.RadiusBlocks, $"{fixture.Family} must keep the declared physical core minimum.");
            Assert.AreEqual(expectedSamples, core.PureCoreSampleCount,
                $"{fixture.Family} fixed core extent must be wholly owner-pure.");
            Assert.AreEqual(expectedSamples, core.ConnectedPureCoreSamples,
                $"{fixture.Family} pure core must be connected to its centre under four-neighbour traversal.");
            Assert.AreEqual(expectedBoundary, core.PureCoreBoundarySampleCount,
                $"{fixture.Family} must remain pure through the complete boundary of the fixed core map.");
            Assert.IsGreaterThan(1e-8, core.AbsoluteAmplitude, $"{fixture.Family} absolute core map must carry measurable relief.");
            Assert.IsGreaterThan(1e-8, core.MaximumAbsoluteProminence, $"{fixture.Family} absolute core prominence must be measurable.");

            L03BMapPoint centre = core.MapSamples.Single(point =>
                point.X == fixture.CenterX && point.Z == fixture.CenterZ);
            Assert.AreEqual(centre.Sample.ModelAltitudeNormalized,
                centre.Sample.GeologicalDatumNormalized +
                centre.Sample.PrimaryResidualContributionNormalized +
                centre.Sample.ForeignResidualContributionNormalized,
                1e-12,
                "Absolute altitude must decompose into independently published terms.");
        }
    }

    [TestMethod]
    public void ThreePhysicalScalesUseDistinctTransitionPointsWithMeasuredForeignResiduals()
    {
        (LandscapeModel model, AtlasMesh atlas) = Build("vast-expeditions", FixtureSeed);
        foreach (L03BMultiscaleFixture fixture in L03BMultiscalePrequalification.CreateFamilyFixtures(model, atlas, FixtureSeed))
        {
            L03BFixtureAssessment assessment = L03BMultiscalePrequalification.Assess(model, atlas, fixture);
            Assert.AreEqual(3, assessment.Scales.Select(scale =>
                (scale.TransitionProfile.CenterX, scale.TransitionProfile.CenterZ)).Distinct().Count(),
                $"{fixture.Family} must use a distinct transition point at every physical scale.");

            foreach (L03BScaleMeasurement scale in assessment.Scales)
            {
                Assert.AreEqual(L03BMultiscalePrequalification.ProfilePointCount,
                    scale.TransitionProfile.Points.Select(point => (point.X, point.Z)).Distinct().Count(),
                    $"{fixture.Family}/{scale.Scale} transition profile points must remain unique.");
                Assert.IsGreaterThan(0, scale.TransitionSampleCount,
                    $"{fixture.Family}/{scale.Scale} must cross an actual transition.");
                Assert.IsGreaterThan(0, scale.NonZeroForeignTransitionSampleCount,
                    $"{fixture.Family}/{scale.Scale} must measure a non-zero foreign residual.");
                Assert.IsGreaterThanOrEqualTo(1e-12, scale.MinimumAbsoluteForeignResidualContribution);
                Assert.IsGreaterThan(1e-8, scale.MaximumAbsoluteForeignResidualContribution,
                    $"{fixture.Family}/{scale.Scale} foreign contribution must be materially measurable.");
                foreach (L03BTransitionPoint point in scale.TransitionProfile.Points.Where(point =>
                    point.Sample.IsTransition && Math.Abs(point.Sample.ForeignResidualContributionNormalized) >= 1e-12))
                {
                    Assert.IsGreaterThan(0d, point.Sample.ForeignResidualWeight);
                    Assert.AreEqual(point.Sample.ModelAltitudeNormalized,
                        point.Sample.GeologicalDatumNormalized +
                        point.Sample.PrimaryResidualContributionNormalized +
                        point.Sample.ForeignResidualContributionNormalized,
                        1e-12);
                }
            }
        }
    }

    [TestMethod]
    public void BlindPilotUsesSecretPermutationAndKeepsMappingInSeparateKey()
    {
        (LandscapeModel model, AtlasMesh atlas) = Build("vast-expeditions", FixtureSeed);
        L03BFixtureAssessment[] assessments = L03BMultiscalePrequalification.CreateFamilyFixtures(model, atlas, FixtureSeed)
            .Select(fixture => L03BMultiscalePrequalification.Assess(model, atlas, fixture)).ToArray();
        byte[] secretA = Encoding.UTF8.GetBytes("controller-only-permutation-A-20260907");
        byte[] secretB = Encoding.UTF8.GetBytes("controller-only-permutation-B-20260907");

        L03BBlindReviewPacket packetA = L03BMultiscalePrequalification.CreateBlindReviewPacket(assessments, secretA);
        L03BBlindReviewPacket packetB = L03BMultiscalePrequalification.CreateBlindReviewPacket(assessments, secretB);
        IReadOnlyList<L03BBlindAnswerKeyLine> keyA = L03BMultiscalePrequalification.CreateBlindAnswerKey(assessments, secretA);
        string blindArtifact = L03BMultiscalePrequalification.RenderBlindReview(packetA);
        string separateKey = L03BMultiscalePrequalification.RenderBlindAnswerKey(keyA);

        Assert.HasCount(assessments.Length * 3, packetA.Cards);
        Assert.HasCount(assessments.Length * 3, packetA.Profiles);
        Assert.HasCount(assessments.Length * 3, packetA.Metrics);
        Assert.HasCount(assessments.Length, keyA);
        Assert.IsTrue(packetA.Cards.All(card => Regex.IsMatch(card.Code, "^Q-[0-9A-F]{16}$", RegexOptions.CultureInvariant)));
        Assert.AreEqual(assessments.Length, packetA.Cards.Select(card => card.Code).Distinct().Count());
        Assert.IsFalse(
            keyA.Select(item => item.Family).SequenceEqual(Enum.GetValues<LandscapeFamily>()),
            "The reviewer order must be a secret-derived permutation, not enum order.");
        Assert.IsFalse(
            packetA.Cards.Select(item => item.Code).SequenceEqual(packetB.Cards.Select(item => item.Code)),
            "Changing the controller secret must change both opaque IDs and their permutation.");

        Assert.DoesNotContain(Encoding.UTF8.GetString(secretA), blindArtifact, StringComparison.Ordinal);
        Assert.DoesNotContain(FixtureSeed.ToString(CultureInfo.InvariantCulture), blindArtifact, StringComparison.Ordinal);
        foreach (LandscapeFamily family in Enum.GetValues<LandscapeFamily>())
        {
            Assert.DoesNotContain(family.ToString(), blindArtifact, StringComparison.Ordinal);
            StringAssert.Contains(separateKey, family.ToString());
        }

        foreach (L03BBlindAnswerKeyLine line in keyA)
        {
            StringAssert.Contains(separateKey, line.Code);
            Assert.DoesNotContain(line.CellId.ToString(), blindArtifact, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("cellId", blindArtifact, StringComparison.Ordinal);
        Assert.DoesNotContain("centerX", blindArtifact, StringComparison.Ordinal);
        Assert.DoesNotContain("centerZ", blindArtifact, StringComparison.Ordinal);

        CollectionAssert.DoesNotContain(
            typeof(L03BBlindReviewPacket).GetProperties().Select(property => property.Name).ToArray(),
            "AnswerKey");
        CollectionAssert.DoesNotContain(
            typeof(L03BBlindMapCard).GetProperties().Select(property => property.Name).ToArray(),
            "Family");
        CollectionAssert.DoesNotContain(
            typeof(L03BBlindAbsoluteProfile).GetProperties().Select(property => property.Name).ToArray(),
            "Seed");
        StringAssert.Contains(blindArtifact, "[absolute-map-cards]");
        StringAssert.Contains(blindArtifact, "[absolute-transition-profiles]");
        StringAssert.Contains(blindArtifact, "[measurements]");
        StringAssert.Contains(blindArtifact, packetA.Cards[0].AbsoluteAltitudes[0].ToString("R", CultureInfo.InvariantCulture));
        StringAssert.Contains(blindArtifact, packetA.Profiles[0].AbsoluteAltitudes[0].ToString("R", CultureInfo.InvariantCulture));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            L03BMultiscalePrequalification.CreateBlindReviewPacket(assessments, new byte[15]));
    }

    private static long WorldEdgeMargin(L03BMultiscaleFixture fixture) => Math.Min(
        Math.Min(fixture.CenterX - fixture.Bounds.MinX, fixture.Bounds.MaxXExclusive - 1 - fixture.CenterX),
        Math.Min(fixture.CenterZ - fixture.Bounds.MinZ, fixture.Bounds.MaxZExclusive - 1 - fixture.CenterZ));

    private static (LandscapeModel Model, AtlasMesh Atlas) Build(string profileId, int seed)
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile(profileId);
        GenerationIdentity identity = L03BTestSupport.Identity(seed, profile);
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(identity.NativeSeed, profile);
        ReliefBudgetRequest budget = profileId == "laboratory"
            ? new ReliefBudgetRequest(28, 40, 96)
            : new ReliefBudgetRequest(64, 48, 128);
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(
            identity, atlas, plates, profile, new LandscapeGenerationSettings(budget, profile.SiteQuota, 1.25)));
        return (model, atlas);
    }
}
