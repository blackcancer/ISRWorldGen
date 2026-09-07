using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03B;

[TestClass]
public sealed class MultiscalePrequalificationTests
{
    [TestMethod]
    public void FamilyControlledFixturesExposeAbsoluteThreeScaleMetricsAndExactDecomposition()
    {
        var assessments = new List<L03BFixtureAssessment>();
        foreach (string profileId in new[] { "laboratory", "balanced", "vast-expeditions" })
        {
            (LandscapeModel model, AtlasMesh atlas) = Build(profileId, 20260907);
            IReadOnlyList<L03BMultiscaleFixture> fixtures = L03BMultiscalePrequalification.CreateFamilyFixtures(model, atlas, 20260907);
            assessments.AddRange(fixtures.Select(fixture => L03BMultiscalePrequalification.Assess(model, fixture)));

            foreach (L03BMultiscaleFixture fixture in fixtures)
            {
                LandscapeSample centre = model.Sample(fixture.CenterX, fixture.CenterZ);
                Assert.AreEqual(fixture.CellId, centre.DominantCellId);
                Assert.AreEqual(centre.ModelAltitudeNormalized,
                    centre.GeologicalDatumNormalized + centre.PrimaryResidualContributionNormalized + centre.ForeignResidualContributionNormalized,
                    1e-12,
                    "The common absolute result must be exactly attributable to datum and both residual paths.");
                Assert.AreEqual(0d, centre.ForeignResidualWeight);
                Assert.AreEqual(0d, centre.ForeignResidualContributionNormalized);
                Assert.AreEqual(0d, centre.TransitionDistanceRatio);
            }
        }

        CollectionAssert.AreEquivalent(Enum.GetValues<LandscapeFamily>(), assessments.Select(item => item.Fixture.Family).Distinct().ToArray());
        foreach (IGrouping<LandscapeFamily, L03BFixtureAssessment> family in assessments.GroupBy(item => item.Fixture.Family))
        {
            // Worst fixture only: no median can conceal a weak controlled fixture.
            double worstDetailAmplitude = family.Min(item => item.Scales.Single(scale => scale.Scale == L03BScale.Detail).AbsoluteAmplitude);
            double worstDetailProminence = family.Min(item => item.Scales.Single(scale => scale.Scale == L03BScale.Detail).MaximumAbsoluteProminence);
            Assert.IsGreaterThan(1e-10, worstDetailAmplitude, $"{family.Key} raw detail amplitude");
            Assert.IsGreaterThan(1e-10, worstDetailProminence, $"{family.Key} raw detail prominence");
        }
    }

    [TestMethod]
    public void CoreSignatureIsConnectedAndPureAcrossProfilesWithoutAnEnhancedView()
    {
        foreach (string profileId in new[] { "laboratory", "balanced", "vast-expeditions" })
        {
            (LandscapeModel model, AtlasMesh atlas) = Build(profileId, 20260907);
            foreach (L03BFixtureAssessment assessment in L03BMultiscalePrequalification.CreateFamilyFixtures(model, atlas, 20260907)
                .Select(fixture => L03BMultiscalePrequalification.Assess(model, fixture)))
            {
                L03BScaleMeasurement core = assessment.Scales.Single(scale => scale.Scale == L03BScale.Core);
                Assert.AreEqual(25, core.ConnectedCoreSamples, $"{profileId}/{assessment.Fixture.Family} core must remain connected and pure.");
                Assert.AreEqual(0d, core.WorstForeignResidualWeight, $"{profileId}/{assessment.Fixture.Family} core must have no foreign residual.");
                Assert.AreEqual(0d, core.MaximumTransitionDistanceRatio, $"{profileId}/{assessment.Fixture.Family} core must be outside the transition band.");
                Assert.IsGreaterThan(1e-12, core.AbsoluteAmplitude,
                    "Acceptance reads the unenhanced absolute field; a display stretch cannot create this amplitude.");
                Assert.IsGreaterThan(1e-12, core.MaximumAbsoluteProminence,
                    "Acceptance reads the unenhanced absolute field; a display stretch cannot create this prominence.");
            }
        }
    }

    [TestMethod]
    public void BlindPilotIsNeutralAndCarriesNoFamilyMappingOrEvidenceSideEffects()
    {
        (LandscapeModel model, AtlasMesh atlas) = Build("vast-expeditions", 20260907);
        L03BFixtureAssessment[] assessments = L03BMultiscalePrequalification.CreateFamilyFixtures(model, atlas, 20260907)
            .Select(fixture => L03BMultiscalePrequalification.Assess(model, fixture)).ToArray();
        L03BBlindPilotLine[] pilot = L03BMultiscalePrequalification.CreateBlindPilot(assessments).ToArray();
        string rendered = L03BMultiscalePrequalification.RenderBlindPilot(pilot);

        Assert.HasCount(assessments.Length * 3, pilot);
        StringAssert.Contains(rendered, "fixture,scale,minAbsolute");
        foreach (LandscapeFamily family in Enum.GetValues<LandscapeFamily>())
        {
            Assert.DoesNotContain(family.ToString(), rendered);
        }

        Assert.DoesNotContain("20260907", rendered);
        CollectionAssert.DoesNotContain(typeof(L03BBlindPilotLine).GetProperties().Select(property => property.Name).ToArray(), "Family");
        CollectionAssert.DoesNotContain(typeof(L03BBlindPilotLine).GetProperties().Select(property => property.Name).ToArray(), "Seed");
        CollectionAssert.DoesNotContain(typeof(L03BBlindPilotLine).GetProperties().Select(property => property.Name).ToArray(), "CenterX");
    }

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
